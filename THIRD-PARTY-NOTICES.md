# Third-party notices

## Husk mode

Took inspiration from Mochiiwuff's work when it was under an MIT Licence (capri-desktop 0.4.3, commit f839580), exclusively for husk mode.

## Speech models

Her own ears run [NVIDIA Parakeet TDT 0.6B v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2) for English and Parakeet TDT 0.6B Ultra for her other 24 languages: Ultra's weights are by Moondream, trained from [nvidia/parakeet-tdt-0.6b-v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3). Both are licensed [CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/), and the int8 ONNX exports are by sherpa-onnx (Apache-2.0). Neither NVIDIA nor Moondream endorses Capri.

## QR codes

Capri Remote's pairing QR code (app/RemoteQr.cs) is ported from Project Nayuki's [QR Code generator library](https://www.nayuki.io/page/qr-code-generator-library), Copyright (c) Project Nayuki, under the [MIT License](https://opensource.org/licenses/MIT). The licence text is in the file's header.

## Arch v0.8.6.1 native adaptations

The click animation is by **mixmix312352**, reused from the pinned upstream click strip. The stand/orientation, editor PanelArt and CapriMainframe-Bold font are reused from lachydotmcg/capri-desktop v0.8.6.1 under the existing project terms. Preserve upstream LICENSE.md and notices. Original Mochiiwuff source attribution remains https://github.com/mochiiwuff/capri-desktop. No generated visual media was added.

The Linux native font bootstrap adapts SkiaSharp’s MIT-licensed LibraryLoader isolation flag from PR2247, merge `482e6ee2913a08a7cad76520ccf5fbce97c7c23b`. Its source provenance and MIT license are retained in `docs/provenance/SkiaSharp`; packaged HarfBuzzSharp and SkiaSharp dependency pins are unchanged.
