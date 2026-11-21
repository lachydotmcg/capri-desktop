using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private MailWatch mailWatch;
        private double mailSynced = -10;

        private void SyncMailWatch()
        {
            string owner = Plugins.BuiltInOn("mail");
            bool wanted = !testing && !quitting && owner != null && MailTools.Watch && MailTools.Ready;
            MailAccount account = wanted ? MailTools.Account : null;
            if (mailWatch != null && (!wanted || mailWatch.Account != account)) StopMailWatch();
            if (!wanted || mailWatch != null) return;
            mailWatch = new MailWatch(account);
            mailWatch.OwnHands = delegate { string id = Plugins.BuiltInOn("mail"); return id != null && MailTools.OwnHands && Plugins.GrantOf(id, "move_mail") != Plugins.Off; };
            mailWatch.Start();
        }

        private void StopMailWatch()
        {
            MailWatch was = mailWatch;
            mailWatch = null;
            if (was != null) was.Stop();
        }

        internal string MailWatchState { get { MailWatch w = mailWatch; return w == null ? "off" : w.State; } }

        private bool CheckMail()
        {
            if (quitting || testing) return false;
            double now = clock.Elapsed.TotalSeconds;
            if (now - mailSynced >= 2) { mailSynced = now; SyncMailWatch(); }
            if (mailWatch == null || !mailWatch.Waiting || Hands.Streaming) return false;
            if (outro != null || dragging || pressed || menu.Visible || typer.Visible) return false;
            List<MailNews> news = mailWatch.Take();
            string line = MailWatch.Line(news);
            if (line == null) return false;
            brain.Remember(MailWatch.Note(news), line);
            SayOwn("mail", line);
            return true;
        }
    }
}
