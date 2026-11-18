using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CapriDesktop
{
    internal static class RemotePush
    {
        internal const string Feature = "push";
        internal const string Subject = "https://capriai.app/remote/";
        private const int Record = 4096, KeyBytes = 65, SaltBytes = 16, AuthBytes = 16, MaxEndpoint = 1024;
        private static readonly ECCurve Curve = ECCurve.NamedCurves.nistP256;
        private static readonly string[] Suffixes = { ".push.services.mozilla.com", ".push.apple.com", ".notify.windows.com" };

        internal sealed class Keys { internal byte[] Private; internal byte[] Public; }

        internal static Keys NewKeys()
        {
            using (ECDsa ec = ECDsa.Create(Curve))
            {
                ECParameters p = ec.ExportParameters(true);
                Keys keys = new Keys();
                keys.Private = Fixed(p.D);
                keys.Public = Join(new byte[] { 4 }, Fixed(p.Q.X), Fixed(p.Q.Y));
                return keys;
            }
        }

        internal static Keys FromPrivate(byte[] privateScalar)
        {
            if (privateScalar == null || privateScalar.Length != 32) return null;
            uint[] d = Field.FromBytes(privateScalar, 0);
            if (Field.IsZero(d) || !Field.Less(d, Field.N)) return null;
            byte[] point = Field.Multiply(d);
            if (point == null || !PointOk(point)) return null;
            Keys keys = new Keys();
            keys.Private = (byte[])privateScalar.Clone();
            keys.Public = point;
            return keys;
        }

        internal static bool EndpointOk(string endpoint)
        {
            if (endpoint == null || endpoint.Length > MaxEndpoint) return false;
            foreach (char c in endpoint)
                if (c < 0x21 || c > 0x7e) return false;
            if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            int end = endpoint.Length;
            for (int i = 8; i < endpoint.Length; i++)
                if (endpoint[i] == '/' || endpoint[i] == '?' || endpoint[i] == '#') { end = i; break; }
            string host = endpoint.Substring(8, end - 8).ToLowerInvariant();
            if (host.Length == 0 || host.Length > 253) return false;
            foreach (char c in host)
                if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '.')) return false;
            if (host.StartsWith(".") || host.EndsWith(".") || host.Contains("..")) return false;
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != host || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            if (host == "fcm.googleapis.com") return true;
            foreach (string suffix in Suffixes)
                if (host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static bool PointOk(byte[] p256dh)
        {
            if (p256dh == null || p256dh.Length != KeyBytes || p256dh[0] != 4) return false;
            uint[] x = Field.FromBytes(p256dh, 1), y = Field.FromBytes(p256dh, 33);
            return Field.OnCurve(x, y);
        }

        internal static byte[] Encrypt(byte[] plain, byte[] p256dh, byte[] auth)
        {
            Keys sender = NewKeys();
            return Encrypt(plain, p256dh, auth, sender.Private, sender.Public, RemoteCrypto.Random(SaltBytes));
        }

        internal static byte[] Encrypt(byte[] plain, byte[] p256dh, byte[] auth, byte[] senderPrivate, byte[] senderPublic, byte[] salt)
        {
            if (plain == null || auth == null || auth.Length != AuthBytes || salt == null || salt.Length != SaltBytes) return null;
            if (senderPrivate == null || senderPrivate.Length != 32 || senderPublic == null || senderPublic.Length != KeyBytes) return null;
            if (plain.Length > Record - 17 || !PointOk(p256dh)) return null;
            try
            {
                byte[] cek, nonce;
                Derive(senderPrivate, senderPublic, p256dh, p256dh, senderPublic, auth, salt, out cek, out nonce);
                byte[] padded = new byte[plain.Length + 1];
                Buffer.BlockCopy(plain, 0, padded, 0, plain.Length);
                padded[plain.Length] = 2;
                byte[] sealedBytes = GcmSeal(cek, nonce, new byte[0], padded);
                byte[] head = new byte[SaltBytes + 5];
                Buffer.BlockCopy(salt, 0, head, 0, SaltBytes);
                head[16] = (byte)((Record >> 24) & 255);
                head[17] = (byte)((Record >> 16) & 255);
                head[18] = (byte)((Record >> 8) & 255);
                head[19] = (byte)(Record & 255);
                head[20] = KeyBytes;
                return Join(head, senderPublic, sealedBytes);
            }
            catch (Exception) { return null; }
        }

        internal static byte[] Decrypt(byte[] body, byte[] uaPrivate, byte[] uaPublic, byte[] auth)
        {
            try
            {
                if (body == null || uaPrivate == null || uaPrivate.Length != 32 || uaPublic == null || uaPublic.Length != KeyBytes || auth == null || auth.Length != AuthBytes) return null;
                int start = SaltBytes + 5 + KeyBytes;
                if (body.Length < start + 17) return null;
                long rs = ((long)body[16] << 24) | ((long)body[17] << 16) | ((long)body[18] << 8) | body[19];
                if (body[20] != KeyBytes || rs < 18 || body.Length - start > rs) return null;
                byte[] salt = new byte[SaltBytes], sender = new byte[KeyBytes], cipher = new byte[body.Length - start];
                Buffer.BlockCopy(body, 0, salt, 0, SaltBytes);
                Buffer.BlockCopy(body, 21, sender, 0, KeyBytes);
                Buffer.BlockCopy(body, start, cipher, 0, cipher.Length);
                if (!PointOk(sender)) return null;
                byte[] cek, nonce;
                Derive(uaPrivate, uaPublic, sender, uaPublic, sender, auth, salt, out cek, out nonce);
                byte[] padded = GcmOpen(cek, nonce, new byte[0], cipher);
                if (padded == null) return null;
                int n = padded.Length;
                while (n > 0 && padded[n - 1] == 0) n--;
                if (n == 0 || padded[n - 1] != 2) return null;
                byte[] plain = new byte[n - 1];
                Buffer.BlockCopy(padded, 0, plain, 0, n - 1);
                return plain;
            }
            catch (Exception) { return null; }
        }

        internal static string Vapid(string endpoint, Keys keys, long unixSeconds)
        {
            Uri uri = new Uri(endpoint);
            string header = "{\"typ\":\"JWT\",\"alg\":\"ES256\"}";
            string claims = "{\"aud\":\"" + uri.Scheme + "://" + uri.Host + "\",\"exp\":" + (unixSeconds + 43200).ToString(CultureInfo.InvariantCulture) + ",\"sub\":\"" + Subject + "\"}";
            string signing = RemoteCrypto.B64u(Encoding.UTF8.GetBytes(header)) + "." + RemoteCrypto.B64u(Encoding.UTF8.GetBytes(claims));
            using (ECDsa ec = ECDsa.Create(Curve))
            {
                ec.ImportParameters(Params(keys.Private, keys.Public));
                byte[] signature = ec.SignData(Encoding.ASCII.GetBytes(signing), HashAlgorithmName.SHA256);
                return "vapid t=" + signing + "." + RemoteCrypto.B64u(signature) + ", k=" + RemoteCrypto.B64u(keys.Public);
            }
        }

        internal static string Post(string endpoint, byte[] body, string authorization, int ttl, string urgency, string topic)
        {
            try
            {
                if (!EndpointOk(endpoint) || body == null) return "failed";
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
                request.Method = "POST";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.AllowAutoRedirect = false;
                request.ContentType = "application/octet-stream";
                request.ContentLength = body.Length;
                request.Headers["TTL"] = ttl.ToString(CultureInfo.InvariantCulture);
                if (urgency != null) request.Headers["Urgency"] = urgency;
                if (topic != null) request.Headers["Topic"] = topic;
                request.Headers["Content-Encoding"] = "aes128gcm";
                request.Headers["Authorization"] = authorization;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);
                int code;
                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                        code = (int)response.StatusCode;
                }
                catch (WebException e)
                {
                    HttpWebResponse response = e.Response as HttpWebResponse;
                    if (response == null) return "failed";
                    code = (int)response.StatusCode;
                    response.Close();
                }
                if (code == 200 || code == 201 || code == 202) return "sent";
                if (code == 404 || code == 410) return "gone";
                return "failed";
            }
            catch (Exception) { return "failed"; }
        }

        internal static void Smoke(List<string> checks)
        {
            byte[] asPrivate = Need("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw", 32);
            byte[] asPublic = Need("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8", 65);
            byte[] uaPublic = Need("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4", 65);
            byte[] uaPrivate = Need("q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94", 32);
            byte[] salt = Need("DGv6ra1nlYgDCS1FRnbzlw", 16);
            byte[] auth = Need("BTBZMqHH6r4Tts7J_aSIgg", 16);
            byte[] plain = Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon");
            string expected = "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

            byte[] body = Encrypt(plain, uaPublic, auth, asPrivate, asPublic, salt);
            if (body == null || RemoteCrypto.B64u(body) != expected) throw new Exception("Web push encryption does not match RFC 8291 appendix A");
            checks.Add("PASS web push encryption matches RFC 8291 appendix A byte for byte");

            byte[] back = Decrypt(body, uaPrivate, uaPublic, auth);
            if (back == null || Encoding.UTF8.GetString(back) != "When I grow up, I want to be a watermelon") throw new Exception("Web push decryption of the RFC 8291 example failed");
            Keys fresh = NewKeys();
            byte[] message = Encoding.UTF8.GetBytes("{\"kind\":\"test\",\"title\":\"Capri\"}");
            byte[] one = Encrypt(message, fresh.Public, auth), two = Encrypt(message, fresh.Public, auth);
            if (one == null || two == null || RemoteCrypto.B64u(one) == RemoteCrypto.B64u(two)) throw new Exception("Web push encryption did not use a fresh key and salt");
            byte[] round = Decrypt(one, fresh.Private, fresh.Public, auth);
            if (round == null || Encoding.UTF8.GetString(round) != Encoding.UTF8.GetString(message)) throw new Exception("Web push did not round-trip");
            for (int i = 0; i < one.Length; i += 7)
            {
                byte[] bad = (byte[])one.Clone();
                bad[i] ^= 1;
                if (Decrypt(bad, fresh.Private, fresh.Public, auth) != null) throw new Exception("A tampered web push body was accepted at byte " + i);
            }
            byte[] shortAuth = new byte[16];
            if (Decrypt(one, fresh.Private, fresh.Public, shortAuth) != null) throw new Exception("A web push body opened with the wrong auth secret");
            if (Encrypt(new byte[4080], fresh.Public, auth) != null || Encrypt(new byte[4079], fresh.Public, auth) == null) throw new Exception("Web push size limit is wrong");
            checks.Add("PASS web push decrypts, round-trips with fresh keys and salt, and refuses a tampered body");

            byte[] k = Hex("feffe9928665731c6d6a8f9467308308");
            byte[] p = Hex("d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39");
            byte[] a = Hex("feedfacedeadbeeffeedfacedeadbeefabaddad2");
            byte[] iv = Hex("cafebabefacedbaddecaf888");
            string want = "42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e0915bc94fbc3221a5db94fae95ae7121a47";
            byte[] g = GcmSeal(k, iv, a, p);
            if (Convert.ToBase64String(g) != Convert.ToBase64String(Hex(want))) throw new Exception("AES-GCM does not match the NIST test case 4");
            byte[] opened = GcmOpen(k, iv, a, g);
            if (opened == null || Convert.ToBase64String(opened) != Convert.ToBase64String(p)) throw new Exception("AES-GCM did not open the NIST test case 4");
            g[3] ^= 1;
            if (GcmOpen(k, iv, a, g) != null) throw new Exception("AES-GCM accepted a tampered message");
            byte[] zeroKey = new byte[16], zeroIv = new byte[12];
            if (Convert.ToBase64String(GcmSeal(zeroKey, zeroIv, new byte[0], new byte[0])) != Convert.ToBase64String(Hex("58e2fccefa7e3061367f1d57a4e7455a"))) throw new Exception("AES-GCM does not match the NIST test case 1");
            checks.Add("PASS AES-128-GCM matches the NIST test cases 1 and 4 and refuses a tampered message");

            Keys vapidKeys = NewKeys();
            string endpoint = "https://fcm.googleapis.com/fcm/send/abc:def_123";
            long now = 1700000000L;
            string header = Vapid(endpoint, vapidKeys, now);
            if (!header.StartsWith("vapid t=") || !header.EndsWith(", k=" + RemoteCrypto.B64u(vapidKeys.Public))) throw new Exception("VAPID header is malformed");
            string jwt = header.Substring(8, header.IndexOf(", k=", StringComparison.Ordinal) - 8);
            string[] parts = jwt.Split('.');
            if (parts.Length != 3) throw new Exception("VAPID token does not have three parts");
            byte[] h = RemoteCrypto.FromB64u(parts[0]), c = RemoteCrypto.FromB64u(parts[1]), sig = RemoteCrypto.FromB64u(parts[2], 64);
            if (h == null || c == null || sig == null) throw new Exception("VAPID token is not base64url");
            if (Encoding.UTF8.GetString(h) != "{\"typ\":\"JWT\",\"alg\":\"ES256\"}") throw new Exception("VAPID header is wrong");
            if (Encoding.UTF8.GetString(c) != "{\"aud\":\"https://fcm.googleapis.com\",\"exp\":1700043200,\"sub\":\"https://capriai.app/remote/\"}") throw new Exception("VAPID claims are wrong");
            using (ECDsa ec = ECDsa.Create(Curve))
            {
                ec.ImportParameters(Params(null, vapidKeys.Public));
                if (!ec.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), sig, HashAlgorithmName.SHA256)) throw new Exception("VAPID signature does not verify");
            }
            checks.Add("PASS VAPID token has the right aud, exp and sub and its ES256 signature verifies");

            string[] good = { "https://fcm.googleapis.com/fcm/send/abc", "https://FCM.googleapis.com/wpush/v2/xyz", "https://updates.push.services.mozilla.com/wpush/v2/abc", "https://web.push.apple.com/QGxyz", "https://db5p.notify.windows.com/?token=abc" };
            foreach (string e in good)
                if (!EndpointOk(e)) throw new Exception("Push endpoint refused: " + e);
            string[] bad2 = { "http://fcm.googleapis.com/fcm/send/abc", "https://fcm.googleapis.com:443/fcm/send/abc", "https://fcm.googleapis.com:8443/x", "https://user@fcm.googleapis.com/x", "https://user:pw@fcm.googleapis.com/x", "https://fcm.googleapis.com@evil.com/x", "https://evil.com/fcm.googleapis.com", "https://evilfcm.googleapis.com.attacker.com/x", "https://fcm.googleapis.com.evil.com/x", "https://xpush.apple.com/x", "https://push.apple.com/x", "https://evilfcm.googleapis.com/x", "https://127.0.0.1/x", "https://[::1]/x", "https://10.0.0.1.push.apple.com.evil.com/x", "https://fcm.googleapis.com\\@evil.com/x", "https://fcm.googleapis.com./x", "", "ftp://fcm.googleapis.com/x", "https://a.push.apple.com.evil/x", "https://fcm.googleapis.com/" + new string('a', 1010) };
            foreach (string e in bad2)
                if (EndpointOk(e)) throw new Exception("Push endpoint accepted: " + e);
            if (EndpointOk(null) || EndpointOk("https://fcm.googleapis.com/" + new string('a', 996)) == false) throw new Exception("Push endpoint length rule is wrong");
            checks.Add("PASS push endpoints on FCM, Mozilla, Apple and Windows hosts are taken and lookalikes, http, ports, user info, IPs and long ones are refused");

            if (!PointOk(asPublic) || !PointOk(uaPublic) || !PointOk(vapidKeys.Public)) throw new Exception("A real P-256 point was refused");
            byte[] cut = new byte[64];
            Buffer.BlockCopy(asPublic, 0, cut, 0, 64);
            byte[] prefix = (byte[])asPublic.Clone();
            prefix[0] = 3;
            byte[] off = (byte[])asPublic.Clone();
            off[64] ^= 1;
            byte[] zero = new byte[65];
            zero[0] = 4;
            byte[] big = (byte[])asPublic.Clone();
            for (int i = 1; i <= 32; i++) big[i] = 255;
            if (PointOk(null) || PointOk(cut) || PointOk(prefix) || PointOk(off) || PointOk(zero) || PointOk(big) || PointOk(new byte[66])) throw new Exception("A bad P-256 point was accepted");
            Keys rebuilt = FromPrivate(asPrivate);
            if (rebuilt == null || RemoteCrypto.B64u(rebuilt.Public) != RemoteCrypto.B64u(asPublic)) throw new Exception("Public key from private scalar is wrong");
            Keys again = FromPrivate(vapidKeys.Private);
            if (again == null || RemoteCrypto.B64u(again.Public) != RemoteCrypto.B64u(vapidKeys.Public)) throw new Exception("Public key from a fresh private scalar is wrong");
            if (FromPrivate(new byte[32]) != null || FromPrivate(new byte[31]) != null || FromPrivate(null) != null) throw new Exception("A bad private scalar was accepted");
            checks.Add("PASS P-256 points are checked on the curve and the public key rebuilds from the private scalar");
        }

        private static byte[] Need(string text, int length)
        {
            byte[] bytes = RemoteCrypto.FromB64u(text, length);
            if (bytes == null) throw new Exception("Push test value is not base64url");
            return bytes;
        }

        private static byte[] Hex(string text)
        {
            byte[] bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(text.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        private static byte[] Fixed(byte[] value)
        {
            if (value.Length == 32) return value;
            byte[] fixedBytes = new byte[32];
            if (value.Length > 32) Buffer.BlockCopy(value, value.Length - 32, fixedBytes, 0, 32);
            else Buffer.BlockCopy(value, 0, fixedBytes, 32 - value.Length, value.Length);
            return fixedBytes;
        }

        private static byte[] Join(params byte[][] parts)
        {
            int total = 0;
            foreach (byte[] part in parts) total += part.Length;
            byte[] all = new byte[total];
            int at = 0;
            foreach (byte[] part in parts)
            {
                Buffer.BlockCopy(part, 0, all, at, part.Length);
                at += part.Length;
            }
            return all;
        }

        private static ECParameters Params(byte[] d, byte[] point)
        {
            ECParameters p = new ECParameters();
            p.Curve = Curve;
            byte[] x = new byte[32], y = new byte[32];
            Buffer.BlockCopy(point, 1, x, 0, 32);
            Buffer.BlockCopy(point, 33, y, 0, 32);
            p.Q.X = x;
            p.Q.Y = y;
            if (d != null) p.D = d;
            return p;
        }

        private static byte[] Hmac(byte[] key, byte[] data)
        {
            using (HMACSHA256 h = new HMACSHA256(key)) return h.ComputeHash(data);
        }

        private static void Derive(byte[] myPrivate, byte[] myPublic, byte[] peerPublic, byte[] uaPublic, byte[] asPublic, byte[] auth, byte[] salt, out byte[] cek, out byte[] nonce)
        {
            byte[] prkKey;
            using (ECDiffieHellman me = ECDiffieHellman.Create(Curve))
            using (ECDiffieHellman peer = ECDiffieHellman.Create(Curve))
            {
                me.ImportParameters(Params(myPrivate, myPublic));
                peer.ImportParameters(Params(null, peerPublic));
                prkKey = me.DeriveKeyFromHmac(peer.PublicKey, HashAlgorithmName.SHA256, auth);
            }
            byte[] info = Join(Encoding.ASCII.GetBytes("WebPush: info\0"), uaPublic, asPublic);
            byte[] ikm = Hmac(prkKey, Join(info, new byte[] { 1 }));
            byte[] prk = Hmac(salt, ikm);
            byte[] key = Hmac(prk, Join(Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"), new byte[] { 1 }));
            byte[] iv = Hmac(prk, Join(Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"), new byte[] { 1 }));
            cek = new byte[16];
            nonce = new byte[12];
            Buffer.BlockCopy(key, 0, cek, 0, 16);
            Buffer.BlockCopy(iv, 0, nonce, 0, 12);
        }

        private static byte[] Block(ICryptoTransform ecb, byte[] input)
        {
            byte[] output = new byte[16];
            ecb.TransformBlock(input, 0, 16, output, 0);
            return output;
        }

        private static void Mul(byte[] x, byte[] h, byte[] result)
        {
            ulong zh = 0, zl = 0, vh = 0, vl = 0;
            for (int i = 0; i < 8; i++)
            {
                vh = (vh << 8) | h[i];
                vl = (vl << 8) | h[8 + i];
            }
            for (int i = 0; i < 128; i++)
            {
                if (((x[i >> 3] >> (7 - (i & 7))) & 1) != 0)
                {
                    zh ^= vh;
                    zl ^= vl;
                }
                bool lsb = (vl & 1) != 0;
                vl = (vl >> 1) | (vh << 63);
                vh >>= 1;
                if (lsb) vh ^= 0xe100000000000000UL;
            }
            for (int i = 0; i < 8; i++)
            {
                result[i] = (byte)(zh >> (56 - 8 * i));
                result[8 + i] = (byte)(zl >> (56 - 8 * i));
            }
        }

        private static void Ghash(byte[] h, byte[] y, byte[] data, int length)
        {
            for (int at = 0; at < length; at += 16)
            {
                int n = Math.Min(16, length - at);
                for (int i = 0; i < n; i++) y[i] ^= data[at + i];
                Mul(y, h, y);
            }
        }

        private static byte[] Tag(ICryptoTransform ecb, byte[] h, byte[] j0, byte[] aad, byte[] cipher)
        {
            byte[] y = new byte[16];
            Ghash(h, y, aad, aad.Length);
            Ghash(h, y, cipher, cipher.Length);
            byte[] lengths = new byte[16];
            ulong abits = (ulong)aad.Length * 8, cbits = (ulong)cipher.Length * 8;
            for (int i = 0; i < 8; i++)
            {
                lengths[i] = (byte)(abits >> (56 - 8 * i));
                lengths[8 + i] = (byte)(cbits >> (56 - 8 * i));
            }
            Ghash(h, y, lengths, 16);
            byte[] mask = Block(ecb, j0);
            for (int i = 0; i < 16; i++) y[i] ^= mask[i];
            return y;
        }

        private static byte[] Ctr(ICryptoTransform ecb, byte[] j0, byte[] input)
        {
            byte[] counter = (byte[])j0.Clone();
            byte[] output = new byte[input.Length];
            for (int at = 0; at < input.Length; at += 16)
            {
                for (int i = 15; i >= 12; i--)
                    if (++counter[i] != 0) break;
                byte[] stream = Block(ecb, counter);
                int n = Math.Min(16, input.Length - at);
                for (int i = 0; i < n; i++) output[at + i] = (byte)(input[at + i] ^ stream[i]);
            }
            return output;
        }

        private static byte[] GcmSeal(byte[] key, byte[] iv, byte[] aad, byte[] plain)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                using (ICryptoTransform ecb = aes.CreateEncryptor())
                {
                    byte[] h = Block(ecb, new byte[16]);
                    byte[] j0 = new byte[16];
                    Buffer.BlockCopy(iv, 0, j0, 0, 12);
                    j0[15] = 1;
                    byte[] cipher = Ctr(ecb, j0, plain);
                    return Join(cipher, Tag(ecb, h, j0, aad, cipher));
                }
            }
        }

        private static byte[] GcmOpen(byte[] key, byte[] iv, byte[] aad, byte[] sealedBytes)
        {
            if (sealedBytes.Length < 16) return null;
            byte[] cipher = new byte[sealedBytes.Length - 16];
            Buffer.BlockCopy(sealedBytes, 0, cipher, 0, cipher.Length);
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = key;
                using (ICryptoTransform ecb = aes.CreateEncryptor())
                {
                    byte[] h = Block(ecb, new byte[16]);
                    byte[] j0 = new byte[16];
                    Buffer.BlockCopy(iv, 0, j0, 0, 12);
                    j0[15] = 1;
                    byte[] tag = Tag(ecb, h, j0, aad, cipher);
                    int diff = 0;
                    for (int i = 0; i < 16; i++) diff |= tag[i] ^ sealedBytes[cipher.Length + i];
                    if (diff != 0) return null;
                    return Ctr(ecb, j0, cipher);
                }
            }
        }

        private static class Field
        {
            internal static readonly uint[] P = Parse("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff");
            internal static readonly uint[] N = Parse("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551");
            private static readonly uint[] B = Parse("5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b");
            private static readonly uint[] Gx = Parse("6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296");
            private static readonly uint[] Gy = Parse("4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5");

            private static uint[] Parse(string hex)
            {
                byte[] bytes = new byte[32];
                for (int i = 0; i < 32; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                return FromBytes(bytes, 0);
            }

            internal static uint[] FromBytes(byte[] bytes, int offset)
            {
                uint[] r = new uint[8];
                for (int i = 0; i < 8; i++)
                {
                    int at = offset + 28 - 4 * i;
                    r[i] = ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) | ((uint)bytes[at + 2] << 8) | bytes[at + 3];
                }
                return r;
            }

            internal static byte[] ToBytes(uint[] v)
            {
                byte[] r = new byte[32];
                for (int i = 0; i < 8; i++)
                {
                    int at = 28 - 4 * i;
                    r[at] = (byte)(v[i] >> 24);
                    r[at + 1] = (byte)(v[i] >> 16);
                    r[at + 2] = (byte)(v[i] >> 8);
                    r[at + 3] = (byte)v[i];
                }
                return r;
            }

            internal static bool IsZero(uint[] a)
            {
                uint all = 0;
                for (int i = 0; i < 8; i++) all |= a[i];
                return all == 0;
            }

            internal static bool Less(uint[] a, uint[] b)
            {
                for (int i = 7; i >= 0; i--)
                    if (a[i] != b[i]) return a[i] < b[i];
                return false;
            }

            private static bool Equal(uint[] a, uint[] b)
            {
                for (int i = 0; i < 8; i++)
                    if (a[i] != b[i]) return false;
                return true;
            }

            private static uint[] Add(uint[] a, uint[] b)
            {
                uint[] r = new uint[8];
                ulong carry = 0;
                for (int i = 0; i < 8; i++)
                {
                    carry += (ulong)a[i] + b[i];
                    r[i] = (uint)carry;
                    carry >>= 32;
                }
                if (carry != 0 || !Less(r, P)) Take(r, P);
                return r;
            }

            private static void Take(uint[] r, uint[] m)
            {
                long borrow = 0;
                for (int i = 0; i < 8; i++)
                {
                    borrow += (long)r[i] - m[i];
                    r[i] = (uint)borrow;
                    borrow >>= 32;
                }
            }

            private static uint[] Sub(uint[] a, uint[] b)
            {
                uint[] r = new uint[8];
                long borrow = 0;
                for (int i = 0; i < 8; i++)
                {
                    borrow += (long)a[i] - b[i];
                    r[i] = (uint)borrow;
                    borrow >>= 32;
                }
                if (borrow != 0)
                {
                    ulong carry = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        carry += (ulong)r[i] + P[i];
                        r[i] = (uint)carry;
                        carry >>= 32;
                    }
                }
                return r;
            }

            private static uint[] Mul(uint[] a, uint[] b)
            {
                uint[] r = new uint[8];
                for (int i = 255; i >= 0; i--)
                {
                    r = Add(r, r);
                    if (((b[i >> 5] >> (i & 31)) & 1) != 0) r = Add(r, a);
                }
                return r;
            }

            private static uint[] Small(uint[] a, int times)
            {
                uint[] r = new uint[8];
                for (int i = 0; i < times; i++) r = Add(r, a);
                return r;
            }

            private static uint[] Invert(uint[] a)
            {
                uint[] e = (uint[])P.Clone();
                e[0] -= 2;
                uint[] r = new uint[8];
                r[0] = 1;
                for (int i = 255; i >= 0; i--)
                {
                    r = Mul(r, r);
                    if (((e[i >> 5] >> (i & 31)) & 1) != 0) r = Mul(r, a);
                }
                return r;
            }

            internal static bool OnCurve(uint[] x, uint[] y)
            {
                if (!Less(x, P) || !Less(y, P)) return false;
                uint[] right = Add(Sub(Mul(Mul(x, x), x), Small(x, 3)), B);
                return Equal(Mul(y, y), right);
            }

            private static void Double(uint[] x, uint[] y, uint[] z, out uint[] x3, out uint[] y3, out uint[] z3)
            {
                uint[] delta = Mul(z, z), gamma = Mul(y, y), beta = Mul(x, gamma);
                uint[] alpha = Small(Mul(Sub(x, delta), Add(x, delta)), 3);
                x3 = Sub(Mul(alpha, alpha), Small(beta, 8));
                uint[] yz = Add(y, z);
                z3 = Sub(Sub(Mul(yz, yz), gamma), delta);
                y3 = Sub(Mul(alpha, Sub(Small(beta, 4), x3)), Small(Mul(gamma, gamma), 8));
            }

            internal static byte[] Multiply(uint[] k)
            {
                uint[] x = null, y = null, z = null;
                for (int i = 255; i >= 0; i--)
                {
                    if (x != null)
                    {
                        uint[] dx, dy, dz;
                        Double(x, y, z, out dx, out dy, out dz);
                        x = dx;
                        y = dy;
                        z = dz;
                    }
                    if (((k[i >> 5] >> (i & 31)) & 1) == 0) continue;
                    if (x == null)
                    {
                        x = (uint[])Gx.Clone();
                        y = (uint[])Gy.Clone();
                        z = new uint[8];
                        z[0] = 1;
                        continue;
                    }
                    uint[] z1z1 = Mul(z, z), u2 = Mul(Gx, z1z1), s2 = Mul(Gy, Mul(z, z1z1));
                    uint[] h = Sub(u2, x), r = Small(Sub(s2, y), 2);
                    if (IsZero(h)) return null;
                    uint[] hh = Mul(h, h), ii = Small(hh, 4), j = Mul(h, ii), v = Mul(x, ii);
                    uint[] x3 = Sub(Sub(Mul(r, r), j), Small(v, 2));
                    uint[] y3 = Sub(Mul(r, Sub(v, x3)), Small(Mul(y, j), 2));
                    uint[] zh = Add(z, h);
                    uint[] z3 = Sub(Sub(Mul(zh, zh), z1z1), hh);
                    x = x3;
                    y = y3;
                    z = z3;
                }
                if (x == null || IsZero(z)) return null;
                uint[] zi = Invert(z), zi2 = Mul(zi, zi);
                byte[] point = new byte[65];
                point[0] = 4;
                Buffer.BlockCopy(ToBytes(Mul(x, zi2)), 0, point, 1, 32);
                Buffer.BlockCopy(ToBytes(Mul(y, Mul(zi2, zi))), 0, point, 33, 32);
                return point;
            }
        }
    }
}
