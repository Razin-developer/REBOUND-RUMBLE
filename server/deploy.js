// One-command deploy to Vercel:  npm run deploy
// Reads MONGODB_URI + JWT_SECRET from .env, pushes them to Vercel as production env vars, deploys, then writes the
// resulting URL into ../Assets/Scripts/Net.cs (DefaultUrl) so the game talks to the live server.
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const isWin = process.platform === 'win32';
const run = (args, opts = {}) => spawnSync('npx', ['--yes', 'vercel', ...args], { shell: isWin, encoding: 'utf8', stdio: opts.input ? ['pipe', 'inherit', 'inherit'] : (opts.capture ? ['inherit', 'pipe', 'pipe'] : 'inherit'), input: opts.input });

function readEnv() {
  const out = {};
  if (!fs.existsSync('.env')) { console.error('Missing server/.env (copy .env.example and fill it in).'); process.exit(1); }
  for (const line of fs.readFileSync('.env', 'utf8').split(/\r?\n/)) {
    const m = line.match(/^([A-Z_]+)=(.*)$/); if (m) out[m[1]] = m[2];
  }
  return out;
}

const env = readEnv();
for (const k of ['MONGODB_URI', 'JWT_SECRET']) if (!env[k]) { console.error('.env is missing ' + k); process.exit(1); }

console.log('\n[1/4] Checking Vercel login...');
if (run(['whoami'], { capture: true }).status !== 0) {
  console.log('Not logged in - opening Vercel login (follow the browser prompt).');
  if (run(['login']).status !== 0) process.exit(1);
}

console.log('\n[2/4] Linking project...');
if (run(['link', '--yes', '--project', 'rebound-rumble-api']).status !== 0) process.exit(1);

console.log('\n[3/4] Setting environment variables (production)...');
for (const k of ['MONGODB_URI', 'JWT_SECRET']) {
  const r = run(['env', 'add', k, 'production', '--force'], { input: env[k] });
  if (r.status !== 0) { console.error('Could not set ' + k); process.exit(1); }
}

console.log('\n[4/4] Deploying to production...');
const d = run(['deploy', '--prod', '--yes'], { capture: true });
const text = (d.stdout || '') + '\n' + (d.stderr || '');
process.stdout.write(text);
if (d.status !== 0) process.exit(1);
let url = null;
const alias = text.match(/Aliased:\s*(https:\/\/\S+)/);
if (alias) url = alias[1];
if (!url) { const m = text.match(/https:\/\/[a-z0-9-]+\.vercel\.app/gi); if (m) url = m[m.length - 1]; }
if (!url) { console.log('Deployed, but could not detect the URL. Copy it into Assets/Scripts/Net.cs (DefaultUrl).'); process.exit(0); }

const netCs = path.join(__dirname, '..', 'Assets', 'Scripts', 'Net.cs');
if (fs.existsSync(netCs)) {
  const src = fs.readFileSync(netCs, 'utf8');
  const next = src.replace(/(public const string DefaultUrl = ")[^"]*(")/, '$1' + url + '$2');
  if (next !== src) { fs.writeFileSync(netCs, next); console.log('\nUpdated Net.cs DefaultUrl -> ' + url); }
}
console.log('\nDone. Test it:  ' + url + '/api/health   (should show {"ok":true,"db":true})');
console.log('If you get a 401 page: Vercel > Project > Settings > Deployment Protection > turn it off for Production.');
console.log('If db is false/503: Atlas > Network Access > allow 0.0.0.0/0.');
