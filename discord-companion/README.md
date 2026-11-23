# Capri's Discord bot

Open **Discord Bot** from the CAPRI section of Windows Mainframe. Set up a Discord application with Server Install, then paste its bot token into the sheet. Node.js 22.12 or newer with npm is required. Capri installs the pinned voice packages on the first connection and saves the token with Windows user protection after Discord accepts it.

Use **Add to server** to install the bot. Its owner can run **/capri join** from a server voice channel or **/capri ask** for a private text reply. During a call, say Capri's name to get a reply. The helper posts a notice in the voice channel before it starts listening and leaves when the owner leaves, runs **/capri leave**, or stops the bot in Mainframe.

Capri uses the selected model and voice. The voice channel has its own short conversation history, which clears on leave. Incoming audio stays in memory and goes through Capri's speech recognizer. Voice replies fall back to text in the voice channel when synthesis fails.

The Windows build embeds the helper and its lockfile. The desktop process starts it with private standard I/O pipes. The helper connects directly to Discord and passes text and received audio to Capri. A dependency and codec check is available locally from this folder with `npm ci --ignore-scripts` followed by `node companion.mjs --check`.
