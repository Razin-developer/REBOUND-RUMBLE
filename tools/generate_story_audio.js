// REBOUND RUMBLE - generates the story narration + scene ambience with ElevenLabs.
//
//   PowerShell:   $env:ELEVENLABS_API_KEY="your key"; node tools/generate_story_audio.js
//   Git Bash:     ELEVENLABS_API_KEY=your_key node tools/generate_story_audio.js
//
// Needs Node 18+ (you already have it for the server). No packages to install.
// Files are written to Assets/Resources/Audio/story/ and are skipped if they already exist (so re-running costs nothing).
// Options:  --force (regenerate everything)   --vo-only   --amb-only
// Optional env: VOICE_ID (default: George, a warm storyteller voice)   TTS_MODEL (default eleven_multilingual_v2)

const fs = require('fs');
const path = require('path');

const KEY = process.env.ELEVENLABS_API_KEY;
if (!KEY) { console.error('Set ELEVENLABS_API_KEY first (see the top of this file).'); process.exit(1); }
const VOICE = process.env.VOICE_ID || 'JBFqnCBsd6RMkjVDRZzb';          // George - warm, deep British storyteller
const MODEL = process.env.TTS_MODEL || 'eleven_multilingual_v2';
const OUT = path.join(__dirname, '..', 'Assets', 'Resources', 'Audio', 'story');
const force = process.argv.includes('--force');
const voOnly = process.argv.includes('--vo-only');
const ambOnly = process.argv.includes('--amb-only');
fs.mkdirSync(OUT, { recursive: true });

// narration: [shot, line, text]  (must match the lines in Cinematic.cs)
const VO = [
  [0, 0, 'Two thousand years ago.'],
  [1, 0, 'He found ten rings.'],
  [1, 1, 'And the rings found him.'],
  [2, 0, 'Kingdoms fell like candle flames.'],
  [3, 0, 'Empires burned. He did not.'],
  [4, 0, 'She asked for nothing.'],
  [4, 1, 'That frightened him more than any army.'],
  [5, 0, 'Immortality was a long winter. She was the spring.'],
  [6, 0, 'And the world got small, and wonderful.'],
  [7, 0, 'They did not come to win. They came without end.'],
  [8, 0, 'So Rin picks up the ring.'],
];

// scene ambience: [shot, seconds, prompt]  (looped in the game under the score)
const AMB = [
  [0, 12, 'Cold night wind over a silent mountain ridge, distant low rumble, eerie and empty, cinematic ambience'],
  [1, 15, 'Ten ancient magical rings humming and resonating, deep metallic ringing tones, ethereal shimmering chimes, cinematic'],
  [2, 15, 'Distant ancient battlefield, slow war drums, marching armies, crackling fire, wind, cinematic ambience'],
  [3, 15, 'Burning city crackling fire, collapsing stone, howling wind, mournful low drone, centuries passing'],
  [4, 15, 'Quiet village at dusk, gentle breeze, crickets, soft fire crackle, peaceful and warm ambience'],
  [5, 15, 'Intimate quiet room tone, a slow heartbeat, a dying fire, soft wind, melancholic and tense'],
  [6, 15, 'Peaceful countryside morning, birds singing, gentle breeze, a child laughing in the distance, warm'],
  [7, 15, 'Huge army marching at night, heavy boots, distant monstrous roars, rolling thunder, ominous'],
  [8, 12, 'Tense silence then a ring of power igniting, rising energy swell, metallic ring, heroic cinematic'],
  [9, 8, 'Epic cinematic title hit, deep boom, shimmering metallic rings, long reverb tail, triumphant'],
];

async function post(url, body) {
  const r = await fetch(url, { method: 'POST', headers: { 'xi-api-key': KEY, 'Content-Type': 'application/json', Accept: 'audio/mpeg' }, body: JSON.stringify(body) });
  if (!r.ok) throw new Error('HTTP ' + r.status + ' ' + (await r.text()).slice(0, 300));
  return Buffer.from(await r.arrayBuffer());
}

async function main() {
  let ok = 0, skipped = 0, failed = 0;
  async function job(file, fn) {
    const p = path.join(OUT, file);
    if (!force && fs.existsSync(p) && fs.statSync(p).size > 1000) { skipped++; console.log('skip  ' + file); return; }
    try { fs.writeFileSync(p, await fn()); ok++; console.log('ok    ' + file); }
    catch (e) { failed++; console.error('FAIL  ' + file + '  ' + e.message); }
  }
  if (!ambOnly) for (const [s, l, text] of VO)
    await job(`vo_${s}_${l}.mp3`, () => post(`https://api.elevenlabs.io/v1/text-to-speech/${VOICE}?output_format=mp3_44100_128`, {
      text, model_id: MODEL, voice_settings: { stability: 0.45, similarity_boost: 0.8, style: 0.35, use_speaker_boost: true },
    }));
  if (!voOnly) for (const [s, secs, prompt] of AMB)
    await job(`amb_${s}.mp3`, () => post('https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128', {
      text: prompt, duration_seconds: secs, prompt_influence: 0.5,
    }));
  console.log(`\nDone: ${ok} generated, ${skipped} skipped, ${failed} failed.\nOpen Unity so it imports the new files, then play the story.`);
  if (failed) process.exit(2);
}
main();
