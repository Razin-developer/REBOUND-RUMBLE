// REBOUND RUMBLE API: username/password auth (no email), unique usernames, score submission + leaderboard.
// Secrets come from environment variables only (MONGODB_URI, JWT_SECRET) - never hard-code them.
const express = require('express');
const cors = require('cors');
const rateLimit = require('express-rate-limit');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const mongoose = require('mongoose');

const MONGODB_URI = process.env.MONGODB_URI;
const JWT_SECRET = process.env.JWT_SECRET;
const PORT = process.env.PORT || 3000;
if (!JWT_SECRET) { console.error('JWT_SECRET is required'); throw new Error('JWT_SECRET is required'); }

// ---------- models ----------
const userSchema = new mongoose.Schema({
  username: { type: String, required: true, trim: true },
  usernameLower: { type: String, required: true, unique: true, index: true }, // case-insensitive uniqueness
  passwordHash: { type: String, required: true },
  createdAt: { type: Date, default: Date.now },
});
const scoreSchema = new mongoose.Schema({
  user: { type: mongoose.Schema.Types.ObjectId, ref: 'User', required: true, index: true },
  username: { type: String, required: true },
  clientId: { type: String, required: true },            // lets offline scores be re-sent safely (no duplicates)
  score: { type: Number, required: true, min: 0 },
  seconds: { type: Number, default: 0, min: 0 },
  kills: { type: Number, default: 0, min: 0 },
  createdAt: { type: Date, default: Date.now },
});
scoreSchema.index({ user: 1, clientId: 1 }, { unique: true });
scoreSchema.index({ score: -1 });
// own database + own collection names: this Atlas cluster is shared with other projects (its default `test.users` already
// holds other data, which made the unique-username index fail with E11000)
const API_VERSION = '2026-11-14.6';
const DB_NAME = process.env.DB_NAME || 'rebound_rumble';
const User = mongoose.model('User', userSchema, 'rr_users');
const Score = mongoose.model('Score', scoreSchema, 'rr_scores');

// ---------- app ----------
const app = express();
app.set('trust proxy', 1);                 // behind Render / Cloudflare
app.use(cors({ origin: true }));           // token auth in a header (no cookies), so any origin (itch.io, localhost) is fine
app.use(express.json({ limit: '50kb' }));
// never let browsers / CDNs cache API answers (a cached leaderboard looks like 'scores not updating')
app.use((req, res, next) => { res.set('Cache-Control', 'no-store, max-age=0'); res.removeHeader('ETag'); next(); });
app.set('etag', false);
app.use('/api/', rateLimit({ windowMs: 60 * 1000, max: 120, standardHeaders: true, legacyHeaders: false }));
const authLimiter = rateLimit({ windowMs: 15 * 60 * 1000, max: 30, standardHeaders: true, legacyHeaders: false });

const USER_RE = /^[A-Za-z0-9_]{3,16}$/;
const sign = (u) => jwt.sign({ id: u._id.toString(), username: u.username }, JWT_SECRET, { expiresIn: '30d' });

function auth(req, res, next) {
  const h = req.headers.authorization || '';
  const t = h.startsWith('Bearer ') ? h.slice(7) : null;
  if (!t) return res.status(401).json({ error: 'Not signed in' });
  try { req.user = jwt.verify(t, JWT_SECRET); next(); }
  catch (e) { res.status(401).json({ error: 'Session expired, please log in again' }); }
}

