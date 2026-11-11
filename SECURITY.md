# Security and privacy

## Zero telemetry

The app has no analytics, no crash reports and no usage pings. We never receive your chat logs, transcriptions, screenshots, files, keys or notes, except encrypted Remote traffic we can't read.

Your settings, chats, transcripts, notes and error logs are stored on your PC, in `%LOCALAPPDATA%\Capri` on Windows. Keys you save are encrypted for your Windows account. On Mac and Linux they sit unencrypted in local files that only your account can read. If you pick an Obsidian vault for her notes, they live in that vault instead, and go wherever your vault syncs.

The [code is public](https://github.com/lachydotmcg/capri-desktop), so you can check all of this yourself.

## What the website counts

The website is separate from the app. The Cloudflare worker for its counters and help chat keeps only these:

- **Download and star totals.** Two running totals. Each IP address counts once a day. To tell, the worker stores a SHA-256 hash of your IP, the day, the counter's name and a salt, never the IP itself. Old hashes are deleted the next time someone presses one of those buttons on a later day.
- **Help chat counts.** How many questions and pictures each visitor sent that day, under the same kind of hash, plus a total for each day. Old visitor counts are deleted by later help requests. The daily totals stay.

Your browser also remembers that you pressed the star, in its own local storage.

In the website's **help chat**, what you type, your last few messages and any picture you add go through the worker to Claude or Cloudflare Workers AI, on the project's accounts, to get you an answer. The worker doesn't save them. Your browser keeps that chat in session storage for that tab.

Cloudflare logging is on for the worker. If no model can answer, the worker logs the errors they gave back. Those errors aren't scrubbed, so one could repeat part of what you asked.

GitHub hosts the website and the downloads, and Microsoft hosts the Store copy. Cloudflare runs the counters, help chat and Remote relay. They keep their own logs under their own privacy policies, and so does Anthropic for help chat requests. The site's fonts, scripts and pictures come from the site itself, with no third-party analytics. When you visit, your browser asks GitHub for the latest release and the changelog.

## What the app connects to

| What | Where it goes | When |
| --- | --- | --- |
| Update checks | `api.github.com` | After she starts, then every three hours on Windows and every six on Mac and Linux, or when you check |
| Updates | `github.com` | On Windows a new release downloads by itself. On Mac and Linux you click through to it |
| Free voice and ears packs | GitHub releases | The installer gets the free voice. The app gets any missing free voice or ears pack when she starts |
| Brain, vision, HD voice and wake word packs | GitHub releases and `huggingface.co` | When you set one up or turn it on. If one goes missing while it's still on, she gets it again. Ollama downloads its own models |
| Local brains and HD voice | Addresses on your own PC | When she finds, loads and uses them |
| Cloud brains and voices | Your provider, or the server address you entered | After you connect one: model lists, tests, chats, speech and jobs. Greetings, screen watching and memory summaries can send requests by themselves, depending on your settings |
| Windows online speech | Microsoft, through Windows | During voice chat, when her own ears aren't ready or don't cover your language, if online speech is on in Windows |
| Searches, music and web links | DuckDuckGo, YouTube and the sites you ask for | When you ask for a search, a song or a site, or press a link. Your search words go to that site |
| Discord status | The Discord app on your PC | On by default. She passes her activity text, version, start time and links, and Discord shares your status |
| Capri Remote | `capri-remote.lachydotmcg.workers.dev` on Cloudflare | While Remote is on (Windows only, off by default) |

Anything online sees ordinary connection details, such as your IP address and the address asked for.

## Capri Remote

Your PC and phone's browser connect out to a Cloudflare Worker, with one Durable Object per room. Remote is Windows only and off by default.

- **Pairing.** Scan the QR, check the code matches on both devices, then press Allow on your PC. A QR works once, for up to 10 minutes. A paired phone can also request a one-time link for another browser, which pairs without a new Allow. A phone a link added can't make links itself, and each phone can ask for one a minute.
- **Private traffic.** Messages, her replies, screen frames, call audio, her voice and keys sent to the PC are end-to-end encrypted. We can't read them. The pairing key follows `#` in the link, so it never reaches GitHub Pages or the relay. The relay never gets device or session keys either.
- **What the relay sees.** Room and device ids, timestamps, online phones, and encrypted frame sizes and timing. It also receives PC and device tokens at sign-in and the one-time code at pairing. Cloudflare sees ordinary connection details such as IP addresses.
- **What the relay keeps.** SHA-256 hashes of the PC token, device tokens and current one-time code, device ids and timestamps. A phone's token hash is deleted when the PC revokes it. A room is deleted 30 days after its PC was last seen. Messages aren't stored there.
- **Relay logs.** Cloudflare logging is on for errors only: the relay records an error's stack or text. It never logs frames, and Cloudflare's per-request logs are off.
- **Abuse limits.** The relay counts new connections and pairing changes per IP address over a minute, with Cloudflare's rate limiter, so one address can't run up costs. It doesn't save addresses.
- **On your PC.** The room id, PC token and each phone's device id, key, name and date are saved with DPAPI encryption for your Windows user. The PC doesn't keep phone tokens.
- **In your phone's browser.** IndexedDB keeps the room id, device id, device token and device key. localStorage keeps the PC's name and pairing date, and separately up to 40 chats with no timed expiry. Reopening loads the last 200 messages per chat. Unpairing or removal leaves saved chats. Clear capriai.app site data to erase them.
- **Removing a phone.** Revoke on the PC removes its saved pairing. The relay deletes its token hash and closes its connection when the PC reaches it, or at the PC's next sign-in if offline. The phone clears its pairing when it receives the removal notice, or when the relay rejects the removed phone. Unpair while connected to your PC tells it too; otherwise it only clears this browser's pairing. Revoking a phone also revokes the phones its links added. Remove all phones revokes every phone and starts a fresh room and PC token, so a phone the relay didn't hear about is cut off too.
- **Keys sent to the PC.** They travel end-to-end encrypted and are saved with DPAPI, just like a key typed in her Mainframe. They are never sent back to the phone.
- **Screen view.** Frames go only while a phone watches, end-to-end encrypted. A badge stays on the PC while a phone watches or calls.
- **Phone only.** Your own key is saved in localStorage in that browser on capriai.app. It goes straight to your chosen provider with your chat: Anthropic (`api.anthropic.com`), OpenAI (`api.openai.com`), Google AI (`generativelanguage.googleapis.com`), OpenRouter (`openrouter.ai`) or DeepSeek (`api.deepseek.com`). Saved chat stays in that browser. Those requests use no relay and pass through nothing of ours.
- **Shared site storage.** Capri Remote shares browser storage with the rest of capriai.app. A script on any capriai.app page could read a phone-only key, saved chats and the phone's relay sign-in, but not its encryption key. Those pages run only the site's own scripts, plus YouTube's player script on the home page after you press Play on her song. Remote's own page allows only its own scripts.

## Your brain and voice providers

What you send to OpenAI, Claude, DeepSeek, NVIDIA, Google AI (Gemini), OpenRouter, Fish Audio or ElevenLabs goes straight from your PC to them with your own key, including NVIDIA's free key. A custom OpenAI-compatible provider or server gets what you send it at the address you entered. Those provider requests do not pass through anything of ours.

Google AI requests go to `generativelanguage.googleapis.com/v1beta/openai/`; OpenRouter requests go to `openrouter.ai/api/v1`. A brain key goes only to its provider, and authenticated brain requests do not follow redirects. A custom provider key goes only to the address you entered, and authenticated requests there do not follow redirects. OpenRouter gets `X-Title: Capri`, with no `HTTP-Referer` or user identifier added by Capri. OpenRouter routes the request to the provider serving the selected model, whose privacy policy also applies.

What a provider logs or keeps is up to them, under their own privacy policy.

- **A brain** can receive your chat history, transcriptions, notes, memories, screenshots, files and tool results, whatever the job needs.
- **A voice service** receives the words to speak and your voice settings.
- **ElevenLabs voice chat** also receives your mic audio, remembered context and tool results, and its hosted agent runs on Gemini.
- **Fish Audio's voice creation** uploads the clip you pick.

A local brain, her own ears and her local voices stay on your PC. A custom brain or voice server gets what you send it, wherever you pointed it.

## Windows or your antivirus warned me

Her Windows builds on GitHub and the website aren't code signed, and each new release starts with no reputation, so you may see **Windows protected your PC**. Press **More info**, then **Run anyway**.

The [Microsoft Store copy](https://apps.microsoft.com/detail/9P0KFRB76X0G) (CapriAI) is signed and checked by Microsoft, so it doesn't get this warning. It updates through the Store.

Antivirus apps can flag her too. Taking screenshots, moving the mouse, pressing keys, listening to the mic and starting apps are her job, and they look a lot like what malware does. That usually means a false alarm, but an alert alone can't tell you a file is safe, so check it:

1. Only download her from the [official release page](https://github.com/lachydotmcg/capri-desktop/releases/latest), the [official website](https://capriai.app/) or the [Microsoft Store](https://apps.microsoft.com/detail/9P0KFRB76X0G).
2. In PowerShell, from your download folder, run:

   ```powershell
   Get-FileHash -LiteralPath .\Capri-Setup.exe -Algorithm SHA256
   ```

3. Compare the `Hash` with the `Capri-Setup.exe` line in `SHA256SUMS.txt` on the same release. A match means you have the file we published.

Keep your antivirus on. If it quarantined her and the hash matches, restore that one file.

You can also build her yourself from the public code. [CONTRIBUTING.md](CONTRIBUTING.md) has what you need.

## Reporting a problem

Tell us in the [Discord](https://discord.gg/capridesktop) or open a [GitHub issue](https://github.com/lachydotmcg/capri-desktop/issues). If it's a security hole, open a ticket in the Discord instead, so the details stay private until it's fixed.
