using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class MailVerdict
    {
        internal const string Fine = "fine", Suspicious = "suspicious", Phishing = "phishing";
        internal string Level = Fine;
        internal int Score;
        internal int Strong;
        internal bool Sure;
        internal string From = "", Subject = "", MessageId = "";
        internal List<string> Reasons = new List<string>();
        internal List<int> Weights = new List<int>();

        internal void Add(int weight, string reason)
        {
            if (weight <= 0 || Reasons.Contains(reason)) return;
            int at = 0;
            while (at < Weights.Count && Weights[at] >= weight) at++;
            Reasons.Insert(at, reason);
            Weights.Insert(at, weight);
            Score += weight;
            if (weight >= 3) Strong++;
        }

        internal void Settle()
        {
            Level = Score >= 6 ? Phishing : Score >= 3 ? Suspicious : Fine;
            Sure = Level == Phishing && Score >= 8 && Strong >= 2;
        }
    }

    internal static class MailPhish
    {
        private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(400);

        private static readonly string[] Brands =
        {
            "PayPal|paypal|paypal.com,paypal.me,paypalobjects.com",
            "Apple|~apple,icloud,itunes,appleid|apple.com,icloud.com,me.com,mac.com,itunes.com",
            "Microsoft|microsoft,office365,onedrive,outlook,xbox,hotmail|microsoft.com,outlook.com,live.com,hotmail.com,office.com,office365.com,microsoftonline.com,onedrive.com,xbox.com,sharepoint.com,microsoft365.com,msn.com,skype.com,windows.com,azure.com",
            "Google|google,gmail|google.com,gmail.com,googlemail.com,youtube.com,android.com,withgoogle.com,googleusercontent.com",
            "Amazon|~amazon,primevideo|amazon.com,amazon.co.uk,amazon.com.au,amazon.de,amazon.fr,amazon.ca,amazon.in,amazon.it,amazon.es,amazon.co.jp,primevideo.com,a2z.com,amazonaws.com",
            "Netflix|netflix|netflix.com",
            "Facebook|facebook,instagram,whatsapp,~meta|facebook.com,facebookmail.com,fb.com,meta.com,instagram.com,whatsapp.com",
            "Steam|~steam,steampowered,steamcommunity|steampowered.com,steamcommunity.com,valvesoftware.com",
            "Discord|~discord|discord.com,discord.gg,discordapp.com",
            "Roblox|roblox|roblox.com",
            "Epic Games|epicgames|epicgames.com,unrealengine.com",
            "DHL|dhl|dhl.com,dhl.de,dhl.co.uk,dhl.com.au",
            "FedEx|fedex|fedex.com",
            "UPS|~ups|ups.com",
            "USPS|usps|usps.com",
            "Australia Post|auspost,australiapost|auspost.com.au",
            "myGov|mygov|my.gov.au,servicesaustralia.gov.au",
            "ATO|~ato|ato.gov.au",
            "Linkt|linkt|linkt.com.au",
            "CommBank|commbank,commonwealthbank|commbank.com.au,cba.com.au",
            "Westpac|westpac|westpac.com.au",
            "NAB|~nab|nab.com.au",
            "ANZ|~anz|anz.com,anz.com.au",
            "Telstra|telstra|telstra.com,telstra.com.au",
            "Royal Mail|royalmail|royalmail.com,royalmail.co.uk",
            "HMRC|hmrc|hmrc.gov.uk,gov.uk",
            "IRS|~irs|irs.gov",
            "Coinbase|coinbase|coinbase.com",
            "Binance|binance|binance.com",
            "MetaMask|metamask|metamask.io",
            "Chase|~chase|chase.com,jpmorgan.com",
            "Wells Fargo|wellsfargo|wellsfargo.com",
            "Bank of America|bankofamerica|bankofamerica.com,bofa.com",
            "HSBC|hsbc|hsbc.com,hsbc.co.uk,hsbc.com.au",
            "Barclays|barclays|barclays.co.uk,barclays.com",
            "Revolut|revolut|revolut.com",
            "DocuSign|docusign|docusign.com,docusign.net",
            "Dropbox|dropbox|dropbox.com,dropboxmail.com",
            "Adobe|adobe|adobe.com",
            "LinkedIn|linkedin|linkedin.com,lnkd.in",
            "Spotify|spotify|spotify.com",
            "eBay|ebay|ebay.com,ebay.co.uk,ebay.com.au,ebay.de",
            "Norton|norton,nortonlifelock|norton.com,nortonlifelock.com",
            "McAfee|mcafee|mcafee.com",
            "Geek Squad|geeksquad|geeksquad.com,bestbuy.com",
            "Yahoo|yahoo|yahoo.com",
            "TikTok|tiktok|tiktok.com",
            "X|twitter|twitter.com,x.com",
            "Snapchat|snapchat|snapchat.com"
        };

        private static readonly HashSet<string> Webmail = Set("gmail.com googlemail.com outlook.com hotmail.com live.com msn.com yahoo.com ymail.com aol.com icloud.com me.com mac.com proton.me protonmail.com pm.me gmx.com gmx.net mail.com zoho.com yandex.com yandex.ru mail.ru qq.com 163.com 126.com bigpond.com optusnet.com.au tutanota.com tuta.io fastmail.com hey.com");
        private static readonly HashSet<string> Shorteners = Set("bit.ly bitly.com tinyurl.com t.co goo.gl ow.ly is.gd buff.ly cutt.ly rebrand.ly shorturl.at rb.gy tiny.cc s.id v.gd t.ly bit.do lnkd.in qrco.de shorte.st adf.ly");
        private static readonly HashSet<string> Trackers = Set("list-manage.com mailchimp.com mcusercontent.com sendgrid.net sendgrid.com mandrillapp.com sparkpostmail.com hubspotlinks.com hubspotemail.net hs-sites.com rs6.net constantcontact.com createsend.com createsend1.com campaign-archive.com awstrack.me exacttarget.com customeriomail.com klaviyo.com klclick.com klclick1.com klclick2.com klclick3.com substack.com convertkit-mail.com convertkit-mail2.com ck.page mailerlite.com mlsend.com beehiiv.com mailjet.com mjt.lu sendibt2.com sendibt3.com brevo.com sendinblue.com pstmrk.it urldefense.com proofpoint.com mimecast.com safelinks.protection.outlook.com");
        private static readonly HashSet<string> FreeHosts = Set("web.app firebaseapp.com github.io netlify.app vercel.app pages.dev workers.dev r2.dev ngrok.io ngrok-free.app weebly.com wixsite.com glitch.me 000webhostapp.com blogspot.com ipfs.io dweb.link square.site godaddysites.com notion.site webflow.io framer.website forms.gle");
        private static readonly HashSet<string> OddEndings = Set("zip mov top xyz click gq tk ml cf ga icu cyou sbs cfd buzz rest fit monster quest work country stream download racing win bid loan men review date party trade webcam science accountant cricket faith kim su lol");
        private static readonly HashSet<string> Reserved = Set("invalid test example localhost local onion");
        private static readonly HashSet<string> Suffixes = Set("co.uk org.uk ac.uk gov.uk me.uk ltd.uk plc.uk net.uk com.au net.au org.au edu.au gov.au asn.au id.au co.nz org.nz net.nz govt.nz co.jp ne.jp or.jp com.br com.mx co.in co.za com.sg com.hk com.tr com.cn co.kr com.tw com.ar co.id com.my com.ph com.vn com.ng com.pk co.il com.sa com.eg github.io web.app firebaseapp.com netlify.app vercel.app pages.dev workers.dev r2.dev blogspot.com wixsite.com weebly.com glitch.me 000webhostapp.com ngrok-free.app ngrok.io herokuapp.com azurewebsites.net appspot.com s3.amazonaws.com square.site godaddysites.com webflow.io notion.site framer.website");
        private static readonly HashSet<string> Bait = Set("account accounts login logon signin secure security verify verification validate update support service services billing payment payments password wallet bank banking confirm recover recovery unlock auth authentication online webmail mail admin invoice delivery parcel refund bonus helpdesk customer apple paypal microsoft google amazon netflix office outlook steam discord roblox");
        private static readonly HashSet<string> ServiceWords = Set("support security account accounts id team service services billing help customer customers notification notifications alert alerts delivery verify verification online bank card payments pay wallet store rewards care desk admin administrator center centre noreply no-reply info mail helpdesk fraud postmaster webmaster it department");
        private static readonly HashSet<string> Risky = Set("exe scr js jse vbs vbe wsf wsh lnk iso img vhd vhdx hta html htm shtml xhtml svg bat cmd com pif msi msix appx appxbundle jar ps1 psm1 reg cpl chm one xll iqy slk apk dll scf url application gadget msc");
        private static readonly HashSet<string> Macros = Set("docm xlsm pptm dotm xltm xlam ppam ppsm sldm potm");
        private static readonly HashSet<string> Archives = Set("zip rar 7z gz tgz cab ace arj tar");

        private static readonly HashSet<string> Endings = Set("com net org io co uk au de fr nz ca us info biz me app dev ru cn in jp br es it nl eu gov edu ai gg tv ly zip mov top xyz click invalid test example");
        private static readonly Regex Threat = new Regex(@"\b(?:suspend(?:ed|sion)?|deactivat(?:ed|ion)|(?:will|has been|have been|was|be) (?:closed|locked|disabled|deleted|terminated|restricted|blocked|frozen)|locked out|unusual (?:sign[- ]?in|activity|login|log[- ]?in)|unauthori[sz]ed (?:access|activity|login|transaction|purchase|charge)|security alert|compromised)\b", Loose, Patience);
        private static readonly Regex Hurry = new Regex(@"\b(?:within|in the next) (?:12|24|48|72|twenty[- ]four|forty[- ]eight) ?(?:hours|hrs|h)\b|\bimmediately\b|\burgent(?:ly)?\b|\bfinal (?:notice|warning|reminder)\b|\blast (?:warning|chance|notice)\b|\baction required\b", Loose, Patience);
        private static readonly Regex Asks = new Regex(@"\b(?:verify|confirm|validate|update|re-?enter|restore|unlock|reactivate)\b[^.!?\n]{0,40}?\b(?:account|identity|password|login|log-in|sign[- ]?in|details|information|credentials|billing|payment)\b|\b(?:enter|type|provide|send|share)\b[^.!?\n]{0,30}?\b(?:password|passcode|pin|login details|security code|one[- ]time code|2fa code|verification code)\b", Loose, Patience);
        private static readonly Regex Money = new Regex(@"\bgift ?cards?\b|\bwire transfer\b|\bbank transfer\b|\b(?:bitcoin|btc|usdt|ethereum|crypto(?:currency)?|wallet address|seed phrase|recovery phrase)\b|\boverdue (?:invoice|payment|balance)\b|\boutstanding (?:invoice|balance|payment)\b|\bpayment (?:failed|declined|unsuccessful)\b|\btax refund\b|\byou(?:'ve| have) won\b|\bclaim your (?:prize|reward|refund)\b", Loose, Patience);
        private static readonly Regex ZipPassword = new Regex(@"\bpassword\b[^.!?\n]{0,40}\b(?:zip|attachment|archive|file|document|open)\b|\b(?:zip|attachment|archive|file|document)\b[^.!?\n]{0,40}\bpassword\b|\bpass(?:word)?\s*(?:is|:)\s*\S+", Loose, Patience);
        private static readonly Regex Anchor = new Regex(@"<a\b([^>]*)>(.*?)</a\s*>", Loose | RegexOptions.Singleline, Patience);
        private static readonly Regex Href = new Regex(@"\bhref\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", Loose, Patience);
        private static readonly Regex Bare = new Regex(@"\b(?:https?://|www\.)[^\s<>""'\)\]]+", Loose, Patience);
        private static readonly Regex Shown = new Regex(@"^\s*(?:https?://)?((?:[a-z0-9-]+\.)+[a-z]{2,})(?:[/?#]\S*)?\s*$", Loose, Patience);

        private static HashSet<string> Set(string words)
        {
            return new HashSet<string>(words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        private sealed class Link
        {
            internal string Href = "", Text = "";
        }

        internal static MailVerdict Check(string raw, List<string> structureNames)
        {
            MailVerdict v = new MailVerdict();
            raw = raw ?? "";
            MailPart root = MailMime.Parse(raw, 0);
            string fromHeader = MailMime.DecodeHeader(root.Header("from"));
            v.From = fromHeader;
            v.Subject = MailMime.DecodeHeader(root.Header("subject"));
            v.MessageId = root.Header("message-id").Trim();
            string fromName, fromAddress, replyName, replyAddress;
            Address(fromHeader, out fromName, out fromAddress);
            Address(MailMime.DecodeHeader(root.Header("reply-to")), out replyName, out replyAddress);
            string fromDomain = MailAccount.Domain(fromAddress), fromSite = Site(fromDomain);
            StringBuilder plain = new StringBuilder(), html = new StringBuilder();
            List<string> files = new List<string>();
            Walk(root, plain, html, files, 0);
            if (structureNames != null) foreach (string name in structureNames) if (!files.Contains(name)) files.Add(name);
            string htmlText = html.Length > 0 ? MailMime.HtmlToText(html.ToString()) : "";
            string words = v.Subject + "\n" + plain + "\n" + htmlText;
            string brand = Claimed(fromName), sender = Official(fromSite);
            Claims(v, fromName, fromAddress, fromSite, brand);
            Replies(v, fromSite, replyAddress, brand);
            Auth(v, root.Header("authentication-results"), fromDomain);
            string org = brand == null ? Organisation(fromName) : null;
            HashSet<string> looked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (fromDomain.Length > 0) Lookalike(v, fromDomain, org, null, looked);
            if (replyAddress.Length > 0) Lookalike(v, MailAccount.Domain(replyAddress), org, null, looked);
            bool asked = Urgency(v, words, sender != null);
            Links(v, Collect(html.ToString(), plain.ToString()), fromSite, sender, org, asked, looked);
            try { if (html.Length > 0 && Regex.IsMatch(html.ToString(), @"<form\b[^>]*\baction\s*=", Loose, Patience)) v.Add(3, "It has a form inside the email that asks you to type things in."); }
            catch (RegexMatchTimeoutException) { }
            Attachments(v, files, words);
            Ends(v, looked);
            v.Settle();
            return v;
        }

        private static void Ends(MailVerdict v, HashSet<string> hosts)
        {
            List<string> made = new List<string>(), odd = new List<string>();
            foreach (string host in hosts)
            {
                string ending = "." + Ending(host);
                if (Reserved.Contains(Ending(host))) { if (!made.Contains(ending)) made.Add(ending); }
                else if (OddEndings.Contains(Ending(host)) && !odd.Contains(ending)) odd.Add(ending);
            }
            made.Sort(StringComparer.Ordinal);
            odd.Sort(StringComparer.Ordinal);
            if (made.Count > 0) v.Add(2, "It points at made-up addresses (" + string.Join(", ", made.ToArray()) + ") that no real website uses.");
            if (odd.Count > 0) v.Add(1, "It points at addresses ending in " + string.Join(", ", odd.ToArray()) + ", endings scammers lean on.");
        }

        private static bool Under(string host, HashSet<string> sites)
        {
            foreach (string site in sites) if (host == site || host.EndsWith("." + site, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void Walk(MailPart p, StringBuilder plain, StringBuilder html, List<string> files, int depth)
        {
            if (p.Parts.Count > 0)
            {
                foreach (MailPart child in p.Parts) Walk(child, plain, html, files, depth + 1);
                return;
            }
            string type = MailMime.ContentType(p.Header("content-type"));
            string name = FileName(p);
            bool attached = p.Header("content-disposition").TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase);
            if (name.Length > 0 && (attached || !type.StartsWith("text/", StringComparison.Ordinal))) { files.Add(name); return; }
            if (attached || type == "message/rfc822" || !type.StartsWith("text/", StringComparison.Ordinal)) return;
            string text;
            try { text = MailMime.Charset(MailMime.Param(p.Header("content-type"), "charset")).GetString(MailMime.TransferDecode(p.Body, p.Header("content-transfer-encoding"))); }
            catch (Exception) { return; }
            if (type == "text/html") { if (html.Length < 400000) html.Append(text).Append('\n'); }
            else if (plain.Length < 200000) plain.Append(text).Append('\n');
        }

        internal static string FileName(MailPart p)
        {
            string name = Parameter(p.Header("content-disposition"), "filename");
            if (name.Length == 0) name = Parameter(p.Header("content-type"), "name");
            return name;
        }

        private static string Parameter(string header, string key)
        {
            if (string.IsNullOrEmpty(header)) return "";
            string plain = MailMime.Param(header, key);
            if (plain.Length > 0) return MailMime.DecodeHeader(plain);
            string starred = MailMime.Param(header, key + "*");
            if (starred.Length > 0) return Rfc2231(starred, true);
            StringBuilder joined = new StringBuilder();
            for (int i = 0; i < 20; i++)
            {
                string piece = MailMime.Param(header, key + "*" + i + "*");
                bool coded = piece.Length > 0;
                if (!coded) piece = MailMime.Param(header, key + "*" + i);
                if (piece.Length == 0) break;
                joined.Append(coded ? Rfc2231(piece, i == 0) : piece);
            }
            return joined.ToString();
        }

        internal static string Rfc2231(string value, bool first)
        {
            string charset = "utf-8", text = value ?? "";
            if (first)
            {
                int a = text.IndexOf('\''), b = a < 0 ? -1 : text.IndexOf('\'', a + 1);
                if (b > a && a >= 0) { if (a > 0) charset = text.Substring(0, a); text = text.Substring(b + 1); }
            }
            List<byte> bytes = new List<byte>();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '%' && i + 2 < text.Length && Uri.IsHexDigit(text[i + 1]) && Uri.IsHexDigit(text[i + 2])) { bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16)); i += 2; }
                else if (text[i] < 256) bytes.Add((byte)text[i]);
            }
            return MailMime.Charset(charset).GetString(bytes.ToArray());
        }

        internal static void StructureNames(object structure, List<string> names)
        {
            List<object> list = structure as List<object>;
            if (list == null) return;
            Dictionary<string, string> pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is List<object>) { StructureNames(list[i], names); continue; }
                string key = list[i] as string, value = i + 1 < list.Count ? list[i + 1] as string : null;
                if (key != null && value != null && !pairs.ContainsKey(key)) pairs[key] = value;
            }
            foreach (string key in new[] { "filename", "name" })
            {
                string found = null, value;
                if (pairs.TryGetValue(key, out value)) found = MailMime.DecodeHeader(value);
                else if (pairs.TryGetValue(key + "*", out value)) found = Rfc2231(value, true);
                else
                {
                    StringBuilder joined = new StringBuilder();
                    for (int i = 0; i < 20; i++)
                    {
                        if (pairs.TryGetValue(key + "*" + i + "*", out value)) joined.Append(Rfc2231(value, i == 0));
                        else if (pairs.TryGetValue(key + "*" + i, out value)) joined.Append(value);
                        else break;
                    }
                    if (joined.Length > 0) found = joined.ToString();
                }
                if (!string.IsNullOrEmpty(found) && !names.Contains(found)) { names.Add(found); break; }
            }
        }

        internal static void Address(string header, out string name, out string address)
        {
            string h = (header ?? "").Trim();
            int open = h.LastIndexOf('<'), close = h.LastIndexOf('>');
            if (open >= 0 && close > open)
            {
                address = h.Substring(open + 1, close - open - 1).Trim();
                name = h.Substring(0, open).Trim().Trim('"', '\'', ' ').Trim();
            }
            else
            {
                address = Regex.Replace(h, @"\([^)]*\)", "").Trim();
                Match comment = Regex.Match(h, @"\(([^)]*)\)");
                name = comment.Success ? comment.Groups[1].Value.Trim() : "";
            }
            if (address.IndexOf('@') < 0) address = "";
        }

        internal static string Site(string host)
        {
            string h = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            string[] labels = h.Split('.');
            if (labels.Length <= 2) return h;
            string lastTwo = labels[labels.Length - 2] + "." + labels[labels.Length - 1];
            string lastThree = labels.Length >= 3 ? labels[labels.Length - 3] + "." + lastTwo : lastTwo;
            if (labels.Length >= 4 && Suffixes.Contains(lastThree)) return labels[labels.Length - 4] + "." + lastThree;
            if (Suffixes.Contains(lastTwo)) return lastThree;
            return lastTwo;
        }

        private static string Label(string site)
        {
            int dot = site.IndexOf('.');
            return dot < 0 ? site : site.Substring(0, dot);
        }

        private static string Ending(string host)
        {
            int dot = host.LastIndexOf('.');
            return dot < 0 ? "" : host.Substring(dot + 1);
        }

        private static string[] Brand(int i, out string[] tokens, out string[] sites)
        {
            string[] parts = Brands[i].Split('|');
            tokens = parts[1].Split(',');
            sites = parts[2].Split(',');
            return parts;
        }

        private static string Official(string site)
        {
            if (string.IsNullOrEmpty(site)) return null;
            for (int i = 0; i < Brands.Length; i++)
            {
                string[] tokens, sites;
                string[] parts = Brand(i, out tokens, out sites);
                if (Array.IndexOf(sites, site) >= 0) return parts[0];
            }
            return null;
        }

        private static List<string> Words(string text)
        {
            List<string> list = new List<string>();
            foreach (string w in Regex.Split((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")) if (w.Length > 0) list.Add(w);
            return list;
        }

        private static string Claimed(string displayName)
        {
            List<string> words = Words(displayName);
            if (words.Count == 0) return null;
            bool service = words.Exists(w => ServiceWords.Contains(w));
            for (int i = 0; i < Brands.Length; i++)
            {
                string[] tokens, sites;
                string[] parts = Brand(i, out tokens, out sites);
                foreach (string raw in tokens)
                {
                    bool common = raw.StartsWith("~", StringComparison.Ordinal);
                    string token = raw.TrimStart('~');
                    bool hit = false;
                    for (int at = 0; at < words.Count && !hit; at++)
                    {
                        string joined = "";
                        for (int n = 0; n < 3 && at + n < words.Count; n++)
                        {
                            joined += words[at + n];
                            if (joined == token) { hit = true; break; }
                        }
                    }
                    if (hit && (!common || service || words.Count == 1)) return parts[0];
                }
            }
            return null;
        }

        private static string Organisation(string displayName)
        {
            List<string> words = Words(displayName);
            if (words.Count < 2 || !words.Exists(w => ServiceWords.Contains(w))) return null;
            string first = words[0];
            if (first.Length < 4 || ServiceWords.Contains(first) || !Regex.IsMatch(first, @"^\p{L}+$")) return null;
            return first;
        }

        private static void Claims(MailVerdict v, string fromName, string fromAddress, string fromSite, string brand)
        {
            if (fromAddress.Length == 0) return;
            bool webmail = Webmail.Contains(fromSite);
            string shownName = Plain(fromName, 60);
            if (brand != null && Official(fromSite) != brand)
            {
                if (webmail) v.Add(4, "It says it's from " + brand + ", but it came from a personal " + fromSite + " address.");
                else v.Add(3, "It says it's from " + brand + ", but it came from " + fromSite + ", which isn't " + brand + "'s.");
            }
            else if (brand == null && webmail && Organisation(fromName) != null)
                v.Add(3, "It says it's from " + shownName + ", but it came from a personal " + fromSite + " address.");
            else if (brand == null && webmail && Words(fromName).Exists(w => w == "support" || w == "security" || w == "billing" || w == "verification" || w == "helpdesk" || w == "administrator" || w == "postmaster"))
                v.Add(3, "It says it's from " + shownName + ", but it came from a personal " + fromSite + " address.");
            Match shown = Regex.Match(fromName ?? "", @"(?:[a-z0-9._%+-]+@)?((?:[a-z0-9-]+\.)+([a-z]{2,6}))\b", Loose);
            if (shown.Success && Endings.Contains(shown.Groups[2].Value))
            {
                string named = Site(shown.Groups[1].Value);
                string namedBrand = Official(named);
                if (named.Length > 0 && named != fromSite && (namedBrand == null || namedBrand != Official(fromSite))) v.Add(3, "Its name shows " + Plain(shown.Value, 60) + ", but it really came from " + fromAddress + ".");
            }
        }

        private static void Replies(MailVerdict v, string fromSite, string replyAddress, string brand)
        {
            if (replyAddress.Length == 0) return;
            string replySite = Site(MailAccount.Domain(replyAddress));
            if (replySite.Length == 0 || replySite == fromSite) return;
            if (Webmail.Contains(replySite) && (!Webmail.Contains(fromSite) || brand != null)) v.Add(3, "Replies would go to a personal " + replySite + " address, not to " + fromSite + ".");
            else v.Add(2, "Replies would go to " + replySite + ", not to " + fromSite + ".");
        }

        private static void Auth(MailVerdict v, string results, string fromDomain)
        {
            if (string.IsNullOrEmpty(results)) return;
            string who = Regex.IsMatch(results, @"^\s*mx\.google\.com\b", Loose) ? "Gmail's own sender check" : "Your mail server's sender check";
            string domain = fromDomain.Length > 0 ? fromDomain : "where it claims";
            if (Regex.IsMatch(results, @"\bdmarc\s*=\s*fail\b", Loose)) v.Add(4, who + " failed: it didn't really come from " + domain + " (DMARC).");
            if (Regex.IsMatch(results, @"\bspf\s*=\s*fail\b", Loose)) v.Add(2, "It came from a server " + domain + " doesn't allow (SPF failed).");
            else if (Regex.IsMatch(results, @"\bspf\s*=\s*softfail\b", Loose)) v.Add(1, "It came from a server " + domain + " doesn't vouch for (SPF softfail).");
            if (Regex.IsMatch(results, @"\bdkim\s*=\s*fail\b", Loose)) v.Add(1, "Its signature didn't check out (DKIM failed).");
        }

        private static bool Urgency(MailVerdict v, string text, bool officialSender)
        {
            List<string> said = new List<string>();
            int points = 0;
            bool asked = false;
            Regex[] kinds = { Threat, Asks, Money, Hurry };
            int[] weights = { 2, 2, 2, 1 };
            for (int i = 0; i < kinds.Length; i++)
            {
                Match m;
                try { m = kinds[i].Match(text.Length > 300000 ? text.Substring(0, 300000) : text); }
                catch (RegexMatchTimeoutException) { continue; }
                if (!m.Success) continue;
                points += weights[i];
                if (i == 1) asked = true;
                said.Add("\"" + Plain(m.Value.ToLowerInvariant(), 40) + "\"");
            }
            if (points > 0 && !officialSender) v.Add(Math.Min(points, 6), "It leans on scary, urgent asks: " + string.Join(", ", said.ToArray()) + ".");
            return asked;
        }

        private static List<Link> Collect(string html, string plain)
        {
            List<Link> links = new List<Link>();
            try
            {
                foreach (Match a in Anchor.Matches(html.Length > 400000 ? html.Substring(0, 400000) : html))
                {
                    if (links.Count >= 200) break;
                    Match h = Href.Match(a.Groups[1].Value);
                    if (!h.Success) continue;
                    string href = h.Groups[1].Success ? h.Groups[1].Value : h.Groups[2].Success ? h.Groups[2].Value : h.Groups[3].Value;
                    links.Add(new Link { Href = WebUtility.HtmlDecode(href).Trim(), Text = MailMime.HtmlToText(a.Groups[2].Value) });
                }
            }
            catch (RegexMatchTimeoutException) { }
            try
            {
                foreach (Match m in Bare.Matches(plain.Length > 200000 ? plain.Substring(0, 200000) : plain))
                {
                    if (links.Count >= 300) break;
                    links.Add(new Link { Href = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', '>'), Text = "" });
                }
            }
            catch (RegexMatchTimeoutException) { }
            return links;
        }

        internal static string Host(string url, out string userInfo, out string scheme)
        {
            userInfo = "";
            scheme = "";
            string u = (url ?? "").Trim();
            Match s = Regex.Match(u, @"^([a-z][a-z0-9+.-]*):", Loose);
            if (s.Success) scheme = s.Groups[1].Value.ToLowerInvariant();
            if (u.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) { scheme = "http"; u = "http://" + u; }
            if (scheme != "http" && scheme != "https") return "";
            int start = u.IndexOf("//", StringComparison.Ordinal);
            if (start < 0) return "";
            string rest = u.Substring(start + 2);
            int end = rest.IndexOfAny(new[] { '/', '?', '#', '\\' });
            string authority = end < 0 ? rest : rest.Substring(0, end);
            int at = authority.LastIndexOf('@');
            if (at >= 0) { userInfo = authority.Substring(0, at); authority = authority.Substring(at + 1); }
            if (authority.StartsWith("[", StringComparison.Ordinal)) { int close = authority.IndexOf(']'); return close > 0 ? authority.Substring(0, close + 1) : authority; }
            int colon = authority.LastIndexOf(':');
            if (colon >= 0) authority = authority.Substring(0, colon);
            return WebUtility.UrlDecode(authority).Trim().TrimEnd('.').ToLowerInvariant();
        }

        private static bool Numeric(string host)
        {
            return host.StartsWith("[", StringComparison.Ordinal) || Regex.IsMatch(host, @"^\d{1,3}(?:\.\d{1,3}){3}$") || Regex.IsMatch(host, @"^(?:0x[0-9a-f]+|\d{6,})$", Loose);
        }

        private static void Links(MailVerdict v, List<Link> links, string fromSite, string sender, string org, bool asked, HashSet<string> looked)
        {
            int mismatches = 0;
            foreach (Link link in links)
            {
                string userInfo, scheme;
                string host = Host(link.Href, out userInfo, out scheme);
                if (scheme == "javascript" || scheme == "data" || scheme == "vbscript") { v.Add(3, "A link runs code or opens a hidden page instead of a website."); continue; }
                if (host.Length == 0) continue;
                string site = Site(host);
                if (userInfo.Length > 0 && Regex.IsMatch(userInfo, @"[a-z0-9-]+\.[a-z]{2,}", Loose)) v.Add(4, "A link pretends to go to " + Plain(userInfo, 50) + " but really goes to " + host + ".");
                if (Numeric(host)) v.Add(3, "A link goes to a bare number address (" + Plain(host, 40) + ") instead of a name.");
                if (Shorteners.Contains(host) && !(sender != null && Official(Site(host)) == sender)) v.Add(1, "A link hides where it goes behind a link shortener (" + host + ").");
                if (Under(host, FreeHosts)) v.Add(asked ? 2 : 1, "A link goes to a free website host (" + site + "), where fake sign-in pages often live.");
                Match shown = null;
                try { shown = Shown.Match(link.Text ?? ""); }
                catch (RegexMatchTimeoutException) { }
                if (shown != null && shown.Success && mismatches < 2)
                {
                    string shownHost = shown.Groups[1].Value.ToLowerInvariant(), shownSite = Site(shownHost);
                    bool tracked = Under(host, Trackers) || site == fromSite;
                    bool same = shownSite == site || (Official(shownSite) != null && Official(shownSite) == Official(site));
                    if (!same && !tracked)
                    {
                        v.Add(4, "A link shows " + Plain(shownHost, 60) + " but really goes to " + Plain(host, 60) + ".");
                        mismatches++;
                    }
                }
                if (!Numeric(host)) Lookalike(v, host, org, fromSite, looked);
            }
        }

        private static string Fold(string text, char one)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            foreach (char c in text.ToLowerInvariant())
            {
                switch (c)
                {
                    case '0': sb.Append('o'); break;
                    case '1': sb.Append(one); break;
                    case '3': sb.Append('e'); break;
                    case '4': sb.Append('a'); break;
                    case '5': sb.Append('s'); break;
                    case '7': sb.Append('t'); break;
                    case '8': sb.Append('b'); break;
                    case '$': sb.Append('s'); break;
                    case '@': sb.Append('a'); break;
                    case '|': sb.Append('l'); break;
                    case '\u0430': sb.Append('a'); break;
                    case '\u0435': sb.Append('e'); break;
                    case '\u043e': sb.Append('o'); break;
                    case '\u0440': sb.Append('p'); break;
                    case '\u0441': sb.Append('c'); break;
                    case '\u0443': sb.Append('y'); break;
                    case '\u0445': sb.Append('x'); break;
                    case '\u0456': sb.Append('i'); break;
                    case '\u0458': sb.Append('j'); break;
                    case '\u0455': sb.Append('s'); break;
                    case '\u04bb': sb.Append('h'); break;
                    case '\u0501': sb.Append('d'); break;
                    case '\u0261': sb.Append('g'); break;
                    case '\u03bf': sb.Append('o'); break;
                    case '\u03b1': sb.Append('a'); break;
                    case '\u03bd': sb.Append('v'); break;
                    case '\u0131': sb.Append('i'); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString().Replace("rn", "m").Replace("vv", "w");
        }

        private static int Distance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + cost);
                }
            return d[a.Length, b.Length];
        }

        private static bool Wraps(string label, string token, bool strict)
        {
            if (label == token || label.Length <= token.Length) return false;
            string[] parts = label.Split('-');
            bool named = false, baited = false;
            foreach (string part in parts)
            {
                if (part == token || Fold(part, 'l') == token || Fold(part, 'i') == token) named = true;
                else if (Bait.Contains(part) || Bait.Contains(Fold(part, 'l')) || Bait.Contains(Fold(part, 'i'))) baited = true;
            }
            if (named && parts.Length > 1) return !strict || baited;
            if (token.Length < 5) return false;
            if (label.StartsWith(token, StringComparison.Ordinal) && Bait.Contains(Fold(label.Substring(token.Length), 'l'))) return true;
            if (label.EndsWith(token, StringComparison.Ordinal) && Bait.Contains(Fold(label.Substring(0, label.Length - token.Length), 'l'))) return true;
            return false;
        }

        private static void Lookalike(MailVerdict v, string host, string org, string fromSite, HashSet<string> looked)
        {
            if (string.IsNullOrEmpty(host) || !looked.Add(host)) return;
            string ascii = host, shown = host;
            bool foreign = false;
            if (host.IndexOf("xn--", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try { shown = new IdnMapping().GetUnicode(host); foreign = true; }
                catch (ArgumentException) { }
            }
            else
            {
                foreach (char c in host) if (c > 127) { foreign = true; break; }
                if (foreign) try { ascii = new IdnMapping().GetAscii(host); } catch (ArgumentException) { }
            }
            string site = Site(shown), label = Label(site), folded = Fold(label, 'l'), foldedI = Fold(label, 'i');
            bool official = Official(Site(ascii)) != null || Official(site) != null;
            int before = v.Reasons.Count;
            if (!official)
            {
                for (int i = 0; i < Brands.Length && v.Reasons.Count == before; i++)
                {
                    string[] tokens, sites;
                    string[] parts = Brand(i, out tokens, out sites);
                    foreach (string raw in tokens)
                    {
                        bool common = raw.StartsWith("~", StringComparison.Ordinal);
                        string token = raw.TrimStart('~');
                        if (foreign && (folded == token || foldedI == token) && label != token) { v.Add(4, Plain(host, 60) + " spells " + parts[0] + " with letters from another alphabet."); break; }
                        if ((folded == token || foldedI == token) && label != token) { v.Add(4, Plain(host, 60) + " looks like " + parts[0] + "'s address, with look-alike characters swapped in."); break; }
                        if (label == token && Ending(site).Length > 2 && !common) { v.Add(3, Plain(host, 60) + " uses " + parts[0] + "'s name on an address " + parts[0] + " doesn't use."); break; }
                        if (!common && token.Length >= 5 && label.Length >= 5 && Distance(label, token) == 1) { v.Add(3, Plain(host, 60) + " is one letter off " + parts[0] + "'s name."); break; }
                        if (Wraps(label, token, common)) { v.Add(3, Plain(host, 60) + " wraps " + parts[0] + "'s name in extra words to look official."); break; }
                        string under = shown.Length > site.Length ? shown.Substring(0, shown.Length - site.Length) : "";
                        if (Regex.IsMatch(under, @"(?:^|[.-])" + Regex.Escape(token) + @"(?:[.-])", Loose) && token.Length >= 4) { v.Add(3, Plain(host, 60) + " puts " + parts[0] + "'s name in front of someone else's address (" + site + ")."); break; }
                    }
                }
            }
            if (foreign && v.Reasons.Count == before && Regex.IsMatch(shown, @"\p{IsCyrillic}|\p{IsGreekandCoptic}") && Regex.IsMatch(shown, @"[a-z]")) v.Add(2, Plain(shown, 60) + " mixes letters from different alphabets.");
            if (org != null && !official && site != fromSite && Wraps(label, org, true)) v.Add(3, Plain(host, 60) + " wraps " + Up(org) + " in extra words to look official.");
            foreach (string word in label.Split('-'))
            {
                string f = Fold(word, 'l'), g = Fold(word, 'i');
                if (word != f && Regex.IsMatch(word, @"\d") && Regex.IsMatch(word, @"[a-z]") && (Bait.Contains(f) || Bait.Contains(g)))
                {
                    v.Add(2, Plain(host, 60) + " spells " + (Bait.Contains(f) ? f : g) + " as " + word + ", a look-alike trick.");
                    break;
                }
            }
        }

        private static void Attachments(MailVerdict v, List<string> files, string words)
        {
            bool zipPassword;
            try { zipPassword = ZipPassword.IsMatch(words.Length > 300000 ? words.Substring(0, 300000) : words); }
            catch (RegexMatchTimeoutException) { zipPassword = false; }
            foreach (string file in files)
            {
                string name = (file ?? "").Trim();
                if (name.Length == 0) continue;
                string shown = Plain(name.Replace("\u202e", ""), 60);
                if (name.IndexOf('\u202e') >= 0) { v.Add(4, "An attachment's name (" + shown + ") hides its real ending with a reversed-text trick."); continue; }
                Match two = Regex.Match(name, @"\.([a-z0-9]{2,4})\s*\.([a-z0-9]{2,12})\s*$", Loose);
                string ending = Ending(name.ToLowerInvariant()).Trim();
                if (two.Success && Regex.IsMatch(two.Groups[1].Value, @"^(?:pdf|docx?|xlsx?|pptx?|txt|jpe?g|png|gif|zip|rar|mp3|mp4|mov|wav|csv|rtf)$", Loose) && !Archives.Contains(two.Groups[2].Value))
                    v.Add(4, "An attachment, " + shown + ", wears two endings to hide what it really is.");
                else if (Risky.Contains(ending)) v.Add(4, "It has an attachment, " + shown + ", of a kind that can run code or open a fake sign-in page.");
                else if (Macros.Contains(ending)) v.Add(2, "It has a macro document, " + shown + ", which can run code when it's opened.");
                else if (Archives.Contains(ending) && zipPassword) v.Add(4, "It has a locked archive, " + shown + ", with the password in the email, a trick to slip past virus checks.");
            }
        }

        private static string Plain(string text, int most)
        {
            string line = Regex.Replace(text ?? "", @"[\p{Cc}\p{Cf}\s]+", " ").Trim();
            if (line.Length > most) line = line.Substring(0, most).TrimEnd() + "...";
            return line;
        }

        private static string Up(string word) { return string.IsNullOrEmpty(word) ? word : char.ToUpperInvariant(word[0]) + word.Substring(1); }

        internal static string Said(MailVerdict v, bool seen)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Verdict: " + v.Level);
            if (v.Level == MailVerdict.Phishing) sb.Append(v.Sure ? " (almost certainly a scam)" : " (very likely a scam)");
            else if (v.Level == MailVerdict.Suspicious) sb.Append(" (some warning signs, so go carefully)");
            else sb.Append(" (nothing here looks like phishing)");
            sb.Append(".\n");
            sb.Append("From: " + Plain(v.From, 200) + "\n");
            sb.Append("Subject: " + (v.Subject.Length == 0 ? "(no subject)" : Plain(v.Subject, 200)) + "\n");
            if (v.Reasons.Count > 0)
            {
                sb.Append(v.Level == MailVerdict.Fine ? "Small things, not enough to worry about:\n" : "Why:\n");
                for (int i = 0; i < v.Reasons.Count && i < 8; i++) sb.Append("- " + v.Reasons[i] + "\n");
            }
            sb.Append("Its links and attachments weren't opened" + (seen ? "." : ", and it's still unread."));
            return sb.ToString();
        }
    }
}
