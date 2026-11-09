using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CapriDesktop
{
    // Capri Remote, protocol 1 (remote/PROTOCOL.md section 6). .NET Framework 4.8 has no AES-GCM, so every envelope is
    // AES-256-CBC with PKCS#7 and an HMAC-SHA256 tag over its 8-byte header, IV and ciphertext (encrypt-then-MAC), with keys
    // from HKDF-SHA256, which is hand-rolled on HMACSHA256 here. All of it is checked byte for byte against
    // remote/test/vectors.json by RemoteVectors, in her smoke test and in app/RemoteTests.
    internal static class RemoteCrypto
    {
        internal const byte Version = 1;
        internal const byte KindPair = 1, KindHello = 2, KindSession = 3;
        internal const byte PhoneToPc = 1, PcToPhone = 2;
        internal const int HeaderBytes = 8, IvBytes = 16, TagBytes = 32, IdBytes = 16, MinEnvelope = HeaderBytes + IvBytes + 16 + TagBytes;
        internal const string PairLabel = "capri-remote/1 pair", ConfirmLabel = "capri-remote/1 confirm", HelloLabel = "capri-remote/1 hello", SessionLabel = "capri-remote/1 session";
        internal const string QrBase = "https://capriai.app/remote/";
        private static readonly RNGCryptoServiceProvider Dice = new RNGCryptoServiceProvider();

        internal static byte[] Random(int count)
        {
            byte[] bytes = new byte[count];
            Dice.GetBytes(bytes);
            return bytes;
        }

        internal static byte[] Ascii(string text) { return Encoding.ASCII.GetBytes(text); }

        internal static byte[] Join(byte[] first, byte[] second)
        {
            byte[] both = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, both, 0, first.Length);
            Buffer.BlockCopy(second, 0, both, first.Length, second.Length);
            return both;
        }

        internal static byte[] Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(data);
        }

        // What the relay keeps and compares: base64url(SHA-256(raw bytes)) of the PC token, a device token or a one-time code.
        internal static string Hash(byte[] raw) { return B64u(Sha256(raw)); }

        // HKDF-SHA256, RFC 5869: PRK = HMAC(salt, ikm), then T(i) = HMAC(PRK, T(i-1) || info || i), cut to length bytes.
        internal static byte[] Hkdf(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            if (length < 0 || length > 255 * 32) throw new ArgumentOutOfRangeException("length");
            byte[] prk;
            using (HMACSHA256 extract = new HMACSHA256(salt ?? new byte[32])) prk = extract.ComputeHash(ikm);
            byte[] okm = new byte[length];
            byte[] block = new byte[0];
            try
            {
                using (HMACSHA256 expand = new HMACSHA256(prk))
                {
                    int at = 0;
                    for (int i = 1; at < length; i++)
                    {
                        byte[] input = new byte[block.Length + info.Length + 1];
                        Buffer.BlockCopy(block, 0, input, 0, block.Length);
                        Buffer.BlockCopy(info, 0, input, block.Length, info.Length);
                        input[input.Length - 1] = (byte)i;
                        block = expand.ComputeHash(input);
                        int take = Math.Min(block.Length, length - at);
                        Buffer.BlockCopy(block, 0, okm, at, take);
                        at += take;
                    }
                }
            }
            finally { Array.Clear(prk, 0, prk.Length); }
            return okm;
        }

        internal static RemoteKeys PairKeys(byte[] pairingKey, byte[] pairNonce) { return RemoteKeys.Split(Hkdf(pairingKey, pairNonce, Ascii(PairLabel), 128)); }

        internal static RemoteKeys HelloKeys(byte[] deviceKey, byte[] deviceId) { return RemoteKeys.Split(Hkdf(deviceKey, deviceId, Ascii(HelloLabel), 128)); }

        internal static RemoteKeys SessionKeys(byte[] deviceKey, byte[] phoneNonce, byte[] pcNonce) { return RemoteKeys.Split(Hkdf(deviceKey, Join(phoneNonce, pcNonce), Ascii(SessionLabel), 128)); }

        // The six digits both screens show while a phone waits for Allow, as "318 338": a big-endian uint32 of 4 HKDF bytes,
        // mod 1,000,000.
        internal static string Confirm(byte[] pairingKey, byte[] pairNonce)
        {
            byte[] x = Hkdf(pairingKey, pairNonce, Ascii(ConfirmLabel), 4);
            uint value = ((uint)x[0] << 24) | ((uint)x[1] << 16) | ((uint)x[2] << 8) | x[3];
            string digits = (value % 1000000).ToString("D6", CultureInfo.InvariantCulture);
            return digits.Substring(0, 3) + " " + digits.Substring(3);
        }

        // envelope = version, kind, direction, 0, seq (big-endian) || IV || AES-256-CBC-PKCS7 || HMAC-SHA256 of all before it.
        internal static byte[] Seal(RemoteKey key, byte kind, byte direction, uint seq, byte[] plaintext, byte[] iv = null)
        {
            if (iv == null) iv = Random(IvBytes);
            if (iv.Length != IvBytes) throw new ArgumentException("The IV must be 16 bytes.");
            byte[] cipher;
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key.Enc;
                aes.IV = iv;
                using (ICryptoTransform encrypt = aes.CreateEncryptor()) cipher = encrypt.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }
            byte[] envelope = new byte[HeaderBytes + IvBytes + cipher.Length + TagBytes];
            envelope[0] = Version;
            envelope[1] = kind;
            envelope[2] = direction;
            envelope[3] = 0;
            envelope[4] = (byte)(seq >> 24);
            envelope[5] = (byte)(seq >> 16);
            envelope[6] = (byte)(seq >> 8);
            envelope[7] = (byte)seq;
            Buffer.BlockCopy(iv, 0, envelope, HeaderBytes, IvBytes);
            Buffer.BlockCopy(cipher, 0, envelope, HeaderBytes + IvBytes, cipher.Length);
            byte[] tag;
            using (HMACSHA256 mac = new HMACSHA256(key.Mac)) tag = mac.ComputeHash(envelope, 0, envelope.Length - TagBytes);
            Buffer.BlockCopy(tag, 0, envelope, envelope.Length - TagBytes, TagBytes);
            return envelope;
        }

        internal static byte[] Open(RemoteKey key, byte kind, byte direction, uint seq, byte[] envelope)
        {
            string reason;
            return Open(key, kind, direction, seq, envelope, 0, envelope == null ? 0 : envelope.Length, out reason);
        }

        // The checks in PROTOCOL.md's order: the length, then the tag over all 32 bytes in constant time before anything is
        // decrypted, then the header the tag covers, the exact next sequence number, and strict PKCS#7. Null when any fails;
        // reason is only for the tests and never goes to the other side.
        internal static byte[] Open(RemoteKey key, byte kind, byte direction, uint seq, byte[] envelope, int offset, int count, out string reason)
        {
            reason = null;
            if (envelope == null || offset < 0 || count < MinEnvelope || offset + count > envelope.Length || (count - HeaderBytes - IvBytes - TagBytes) % 16 != 0) { reason = "length"; return null; }
            byte[] expected;
            using (HMACSHA256 mac = new HMACSHA256(key.Mac)) expected = mac.ComputeHash(envelope, offset, count - TagBytes);
            if (!Same(expected, 0, envelope, offset + count - TagBytes, TagBytes)) { reason = "mac"; return null; }
            if (envelope[offset] != Version) { reason = "version"; return null; }
            if (envelope[offset + 1] != kind || envelope[offset + 2] != direction || envelope[offset + 3] != 0) { reason = "context"; return null; }
            if (SeqOf(envelope, offset) != seq) { reason = "seq"; return null; }
            byte[] iv = new byte[IvBytes];
            Buffer.BlockCopy(envelope, offset + HeaderBytes, iv, 0, IvBytes);
            byte[] plain;
            try
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.None;
                    aes.Key = key.Enc;
                    aes.IV = iv;
                    using (ICryptoTransform decrypt = aes.CreateDecryptor()) plain = decrypt.TransformFinalBlock(envelope, offset + HeaderBytes + IvBytes, count - HeaderBytes - IvBytes - TagBytes);
                }
            }
            catch (CryptographicException) { reason = "padding"; return null; }
            int pad = plain.Length == 0 ? 0 : plain[plain.Length - 1];
            bool bad = pad < 1 || pad > 16 || pad > plain.Length;
            for (int i = plain.Length - pad; !bad && i < plain.Length; i++) bad = plain[i] != pad;
            if (bad) { reason = "padding"; return null; }
            byte[] message = new byte[plain.Length - pad];
            Buffer.BlockCopy(plain, 0, message, 0, message.Length);
            Array.Clear(plain, 0, plain.Length);
            return message;
        }

        internal static uint SeqOf(byte[] envelope, int offset) { return ((uint)envelope[offset + 4] << 24) | ((uint)envelope[offset + 5] << 16) | ((uint)envelope[offset + 6] << 8) | envelope[offset + 7]; }

        // .NET Framework 4.8 has no FixedTimeEquals: every byte pair is XORed and ORed together, so the time never says where
        // two tags first differ.
        internal static bool Same(byte[] a, int aAt, byte[] b, int bAt, int count)
        {
            int diff = 0;
            for (int i = 0; i < count; i++) diff |= a[aAt + i] ^ b[bAt + i];
            return diff == 0;
        }

        // base64url without padding (RFC 4648 section 5).
        internal static string B64u(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }

        // Strict: no padding, nothing outside A-Z a-z 0-9 - _, canonical trailing bits, and exactly length bytes when it's
        // given. Null for anything else.
        internal static byte[] FromB64u(string text, int length = -1)
        {
            if (text == null || text.Length % 4 == 1) return null;
            foreach (char c in text)
                if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_')) return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4)); }
            catch (FormatException) { return null; }
            if (B64u(bytes) != text) return null;
            if (length >= 0 && bytes.Length != length) return null;
            return bytes;
        }

        internal static string QrUrl(string room, string code, string pairingKey) { return QrBase + "#r=" + room + "&c=" + code + "&k=" + pairingKey; }
    }

    // One direction's two keys: AES-256 and HMAC-SHA256.
    internal sealed class RemoteKey
    {
        internal readonly byte[] Enc, Mac;

        internal RemoteKey(byte[] enc, byte[] mac) { Enc = enc; Mac = mac; }
    }

    // A context's 128 bytes of HKDF output, split in PROTOCOL.md's fixed order: phone to PC AES, phone to PC HMAC, PC to phone
    // AES, PC to phone HMAC.
    internal sealed class RemoteKeys
    {
        internal readonly RemoteKey PhoneToPc, PcToPhone;

        private RemoteKeys(RemoteKey phoneToPc, RemoteKey pcToPhone) { PhoneToPc = phoneToPc; PcToPhone = pcToPhone; }

        internal static RemoteKeys Split(byte[] okm)
        {
            if (okm == null || okm.Length != 128) throw new ArgumentException("A context needs 128 bytes of keys.");
            return new RemoteKeys(new RemoteKey(Part(okm, 0), Part(okm, 32)), new RemoteKey(Part(okm, 64), Part(okm, 96)));
        }

        private static byte[] Part(byte[] okm, int at)
        {
            byte[] part = new byte[32];
            Buffer.BlockCopy(okm, at, part, 0, 32);
            return part;
        }
    }

    // The PC's side of one pair, hello or session context. Each direction counts from 0 and goes up by one per envelope; a
    // frame is only taken with exactly the next number, so a replayed, reordered or dropped one ends the session instead.
    internal sealed class RemoteChannel
    {
        internal readonly byte Kind;
        private readonly RemoteKeys keys;
        private readonly object gate = new object();
        private uint sent, taken;

        internal RemoteChannel(RemoteKeys keys, byte kind)
        {
            this.keys = keys;
            Kind = kind;
        }

        internal byte[] Seal(string json, byte[] iv = null)
        {
            lock (gate)
            {
                if (sent == uint.MaxValue) throw new InvalidOperationException("This session has sent all it can.");
                return RemoteCrypto.Seal(keys.PcToPhone, Kind, RemoteCrypto.PcToPhone, sent++, Encoding.UTF8.GetBytes(json), iv);
            }
        }

        // The message a phone sent, or null when the frame fails any check. Only a frame that opens moves the count on.
        internal RemoteJson.Map Open(byte[] frame, int offset, int count)
        {
            lock (gate)
            {
                string reason;
                byte[] plain = RemoteCrypto.Open(keys.PhoneToPc, Kind, RemoteCrypto.PhoneToPc, taken, frame, offset, count, out reason);
                RemoteJson.Map message = plain == null ? null : RemoteJson.Message(plain);
                if (message == null) return null;
                taken++;
                return message;
            }
        }
    }
}