// ---- MongoDB connection, cached so it survives across serverless invocations (Vercel) ----
const cache = global._rrMongo || (global._rrMongo = { promise: null, ready: false, lastError: '' });
// Atlas "copy connection string" often carries extra options (authMechanism=SCRAM-SHA-1, replicaSet, loadBalanced...)
// that can make a serverless connection fail. Keep only the essentials.
function cleanUri(u) { return String(u || '').trim().replace(/^["']|["']$/g, '').split('?')[0] + '?retryWrites=true&w=majority'; }
const safe = (m) => String(m || '').replace(/mongodb(\+srv)?:\/\/\S+/g, '<uri>').slice(0, 220);
async function connectDB() {
  if (cache.ready && mongoose.connection.readyState === 1) return;
  if (!MONGODB_URI) throw new Error('MONGODB_URI is not set');
  if (!cache.promise) {
    cache.promise = mongoose.connect(cleanUri(MONGODB_URI), { dbName: DB_NAME, serverSelectionTimeoutMS: 8000, maxPoolSize: 5, bufferCommands: false })
      .then(() => Promise.all([User.init(), Score.init()]).catch((e) => console.error('index', safe(e.message))))   // indexes are best-effort: never block the API on them
      .then(() => { cache.ready = true; });
  }
  try { await cache.promise; cache.lastError = ''; } catch (e) { cache.promise = null; cache.lastError = safe(e.message); throw e; }
}
// health first (before the DB gate): reports WHY the database is unreachable instead of just failing
app.get('/api/health', async (req, res) => {
  try { await connectDB(); res.json({ ok: true, db: true, v: API_VERSION, dbName: DB_NAME }); }
  catch (e) { res.status(503).json({ ok: false, db: false, hasUri: !!MONGODB_URI, reason: safe(e.message) }); }
});
app.use(async (req, res, next) => {
  try { await connectDB(); next(); }
  catch (e) { console.error('db', e.message); res.status(503).json({ error: 'Database unavailable: ' + safe(e.message) }); }
});

app.get('/', (req, res) => res.json({ ok: true, name: 'rebound-rumble-api' }));

app.post('/api/register', authLimiter, async (req, res) => {
  try {
    const username = String((req.body && req.body.username) || '').trim();
    const password = String((req.body && req.body.password) || '');
    if (!USER_RE.test(username)) return res.status(400).json({ error: 'Username: 3-16 letters, numbers or _' });
    if (password.length < 6 || password.length > 72) return res.status(400).json({ error: 'Password must be 6-72 characters' });
    const lower = username.toLowerCase();
    if (await User.findOne({ usernameLower: lower })) return res.status(409).json({ error: 'That username is taken' });
    const user = await User.create({ username, usernameLower: lower, passwordHash: await bcrypt.hash(password, 10) });
    res.json({ token: sign(user), username: user.username });
  } catch (e) {
    if (e && e.code === 11000) return res.status(409).json({ error: 'That username is taken' });
    console.error('register', e.message); res.status(500).json({ error: 'Server error' });
  }
});

app.post('/api/login', authLimiter, async (req, res) => {
  try {
    const username = String((req.body && req.body.username) || '').trim().toLowerCase();
    const password = String((req.body && req.body.password) || '');
    const user = await User.findOne({ usernameLower: username });
    const ok = user && (await bcrypt.compare(password, user.passwordHash));
    if (!ok) return res.status(401).json({ error: 'Wrong username or password' });
    res.json({ token: sign(user), username: user.username });
  } catch (e) { console.error('login', e.message); res.status(500).json({ error: 'Server error' }); }
});

// Accepts a batch so scores earned offline can be uploaded together. Duplicates (same clientId) are ignored.
app.post('/api/scores', auth, async (req, res) => {
  try {
    const list = Array.isArray(req.body && req.body.scores) ? req.body.scores.slice(0, 50) : [];
    console.log('scores in', req.user.username, JSON.stringify(list));
    const results = [];
    let saved = 0;
    for (const s of list) {
      const id = String((s && s.id) || '').slice(0, 64);
      const score = Math.floor(Number(s && s.score)), seconds = Math.floor(Number(s && s.seconds) || 0), kills = Math.floor(Number(s && s.kills) || 0);
      if (!id || !Number.isFinite(score) || score < 0 || score > 5000000 || seconds < 0 || seconds > 86400 || kills < 0 || kills > 100000) {
        console.warn('score rejected', JSON.stringify(s)); results.push({ id, status: 'rejected' }); continue;
      }
      try {
        const doc = await Score.create({ user: req.user.id, username: req.user.username, clientId: id, score, seconds, kills });
        saved++; results.push({ id, status: 'saved', _id: String(doc._id) });
      } catch (e) {
        if (e && e.code === 11000) results.push({ id, status: 'duplicate' });
        else { console.error('score create', e.message); results.push({ id, status: 'error', error: safe(e.message) }); }
      }
    }
    const total = await Score.countDocuments({ user: req.user.id });
    const bestDoc = await Score.findOne({ user: req.user.id }).sort({ score: -1 }).lean();
    const failed = results.some((r) => r.status === 'error');
    console.log('scores result', JSON.stringify(results), 'total', total);
    res.status(failed ? 500 : 200).json({ ok: !failed, saved, total, best: bestDoc ? bestDoc.score : 0, results,
      accepted: results.filter((r) => r.status !== 'error').map((r) => r.id), db: DB_NAME, v: API_VERSION });
  } catch (e) { console.error('scores', e.message); res.status(500).json({ error: 'Server error: ' + safe(e.message) }); }
});

// Public truth-teller: open https://<api>/api/stats in a browser to see exactly what is stored (and which database/collections).
app.get('/api/stats', async (req, res) => {
  try {
    const [users, scores, last, top] = await Promise.all([
      User.countDocuments({}), Score.countDocuments({}),
      Score.find({}).sort({ createdAt: -1 }).limit(5).lean(), Score.find({}).sort({ score: -1 }).limit(5).lean(),
    ]);
    const fmt = (r) => ({ username: r.username, score: r.score, seconds: r.seconds, at: r.createdAt });
    res.json({ v: API_VERSION, db: DB_NAME, collections: { users: 'rr_users', scores: 'rr_scores' }, users, scores, latest: last.map(fmt), top: top.map(fmt) });
  } catch (e) { res.status(500).json({ error: safe(e.message) }); }
});

// The signed-in player's own stored scores (for debugging / "did my run arrive?").
app.get('/api/scores/mine', auth, async (req, res) => {
  try {
    const rows = await Score.find({ user: req.user.id }).sort({ createdAt: -1 }).limit(10).lean();
    const best = await Score.findOne({ user: req.user.id }).sort({ score: -1 }).lean();
    res.json({ username: req.user.username, best: best ? best.score : 0, recent: rows.map((r) => ({ score: r.score, seconds: r.seconds, at: r.createdAt })) });
  } catch (e) { console.error('mine', e.message); res.status(500).json({ error: 'Server error: ' + safe(e.message) }); }
});

// Best score per player, top N.
app.get('/api/leaderboard', async (req, res) => {
  try {
    const n = Math.min(50, Math.max(1, parseInt(req.query.limit, 10) || 10));
    const rows = await Score.aggregate([
      { $sort: { score: -1, seconds: 1 } },
      { $group: { _id: '$user', username: { $first: '$username' }, score: { $first: '$score' }, seconds: { $first: '$seconds' }, kills: { $first: '$kills' } } },
      { $sort: { score: -1, seconds: 1 } },
      { $limit: n },
    ]);
    // total score = every run a player has ever banked, added up
    const totalRows = await Score.aggregate([
      { $group: { _id: '$user', username: { $first: '$username' }, total: { $sum: '$score' }, runs: { $sum: 1 }, seconds: { $sum: '$seconds' } } },
      { $sort: { total: -1 } },
      { $limit: n },
    ]);
    // the signed-in player's own latest runs, so every finished game visibly shows up even if it is not a new best
    let mine = [];
    let me = { best: 0, total: 0, runs: 0, rankBest: 0, rankTotal: 0, players: 0 };
    const h = req.headers.authorization || '';
    if (h.startsWith('Bearer ')) {
      try {
        const u = jwt.verify(h.slice(7), JWT_SECRET);
        const last = await Score.find({ user: u.id }).sort({ createdAt: -1 }).limit(5).lean();
        mine = last.map((r) => ({ username: r.username, score: r.score, seconds: r.seconds, kills: r.kills }));
        // my standing, even when I am outside the top 10
        const per = await Score.aggregate([{ $group: { _id: '$user', best: { $max: '$score' }, total: { $sum: '$score' }, runs: { $sum: 1 } } }]);
        const m = per.find((p) => String(p._id) === String(u.id));
        if (m) me = { best: m.best, total: m.total, runs: m.runs,
          rankBest: 1 + per.filter((p) => p.best > m.best).length, rankTotal: 1 + per.filter((p) => p.total > m.total).length, players: per.length };
      } catch (e) { /* invalid token: just no personal list */ }
    }
    res.json({ entries: rows.map((r) => ({ username: r.username, score: r.score, seconds: r.seconds, kills: r.kills })), mine, me, totals: totalRows.map((r) => ({ username: r.username, total: r.total, runs: r.runs, seconds: r.seconds })), v: API_VERSION });
  } catch (e) { console.error('leaderboard', e.message); res.status(500).json({ error: 'Server error' }); }
});

// Vercel's Express runtime wants the app itself as the export.
module.exports = app;
module.exports.app = app;
module.exports.User = User;
module.exports.Score = Score;

if (require.main === module) {
  if (!MONGODB_URI) { console.error('MONGODB_URI is required'); process.exit(1); }
  connectDB()
    .then(() => app.listen(PORT, () => console.log('API listening on ' + PORT)))
    .catch((e) => { console.error('MongoDB connection failed:', e.message); process.exit(1); });
}
