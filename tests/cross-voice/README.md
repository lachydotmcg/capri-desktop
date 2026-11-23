# Cross voice provider checks

Run `dotnet run --project tests/cross-voice/CrossVoiceChecks.csproj -c Release`.

These checks compile the cross build's `Speakers.cs` and `VoiceSpeakers.cs` against the real shared `app/Voice.cs` (with `CROSS` defined), plus small brain and companion stubs. They prove the provider pin: every saved provider id, including Fish Audio and OpenAI, resolves through the same `Speakers.Pick`, `Speakers.Pickable` and `Speakers.PickName` that `Pet.Speaker` and `Pet.RefreshVoice` use, and unknown ids still fall back to ElevenLabs.

They also run the Fish Audio and OpenAI requests against a loopback fake, checking the exact Windows request shapes (`POST /v1/tts` with the voice in the body, model header and bearer key; `POST /v1/audio/speech` with model, input, voice and `response_format`), Fish Audio's cue text and its stripping for every other provider, the shared error messages, and the existing secret-file helper behind `voice-fish-key` and `voice-openai-key`.

They do not open a window, run the tray menu, touch a real service, or test ElevenLabs' key listing. The full `--smoke` run on the built app covers the rest of the provider menu and Mainframe paths.
