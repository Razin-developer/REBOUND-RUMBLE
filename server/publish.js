// Publish the whole project to a PUBLIC GitHub repo and connect it to the Vercel project:  npm run publish
// Safe by design: refuses to continue if a .env file would be committed.
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const isWin = process.platform === 'win32';
const sh = (cmd, args, opts = {}) => spawnSync(cmd, args, { cwd: opts.cwd || root, shell: isWin, encoding: 'utf8', stdio: opts.capture ? ['inherit', 'pipe', 'pipe'] : 'inherit' });
const out = (cmd, args, cwd) => { const r = sh(cmd, args, { capture: true, cwd }); return { ok: r.status === 0, text: ((r.stdout || '') + (r.stderr || '')).trim() }; };

if (!out('git', ['--version']).ok) { console.error('git is not installed.'); process.exit(1); }
if (!fs.existsSync(path.join(root, '.git'))) { console.log('Creating git repo...'); sh('git', ['init', '-b', 'main']); }

sh('git', ['add', '-A']);
const staged = out('git', ['diff', '--cached', '--name-only']).text.split(/\r?\n/);
const bad = staged.filter((f) => /(^|\/)\.env$/.test(f) || /\.env\.local$/.test(f));
if (bad.length) { console.error('ABORT: secrets would be committed: ' + bad.join(', ')); sh('git', ['reset']); process.exit(1); }

if (staged.filter(Boolean).length) {
  const mf = path.join(root, '.git', 'RR_MSG.txt');
  fs.writeFileSync(mf, 'Update REBOUND RUMBLE game and Vercel API\n');
  sh('git', ['commit', '-F', '"' + mf + '"']);
}
else console.log('Nothing new to commit.');

let remote = out('git', ['remote', 'get-url', 'origin']);
if (!remote.ok) {
  if (out('gh', ['--version']).ok) {
    console.log('Creating public GitHub repo "rebound-rumble" and pushing...');
    if (sh('gh', ['repo', 'create', 'rebound-rumble', '--public', '--source', '.', '--remote', 'origin', '--push']).status !== 0) process.exit(1);
  } else {
    console.log('\nGitHub CLI (gh) not found. Create an EMPTY public repo on github.com, then run:');
    console.log('  git remote add origin https://github.com/<you>/rebound-rumble.git');
    console.log('  npm run publish');
    process.exit(0);
  }
} else if (sh('git', ['push', '-u', 'origin', 'main']).status !== 0) process.exit(1);

remote = out('git', ['remote', 'get-url', 'origin']);
const slug = (remote.text.match(/github\.com[/:]([^/]+\/[^/.\s]+)/) || [])[1];
if (slug && out('gh', ['--version']).ok) {
  console.log('\nMaking ' + slug + ' public...');
  const v = out('gh', ['repo', 'edit', slug, '--visibility', 'public', '--accept-visibility-change-consequences']);
  console.log(v.ok ? 'Repo is public.' : 'Could not change visibility automatically: ' + v.text + '\n(GitHub > repo > Settings > Danger Zone > Change visibility)');
} else console.log('\nMake the repo public: GitHub > repo > Settings > Danger Zone > Change visibility.');
console.log('\nConnecting the repo to Vercel...');
const c = out('npx', ['--yes', 'vercel', 'git', 'connect', remote.text, '--yes'], __dirname);
console.log(c.text);
console.log('\nLast step (dashboard, once): Vercel > rebound-rumble-api > Settings > Build and Deployment > Root Directory = server');
console.log('After that, every push to GitHub redeploys the API automatically.');
