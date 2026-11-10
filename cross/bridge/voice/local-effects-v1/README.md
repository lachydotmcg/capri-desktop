# Capri local effects v1

The default effect is `bright-double-v1`: +2 semitones,
Rubber Band preserved formants, tempo 1.0, direct .90, high-pass residual doubles
.07 at 12 ms and .04 at 38 ms, finite 80 ms tail. It has no room reverb or pitch
modulation. A single low-gain grain is limited to short harmless interjections;
names/numbers/technical clauses retain the subtle double only. `bright-dry-v1`
and `bright-shifted-v1` isolate pitch/formant behavior. The original schema-3
Lachy profile remains the dry, unchanged control. The optional 70% Bella derived
base hypothesis is not selected or claimed to be tested; this increment isolates
processing without creating or relabeling a voice table.

Kokoro supplies the speech; these effects add pitch and short-delay processing.
There are no spoken emotion tags.

Schema 4 wraps an immutable, hash-checked schema-3 base profile, pins the processor
and actual Rubber Band library, and allows only three fixed filter modes. Changing
model/table/language/speed still fails the original base validation. No downloads
or network calls run during synthesis. Missing/changed processor fails visibly;
there is no alternate voice/cloud fallback. Each effect profile has its own hash.

Processing occurs inside the existing warm worker before the PCM file is published.
The final envelope uses processed samples; approximate words use processed speech
duration excluding the bounded tail. The native Qt clock stays authoritative.
Stop/epoch/parent-death termination revokes the worker and processor process group;
wet state is clause-local and never survives a request. One player/model remains.

Runtime: existing private CPython 3.14/sherpa-onnx 1.13.8/NumPy 2.4.2, plus local
FFmpeg 9.0.2 (Arch package 2:9.0.2-2, GPL-3.0-only) linked to Rubber Band 4.0.0
(Arch 4.0.0-2, GPL-2.0-or-later). Processor binaries and OS dependency inventory
are retained externally with profile pins; no binaries/weights are shipped in Git
or the review. Recovery on Arch requires the documented system ABI/dependencies;
this is not a portable OS image. Full new component COPYING texts and source URLs
are alongside this file. Corresponding upstream source releases:
https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz and
https://github.com/breakfastquay/rubberband/tree/v4.0.0 .

Required Notice: Copyright 2026 Lachy (https://github.com/lachydotmcg).
The exact source pack and all existing component/character/contributor notices
remain in `../lachy-capri-voice-1/` and `../../THIRD_PARTY_NOTICES.md`.
New local speech/personality/effects require downloadable, retainable, recoverable
components under documented terms. The pre-existing ChatGPT-plan brain still
requires its established remote account; the whole app is not offline.
