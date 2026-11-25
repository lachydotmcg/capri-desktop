using System;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private void CompanionSwitched()
        {
            kokoro.Style = CompanionPack.Style ?? LocalVoice.Find(preferences.VoiceStyle);
            if (mainframe == null || mainframe.IsDisposed) return;
            ShowVoice(false);
            mainframe.ShowPersonality();
        }
    }
}
