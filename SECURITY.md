# Security and privacy

## Zero telemetry

The app has no analytics, no crash reports and no usage pings. We never receive your chat logs, transcriptions, screenshots, files, keys or notes.

Your settings, chats, transcripts, notes and error logs stay on your PC, in `%LOCALAPPDATA%\Capri` on Windows. Keys you save are encrypted for your Windows account. On Mac and Linux they sit unencrypted in local files that only your account can read. If you pick an Obsidian vault for her notes, they live in that vault instead, and go wherever your vault syncs.

The [code is public](https://github.com/lachydotmcg/capri-desktop), so you can check all of this yourself.

## What the website counts

The website is separate from the app. The Cloudflare worker behind it keeps only these:

- **Download and star totals.** Two running totals. Each IP address counts once a day. To tell, the worker stores a SHA-256 hash of your IP, the day, the counter's name and a salt, never the IP itself. Old hashes are deleted the next time someone presses one of those buttons on a later day.
- **Help chat counts.** How many questions and pictures each visitor sent that day, under the same kind of hash, plus a total for each day. Old visitor counts are deleted by later help requests. The daily totals stay.

Your browser also remembers that you pressed the star, in its own local storage.

The **help chat** on the website is the only place your words reach anything of ours. What you type, your last few messages and any picture you add go through the worker to Claude or Cloudflare Workers AI, on the project's accounts, to get you an answer. The worker doesn't save them. Your browser keeps that chat in session storage for that tab.

Cloudflare logging is on for the worker. If no model can answer, the worker logs the errors they gave back. Those errors aren't scrubbed, so one could repeat part of what you asked.

GitHub hosts the website and the downloads. Cloudflare runs the counters and the help chat. They keep their own logs under their own privacy policies, and so does Anthropic for help chat requests. The site's fonts, scripts and pictures come from the site itself, with no third-party analytics. When you visit, your browser asks GitHub for the latest release and the changelog.

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

Anything online sees ordinary connection details, such as your IP address and the address asked for.

## Your brain and voice providers

What you send to OpenAI, Claude, DeepSeek, NVIDIA, Google AI (Gemini), OpenRouter, Fish Audio or ElevenLabs goes straight from your PC to them with your own key, including NVIDIA's free key. None of it passes through anything of ours, and we never see it.

Google AI requests go to `generativelanguage.googleapis.com/v1beta/openai/`; OpenRouter requests go to `openrouter.ai/api/v1`. A brain key goes only to its provider, and authenticated brain requests do not follow redirects. OpenRouter gets `X-Title: Capri`, with no `HTTP-Referer` or user identifier added by Capri. OpenRouter routes the request to the provider serving the selected model, whose privacy policy also applies.

What a provider logs or keeps is up to them, under their own privacy policy.

- **A brain** can receive your chat history, transcriptions, notes, memories, screenshots, files and tool results, whatever the job needs.
- **A voice service** receives the words to speak and your voice settings.
- **ElevenLabs voice chat** also receives your mic audio, remembered context and tool results, and its hosted agent runs on Gemini.
- **Fish Audio's voice creation** uploads the clip you pick.

A local brain, her own ears and her local voices stay on your PC. A custom brain or voice server gets what you send it, wherever you pointed it.

## Windows or your antivirus warned me

Her Windows builds aren't code signed, and each new release starts with no reputation, so you may see **Windows protected your PC**. Press **More info**, then **Run anyway**.

Antivirus apps can flag her too. Taking screenshots, moving the mouse, pressing keys, listening to the mic and starting apps are her job, and they look a lot like what malware does. That usually means a false alarm, but an alert alone can't tell you a file is safe, so check it:

1. Only download her from the [official release page](https://github.com/lachydotmcg/capri-desktop/releases/latest) or the [official website](https://capriai.app/).
2. In PowerShell, from your download folder, run:

   ```powershell
   Get-FileHash -LiteralPath .\Capri-Setup.exe -Algorithm SHA256
   ```

3. Compare the `Hash` with the `Capri-Setup.exe` line in `SHA256SUMS.txt` on the same release. A match means you have the file we published.

Keep your antivirus on. If it quarantined her and the hash matches, restore that one file.

You can also build her yourself from the public code. [CONTRIBUTING.md](CONTRIBUTING.md) has what you need.

## Reporting a problem

Tell us in the [Discord](https://discord.gg/F4fDHfGtFk) or open a [GitHub issue](https://github.com/lachydotmcg/capri-desktop/issues). If it's a security hole, open a ticket in the Discord instead, so the details stay private until it's fixed.
