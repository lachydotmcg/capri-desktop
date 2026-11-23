# Cloud voice provider lifetimes

Verified on 23 November 2026. These provider announcements apply to the cloud
voice choices on Windows, macOS and Linux. They do not affect a local voice
server or a voice model run on the user's computer.

| Provider | Current Capri model | Announced lifetime | Follow-up |
|---|---|---|---|
| Fish Audio | `s2.1-pro-free` | Free window advertised through **30 November 2026** | Availability and terms after that date are unconfirmed. Recheck the provider announcement before promising continued free service. |
| OpenAI | `gpt-4o-mini-tts` at `/v1/audio/speech` | Shuts down **6 January 2027**, alongside `tts-1` and `tts-1-hd` | Plan a separate Realtime integration. The announced replacement `gpt-realtime-2.1-mini` requires Realtime and cannot be substituted at the Speech endpoint. |

Sources: [Fish Audio free API announcement](https://fish.audio/blog/s2-1-pro-free-api/)
and [OpenAI text-to-speech guide](https://developers.openai.com/api/docs/guides/text-to-speech).

Capri keeps the user's selected provider and model. This disclosure change does
not add a date cutoff, switch to a paid Fish model, or silently choose another
provider. When cloud synthesis fails on macOS or Linux, her existing reply path
shows and types the original text; the voice menu records the problem. A slow
voice request also has the existing text timeout fallback.

The macOS and Linux voice menus and key setup disclose these dates. Windows'
existing Fish Audio and OpenAI setup descriptions disclose them too. Windows'
provider and failure behavior is unchanged by these descriptions.

Before either deadline, review the official announcement and decide explicitly
whether to update support or keep the warning with graceful failure. Any paid
replacement needs an explicit product decision and user choice. OpenAI Realtime
migration is separate implementation work, including transport, audio handling
and failure tests; a model-name change is insufficient.

Loopback fake-server and menu checks establish request shapes, warnings and
failure handling. They do not establish current provider availability or live
provider acceptance, and no live paid request is required for these checks.
