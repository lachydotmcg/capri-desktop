using System.Collections.Generic;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private MailWatch mailWatch;
        private double mailAt;

        private void SyncMailWatch()
        {
            bool wanted = Program.Smoke == null && !quitting && Plugins.BuiltInOn("mail") != null && MailTools.Watch && MailTools.Ready;
            MailAccount account = wanted ? MailTools.Account : null;
            if (mailWatch != null && (!wanted || mailWatch.Account != account)) StopMailWatch();
            if (!wanted || mailWatch != null) return;
            mailWatch = new MailWatch(account);
            mailWatch.OwnHands = () => { string id = Plugins.BuiltInOn("mail"); return id != null && MailTools.OwnHands && Plugins.GrantOf(id, "move_mail") != Plugins.Off; };
            mailWatch.Start();
        }

        private void StopMailWatch()
        {
            MailWatch was = mailWatch;
            mailWatch = null;
            if (was != null) was.Stop();
        }

        private bool CheckMail()
        {
            if (Now < mailAt) return false;
            mailAt = Now + 2;
            SyncMailWatch();
            if (quitting || mailWatch == null || !mailWatch.Waiting || Hands.Streaming) return false;
            if (away || dragging || pressed || outro != null || ContextMenu.IsOpen || Talk.Typing || Listening || Talk.Aloud || !Talk.Quiet || reminderLine != null) return false;
            List<MailNews> news = mailWatch.Take();
            string line = MailWatch.Line(news);
            if (line == null) return false;
            OwnLine(MailWatch.Note(news), "mail", line);
            return true;
        }
    }
}
