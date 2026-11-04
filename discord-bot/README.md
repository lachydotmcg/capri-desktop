1. Use Node.js 24 or newer. Open a terminal in this `discord-bot` folder.

2. Open [Capri's existing app in the Discord Developer Portal](https://discord.com/developers/applications/1564889759157850182/bot). It is the app Lachy made for her Discord status. In the Bot tab, set her bot name to Capri and choose Reset Token. Keep the token for your local `.env` file.

3. In the Bot tab, turn on Message Content Intent and save.

4. [Invite Capri to the Capri server](https://discord.com/oauth2/authorize?client_id=1564889759157850182&scope=bot&permissions=274877975552). This uses only the `bot` scope. Permissions `274877975552` give View Channels, Send Messages, Send Messages in Threads and Read Message History. Channel permission overrides must allow these too.

5. Deploy the edited `worker/src/index.js` to the existing `capri-reactions` worker using the project's usual deployment. In the Cloudflare dashboard, open Workers & Pages, then `capri-reactions`, then Settings, Variables and Secrets. Add `DISCORD_RELAY_SECRET` as a Secret, the same way `ANTHROPIC_API_KEY` was set. Use a new random value and save it.

6. Copy `.env.example` to `.env` beside `capri-bot.mjs`. Set `DISCORD_TOKEN` to the bot token. Set `CAPRI_RELAY_SECRET` to the exact same value as the worker's `DISCORD_RELAY_SECRET`. The default `CAPRI_HELP_URL` is already filled in. Leave `CAPRI_CHANNELS` blank for all channels, or enter comma separated channel ids. In Discord, enable Developer Mode and right-click a channel to copy its id. DMs always work.

7. Run `npm install` in this folder.

8. Run `start-capri-bot.cmd`. Keep its window open. It should say `Capri is online.`

9. Try `!capri how do I change her voice?`, mention her user directly, reply to one of her answers, or send her a DM. She can read one PNG, JPEG or WebP attachment under 1 MB. `bot.log` records time, a short user hash and the result. It does not record chat text or tokens.

10. For Windows login startup, run `powershell -NoProfile -ExecutionPolicy Bypass -File .\install-startup.ps1`. This registers the current user's `Capri Discord Bot` task. It starts her hidden at the next login. Close the bot window before then so only one copy runs. To stop login startup, disable that task in Task Scheduler.

She is only online while the PC running her is on. For the hidden startup task, that Windows user must also be logged in.
