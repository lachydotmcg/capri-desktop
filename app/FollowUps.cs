using System;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private readonly FollowUp followUp = new FollowUp();
        private int answerVoice = -1;
        private int followTicket = -1, followInput, followMic;
        private bool followLogged;

        internal bool FollowUps { get { return preferences.FollowUps; } }

        internal void ToggleFollowUps()
        {
            preferences.FollowUps = !preferences.FollowUps;
            if (!preferences.FollowUps) followUp.Drop();
            SavePreferences();
            mainframe.Sync();
        }

        private bool FollowUpAllowed()
        {
            return preferences.FollowUps && !quitting && !Hushed && working == null && !MidJob(job) && agent == null
                && (!chatting || local && earsReady) && !typer.Visible && !pushing && !transcribing;
        }

        private int FollowInput()
        {
            if (testing) return 0;
            uint sampled;
            long quiet = Native.IdleMilliseconds(out sampled);
            return unchecked((int)(sampled - quiet));
        }

        private void CheckFollowUp(double now)
        {
            if (followTicket == asking && !followLogged && !FollowDelivery(followTicket)) { asking++; CloseTalk(); }
            bool finished = talkState == TalkState.Off && !LineBusy && (!chatting || !earsShut);
            string line = followUp.Take(now, asking, FollowInput(), micTalked, FollowUpAllowed(), finished);
            if (line == null) return;
            followTicket = asking + 1;
            followInput = FollowInput();
            followMic = micTalked;
            followLogged = false;
            Remark("follow-up", line);
        }

        private bool FollowDelivery(int ticket)
        {
            return ticket != followTicket || FollowUpAllowed() && followInput == FollowInput() && followMic == micTalked;
        }
    }
}
