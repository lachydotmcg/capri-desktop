Capri free voice pack 1

Kokoro-82M v1.0 (hexgrad), converted by sherpa-onnx. Apache License 2.0. kokoro-LICENSE.txt
sherpa-onnx v1.13.8 (k2-fsa). Apache License 2.0. sherpa-onnx-LICENSE.txt
  Source: https://github.com/k2-fsa/sherpa-onnx/tree/v1.13.8
ONNX Runtime (Microsoft). MIT License. onnxruntime-LICENSE.txt
espeak-ng data and phonemizer, built into sherpa-onnx-c-api.dll. GNU GPL v3. espeak-ng-COPYING.txt
  Source: https://github.com/espeak-ng/espeak-ng and https://github.com/csukuangfj/espeak-ng

voices.bin is the Kokoro v1.0 voice table with row 53 replaced by a blend of
af_heart 0.45, af_bella 0.35 and af_nicole 0.2.
The lexicons are the Kokoro English lexicons with Capri's slang words added.
Only the rows Capri uses are kept in voices.bin (2 af_bella, 6 af_nicole,
21 bf_emma, 53 the blend); the other rows are zeroed. espeak-ng-data
keeps English only.
