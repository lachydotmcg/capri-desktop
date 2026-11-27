# Capri's Discord bot

Open **Discord Bot** from the CAPRI section of Windows Mainframe. Set up a Discord application with Server Install, then paste its bot token into the sheet. Node.js 22.12 or newer with npm is required. Capri installs the pinned voice packages on the first connection and saves the token with Windows user protection after Discord accepts it.

Use **Add to server** to install the bot. Join a server voice channel and tell Capri **“Join my Discord call”** through her normal desktop chat. She connects with the saved token when needed and greets you in the call. If the bot can see multiple calls, use **/capri join** in the server you want. **/capri ask** gives the owner a private text reply.

By default, only the bot owner can invite Capri. In each server, the bot owner can run **/capri summon-role** and choose one role if they have **Manage Server** there. Members with that role can use **/capri join** to bring Capri to the voice channel they are in. Run **/capri summon-role** without a role to clear it. Changing or clearing the role ends a call started through the old role. The role only grants voice summons. The bot owner still controls **/capri ask** and can use **/capri leave** in the server.

With **Her computer** enabled, the spoken join opens **Capricord**. Its desk icon opens the call window with the channel, avatar circles, and rings that light up as people speak. Use **Wait for name** to quiet Capri, **Talk freely** to let her chime in again, or **Leave** to disconnect her. The owner can also ask her to quiet down or say “Capri, join in again.” The helper posts a notice before it starts listening and leaves when the person who invited her leaves the channel, when her bot is disconnected, or when the owner runs **/capri leave** or stops the bot in Mainframe.

Capri uses the selected model and voice. Discord uses Capri's installed **Her own ears** speech model independently of the desktop mic, with Windows offline speech available as a fallback. To install the local model, open **Mainframe → Settings → Voice → Speech recognition** and choose **Her own ears**. Voice replies fall back to text in the voice channel when synthesis fails.

## Privacy

Capri posts a notice in the voice channel’s text chat, then listens to people in the call. Anyone in the call can choose **Disable my voice** on that notice to stop Capri from hearing them for the rest of the session. Their choice stays in effect if they leave and rejoin the same session. Capricord can still show their avatar and speaking indicator.

Incoming audio stays in memory and is transcribed on the host PC. In open conversation, the host's selected AI provider may receive transcripts, speaker names, and recent call context. In name-only mode, the PC checks for Capri's name before sending a transcript to the model. When a participant disables their voice or leaves, Capri clears their queued speech and resets the local call history. Leaving the session also clears that history. The selected provider's own policy applies to data it has already received. Choose a provider you are authorized to use for this data under [Discord's Developer Terms](https://support-dev.discord.com/hc/en-us/articles/8562894815383-Discord-Developer-Terms-of-Service).

The Windows build embeds the helper and its lockfile. The desktop process starts it with private standard I/O pipes. The helper connects directly to Discord and passes text and received audio to Capri. A dependency and codec check is available locally from this folder with `npm ci --ignore-scripts` followed by `node companion.mjs --check`. Run `node --test summon-role.test.mjs` to check role and call behavior without a Discord connection.
