// REBOUND RUMBLE multiplayer server.
//   * WebSocket (wss on Render) - works from the itch.io web build, Windows/Mac builds and phones
//   * 2-player co-op rooms with a short join code; the HOST'S game is the authority, this server just relays
//   * friends (add / accept / remove / online status / invite) stored in the SAME MongoDB as the account API
//   * accounts are the ones from the main API: the same JWT_SECRET signs the login token
//
// Env: JWT_SECRET (required), MONGODB_URI (optional - without it friends are kept in memory, handy for testing),
//      DB_NAME (default rebound_rumble), PORT (set by Render)
const http = require('http');
const { WebSocketServer } = require('ws');
const jwt = require('jsonwebtoken');

const JWT_SECRET = process.env.JWT_SECRET;
const MONGODB_URI = process.env.MONGODB_URI || '';
const DB_NAME = process.env.DB_NAME || 'rebound_rumble';
const PORT = process.env.PORT || 10000;
if (!JWT_SECRET) { console.error('JWT_SECRET is required'); process.exit(1); }

// ------------------------------------------------------------------ storage (MongoDB or memory)
const lower = (s) => String(s || '').trim().toLowerCase();
let store;
if (MONGODB_URI) {
  const mongoose = require('mongoose');
  const User = mongoose.model('User', new mongoose.Schema({ username: String, usernameLower: String }), 'rr_users');
  const Friendship = mongoose.model('Friendship', new mongoose.Schema({
    a: { type: String, required: true },          // who sent the request (lowercase username)
    b: { type: String, required: true },          // who received it
    status: { type: String, enum: ['pending', 'accepted'], default: 'pending' },
    createdAt: { type: Date, default: Date.now },
  }), 'rr_friends');
  Friendship.collection.createIndex({ a: 1, b: 1 }, { unique: true }).catch(() => {});
  Friendship.collection.createIndex({ b: 1 }).catch(() => {});
  let ready = null;
  const connect = () => ready || (ready = mongoose.connect(MONGODB_URI.trim().replace(/^["']|["']$/g, '').split('?')[0] + '?retryWrites=true&w=majority', { dbName: DB_NAME, serverSelectionTimeoutMs: 8000 }).catch((e) => { ready = null; throw e; }));
  store = {
    kind: 'mongodb',
    async init() { await connect(); },
    async realName(name) { const u = await User.findOne({ usernameLower: lower(name) }).lean(); return u ? u.username : null; },
    async rows(me) { return Friendship.find({ $or: [{ a: me }, { b: me }] }).lean(); },
    async find(a, b) { return Friendship.findOne({ a, b }).lean(); },
    async create(a, b) { try { await Friendship.create({ a, b, status: 'pending' }); } catch (e) { if (!(e && e.code === 11000)) throw e; } },
    async accept(a, b) { await Friendship.updateOne({ a, b }, { $set: { status: 'accepted' } }); },
    async remove(x, y) { await Friendship.deleteMany({ $or: [{ a: x, b: y }, { a: y, b: x }] }); },
  };
} else {
  const names = new Map();   // lower -> real
  const rows = [];           // { a, b, status }
  store = {
    kind: 'memory',
    async init() {},
    note(name) { names.set(lower(name), name); },
    async realName(name) { return names.get(lower(name)) || null; },
    async rows(me) { return rows.filter((r) => r.a === me || r.b === me); },
    async find(a, b) { return rows.find((r) => r.a === a && r.b === b) || null; },
    async create(a, b) { if (!rows.find((r) => r.a === a && r.b === b)) rows.push({ a, b, status: 'pending' }); },
    async accept(a, b) { const r = rows.find((x) => x.a === a && x.b === b); if (r) r.status = 'accepted'; },
    async remove(x, y) { for (let i = rows.length - 1; i >= 0; i--) if ((rows[i].a === x && rows[i].b === y) || (rows[i].a === y && rows[i].b === x)) rows.splice(i, 1); },
  };
}

// ------------------------------------------------------------------ state
const online = new Map();   // lowercase username -> client
const rooms = new Map();    // code -> room
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
function newCode() {
  for (let n = 0; n < 50; n++) {
    let c = ''; for (let i = 0; i < 4; i++) c += CODE_CHARS[Math.floor(Math.random() * CODE_CHARS.length)];
    if (!rooms.has(c)) return c;
  }
  return null;
}
const send = (c, obj) => { if (c && c.ws.readyState === 1) c.ws.send(JSON.stringify(obj)); };
const roomInfo = (r) => ({ code: r.code, players: r.members.map((m) => ({ user: m.user, suit: m.suit, host: m === r.members[0], lobby: !!m.lobby })), started: r.started, map: r.map | 0 });
// tell everybody in the room what the room looks like now (who is in the lobby, started or not, which map)
const pushRoom = (r) => { for (const x of r.members) send(x, { t: 'room', ...roomInfo(r), role: x === r.members[0] ? 'host' : 'guest' }); };
const clampMap = (v) => Math.max(0, Math.min(8, parseInt(v, 10) || 0));

async function friendsFor(c) {
  const me = c.key;
  const rows = await store.rows(me);
  const friends = [], requests = [], sent = [];
  for (const r of rows) {
    const other = r.a === me ? r.b : r.a;
    if (r.status === 'accepted') {
      const o = online.get(other);
      friends.push({ user: o ? o.user : other, online: !!o, room: o && o.room ? o.room.code : '' });
    } else if (r.b === me) requests.push({ user: other });
    else sent.push({ user: other });
  }
  friends.sort((x, y) => (y.online - x.online) || x.user.localeCompare(y.user));
  return { t: 'friends', friends, requests, sent };
}
async function pushFriends(name) { const c = online.get(lower(name)); if (c) send(c, await friendsFor(c)); }
async function pushToFriendsOf(c) {
  const rows = await store.rows(c.key);
  for (const r of rows) if (r.status === 'accepted') pushFriends(r.a === c.key ? r.b : r.a);
}

function leaveRoom(c, why) {
  const r = c.room; if (!r) return;
  c.room = null; c.lobby = false;
  const idx = r.members.indexOf(c);
  if (idx < 0) return;
  if (idx === 0) {                       // host left: the room is over
    for (const m of r.members) if (m !== c) { m.room = null; send(m, { t: 'room_closed', why: why || 'The host left' }); }
    rooms.delete(r.code);
  } else {
    r.members.splice(idx, 1);
    send(r.members[0], { t: 'peer_left', user: c.user });
    pushRoom(r);
  }
  pushToFriendsOf(c);
}

// ------------------------------------------------------------------ message handling
async function onMessage(c, m) {
  if (!m || typeof m.t !== 'string') return;
  if (m.t === 'ping') return send(c, { t: 'pong', n: m.n });
  if (m.t === 'hello') {
    let u;
    try { u = jwt.verify(String(m.token || ''), JWT_SECRET); } catch (e) { send(c, { t: 'error', code: 'auth', msg: 'Sign in again (login expired)' }); return c.ws.close(); }
    c.user = u.username; c.key = lower(u.username); c.suit = Math.max(0, Math.min(11, parseInt(m.suit, 10) || 0));
    if (store.note) store.note(u.username);
    const old = online.get(c.key);
    if (old && old !== c) { send(old, { t: 'error', code: 'dup', msg: 'Signed in from another place' }); leaveRoom(old); old.ws.close(); }
    online.set(c.key, c);
    send(c, { t: 'welcome', user: c.user, db: store.kind });
    send(c, await friendsFor(c));
    pushToFriendsOf(c);
    return;
  }
  if (!c.key) return send(c, { t: 'error', code: 'auth', msg: 'Say hello first' });

  switch (m.t) {
    case 'suit': c.suit = Math.max(0, Math.min(11, parseInt(m.suit, 10) || 0)); break;
    case 'create': {
      leaveRoom(c);
      const code = newCode(); if (!code) return send(c, { t: 'error', msg: 'No free room codes, try again' });
      const r = { code, members: [c], started: false, map: clampMap(m.map), born: Date.now() };
      c.lobby = false;                                   // becomes true when the client reports it is looking at the lobby
      rooms.set(code, r); c.room = r;
      send(c, { t: 'room', ...roomInfo(r), role: 'host' });
      pushToFriendsOf(c);
      break;
    }
    case 'join': {
      const code = String(m.code || '').trim().toUpperCase();
      const r = rooms.get(code);
      if (!r) return send(c, { t: 'error', code: 'nocode', msg: 'No room with that code' });
      if (r.members.includes(c)) return;
      if (r.members.length >= 2) return send(c, { t: 'error', code: 'full', msg: 'That room is full' });
      if (r.started) return send(c, { t: 'error', code: 'started', msg: 'That game is in progress - try again when they are back in the lobby' });
      leaveRoom(c);
      c.lobby = false;
      r.members.push(c); c.room = r;
      pushRoom(r);
      send(r.members[0], { t: 'peer_joined', user: c.user, suit: c.suit });
      pushToFriendsOf(c);
      break;
    }
    case 'leave': leaveRoom(c); send(c, { t: 'left' }); break;
    case 'lobby': {                       // a client tells us whether it is looking at the lobby screen right now
      const r = c.room; if (!r) return;
      const on = m.on === true;
      if (c.lobby === on) return;
      c.lobby = on;
      pushRoom(r);
      break;
    }
    case 'map': {                         // the host picks the map for the next run
      const r = c.room; if (!r || r.members[0] !== c || r.started) return;
      const v = clampMap(m.map);
      if (r.map === v) return;
      r.map = v; pushRoom(r);
      break;
    }
    case 'start': {
      const r = c.room; if (!r || r.members[0] !== c) return;
      if (r.started) return send(c, { t: 'error', code: 'running', msg: 'A run is already in progress' });
      if (r.members.length < 2) return send(c, { t: 'error', msg: 'Waiting for your friend to join' });
      const slow = r.members.find((x) => !x.lobby);       // everybody must be back in the lobby first
      if (slow) return send(c, { t: 'error', code: 'notready', msg: 'Waiting for ' + slow.user + ' to reach the lobby' });
      r.started = true;
      for (const x of r.members) x.lobby = false;
      for (const x of r.members) send(x, { t: 'start', ...roomInfo(r), role: x === c ? 'host' : 'guest' });
      break;
    }
    case 'ended': {                       // the host's run is over (died / quit): everybody goes back to the lobby
      const r = c.room; if (!r || r.members[0] !== c) return;
      r.started = false;
      for (const x of r.members) if (x !== c) send(x, { t: 'ended' });
      pushRoom(r);
      break;
    }
    case 'rt': {          // relay game data to the other player (opaque string)
      const r = c.room; if (!r) return;
      const other = r.members.find((x) => x !== c);
      if (other && typeof m.d === 'string' && m.d.length < 60000) send(other, { t: 'rt', d: m.d });
      break;
    }
    case 'friends': send(c, await friendsFor(c)); break;
    case 'friend_add': {
      const real = await store.realName(m.user);
      const target = lower(m.user);
      if (!real) return send(c, { t: 'error', code: 'nouser', msg: 'No player named ' + String(m.user || '').slice(0, 16) });
      if (target === c.key) return send(c, { t: 'error', msg: "That's you!" });
      const rows = await store.rows(c.key);
      if (rows.find((r) => (r.a === c.key && r.b === target) || (r.a === target && r.b === c.key)) && !rows.find((r) => r.a === target && r.b === c.key && r.status === 'pending')) return send(c, { t: 'info', msg: 'Request already sent or already friends' });
      const reverse = await store.find(target, c.key);
      if (reverse && reverse.status === 'pending') { await store.accept(target, c.key); send(c, { t: 'info', msg: 'You and ' + real + ' are now friends!' }); }
      else { await store.create(c.key, target); send(c, { t: 'info', msg: 'Friend request sent to ' + real }); const o = online.get(target); if (o) send(o, { t: 'friend_req', from: c.user }); }
      await pushFriends(c.user); await pushFriends(real);
      break;
    }
    case 'friend_accept': {
      const from = lower(m.user);
      const r = await store.find(from, c.key);
      if (!r) return send(c, { t: 'error', msg: 'No such request' });
      await store.accept(from, c.key);
      await pushFriends(c.user); await pushFriends(m.user);
      const o = online.get(from); if (o) send(o, { t: 'info', msg: c.user + ' accepted your friend request' });
      break;
    }
    case 'friend_remove': { await store.remove(c.key, lower(m.user)); await pushFriends(c.user); await pushFriends(m.user); break; }
    case 'invite': {
      const target = lower(m.user);
      if (!c.room) return send(c, { t: 'error', msg: 'Create a room first' });
      if (c.room.started) return send(c, { t: 'error', msg: 'A run is in progress - invite after you are back in the lobby' });
      if (c.room.members.length >= 2) return send(c, { t: 'error', msg: 'Your room is already full' });
      const rows = await store.rows(c.key);
      if (!rows.find((r) => r.status === 'accepted' && (r.a === target || r.b === target))) return send(c, { t: 'error', msg: 'You can only invite friends' });
      const o = online.get(target);
      if (!o) return send(c, { t: 'error', msg: m.user + ' is offline' });
      send(o, { t: 'invite', from: c.user, code: c.room.code });
      send(c, { t: 'info', msg: 'Invite sent to ' + o.user });
      break;
    }
  }
}

// ------------------------------------------------------------------ http + websocket
const server = http.createServer((req, res) => {
  const body = JSON.stringify({ ok: true, name: 'rebound-rumble-multiplayer', online: online.size, rooms: rooms.size, db: store.kind });
  res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*', 'Cache-Control': 'no-store' });
  res.end(body);
});
const wss = new WebSocketServer({ server, maxPayload: 70 * 1024 });
wss.on('connection', (ws) => {
  const c = { ws, user: '', key: '', suit: 0, room: null, alive: true, bucket: 150, last: Date.now() };
  ws.on('pong', () => { c.alive = true; });
  ws.on('message', (data, isBinary) => {
    if (isBinary) return;
    const now = Date.now();                                    // simple rate limit (~150 msgs/s burst, 120/s sustained)
    c.bucket = Math.min(150, c.bucket + (now - c.last) * 0.12); c.last = now;
    if (c.bucket < 1) return; c.bucket -= 1;
    let m; try { m = JSON.parse(data.toString()); } catch (e) { return; }
    onMessage(c, m).catch((e) => { console.error('msg', m && m.t, e.message); send(c, { t: 'error', msg: 'Server error' }); });
  });
  ws.on('close', () => {
    if (c.key && online.get(c.key) === c) { online.delete(c.key); leaveRoom(c, c.user + ' disconnected'); pushToFriendsOf(c).catch(() => {}); }
  });
  ws.on('error', () => {});
});
setInterval(() => {                                            // drop dead sockets, keep the connection warm
  for (const ws of wss.clients) { const c = ws; if (c._dead) { c.terminate(); continue; } c._dead = true; c.ping(); c.once('pong', () => { c._dead = false; }); }
  const cutoff = Date.now() - 6 * 3600 * 1000;
  for (const [code, r] of rooms) if (r.born < cutoff) { for (const m of r.members) m.room = null; rooms.delete(code); }
}, 25000);

store.init().then(() => {
  server.listen(PORT, () => console.log('multiplayer server on :' + PORT + '  storage=' + store.kind));
}).catch((e) => { console.error('Database error:', String(e.message).replace(/mongodb(\+srv)?:\/\/\S+/g, '<uri>')); process.exit(1); });
