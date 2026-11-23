using System;
using System.Collections.Generic;
using System.IO;
using CapriDesktop;

internal static class MailMove
{
    private const string Moved = "PASS mail: check_mail gives a verdict without marking it read, and move_mail moves just the one email it names to spam or a folder, refusing a missing uid, a missing folder or a uid that changed";

    private const string Twice = "PASS mail: moving the same email twice, one after the other or at the same moment, is one real move and two successes that say where it is; one someone else moved is found by its Message-ID where it went; a uid that was never there, or an email that's nowhere it could have gone, still fails plainly";

    private const string Watched = "PASS mail: when she's already moved phishing on her own, their ask to bin it says where it went, and when they move one while she's on it she doesn't move it again or say it failed; it's one move either way";

    internal static void Check(Action<string> pass)
    {
        string folder = Directory.CreateTempSubdirectory("capri-coverage-pins-mail-").FullName;
        List<string> checks = new List<string>();
        try
        {
            MailChecks.Run(checks, folder);
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Require(checks.Contains(Moved), "the cross build's move_mail checks did not pass, so a move that worked has no cross-side test");
        Require(checks.Contains(Twice), "moving the same email twice on the cross build was not one move and two successes that say where it is");
        Require(checks.Contains(Watched), "watching and their ask over one email on the cross build did not leave it moved once, or said it failed");
        pass("mail move: the same email moved twice through the cross build's MailTools reaches the fake server once and both calls say where it is, and a move she made first or one they made while she watched is not reverted");
    }

    internal static void Require(bool good, string why)
    {
        if (!good) throw new Exception(why);
    }
}
