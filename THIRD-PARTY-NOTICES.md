# Third-party notices

## Husk mode

Took inspiration from Mochiiwuff's work when it was under an MIT Licence (capri-desktop 0.4.3, commit f839580), exclusively for husk mode.

## Speech models

Her own ears run [NVIDIA Parakeet TDT 0.6B v2](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2) for English and Parakeet TDT 0.6B Ultra for her other 24 languages: Ultra's weights are by Moondream, trained from [nvidia/parakeet-tdt-0.6b-v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3). Both are licensed [CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/), and the int8 ONNX exports are by sherpa-onnx (Apache-2.0). Neither NVIDIA nor Moondream endorses Capri.

## QR codes

Capri Remote's pairing QR code (app/RemoteQr.cs) is ported from Project Nayuki's [QR Code generator library](https://www.nayuki.io/page/qr-code-generator-library), Copyright (c) Project Nayuki, under the [MIT License](https://opensource.org/licenses/MIT). The licence text is in the file's header.
