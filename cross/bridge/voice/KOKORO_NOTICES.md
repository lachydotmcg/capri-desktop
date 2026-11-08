# Historical Kokoro CPU profile provenance and terms

These notes describe an earlier retained generic Kokoro comparison profile, not the current downloadable Lachy voice-pack identity. Its installed profile manifest is retained outside Git; current pack pins and required notices are in `lachy-capri-voice-1.lock.json` and `lachy-capri-voice-1/README.txt`.

That comparison profile uses the official sherpa-onnx **v1.13.8** Python CPU API, source
`11afbd009a7f8c08f4bcf2fc1b265d0df4670fbf`, and the official
`kokoro-int8-multi-lang-v1_0.tar.bz2` release asset **677610567**. The archive,
every model/voice/token/lexicon/G2P file, the wheel downloads, and installed
runtime source/library trees were pinned in the externally retained `kokoro-v1.0-int8.json` profile receipt and the retained runtime lock. This repository supplies `tools/kokoro-runtime.lock`; it does not claim the historical profile receipt is bundled. No weights, environments, library binaries or recorded
performer references belong in Git or the review package.

- **Kokoro model and voice resources:** hexgrad/Kokoro-82M, Apache-2.0. The
  official conversion archive includes its Apache license; preserve it. Its
  current 54-entry table retains documented `af_bella=2`, `af_heart=3`,
  `af_sky=10`. These are identified built-in synthetic presets, with no claim
  of Capri likeness, actor cloning, original actor consent or human acceptance.
  The upstream model card credits the CC-BY training contributions Koniwa
  (`tnc`, CC BY 3.0) and SIWIS (CC BY 4.0). Preserve those acknowledgments.
- **sherpa-onnx:** k2-fsa contributors/Xiaomi, Apache-2.0. The wheel bundles
  ONNX Runtime **1.28.2**, Microsoft/contributors, MIT. Installed ELF hashes are
  included in the separate runtime receipt; the dependency tree pin covers them.
- **G2P:** the 1.x sherpa build uses the espeak-ng fork at
  `ed530aa113046142eb5115cf2fc9157854d0ffe1`, as pinned by its CMake source,
  GPL-3.0-or-later. The supplied compiled G2P resource bytes are pinned separately;
  their original build revision is not stated by the model archive. A top-level
  Apache license does not erase embedded component terms. Preserve component
  COPYING/notices and corresponding source obligations when redistributing a
  runtime; this change does not distribute the runtime binaries.
- **NumPy 2.4.2:** BSD-3-Clause with bundled dependency notices, including its
  BLAS/LAPACK libraries. Retain the wheel's complete `licenses` directory.

The `upstream_model_card_revision_inspected` field records the model card's
inspected repository revision. It does **not** establish the conversion's
original source commit. Conversion identity is the official immutable asset
ID, archive SHA-256, and exact extracted-file hashes.

The historical generic comparison profile above did not use Lachy’s voice pack. The current native application does reuse Lachy’s source/artwork and supports the checksum-pinned `lachy-capri-voice-1` pack, including its added voice-table entry and required notice. Those original application/pack terms remain applicable; the generic Kokoro notes do not replace them. No show, fan pack, scraped actor reference or performer clone is
used. Dialogue fixtures are original writing; punctuation/speed controls are
not a learned emotion-control API. Human naturalness and expressiveness remain
listening judgments.

Primary sources:

- [Kokoro model card and training acknowledgments](https://huggingface.co/hexgrad/Kokoro-82M)
- [Official model/voice mapping](https://k2-fsa.github.io/sherpa/onnx/tts/pretrained_models/kokoro.html)
- [Pinned sherpa Python example](https://github.com/k2-fsa/sherpa-onnx/blob/11afbd009a7f8c08f4bcf2fc1b265d0df4670fbf/python-api-examples/offline-tts.py)
- [Pinned espeak source and archive hash](https://github.com/k2-fsa/sherpa-onnx/blob/11afbd009a7f8c08f4bcf2fc1b265d0df4670fbf/cmake/espeak-ng-for-piper.cmake)
- [espeak COPYING](https://github.com/csukuangfj/espeak-ng/blob/ed530aa113046142eb5115cf2fc9157854d0ffe1/COPYING)
- [ONNX Runtime license](https://github.com/microsoft/onnxruntime/blob/v1.28.2/LICENSE)

Sherpa **2.x** intentionally removed the internal espeak path. Do not upgrade
this lock implicitly: a diagnostic 2.0.1 run silently omitted an out-of-lexicon
contraction. An upgrade needs an explicit pinned external phonemizer and tests
for contractions, numbers, Unicode and technical names.
