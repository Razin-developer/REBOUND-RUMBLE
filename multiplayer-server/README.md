# REBOUND RUMBLE - multiplayer server

WebSocket server for 2-player co-op: rooms (4-letter code), friends, invites, and a relay for game data.
It shares accounts with the main API: same `JWT_SECRET`, same MongoDB (friends live in `rr_friends`).

## Host it on Render (about 10 minutes, free)

1. Push this project to GitHub (the repo already has `render.yaml` at the root).
2. Go to https://dashboard.render.com -> **New +** -> **Blueprint** -> connect GitHub -> pick `REBOUND-RUMBLE`.
3. Render reads `render.yaml` and shows the service `rebound-rumble-multiplayer`. It asks for two values:
   - `JWT_SECRET` - paste the SAME value you set in Vercel for the API (otherwise logins will be refused).
   - `MONGODB_URI` - paste the SAME Atlas connection string you use in Vercel.
4. Click **Apply**. Wait until the deploy says **Live**. Your URL looks like `https://rebound-rumble-multiplayer.onrender.com`.
5. Open that URL in a browser. You should see `{"ok":true,"name":"rebound-rumble-multiplayer",...,"db":"mongodb"}`.
   If it says `"db":"memory"`, `MONGODB_URI` was not set.
6. In Unity open `Assets/Scripts/Coop.cs` and set `ServerUrl` to your URL with `wss://` instead of `https://`:
   `public const string ServerUrl = "wss://YOUR-NAME.onrender.com";` then rebuild.

If the name `rebound-rumble-multiplayer` was taken, Render gives a different URL - use that one.

## Good to know

- Free instances sleep after ~15 minutes without traffic; the first connection after that takes up to a minute (the game shows a message). Upgrade the plan or ping `/` every 10 minutes to keep it awake.
- Atlas: Network Access must allow `0.0.0.0/0` (Render IPs change), same as for Vercel.
- Works from the itch.io web build (secure `wss://`), Windows/Mac builds and phones.

## Run locally

    cd multiplayer-server
    npm install
    JWT_SECRET=test node test.js        # starts the server in memory mode and runs a full smoke test
    JWT_SECRET=... MONGODB_URI=... npm start
