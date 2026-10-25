# Development-build limitations

- Interactive Windows laptop validation remains outstanding until the checklist is
  completed. Cross-compilation and portable tests are not desktop runtime evidence.
- Windows CI results must be checked for the exact commit in BUILD-INFO.json. The
  workflow intentionally fails binary upload if Windows regression/smoke tests fail.
- Window collision uses one-way ledges, not full body geometry. Targets that move
  rapidly or newly created windows midflight may require further tuning. Jumps stay
  on the current monitor; monitor changes cancel an active trajectory safely.
- WinForms timers provide approximate 15/30/60 FPS, not guaranteed refresh pacing.
  CPU/RAM targets have not been measured on the intended 8 GB Intel laptop yet.
- Mixed-DPI/taskbar edge cases require manual testing. A window larger than an
  exceptionally small work area is positioned at its top-left, not resized blindly.
- Chat supports a limited, exact-command English/Russian vocabulary. It is not AI.
  No local model or cloud-chat provider is implemented for this build.
- Original optional voice/model implementation code remains for compatibility;
  normal model setup/refresh UI routes to built-in chat. No model discovery, model
  download or voice-provider connection starts automatically.
- Corrupted saved history is preserved and blocks replacement until explicit deletion.
  History is local plaintext. Back up test data before corruption tests.
- This executable is unsigned. Distribution and artwork licensing are unresolved;
  do not publish a public release or bypass operating-system security protections.
