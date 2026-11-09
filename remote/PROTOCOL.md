# Capri Remote protocol, version 1

This is the contract between three parts, built by three agents:

| Part | Where | What it is |
|---|---|---|
| Relay | `remote/relay/` | Cloudflare Worker with one Durable Object per room |
| Phone | `site/remote/`, served at `https://capriai.app/remote/` | Web app in the phone's browser |
| PC | `app/` | Capri, C# on .NET Framework 4.8 |

Phase 1 covers the relay, pairing and chat. Names for phases 2 to 4 are reserved in [section 8](#8-end-to-end-messages).

Machine-readable test vectors: `remote/test/vectors.json`. Reference code (WebCrypto, runs in browsers and Node): `remote/test/lib/capri-remote.mjs`. Fake PC and phone: `remote/test/lib/fakes.mjs`.

Words: **must** is a rule the tests or the other side depend on. **Should** is strong advice.

## 1. Shape

```text
 phone (browser)                                       PC (Capri)
   |  wss://<relay>/v1/rooms/<room>/phone                |  wss://<relay>/v1/rooms/<room>/pc
   +--------------------->  relay  <---------------------+
                     one Durable Object per room
```

- Both sides connect out. Nothing listens on the PC.
- There are two layers:
  - **Relay layer:** JSON text frames between one client and the relay, for sign-in, pairing control, presence and errors. The relay reads these.
  - **End-to-end layer:** binary envelopes between the PC and one phone, encrypted and authenticated with keys only they hold. The relay forwards them without reading them.
- The relay never sees the pairing key, device keys, session keys or any message text. The only secrets it stores are SHA-256 hashes of the PC token, the device tokens and the current one-time code, next to a few timestamps. It never logs frames.

## 2. Values

Binary values in URLs and JSON are base64url without padding (RFC 4648 section 5). Decoders must reject `=` padding, `+` or `/`, non-canonical trailing bits and wrong lengths. Every random value comes from a cryptographic generator (`RNGCryptoServiceProvider` on the PC, `crypto.getRandomValues` on the phone).

| Value | Bytes | Made by | Kept by | Relay sees |
|---|---|---|---|---|
| room id | 16 | PC, once, when Remote is first turned on | PC | yes, it is the address |
| PC token | 32 | PC, with the room id | PC | at sign-in; stores SHA-256 |
| one-time code | 16 | PC, for each QR or pairing link | PC, until used or closed | at pairing; stores SHA-256 |
| pairing key | 32 | PC, for each QR or pairing link | PC and phone, until pairing ends | never (URL fragment only) |
| pair nonce | 16 | phone, for each scan | nobody | yes (it is only a salt) |
| pending id | 16 | relay, for a phone waiting for Allow | nobody | yes |
| device id | 16 | PC, on Allow | PC and phone | yes |
| device token | 32 | PC, on Allow | phone only (the PC keeps nothing) | at sign-in; stores SHA-256 |
| device key | 32 | PC, on Allow | PC and phone | never |
| phone nonce, PC nonce | 16 each | each side, for each session | nobody | never (sealed in hello) |

A "hash" below is always `base64url(SHA-256(raw bytes))`, where the raw bytes are the decoded value, never its text.

## 3. URLs and origins

- **QR code:** `https://capriai.app/remote/#r=<room id>&c=<one-time code>&k=<pairing key>`
  - Everything after `#` stays in the browser, so the pairing key never reaches GitHub Pages or the relay.
  - The phone must strip the fragment from the address bar as soon as it has read it (`history.replaceState`).
- **Relay:** `wss://<relay host>/v1/rooms/<room id>/pc` for the PC and `.../phone` for phones.
  - Planned host: `capri-remote.lachydotmcg.workers.dev`. It is not deployed yet, so both apps take the host from config.
  - Local: `ws://127.0.0.1:8787` from `npm --prefix remote/relay run dev`.
- **Origins** (checked before the upgrade, refused with HTTP 403):
  - Phone connections must send `Origin: https://capriai.app`, or an `http(s)://localhost`, `127.0.0.1` or `[::1]` origin on any port for development.
  - PC connections must send no `Origin` header. A browser always sends one, so this stops web pages acting as a PC. .NET's `ClientWebSocket` sends none unless told to.
- Other routes: `GET /` returns a plain-text banner (handy for a reachability check). A room URL without an upgrade gets 426, a malformed room id gets 400.

## 4. Relay layer

### 4.1 Frames

- **Text frames** are JSON objects with a string field `t`, at most 16 KiB. Unknown fields are ignored.
- **Binary frames** carry one envelope ([section 6](#6-crypto)):
  - phone to relay and relay to phone: the envelope alone.
  - PC to relay: 16-byte device id, then the envelope. The id says which phone gets it.
  - relay to PC: 16-byte device id, then the envelope. The id says which phone sent it. The relay sets it from the phone's sign-in, so a phone cannot pose as another.
- **Size cap:** a binary frame may be at most 65,536 bytes (64 KiB), counting the 16-byte id on the PC side. `MAX_PC_FRAME_BYTES` raises the PC's cap for phase 3 watch frames (about 256 KiB). Keep each sealed message's plaintext under 60,000 bytes.
- **What each connection may send:**

| Connection | Allowed |
|---|---|
| any, before sign-in | exactly one `auth` (or, for a phone, one `pair`), plus heartbeats |
| PC, signed in | PC controls, heartbeats, binary frames |
| phone, signed in | binary frames, heartbeats |
| phone, waiting for Allow | heartbeats only |

Anything else closes the connection with 4400.

### 4.2 Heartbeat and timeouts

- Every client must send the exact 12-character text `{"t":"ping"}` every 30 seconds. The relay answers `{"t":"pong"}` as a Durable Object auto-response, which does not wake the object or cost anything. If no pong comes within 10 seconds, reconnect.
  - Another spelling (for example with a space) still gets a pong, but it wakes the object, so send the exact string.
- A connection must sign in within 10 seconds of opening.
- A phone waiting for Allow is closed after 3 minutes.
- A signed-in phone that sends no frames for 90 seconds is closed; heartbeats count. While phones are connected, the same rule applies to the PC.
- Reconnect with backoff: 1 second doubling to 60 seconds, plus random jitter. Some close codes say not to reconnect ([section 4.5](#45-errors-and-close-codes)).

### 4.3 Client to relay

| `t` | Sender | Fields | What the relay does |
|---|---|---|---|
| `auth` | PC | `token` (PC token) | First sign-in to an unused room claims it and stores the token hash. Later sign-ins must match it. Replies `ready`. |
| `auth` | phone | `device`, `token` (device token) | Checks the device's token hash. Replies `ready` and tells the PC `joined`. |
| `pair` | phone | `code`, `nonce` (pair nonce), `env` (sealed `pair-request`, base64url) | Checks the code: it must be open, unused, unexpired, and the PC online. Marks it used, replies `waiting`, and sends the PC `pending`. |
| `code` | PC | `hash` (of the one-time code), `ttl` (optional, whole seconds, 1 to 600, default 600) | Opens this code, replacing any earlier one. Replies `ok` with `expires` (relay clock, ms). |
| `code-cancel` | PC | none | Forgets the open code. Send it when the pairing dialog closes. |
| `admit` | PC | `pending`, `device`, `tokenHash`, `env` (sealed `pair-allow`) | Stores `device` with `tokenHash`, sends the phone `pair-reply`, closes it with 4001, replies `ok`. |
| `deny` | PC | `pending`, `env` (sealed `pair-deny`; optional) | Sends the phone `pair-reply` if `env` is given, closes it with 4003, replies `ok`. |
| `revoke` | PC | `device` | Deletes the device's token hash and closes its connection with 4403 at once. Replies `ok` (also for a device it did not know). |
| `kick` | PC | `device` | Closes that phone's connection with 4000 so it reconnects with a fresh session. Replies `ok`. |
| `ping` | anyone | none | Heartbeat. |

### 4.4 Relay to client

| `t` | Receiver | Fields | When |
|---|---|---|---|
| `ready` | PC | `claimed` (true if this sign-in created the room), `devices` (every registered device id), `online` (device ids connected now) | After PC `auth`. |
| `ready` | phone | `pc` (is the PC online) | After phone `auth`. |
| `waiting` | phone | none | The code was accepted and the PC is asking its user. |
| `pending` | PC | `id` (pending id), `nonce`, `env` | A phone presented the open code. |
| `pending-left` | PC | `id`, `reason` (`closed` or `timeout`) | That phone left before the PC answered. Dismiss the prompt. |
| `pair-reply` | phone | `env` | The PC's sealed `pair-allow` or `pair-deny`. The close follows at once. |
| `joined` | PC | `device` | A paired phone signed in. |
| `left` | PC | `device` | That phone's connection ended, for any reason. |
| `presence` | phone | `pc` | The PC came online (`true`) or went away (`false`). |
| `ok` | PC | `op`, plus `device`, `pending` or `expires` | A control worked. |
| `error` | anyone | `code`, `message`, and `op` when it is about one frame or control | See below. |
| `pong` | anyone | none | Heartbeat answer. |

These are the only plaintext messages the relay sends: presence (`ready`, `presence`, `joined`, `left`, `pending`, `pending-left`), control answers (`ok`, `waiting`, `pair-reply`) and errors.

### 4.5 Errors and close codes

A **fatal** error sends `{"t":"error","code":"<code>","message":"<words for a person>"}`, then closes with the close code below and the error code as the close reason. Pairing results send `pair-reply` instead of an error.

An `error` without `op` is always fatal. Act on its `code` at once, stop sending and close your side. Don't wait for the `close` event. The close frame follows straight away, but the final close event can lag: local workerd holds back the TCP teardown of a socket that never sent a frame. Use the close code only when the error frame was missed.

| Close | `code` | Sent when | The client should |
|---|---|---|---|
| 1009 | `too-big` | A frame is over its cap | Not resend it; reconnect |
| 1011 | `internal` | The relay hit a bug | Reconnect with backoff |
| 4000 | `kicked` | The PC asked for a fresh session | Phone: reconnect after about 1 s, then hello |
| 4001 | none | Pairing finished | Phone: reconnect with the device token |
| 4003 | none | The PC said no | Phone: say so; do not retry |
| 4400 | `bad-request` | Malformed or out-of-place frame | Log it (it is a bug); reconnect with backoff |
| 4401 | `bad-token` | Wrong PC token, or wrong device token | PC: make a new room id and token, phones re-pair. Phone: forget the pairing. Do not reconnect. |
| 4403 | `unknown-device`, `revoked` | Device not registered, or just revoked | Phone: forget the pairing. Do not reconnect. |
| 4404 | `no-room` | No PC ever claimed this room, or it was deleted after 30 days away | Phone: forget the pairing. Do not reconnect. |
| 4408 | `timeout` | No sign-in in time, no answer to a pairing, or no frames for 90 s | Reconnect if still wanted |
| 4409 | `replaced` | A newer connection signed in as the same PC or phone | Do not reconnect by itself (another tab or copy took over) |
| 4410 | `code-unknown`, `code-used`, `code-expired` | The QR code cannot be used | Phone: ask for a new QR code |
| 4429 | `busy`, `rate-limited` | Too many connections or frames | Wait at least 10 s |
| 4503 | `pc-offline` | Pairing while the PC is offline, or the PC left mid-pairing | Phone: say the PC is offline |

A **non-fatal** error leaves the connection open and always has `op`:

| Receiver | Error | Meaning |
|---|---|---|
| phone | `{"t":"error","op":"send","code":"pc-offline"}` | A binary frame was dropped because the PC is offline. |
| PC | `{"t":"error","op":"send","code":"no-device","device":...}` | A binary frame was dropped because that phone is not connected. |
| PC | `op` `admit`: `no-pending`, `device-exists`, `too-many-devices`; `op` `deny`: `no-pending` | The control did nothing. After `no-pending` the phone is gone: drop the device you made for it. |

## 5. Pairing

```text
PC                                       relay                                    phone
 | code {hash: H(c), ttl: 600}             |                                         |
 |---------------------------------------->|                                         |
 |<----------------------- ok {op: code}   |                                         |
 | shows QR: #r=room&c=c&k=k               |                (scans the QR, derives   |
 |                                         |                 K_pair and the code)    |
 |                                         |<--- pair {code: c, nonce: n, env: E1}   |  E1 = pair-request, sealed with K_pair
 |                                         | H(c) ok, mark used                      |
 |                                         |------------------------- waiting ------>|  phone shows "318 338"
 |<-- pending {id: p, nonce: n, env: E1}   |                                         |
 | opens E1, asks "Allow this phone?       |                                         |
 |   <name>, code 318 338"                 |                                         |
 | Allow:                                  |                                         |
 |-- admit {pending: p, device: d,         |                                         |
 |    tokenHash: H(token), env: E2} ------>|  stores d -> H(token)                   |  E2 = pair-allow, sealed with K_pair
 |                                         |-------------- pair-reply {env: E2} ---->|
 |                                         |-------------- close 4001 -------------->|
 |<------------------------ ok {op: admit} |                                         |
 | forgets k                               |                    opens E2, stores d, token and key, forgets k
 |                                         |<---- new connection: auth {device: d, token}
```

Rules:

- **One QR, one try.** The PC makes a fresh one-time code and pairing key for every QR it shows. The relay accepts a code once, even if the PC then says no or the phone leaves, and for at most 10 minutes. The PC must also accept only one `pending` per QR, and should take the QR off screen once `pending` arrives.
- **Keys for pairing:** `K_pair = HKDF(pairing key, salt = pair nonce, "capri-remote/1 pair")` ([section 6.2](#62-key-derivation)). `pair-request` is the phone-to-PC envelope (kind 1, seq 0) and `pair-allow` or `pair-deny` is the PC-to-phone envelope (kind 1, seq 0).
- **Confirmation code:** both sides compute it from the pairing key and the pair nonce ([section 6.4](#64-confirmation-code)). The phone shows it while waiting. The PC's prompt shows the same code and the phone's `name`, and tells the user to check that the codes match. Someone who photographed the QR can race the real phone, but then the PC shows a code that is not on the real phone, and the real phone gets `code-used`.
- **On Allow** the PC makes a device id (16 bytes), a device token (32) and a device key (32), all random. It sends `admit` with `tokenHash = H(device token)` and the sealed `pair-allow`. It stores the device id, device key, name and date, using DPAPI (`ProtectedData`, current user). It does not keep the device token. Then it forgets the pairing key.
- **On Deny** the PC sends `deny` with a sealed `pair-deny`. If the `pair-request` did not open, it sends `deny` without `env` and quietly shows a new QR.
- If the PC goes offline while a phone waits, the relay closes the phone with 4503. When the pairing dialog closes, the PC sends `code-cancel`.

### 5.1 A pairing link from a paired phone

An iPhone keeps a home screen app's storage apart from Safari's, so the app needs a pairing of its own. A PC that lists `pair` in `hello-ok.features` makes it a pairing link when a phone that's already paired asks ([section 8.3](#83-pairing-links-feature-pair)), so nobody has to go back to the PC:

```text
paired phone                              relay                              PC
 |== pair.ask {id} (session) ================================================>|
 |                                          |<--- code {hash: H(c), ttl: 600} |
 |<=================================== pair.link {re: id, url, ttl} (session) =|  url: the QR URL, #r=room&c=c&k=k
 copies url; the home screen app pastes it and pairs as from a QR (section 5),
 except that the PC sends admit as soon as pending arrives
```

- **A QR's code.** It's a fresh one-time code and pairing key, one try, at most 10 minutes. The relay sees what it sees for a QR: `code`, then `pending` and `admit`.
- **No Allow.** Only a phone with a live session can ask, so the PC admits whoever presents that code at once, with no prompt and no confirmation code to check. Anyone holding the link in those 10 minutes could pair, so the PC:
  - never shows a phone's link as a QR, which anyone near the screen could scan;
  - lists the new phone under PHONES marked as added by link, and tells its user which phone's link it used, so it can be revoked;
  - cancels the link (`code-cancel`) if the phone that asked is removed first.
- **One code at a time.** The relay holds one open code per room:
  - an ask while a QR is up or a phone waits for Allow gets `error` `busy`;
  - a new ask replaces that phone's earlier link nobody used;
  - Pair a phone on the PC replaces a phone's link;
  - closing the pairing dialog leaves a phone's link alone.
- **The link** is `url` with `&via=phone` added by the phone. It only tells the new phone to say "Pairing" rather than "Allow on your PC" while it waits. Phones ignore parameters they don't know.

## 6. Crypto

.NET Framework 4.8 has no AES-GCM, so envelopes use AES-256-CBC with HMAC-SHA256, encrypt-then-MAC, with keys from HKDF-SHA256. Everything here is checked byte for byte by the vectors in [section 11](#11-test-vectors).

### 6.1 Primitives

- **SHA-256**, **HMAC-SHA256**.
- **HKDF-SHA256**, RFC 5869. WebCrypto has it. The PC hand-rolls it on `HMACSHA256`:
  - `PRK = HMAC(key = salt, data = ikm)`
  - `T(1) = HMAC(PRK, info || 0x01)`, `T(i) = HMAC(PRK, T(i-1) || info || i)`
  - `OKM` is the first `L` bytes of `T(1) || T(2) || ...`
- **AES-256-CBC** with PKCS#7 padding and a fresh random 16-byte IV for every envelope. On the PC: `Aes.Create()`, `Mode = CipherMode.CBC`, `Padding = PaddingMode.PKCS7`, 32-byte key.

### 6.2 Key derivation

| Context | ikm | salt | info (ASCII, no terminator) | L |
|---|---|---|---|---|
| pair | pairing key | pair nonce (16 bytes) | `capri-remote/1 pair` | 128 |
| confirm | pairing key | pair nonce | `capri-remote/1 confirm` | 4 |
| hello | device key | device id (16 bytes) | `capri-remote/1 hello` | 128 |
| session | device key | phone nonce then PC nonce (32 bytes) | `capri-remote/1 session` | 128 |

Each 128-byte output splits, in this order, into four keys, so each direction has its own:

| Bytes | Key |
|---|---|
| 0 to 31 | phone-to-PC AES key |
| 32 to 63 | phone-to-PC HMAC key |
| 64 to 95 | PC-to-phone AES key |
| 96 to 127 | PC-to-phone HMAC key |

### 6.3 Envelope

| Offset | Bytes | Field |
|---|---|---|
| 0 | 1 | version, `0x01` |
| 1 | 1 | kind: `1` pair, `2` hello, `3` session |
| 2 | 1 | direction: `1` phone to PC, `2` PC to phone |
| 3 | 1 | reserved, `0x00` |
| 4 | 4 | sequence number, unsigned, big-endian |
| 8 | 16 | IV |
| 24 | n | AES-256-CBC ciphertext of the message's UTF-8 JSON, n a multiple of 16 and at least 16 |
| 24 + n | 32 | HMAC-SHA256 tag over bytes 0 to 23 + n (header, IV and ciphertext) |

The header is inside the MAC, so nobody can change the kind, direction or sequence number of a frame.

**Sealing:** build the header, pick a random IV, encrypt the plaintext with the direction's AES key, then HMAC header, IV and ciphertext with the direction's HMAC key and append the tag.

**Opening.** Do these checks in this order. If any one fails, drop the frame and end that session ([section 9](#9-rules-for-each-side)). Never show or act on any part of the plaintext, and never tell the other side which check failed.

1. Length is at least 72, and length minus 56 is a multiple of 16.
2. The tag matches, compared in constant time over all 32 bytes. .NET Framework 4.8 has no `FixedTimeEquals`, so OR together the XOR of every byte pair. Check the MAC before decrypting anything.
3. Version is 1, kind and direction are what this context expects, and reserved is 0.
4. The sequence number is exactly the next one expected from that direction.
5. Decrypt, and strip PKCS#7 padding strictly.
6. The plaintext is valid UTF-8 and parses as a JSON object with a string `type`.

**Sequence numbers:** every context (pair, hello, one session) keeps one counter per direction. It starts at 0 and goes up by 1 for each envelope sent. Pair and hello contexts carry exactly one envelope each way, so their sequence number is always 0. A session ends (reconnect, new hello) long before a counter would pass 2^32 - 1. The receiver accepts only the exact next number, so a replayed, reordered or dropped frame ends the session instead of slipping through.

### 6.4 Confirmation code

`x` = the 4 bytes of `HKDF(pairing key, pair nonce, "capri-remote/1 confirm", 4)`, read as a big-endian unsigned integer. The code is `x mod 1000000`, written as six digits with leading zeros and shown as `ddd ddd` (for example `318 338`).

## 7. Sessions

Every phone connection starts a new session, and so does every `presence {pc: true}`:

```text
phone                                   relay                                   PC
 | auth {device: d, token}                |                                        |
 |--------------------------------------->|                                        |
 |<------------------------ ready {pc}    |--- joined {device: d} ---------------->|
 | (if pc is true)                        |                                        |
 |== hello: kind 2, dir 1, seq 0, K_hello ==============================(d)=======>|
 |   {type: hello, v: 1, nonce: Np, ...}  |                                        |
 |<=(binary)== hello-ok: kind 2, dir 2, seq 0, K_hello ============================|
 |   {type: hello-ok, v: 1, nonce: Nc, echo: Np, features: [chat], ...}            |
 | both now derive K_session = HKDF(device key, Np || Nc, "capri-remote/1 session")|
 |== chat.send (kind 3, dir 1, seq 0) ============================================>|
 |<=========================================== chat.typing (kind 3, dir 2, seq 0) =|
```

- `K_hello = HKDF(device key, device id, "capri-remote/1 hello")`. It is the same for every session of that device. It is only used for one `hello` and one `hello-ok` per session, and the fresh nonces inside them make every session's keys new.
- The phone makes a new 16-byte phone nonce for every hello. The PC makes a new 16-byte PC nonce for every hello-ok and must put the phone's nonce in `echo`. The phone must check `echo` and drop a hello-ok that does not match. This stops an old hello-ok being replayed.
- A new `hello` from a device always replaces that device's session: new keys, and both counters back to 0. The PC may receive a replayed old hello (the relay could replay one). That only breaks the current session, because the replayer cannot derive the new keys.
- The phone must wait for `ready` before sending binary frames, send hello only when the PC is online, and wait up to 10 seconds for `hello-ok` before reconnecting.
- If `v` is not supported, reply with a sealed `error` `{code: "version", v: <your version>}` in the hello context instead of `hello-ok`.

## 8. End-to-end messages

Each envelope holds one UTF-8 JSON object with a string `type`. Ignore unknown types and unknown fields. Ids are 1 to 64 characters from `A-Z a-z 0-9 _ -`; 16 random bytes in base64url make a good one.

| `type` | Sender | Context | Fields |
|---|---|---|---|
| `pair-request` | phone | pair | `v` (1), `name` (phone's name, at most 64 characters), `app` (e.g. `remote-web/0.1.0`) |
| `pair-allow` | PC | pair | `v`, `device`, `token`, `key` (device id, device token, device key), `name` (PC's name), `app` (e.g. `capri/0.8.7`) |
| `pair-deny` | PC | pair | `v`, `reason`: `denied`, `busy` or `bad-request` |
| `hello` | phone | hello | `v`, `nonce` (phone nonce), `name`, `app` |
| `hello-ok` | PC | hello | `v`, `nonce` (PC nonce), `echo` (the phone nonce), `name`, `app`, `features` (phase 1: `["chat"]`) |
| `chat.send` | phone | session | `id`, `text` (1 to 8,000 characters) |
| `chat.reply` | PC | session | `id`, `text`, `done`, `re` (optional) |
| `chat.tool` | PC | session | `id`, `name`, `summary`, `state`, `re` (optional) |
| `chat.typing` | either | session | `on`, `re` (optional) |
| `revoke` | either | session | none |
| `ping` | either | session | `n` (an integer) |
| `pong` | either | session | `n` (the ping's `n`) |
| `error` | either | hello or session | `code`, `message` (optional), `re` (optional), `v` (with `code: "version"`) |

- **`chat.send`:** the phone's user said something to Capri.
  - As a receipt, the PC answers at once with `chat.typing {on: true, re: <id>}`.
  - The PC ignores an `id` it saw in the last 10 minutes, because a phone may resend after a reconnect.
- **`chat.reply`:** one reply, streamed in parts.
  - Append each part's `text` in order. `done: true` marks the last part, whose `text` may be empty.
  - `id` names the reply; the PC makes it. `re` is the `chat.send` id it answers, and is left out for replies nobody on the phone asked for, such as an answer to the PC's microphone.
  - Batch parts so each phone gets at most about 10 frames a second.
- **`chat.tool`:** a tool Capri is using, shown with the reply.
  - Updates with the same `id` replace each other.
  - `state` is `running`, `done` or `failed`.
  - `summary` is at most 200 characters of plain words, like "Searching the web for train times". It never contains tool arguments that could hold secrets.
- **`chat.typing`:**
  - From the PC: Capri is thinking or typing.
  - From the phone: its user is typing. The PC may ignore this.
  - The phone clears the indicator when a `chat.reply` part arrives, or 20 seconds after the last `on: true`.
- **`revoke`:**
  - From the PC: this phone was removed. The phone deletes its stored pairing, and the relay closes it with 4403 next.
  - From the phone: forget me. The PC deletes the device and sends the relay `revoke`.
- **`ping`/`pong`:** checks that Capri itself is answering, not just the relay. Each one wakes the relay, so send one only when it matters, such as when the page comes back from the background. Relay heartbeats are free.
- **`error` codes:** `version`, `busy`, `bad-message` and `not-allowed` (a feature is turned off). Show unknown codes as a general error.

### 8.1 Her brains and keys (feature `brains`)

A PC that lists `brains` in `hello-ok.features` lets a paired phone see which of her brains she uses, pick another and send a brain's API key. A phone must not send these to a PC that doesn't list it. Her brains are the three rungs under BRAIN in her Mainframe:

| `id` | Her Mainframe | Takes a key |
|---|---|---|
| `normal` | NORMAL: free, a model on the PC | no |
| `medium` | MEDIUM INTELLIGENCE: free keys (NVIDIA, Google AI Studio, OpenRouter) | yes |
| `max` | MAX INTELLIGENCE: any key she knows, or their own plan | yes |

| `type` | Sender | Context | Fields |
|---|---|---|---|
| `brain.get` | phone | session | none |
| `brain.state` | PC | session | `use` (the brain she uses), `name` and `note` (her brain and the note under it, as her Mainframe shows them, at most 64 and 200 characters), `brains` (one `{id, key}` for each brain; `key` is true when one is saved) |
| `brain.use` | phone | session | `brain` (an `id`) |
| `brain.key` | phone | session | `id`, `brain` (`medium` or `max`), `key` (as pasted, 1 to 400 characters, no control characters) |
| `brain.note` | PC | session | `re` (the `brain.key` id), `state` (`checking`, `saved` or `failed`), `text` (her Mainframe's note, at most 200 characters) |

- **Keys are write-only.** The PC never sends a key back, whole or in part (not even its last four characters): `brain.state` says only which brains have one. Notes are her Mainframe's own words, scrubbed like her chat replies.
- **A phone's key is a key typed in her Mainframe.** The PC runs it through the same checks, saves it the same way (DPAPI) and answers with the notes her Mainframe shows: `checking` at once, then `saved` or `failed`. A paid key sent for `medium` fails with "That's a paid key: it goes under MAX INTELLIGENCE." and nothing is saved, so the phone can send it again for `max`.
- **One key at a time.** A `brain.key` while another is still being checked fails with "Still checking the last key."
- **Dropped keys.** A key the PC drops because her brain changed there first (a key typed on the PC, a rung clicked) fails with "Her brain changed on your PC before the key was checked."; one with no answer in 90 seconds fails with "Couldn't check the key in time. Try again." Plans (their own Claude or ChatGPT plan) are picked on the PC and fail with "Plans are picked on your PC."
- **The PC tells its user.** When a phone's key is saved, the PC shows a short notice naming the phone and the brain.
- **`brain.use`** picks a rung, as clicking it in her Mainframe does, and the phone hears the new `brain.state`.
- **State.** The PC answers `brain.get` with `brain.state`, and sends it again to that phone whenever her brains change while its session lasts.
- **Malformed asks** (an unknown `brain`, a missing, empty or too long `key`, a bad `id`) get `error` `bad-message`, with `re` when the id was good.
- **Phone-only mode** (`site/remote/native.js`, Capri on the phone with no PC) is not part of this protocol: its key stays on the phone and goes only to the provider the user picked, never through the relay.

**Reserved for later phases.** Do not send these in phase 1, and ignore them if received:

| Phase | Names |
|---|---|
| 2, settings | `settings.schema` (PC), `settings.set` (phone) |
| 3, watch | `watch.start` (phone), `watch.stop` (phone), `watch.frame` (PC) |
| 4, jobs | `job.state` (PC), `job.step` (PC), `job.recent` (PC), `job.list` (phone), `job.stop` (phone), `job.answer` (phone) |

`hello-ok.features` lists the features the PC supports. Screen view and jobs are defined below.

### 8.2 Screen view and call

`hello-ok.features` lists `watch` when the PC can show its screen, `call` when it takes calls, and `hush` with `call` when a phone's `call.hush` stops her answer on the PC. A phone sends these only when its session's `hello-ok` listed the feature.

| `type` | Sender | Fields |
|---|---|---|
| `watch.start` | phone | `monitor` (`auto`, or an id from `watch.monitors`), `quality` (`saver`, `balanced` or `sharp`), `width` and `height` (the view in device pixels, 64 to 8192) |
| `watch.stop` | phone | none |
| `watch.ack` | phone | `n` (the frame just drawn) |
| `watch.state` | PC | `on`, and `reason` with `on: false` (`not-allowed` or `no-screen`) |
| `watch.monitors` | PC | `list` (each `id`, `name`, `w`, `h`, `primary`), `monitor` (the id shown), `auto`, `missing` (true when the picked monitor is gone) |
| `watch.frame` | PC | `n`, `part`, `parts`, `data` (base64url JPEG bytes); part 0 also has `w`, `h`, `monitor`, `bytes`, `cx`, `cy` |
| `watch.cursor` | PC | `cx`, `cy` |
| `call.start` | phone | `hush` (true from a phone that sends `call.hush`) |
| `call.stop` | phone | none |
| `call.state` | PC | `on`; `voice` and `ears` (her voice and her ears are on) with `on: true`; `reason` with `on: false` (`not-allowed` or `ended`) |
| `call.audio` | phone | `id` (the line), `n`, `rate` (16000), `pcm`, `end` (on the last part) |
| `call.cancel` | phone | `id` |
| `call.heard` | PC | `id`, `text` (what her ears heard: empty when nothing) |
| `call.voice` | PC | `n`, `rate` (24000 or less), `pcm` |
| `call.hush` | PC | none |
| `call.hush` | phone | `re` (the message her answer is for: a line id, or a `chat.send` id), only to a PC that lists `hush` |

- **Watching:**
  - The PC captures only while a phone watches. A second `watch.start` changes the monitor, quality or size.
  - The phone sends `watch.stop` when the view closes or the page goes to the background, and asks again in each new session. The PC also ends a phone's watch when it leaves, is removed, or says hello again.
  - While any phone watches or calls, the PC shows a badge on the monitor shown. The badge is left out of the frames.
- **Frames:**
  - JPEG, as wide as the phone's view but at most 640, 960 or 1280 pixels (saver, balanced, sharp) and never wider than the monitor, at most 2, 3 or 4 a second.
  - A screen that doesn't change sends nothing. The mouse moving over it sends `watch.cursor`.
  - `cx` and `cy` are the mouse in ten-thousandths of the frame's width and height, or -1 when it's on another monitor.
- **Parts:** each frame is cut into parts of 44,000 JPEG bytes (58,667 base64url characters). Each sealed part stays under the 60,000-byte message limit, and each PC frame under the relay's 65,536 (section 4.1). Parts come in order, and part 0 starts a frame. A phone drops a frame with a part missing.
- **Flow control:** the phone acks each frame once it's drawn. The PC stays at most two frames ahead of the acks. With no ack for 4 seconds it sends one more, and after 15 seconds it waits for an ack or a new `watch.start`.
- **Auto:** shows the monitor her pointer is on, else the one she's been asked to watch, else the one with the focused window. It moves once the new one has held for a second.
- **Calls:** hands-free. The phone keeps its mic open for the whole call and sends each thing said as one line.
  - A line starts when talking starts, with about 300 ms from just before, and ends after about 0.8 seconds of quiet. Clicks and blips too short to be words send nothing.
  - No line starts while the phone is muted. While her voice plays, one starts only when he talks over her: his voice has to stay over a bar set by how loud she's been in the last 0.4 seconds (15 dB under it at least, so what the phone's echo cancelling leaves of her stays under) for about 300 ms, and nothing counts in the first 0.3 seconds after her voice starts. That line stops her: the phone drops what hasn't played and sends `call.hush`. With talking over her turned off on the phone (or no echo cancelling), no line starts while her voice plays, and for about a second after she stops it takes louder talking. Muting or hanging up mid-line sends `call.cancel`.
  - The phone sends a line as `call.audio` parts from `n: 0`: mono 16-bit little-endian PCM at 16 kHz, 320 ms a part, with `end: true` on the last. A line is at most 30 seconds.
  - Errors carry the line id as `re`: `too-long`, `bad-message` (a part out of order or at another rate), `not-allowed` (no call), `busy` (her ears couldn't run).
  - The PC's own ears hear the line, the PC answers `call.heard`, and the words go to her as if said at the PC. Her answer comes back as `chat.reply` with `re` set to the line id.
  - `call.cancel` drops a line that's still coming in.
- **Her voice:** while she speaks, `call.voice` parts of a quarter second: mono 16-bit little-endian PCM at her speaker's rate, halved until it's 24 kHz or less. They're numbered from 0 in each call. `call.hush` means she stopped mid-line: drop whatever hasn't played.
  - A PC that lists `hush` voices its answer to a line or message from a phone whose `call.start` had `hush: true` a sentence at a time: each sentence's parts go as soon as it's voiced, while the next is made, so a pause can come between them. Other phones get each answer's parts at once.
- **Interrupt:** the phone's `call.hush` stops her answer to `re` on the PC at once, with whatever of it is still to be voiced or sent, and the PC answers with its own `call.hush`. If she's saying something else by then, nothing changes and nothing comes back. A `call.hush` without a good `re` gets `bad-message`. Either way the phone drops her voice until it pauses for a second or the PC's `call.hush` comes. A PC that doesn't list `hush` never gets `call.hush`: the phone only drops her voice itself.
- **What it carries**, measured through the relay on a drawn 1920x1080 desktop with a 1280x720 view (sealed messages, so base64url and envelopes included; real screens vary):

  | Picture | Typing | Scrolling | Video |
  |---|---|---|---|
  | saver, 2 a second | 40 KB/s | 40 KB/s | 43 KB/s |
  | balanced, 3 a second | 148 KB/s | 148 KB/s | 162 KB/s |
  | sharp, 4 a second | 340 KB/s | 342 KB/s | 379 KB/s |

  A screen that doesn't change costs nothing. Talking sends about 42 KB/s and her voice about 63 KB/s, only while each is going. The phone sends at most about 4 acks and 3 audio parts a second, under the relay's 10 a second.

### 8.3 Pairing links (feature `pair`)

A PC that lists `pair` in `hello-ok.features` makes a pairing link for a paired phone that asks, for another browser on that phone such as its home screen app ([section 5.1](#51-a-pairing-link-from-a-paired-phone)). A phone must not ask a PC that doesn't list it.

| `type` | Sender | Context | Fields |
|---|---|---|---|
| `pair.ask` | phone | session | `id` |
| `pair.link` | PC | session | `re` (the `pair.ask` id), `url` (the QR URL of a fresh one-time code, section 3), `ttl` (seconds it lasts, 600) |

- **Answers.** `pair.link`, or `error` with `re`: `busy` when a QR is up or a phone waits for Allow on the PC. An ask with a bad `id` gets `error` `bad-message`.
- **The phone** takes only the `#` fragment from `url` (`r`, `c` and `k`) and puts it on its own address, so a link always opens the page that made it. It keeps the link in memory and hands it on through the clipboard; the page that pastes it clears the clipboard.

### 8.4 Jobs (feature `jobs`)

A PC that lists `jobs` in `hello-ok.features` lets every paired phone follow, stop and answer its PC jobs, whoever started them. A phone sends these messages only in a session that offers `jobs`. There is no new PC switch and no notification support in this phase. A phone starts a job by talking to Capri in chat or a call.

| `type` | Sender | Fields |
|---|---|---|
| `job.state` | PC | `id`, `task` (her plain words, at most 200 characters), `state` (`running`, `asking`, `done`, `failed` or `stopped`), `steps` (count so far), `started` (Unix ms), `ended` (Unix ms, only once over), `ask` (her question with `asking`, at most 200 characters), `re` (the phone message that started it, when one did) |
| `job.step` | PC | `id` (the job), `n` (from 1), `tool`, `label` (at most 120 characters), `done`, `failed` |
| `job.list` | phone | none; the PC answers with `job.recent` |
| `job.recent` | PC | `list`: up to 10 most recent jobs, newest first, each `id`, `task`, `state`, `started`, `ended` (once over), `steps`, `last` (its last step's label, or empty) |
| `job.stop` | phone | `id`; stops that job as Esc does, and the PC answers with `job.state` |
| `job.answer` | phone | `id`, `yes` (true or false); answers her held question, only while that job is `asking` |

- **Updates.** Every paired phone with a live session gets each job's state and steps. Start, status changes, held questions and the end send `job.state`. Jobs are kept only in PC memory. Recent includes the current job.
- **Steps.** A step's running and finished updates share its `n` and replace each other. `steps` counts steps, not updates. A phone that says hello while a job runs or asks gets its state followed by its last 30 steps, with their original numbers.
- **Batching.** Job messages are queued at about five frames a second per phone. Pending updates for the same step can become its newest update, and a phone that falls behind keeps only its newest 30 pending steps. State and command answers share turns with steps, so a question or stop answer is not held behind a long step list. A new hello clears that phone's old queue and replays the current job. Session messages never carry over to another session.
- **Privacy.** Labels never carry what she typed, including the keys of a typed secret. A type step says "Typing some text" or "Typed some text" and may name where if the label safely says where. Clicked and opened labels may name the button or app. Job titles, questions and Recent follow the same rule. Use short summaries when the original words include typed content. Never send raw tool arguments, typed text, key sequences or failure results as job labels.
- **Answers.** Allow runs the held action once through the same approval path as the PC. Deny leaves it alone. Only the first answer counts. Her reply then goes to the phone that answered as a `chat.reply` with no `re`, as after a typed yes or no. Stop ends it through the PC's stop path, including a held action. A held question stays current until answered or dropped on the PC.
- **Bad commands.** `job.stop` or `job.answer` for anything other than the current job, or an answer when it is not `asking`, gets `error` with `code: "bad-message"` and `re` set to the command's job `id` when valid. A missing or malformed id, or a non-boolean `yes`, also gets `bad-message`.
- **Unknown values.** Ignore unknown types and fields, as in section 8.

## 9. Rules for each side

### PC (`app/`)

- **Off by default.** Connect only while Remote is on. Show the badge while `ready.online` plus `joined` minus `left` is not empty.
- **Crypto:** match [section 6](#6-crypto) byte for byte, and prove it with a unit test that reads `remote/test/vectors.json`. Every `envelopes[]` entry must seal to exactly `envelope` with the given IV and open back to `plaintextHex`. Every `rejects[]` entry must fail to open.
- **Opening frames:** if a device's frame fails to open, drop that device's session and send `kick` for it.
- **One send at a time:** `ClientWebSocket` allows a single `SendAsync` at a time. Take the next sequence number and send under the same lock, so frames leave in sequence order.
- **Sessions are per device.** End one on `left`, and replace it on every new `hello`. After reconnecting to the relay all sessions are gone; wait for each phone's hello.
- **Keep the relay's device list in step with yours.** On every `ready`:
  - send `revoke` for any device id in `devices` you don't know;
  - mark your own devices that are missing from `devices` as removed.
  - `claimed: true` on a room you have used before means the relay deleted it after 30 days away. Every phone must pair again.
- **Treat phone names as untrusted.** `name` comes from whoever holds the QR. Strip control and bidi characters, trim to 64 characters, and fall back to "Unnamed phone".
- **Keep secrets on the PC.** Never put API keys, tokens or anything from the SECURITY page in any message. Store the room id, PC token and device keys with DPAPI.
- **Connecting:**
  - Send no `Origin` header.
  - Send the exact heartbeat every 30 seconds.
  - Reconnect with backoff. After 4401 make a new room. After 4409, stop and tell the user.

### Phone (`site/remote/`)

- **Secure context:** WebCrypto needs a secure context, so the app must run on `https://capriai.app` or `localhost`.
- **The QR fragment:** read it, then strip it with `history.replaceState`. Keep the pairing key in memory only, and drop it after `pair-reply`.
- **While waiting:** show the confirmation code and "Check your PC shows the same code".
- **After `pair-allow`:** store the room id, device id, device token and device key in IndexedDB. The device key can be stored as a non-extractable HKDF `CryptoKey`.
- **Starting sessions:** wait for `ready`. Send hello when `pc` is true, and again after every `presence {pc: true}`.
- **Heartbeat and visibility:** send the exact heartbeat every 30 seconds. When the page becomes visible again, check the socket and reconnect if needed.
- **Errors and close codes:** an `error` without `op` ends the connection. Follow the table in [section 4.5](#45-errors-and-close-codes). Forget the pairing after 4401, 4403 or 4404.

## 10. Relay limits and costs

Limits and upgrade bindings live in `remote/relay/wrangler.jsonc`:

| Variable | Default | Meaning |
|---|---|---|
| `PHONE_ORIGINS` | `https://capriai.app` | Allowed phone origins, comma-separated |
| `ALLOW_LOCAL_ORIGINS` | `false` | Also allow localhost, 127.0.0.1 and [::1] origins |
| `CLAIMS_OPEN` | `true` | Set to `false` to refuse new rooms with 4429 `busy`; claimed rooms keep working |
| `PC_UPGRADES` | 120 per 60 s | PC upgrades per IP, checked before the room wakes |
| `PHONE_UPGRADES` | 1200 per 60 s | Phone upgrades per IP, separate from PCs |
| `WRITE_BURST`, `WRITE_REFILL_MS` | 32, 3000 | PC control writes per room in a burst; one token refills every 3 seconds |
| `WRITES` | 120 per 60 s | Writes a client can cause, per address across every room |
| `MAX_FRAME_BYTES` | 65536 | Biggest binary frame from a phone |
| `MAX_PC_FRAME_BYTES` | 65536 | Biggest binary frame from the PC (about 262144 for phase 3) |
| `CODE_TTL_SECONDS` | 600 | Longest life of a one-time code |
| `AUTH_TIMEOUT_MS` | 10000 | Time to sign in |
| `PENDING_TIMEOUT_MS` | 180000 | Time a pairing phone waits for Allow |
| `IDLE_TIMEOUT_MS` | 90000 | Silence allowed on a signed-in connection |
| `ROOM_TTL_SECONDS` | 2592000 | A room whose PC stays away this long (30 days) is deleted |
| `PHONE_RATE`, `PHONE_BURST` | 10, 60 | Frames per second, and burst, per phone connection |
| `PC_RATE`, `PC_BURST` | 100, 300 | The same for the PC connection |
| `MAX_DEVICES` | 16 | Paired phones per room |
| `MAX_PHONES_ONLINE` | 8 | Phones connected at once |

Fixed in code:

- Text frames are at most 16 KiB.
- At most 8 connections may be signing in at once per room. A ninth pushes out the oldest, so sockets held open without signing in cannot lock out a real client, which signs in within one round trip.
- At most 2 phones may wait for Allow at once.
- Address limits count an IPv6 address by its /64, and an IPv4 address written as IPv6 as that IPv4 address. A missing limit binding skips its check.

Why it costs next to nothing at rest:

- **Hibernation:** a room with only the PC connected sleeps. Heartbeats are auto-responses, which never wake it.
- **Storage writes only on change:**
  - claiming a room;
  - opening, using or cancelling a code;
  - adding or removing a device;
  - the PC's last-seen time, at most daily on sign-in or disconnect, or when a connected room reaches its clean-up deadline;
  - moving the one alarm when an earlier deadline is needed.
- **No writes for forwarded frames.** They go straight from memory.
- **Alarms only when needed:** sign-in deadlines in claimed rooms, phones waiting for Allow, idle checks while a phone is connected, and one clean-up alarm 30 days out. An unclaimed room checks sign-in time on its next frame and writes no alarm.

Abuse limits:

- Excess upgrades get HTTP 429 before the room wakes.
- `code`, `code-cancel` with an open code, `admit` and `revoke` of an existing device spend the room's write budget. An empty budget closes the PC with 4429 `rate-limited`. Reconnecting does not refill it; hibernation or eviction resets it.
- Each of those writes, and each new claim, also counts against `WRITES` for the client's address, so one address can't spend a full budget in many rooms. Connections with no `CF-Connecting-IP`, or from loopback in local development, skip that count.
- With claims open, anyone can still claim a new room id. A claim stores the room and arms its clean-up alarm. These limits do not cap total use across IPs. `CLAIMS_OPEN=false` pauses new claims.

## 11. Test vectors

All inputs are test values made of sequential bytes. Byte strings are hex and URL values are base64url. Keys are named `<context>.<direction>`, so `session.phoneToPc` means the phone-to-PC AES and HMAC keys of the session context.

<!-- vectors:begin (generated by `node remote/test/make-vectors.mjs --write`, do not edit by hand) -->

**Inputs (hex, then base64url)**

```text
room         101112131415161718191a1b1c1d1e1f  EBESExQVFhcYGRobHB0eHw
pcToken      202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f  ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8
code         404142434445464748494a4b4c4d4e4f  QEFCQ0RFRkdISUpLTE1OTw
pairingKey   505152535455565758595a5b5c5d5e5f606162636465666768696a6b6c6d6e6f  UFFSU1RVVldYWVpbXF1eX2BhYmNkZWZnaGlqa2xtbm8
pairNonce    707172737475767778797a7b7c7d7e7f  cHFyc3R1dnd4eXp7fH1-fw
deviceId     808182838485868788898a8b8c8d8e8f  gIGCg4SFhoeIiYqLjI2Ojw
deviceToken  909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf  kJGSk5SVlpeYmZqbnJ2en6ChoqOkpaanqKmqq6ytrq8
deviceKey    b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecf  sLGys7S1tre4ubq7vL2-v8DBwsPExcbHyMnKy8zNzs8
phoneNonce   d0d1d2d3d4d5d6d7d8d9dadbdcdddedf  0NHS09TV1tfY2drb3N3e3w
pcNonce      e0e1e2e3e4e5e6e7e8e9eaebecedeeef  4OHi4-Tl5ufo6err7O3u7w
```

**QR URL and relay hashes**

```text
qrUrl               https://capriai.app/remote/#r=EBESExQVFhcYGRobHB0eHw&c=QEFCQ0RFRkdISUpLTE1OTw&k=UFFSU1RVVldYWVpbXF1eX2BhYmNkZWZnaGlqa2xtbm8
SHA-256(code)       uiK33JX2zIdldXvkvM83zZLs5tSYfcJqMeJ0yb4jaSE   (the "hash" in the code control)
SHA-256(pcToken)    ctu3M2x2eAAj-D2kw1Xy7uqFczsT00d2l5F3kMEikIQ
SHA-256(devToken)   L7vad1drJ2C1hWYh_s-9_0WCj-19rNIG7ZYV5OnejYI   (the "tokenHash" in admit)
```

**HKDF-SHA256, RFC 5869 test case 1 (checks a hand-rolled HKDF)**

```text
ikm  0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b
salt 000102030405060708090a0b0c
info f0f1f2f3f4f5f6f7f8f9
L    42
okm  3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865
```

**Confirmation code: HKDF(ikm=pairingKey, salt=pairNonce, info="capri-remote/1 confirm", L=4)**

```text
okm  05fabc82
code 318 338
```

**Pairing keys: HKDF(ikm=pairingKey, salt=pairNonce, info="capri-remote/1 pair", L=128)**

```text
salt           707172737475767778797a7b7c7d7e7f
okm            d08f44aec72259be81780d94af04ad01436b5277834dcd2bf5d312076e0d0820ab52d6f9af3b8ea3b62f3026a32546f94c627fe6e13b6a418feb7060c6cb46d65724521444fcfd998b60c692533db7caf00acc550d25c9a305d67e3b8ba981c3198fdaad41d3d2423415c540b06aa52d6a797dc496353b9517123636ea538a01
phoneToPc.enc  d08f44aec72259be81780d94af04ad01436b5277834dcd2bf5d312076e0d0820
phoneToPc.mac  ab52d6f9af3b8ea3b62f3026a32546f94c627fe6e13b6a418feb7060c6cb46d6
pcToPhone.enc  5724521444fcfd998b60c692533db7caf00acc550d25c9a305d67e3b8ba981c3
pcToPhone.mac  198fdaad41d3d2423415c540b06aa52d6a797dc496353b9517123636ea538a01
```

**Hello keys: HKDF(ikm=deviceKey, salt=deviceId, info="capri-remote/1 hello", L=128)**

```text
salt           808182838485868788898a8b8c8d8e8f
okm            c66f7abb793e1fa8c70d788b451e5f8db2800152164ccc4500cbbf5e80d19a48283fcb87996cad868593b4a0ad021b648af6e3c77f7b57e6173b522de4c6332b40c6d2533b82e30dbc38a28af12334b9046c4a7968739396bc74915bf58df5efdffae5a82ab97df49375c1f1fa7292f32577a9275fa17d5394f19b58527a9270
phoneToPc.enc  c66f7abb793e1fa8c70d788b451e5f8db2800152164ccc4500cbbf5e80d19a48
phoneToPc.mac  283fcb87996cad868593b4a0ad021b648af6e3c77f7b57e6173b522de4c6332b
pcToPhone.enc  40c6d2533b82e30dbc38a28af12334b9046c4a7968739396bc74915bf58df5ef
pcToPhone.mac  dffae5a82ab97df49375c1f1fa7292f32577a9275fa17d5394f19b58527a9270
```

**Session keys: HKDF(ikm=deviceKey, salt=phoneNonce||pcNonce, info="capri-remote/1 session", L=128)**

```text
salt           d0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeef
okm            381d37b1307559b8f335391faf5077782a506626cf784d150193090f3d4975b187797eab7af3ed015ed073c8262b4152a66c4f1380af7f795f0be6ba49fa4e250e736a5b9eb4d0f6add54ac823e1c9539e6443c86775461f2e01b22f8d0685c6b0cf32a01158959d9f30bd24f98fbbccfcfc83f4d4830ad1f38196bf7d86d703
phoneToPc.enc  381d37b1307559b8f335391faf5077782a506626cf784d150193090f3d4975b1
phoneToPc.mac  87797eab7af3ed015ed073c8262b4152a66c4f1380af7f795f0be6ba49fa4e25
pcToPhone.enc  0e736a5b9eb4d0f6add54ac823e1c9539e6443c86775461f2e01b22f8d0685c6
pcToPhone.mac  b0cf32a01158959d9f30bd24f98fbbccfcfc83f4d4830ad1f38196bf7d86d703
```

**Envelope: pair-request**

```text
keys      pair.phoneToPc   kind 1   dir 1   seq 0
iv        000102030405060708090a0b0c0d0e0f
plaintext 7b2274797065223a22706169722d72657175657374222c2276223a312c226e616d65223a22546573742050686f6e65222c22617070223a2272656d6f74652d7765622f74657374227d
envelope  0101010000000000000102030405060708090a0b0c0d0e0f9faaa0ef7df8a0d76130e285fd1891c140daa0df971879cad7d8e5bc62a7656e818be89008ca5ba05e2740515d6c7cde58ed816fd9933a2f2d023c516bfe4957f7ca5aec435ca74fc70efe09dd6b87db87af72768ccf1f0e61b3a8674c89d920f87644c7172fe07630004f015d443d55
```

**Envelope: pair-allow**

```text
keys      pair.pcToPhone   kind 1   dir 2   seq 0
iv        101112131415161718191a1b1c1d1e1f
plaintext 7b2274797065223a22706169722d616c6c6f77222c2276223a312c22646576696365223a226749474367345346686f65496959714c6a49324f6a77222c22746f6b656e223a226b4a47536b3553566c7065596d5a71626e4a32656e3643686f714f6b7061616e714b6d7171367974727138222c226b6579223a22734c4779733753317472653475627137764c322d763844427773504578636248794d6e4b79387a4e7a7338222c226e616d65223a2254657374205043222c22617070223a2263617072692f74657374227d
envelope  0101020000000000101112131415161718191a1b1c1d1e1f334f777a62117d0cbe965c9fa0b3200341b1d5e1e53d6f9e6b151123ed11240a399b4b9f08f3da612bb49a8e3cda94faf1010e7c40d3a0e7b8fd85a2bec752985ade5f925802f84167c62191fde6edaa0a6d8ad84d4567930e40a29373f0d35cfbd0c9803879953b8403137755a02817a2b4e004d09e7c0c52ad84090662d8d1dc0d66bd3e7ab27cb9a9134338a7083313e60998b9672c91e7a8e024089417f6baefa8253afc91eb06a4c146ad5866377aba26d9c6aaf886a9fee768048d433493f1655805cccc445cc6a84df15f07260b38b66605efec0345c2c08777efc0972ad6ad80b7167a4676f9d09feea026d3
```

**Envelope: pair-deny**

```text
keys      pair.pcToPhone   kind 1   dir 2   seq 0
iv        202122232425262728292a2b2c2d2e2f
plaintext 7b2274797065223a22706169722d64656e79222c2276223a312c22726561736f6e223a2264656e696564227d
envelope  0101020000000000202122232425262728292a2b2c2d2e2fbd45c4f419784d2bdc0ce210d9c7ad062c7152cd21ef28c9098989e5d64f7ba977465dab3de206cdbe6155a983273aded6123f4e081c59428d39afe8735bfc90cca74a48e5571da162ed2edc377b5860
```

**Envelope: hello**

```text
keys      hello.phoneToPc   kind 2   dir 1   seq 0
iv        303132333435363738393a3b3c3d3e3f
plaintext 7b2274797065223a2268656c6c6f222c2276223a312c226e6f6e6365223a22304e4853303954563174665932647262334e33653377222c226e616d65223a22546573742050686f6e65222c22617070223a2272656d6f74652d7765622f74657374227d
envelope  0102010000000000303132333435363738393a3b3c3d3e3f467a3b7fb454c07e98c1edebdd537ddd38ae228afbea6886b8f573f701cb64954911bed760c756a168bc32b3ad70f061b66c3e1a42a403fca31e51ab689461737620cafb3a724585029cb68186d333e015670b1381ecfd2dc6330b73d88c0dfa1efcd482b951f6c295babecffdfee2b5d06f6cae62f2d36d7caa544413bd23bd2333fbe39bf2d019212bc0c533e88c74
```

**Envelope: hello-ok**

```text
keys      hello.pcToPhone   kind 2   dir 2   seq 0
iv        404142434445464748494a4b4c4d4e4f
plaintext 7b2274797065223a2268656c6c6f2d6f6b222c2276223a312c226e6f6e6365223a22344f4869342d546c3575666f36657272374f33753777222c226563686f223a22304e4853303954563174665932647262334e33653377222c226e616d65223a2254657374205043222c22617070223a2263617072692f74657374222c226665617475726573223a5b2263686174225d7d
envelope  0102020000000000404142434445464748494a4b4c4d4e4fee0f194f6f2fe704a3344ce166cbd678a61925a2d7f7cf25f5101332bc3d1058931e99621463b4b8414e64dc1908d933e62f8e56e03171729e1f84315958b1dfb61c6fc4d57b00c2a2298f92b9d87040112e80159effe31306b430cfd10539e2260b2c353d6a109325dab569415dfc33a8d3d66db556cd74c348a0eb825e44d4e33b8721e8983e0025d681437915e03056eaf66c4fc7eb62e20724394ddde022e2185b3879ff52ea18c42e32a2764bcbcc918015db8f9c61e42be34ad5fbfff3
```

**Envelope: chat.send**

```text
keys      session.phoneToPc   kind 3   dir 1   seq 0
iv        505152535455565758595a5b5c5d5e5f
plaintext 7b2274797065223a22636861742e73656e64222c226964223a226d31222c2274657874223a2248656c6c6f2c20436170726921227d
envelope  0103010000000000505152535455565758595a5b5c5d5e5f30ddbf9970f302f27573ec703596c368bde7f8e2875028b53a3424a0850a7b3a2b5acc9f6ef8c29308ad7defb7d1a21daa1c6f052ea3f4973c209cc69417818090270aa9fcd639c1810a793965249fbd79a02323bccfea157477aae499732eba
```

**Envelope: chat.typing (32-byte plaintext, so a whole block of padding)**

```text
keys      session.pcToPhone   kind 3   dir 2   seq 0
iv        606162636465666768696a6b6c6d6e6f
plaintext 7b2274797065223a22636861742e747970696e67222c226f6e223a747275657d
envelope  0103020000000000606162636465666768696a6b6c6d6e6f273e060578097fcc81546ab07ab3c9728b216d7994b638e4661b342e870678cd5b17a5ebe5a756bd13e01c918d7cce17baa7b787bd6acc9353f9546ad00c2eea5b2346878c731ff346c866bf1d3c6f7c
```

**Envelope: chat.reply (seq 0x01020304, non-ASCII text)**

```text
keys      session.pcToPhone   kind 3   dir 2   seq 16909060
iv        707172737475767778797a7b7c7d7e7f
plaintext 7b2274797065223a22636861742e7265706c79222c226964223a227231222c227265223a226d31222c2274657874223a224869204c616368792c20636166c3a920e2989520f09f90b1222c22646f6e65223a747275657d
envelope  0103020001020304707172737475767778797a7b7c7d7e7f7e00cf399b1c51e876c562f8a328e1473bdf9fd6bc45eac5a4145fc58a96f9e3559f9f8b689df4ef781ac5deea50c8918a9572dc67cf1849d9464839932f2550c706ec64764b317dbe6d05352f9f42118a3f3d76b01b5e8c26e6d39a01df7e189ecac530f6677f3b9dfdeea7e34b0cc933a64706af025404c92127c414101960
```

**Must be rejected (every one of these fails to open)**

```text
mac      one bit flipped in the ciphertext: open with session.phoneToPc, kind 3, dir 1, seq 0
         0103010000000000505152535455565758595a5b5c5d5e5f31ddbf9970f302f27573ec703596c368bde7f8e2875028b53a3424a0850a7b3a2b5acc9f6ef8c29308ad7defb7d1a21daa1c6f052ea3f4973c209cc69417818090270aa9fcd639c1810a793965249fbd79a02323bccfea157477aae499732eba
mac      one bit flipped in the sequence number: open with session.phoneToPc, kind 3, dir 1, seq 0
         0103010000000001505152535455565758595a5b5c5d5e5f30ddbf9970f302f27573ec703596c368bde7f8e2875028b53a3424a0850a7b3a2b5acc9f6ef8c29308ad7defb7d1a21daa1c6f052ea3f4973c209cc69417818090270aa9fcd639c1810a793965249fbd79a02323bccfea157477aae499732eba
mac      one bit flipped in the tag: open with session.phoneToPc, kind 3, dir 1, seq 0
         0103010000000000505152535455565758595a5b5c5d5e5f30ddbf9970f302f27573ec703596c368bde7f8e2875028b53a3424a0850a7b3a2b5acc9f6ef8c29308ad7defb7d1a21daa1c6f052ea3f4973c209cc69417818090270aa9fcd639c1810a793965249fbd79a02323bccfea157477aae499732ebb
length   last byte cut off: open with session.phoneToPc, kind 3, dir 1, seq 0
         0103010000000000505152535455565758595a5b5c5d5e5f30ddbf9970f302f27573ec703596c368bde7f8e2875028b53a3424a0850a7b3a2b5acc9f6ef8c29308ad7defb7d1a21daa1c6f052ea3f4973c209cc69417818090270aa9fcd639c1810a793965249fbd79a02323bccfea157477aae499732e
seq      replayed: the valid chat.send when seq 1 is expected: open with session.phoneToPc, kind 3, dir 1, seq 1
         0103010000000000505152535455565758595a5b5c5d5e5f30ddbf9970f302f27573ec703596c368bde7f8e2875028b53a3424a0850a7b3a2b5acc9f6ef8c29308ad7defb7d1a21daa1c6f052ea3f4973c209cc69417818090270aa9fcd639c1810a793965249fbd79a02323bccfea157477aae499732eba
context  valid chat.send opened as a hello: open with session.phoneToPc, kind 2, dir 1, seq 0
         0103010000000000505152535455565758595a5b5c5d5e5f30ddbf9970f302f27573ec703596c368bde7f8e2875028b53a3424a0850a7b3a2b5acc9f6ef8c29308ad7defb7d1a21daa1c6f052ea3f4973c209cc69417818090270aa9fcd639c1810a793965249fbd79a02323bccfea157477aae499732eba
mac      valid chat.send opened with the PC->phone keys: open with session.pcToPhone, kind 3, dir 1, seq 0
         0103010000000000505152535455565758595a5b5c5d5e5f30ddbf9970f302f27573ec703596c368bde7f8e2875028b53a3424a0850a7b3a2b5acc9f6ef8c29308ad7defb7d1a21daa1c6f052ea3f4973c209cc69417818090270aa9fcd639c1810a793965249fbd79a02323bccfea157477aae499732eba
```

**Binary frames between the PC and the relay (16-byte device id, then the envelope)**

```text
relay->PC chat.send    808182838485868788898a8b8c8d8e8f0103010000000000505152535455565758595a5b5c5d5e5f30ddbf9970f302f27573ec703596c368bde7f8e2875028b53a3424a0850a7b3a2b5acc9f6ef8c29308ad7defb7d1a21daa1c6f052ea3f4973c209cc69417818090270aa9fcd639c1810a793965249fbd79a02323bccfea157477aae499732eba
PC->relay chat.typing  808182838485868788898a8b8c8d8e8f0103020000000000606162636465666768696a6b6c6d6e6f273e060578097fcc81546ab07ab3c9728b216d7994b638e4661b342e870678cd5b17a5ebe5a756bd13e01c918d7cce17baa7b787bd6acc9353f9546ad00c2eea5b2346878c731ff346c866bf1d3c6f7c
```

**Text frames built from the same values**

```text
pcAuth        {"t":"auth","token":"ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8"}
pcCode        {"t":"code","hash":"uiK33JX2zIdldXvkvM83zZLs5tSYfcJqMeJ0yb4jaSE","ttl":600}
phonePair     {"t":"pair","code":"QEFCQ0RFRkdISUpLTE1OTw","nonce":"cHFyc3R1dnd4eXp7fH1-fw","env":"AQEBAAAAAAAAAQIDBAUGBwgJCgsMDQ4Pn6qg7334oNdhMOKF_RiRwUDaoN-XGHnK19jlvGKnZW6Bi-iQCMpboF4nQFFdbHzeWO2Bb9mTOi8tAjxRa_5JV_fKWuxDXKdPxw7-Cd1rh9uHr3J2jM8fDmGzqGdMidkg-HZExxcv4HYwAE8BXUQ9VQ"}
relayPending  {"t":"pending","id":"oKGio6SlpqeoqaqrrK2urw","nonce":"cHFyc3R1dnd4eXp7fH1-fw","env":"AQEBAAAAAAAAAQIDBAUGBwgJCgsMDQ4Pn6qg7334oNdhMOKF_RiRwUDaoN-XGHnK19jlvGKnZW6Bi-iQCMpboF4nQFFdbHzeWO2Bb9mTOi8tAjxRa_5JV_fKWuxDXKdPxw7-Cd1rh9uHr3J2jM8fDmGzqGdMidkg-HZExxcv4HYwAE8BXUQ9VQ"}
pcAdmit       {"t":"admit","pending":"oKGio6SlpqeoqaqrrK2urw","device":"gIGCg4SFhoeIiYqLjI2Ojw","tokenHash":"L7vad1drJ2C1hWYh_s-9_0WCj-19rNIG7ZYV5OnejYI","env":"AQECAAAAAAAQERITFBUWFxgZGhscHR4fM093emIRfQy-llyfoLMgA0Gx1eHlPW-eaxURI-0RJAo5m0ufCPPaYSu0mo482pT68QEOfEDToOe4_YWivsdSmFreX5JYAvhBZ8Yhkf3m7aoKbYrYTUVnkw5AopNz8NNc-9DJgDh5lTuEAxN3VaAoF6K04ATQnnwMUq2ECQZi2NHcDWa9PnqyfLmpE0M4pwgzE-YJmLlnLJHnqOAkCJQX9rrvqCU6_JHrBqTBRq1YZjd6uibZxqr4hqn-52gEjUM0k_FlWAXMzERcxqhN8V8HJgs4tmYF7-wDRcLAh3fvwJcq1q2AtxZ6Rnb50J_uoCbT"}
phoneAuth     {"t":"auth","device":"gIGCg4SFhoeIiYqLjI2Ojw","token":"kJGSk5SVlpeYmZqbnJ2en6ChoqOkpaanqKmqq6ytrq8"}
```

The plaintext of each envelope, as text, is in `remote/test/vectors.json` (`envelopes[].plaintext`).

<!-- vectors:end -->

## 12. Running it locally

```text
npm --prefix remote/relay ci          once: installs the pinned wrangler
npm --prefix remote/relay test        crypto vectors, then the relay end to end under wrangler dev
npm --prefix remote/relay run dev     the relay on ws://127.0.0.1:8787 for the phone and PC to use
npm --prefix remote/relay run vectors rewrites vectors.json and the block above after a crypto change
```

The tests never deploy or log in. They run `wrangler dev --local` on a free port with telemetry and network lookups turned off, and keep state in a temporary folder.
