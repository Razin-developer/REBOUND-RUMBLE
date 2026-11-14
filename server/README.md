# REBOUND RUMBLE API (Vercel)

Express + Mongoose, deployed to Vercel as a serverless function. Username/password only (no e-mail),
unique usernames (case-insensitive), scores + global leaderboard. Secrets live in Vercel env vars only.

## Deploy
    cd server
    npm install
    npm run deploy        # does everything below in one go (login, link, env vars, deploy, updates Net.cs)
    # or manually:
    npx vercel login
    npx vercel link --yes --project rebound-rumble-api
    npx vercel env add MONGODB_URI production      # paste your Atlas connection string
    npx vercel env add JWT_SECRET production       # any long random string
    npx vercel deploy --prod --yes

Publish to GitHub + auto-deploy: `npm run publish` (then set Root Directory = `server` once in the Vercel dashboard).

Atlas > Network Access must allow 0.0.0.0/0 (Vercel functions have no fixed IP).
Put the resulting https URL in `Assets/Scripts/Net.cs` (`DefaultUrl`) before building, or paste it in Settings > ACCOUNT.

## Local
    copy .env.example to .env, fill it in, then `npm start`  (http://localhost:3000)

## Endpoints
- GET  /api/health
- POST /api/register {username,password} -> {token,username}
- POST /api/login    {username,password} -> {token,username}
- POST /api/scores   (Bearer token) {scores:[{id,score,seconds,kills}]}  (id makes offline re-sync idempotent)
- GET  /api/leaderboard?limit=10
