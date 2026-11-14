// Local smoke test: JWT_SECRET=x node test.js   (starts the server itself, memory storage)
process.env.JWT_SECRET = process.env.JWT_SECRET || 'test-secret';
process.env.PORT = 18080; delete process.env.MONGODB_URI;
const WebSocket = require('ws'); const jwt = require('jsonwebtoken');
require('./server.js');
const tok = (u) => jwt.sign({ id: u, username: u }, process.env.JWT_SECRET);
function client(u) {
  return new Promise((res) => {
    const ws = new WebSocket('ws://localhost:18080'); const log = [];
    ws.on('message', (d) => log.push(JSON.parse(d)));
    ws.on('open', () => { ws.send(JSON.stringify({ t: 'hello', token: tok(u), suit: 1 })); res({ ws, log, send: (o) => ws.send(JSON.stringify(o)), has: (t) => log.find((x) => x.t === t) }); });
  });
}
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
(async () => {
  await wait(500);
  const a = await client('Alice'), b = await client('Bob'); await wait(200);
  let ok = true; const check = (n, c) => { console.log(c ? 'PASS' : 'FAIL', n); if (!c) ok = false; };
  check('welcome', a.has('welcome') && b.has('welcome'));
  a.send({ t: 'friend_add', user: 'Bob' }); await wait(200);
  check('request reaches bob', b.has('friend_req'));
  b.send({ t: 'friend_accept', user: 'Alice' }); await wait(200);
  const f = [...a.log].reverse().find((x) => x.t === 'friends');
  check('alice sees bob online friend', f && f.friends.length === 1 && f.friends[0].online);
  a.send({ t: 'create' }); await wait(200);
  const room = a.has('room'); check('room code', room && room.code.length === 4);
  a.send({ t: 'invite', user: 'Bob' }); await wait(200);
  const inv = b.has('invite'); check('invite', inv && inv.code === room.code);
  b.send({ t: 'join', code: room.code.toLowerCase() }); await wait(200);
  check('host told peer joined', a.has('peer_joined'));
  // --- lobby handshake: the host cannot start until BOTH players report they are in the lobby
  const last = (c, t) => [...c.log].reverse().find((x) => x.t === t);
  a.send({ t: 'start' }); await wait(200);
  check('start refused while nobody is in the lobby', !a.has('start') && last(a, 'error') && last(a, 'error').code === 'notready');
  a.send({ t: 'map', map: 2 }); a.send({ t: 'lobby', on: true }); await wait(200);
  a.log.length = 0; a.send({ t: 'start' }); await wait(200);
  check('start refused while the partner is not in the lobby', !a.has('start') && last(a, 'error') && /Bob/.test(last(a, 'error').msg));
  b.send({ t: 'lobby', on: true }); await wait(200);
  const ri = last(a, 'room');
  check('host sees both in lobby + map', ri && ri.players.length === 2 && ri.players.every((p) => p.lobby) && ri.map === 2);
  a.send({ t: 'start' }); await wait(200);
  check('both started', a.has('start') && b.has('start') && b.has('start').role === 'guest' && b.has('start').map === 2);
  a.log.length = 0; a.send({ t: 'start' }); await wait(150);
  check('second start while running is refused', !a.has('start') && last(a, 'error') && last(a, 'error').code === 'running');
  const c0 = await client('Dora'); c0.send({ t: 'join', code: room.code }); await wait(150);
  check('cannot join a running room', c0.has('error'));
  a.send({ t: 'invite', user: 'Bob' }); await wait(150);
  // run ends: the guest stays in the room, the host must wait for the guest to report the lobby again
  a.send({ t: 'ended' }); await wait(200);
  check('guest told the run ended', !!b.has('ended'));
  a.send({ t: 'lobby', on: true }); await wait(150);
  a.log.length = 0; a.send({ t: 'start' }); await wait(200);
  check('restart refused until the guest is back in the lobby', !a.has('start') && last(a, 'error') && last(a, 'error').code === 'notready');
  b.send({ t: 'lobby', on: true }); await wait(150);
  a.send({ t: 'start' }); await wait(200);
  check('restart works once both are in the lobby', !!a.has('start'));
  a.send({ t: 'rt', d: 'hello' }); b.send({ t: 'rt', d: 'back' }); await wait(200);
  check('relay both ways', b.has('rt').d === 'hello' && a.has('rt').d === 'back');
  const c = await client('Cara'); c.send({ t: 'join', code: room.code }); await wait(200);
  check('full room rejected', c.has('error'));
  b.ws.close(); await wait(300);
  check('host told peer left', a.has('peer_left'));
  console.log(ok ? 'ALL OK' : 'FAILURES'); process.exit(ok ? 0 : 1);
})();
