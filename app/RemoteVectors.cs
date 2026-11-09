using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // PROTOCOL.md section 9's proof for the PC: every value in remote/test/vectors.json, checked with her own code. Every
    // envelopes[] entry seals to exactly its envelope with the given IV and opens back to its plaintext, every rejects[] entry
    // fails to open, and the PC's own messages and relay controls come out byte for byte. Her smoke test runs it when the
    // repo is there, and so does app/RemoteTests.
    internal static class RemoteVectors
    {
        // remote/test/vectors.json from the working folder or any folder above her exe, or null.
        internal static string Find()
        {
            List<string> roots = new List<string> { Environment.CurrentDirectory };
            for (DirectoryInfo at = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); at != null; at = at.Parent) roots.Add(at.FullName);
            foreach (string root in roots)
            {
                string path = Path.Combine(Path.Combine(Path.Combine(root, "remote"), "test"), "vectors.json");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        // What failed, or an empty list. checks says how many ran.
        internal static List<string> Check(string json, out int checks)
        {
            Run run = new Run();
            try { run.All(json); }
            catch (Exception error) { run.Fail("the vectors couldn't be read: " + error.GetType().Name + ": " + error.Message); }
            checks = run.Count;
            return run.Failed;
        }

        internal static byte[] FromHex(string hex)
        {
            if (hex == null || hex.Length % 2 != 0) throw new FormatException("Bad hex.");
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return bytes;
        }

        internal static string Hex(byte[] bytes)
        {
            StringBuilder text = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private sealed class Run
        {
            internal readonly List<string> Failed = new List<string>();
            internal int Count;
            private RemoteJson.Map inputs;
            private readonly Dictionary<string, RemoteKeys> contexts = new Dictionary<string, RemoteKeys>();

            internal void Fail(string what) { Failed.Add(what); }

            private void Expect(bool ok, string what)
            {
                Count++;
                if (!ok) Failed.Add(what);
            }

            private void Same(string got, string want, string what) { Expect(got == want, what + ": got " + got + ", want " + want); }

            private byte[] Input(string name)
            {
                RemoteJson.Map input = inputs.Child(name);
                return FromHex(input.Text("hex"));
            }

            private string InputB64u(string name) { return inputs.Child(name).Text("b64u"); }

            private RemoteKey Key(string name)
            {
                string[] parts = name.Split('.');
                RemoteKeys keys = contexts[parts[0]];
                return parts[1] == "phoneToPc" ? keys.PhoneToPc : keys.PcToPhone;
            }

            private static int Int(RemoteJson.Map map, string name)
            {
                double value;
                if (!map.Number(name, out value)) throw new FormatException("No " + name + ".");
                return (int)value;
            }

            private static uint Seq(RemoteJson.Map map)
            {
                double value;
                if (!map.Number("seq", out value)) throw new FormatException("No seq.");
                return (uint)value;
            }

            internal void All(string json)
            {
                RemoteJson.Map v = RemoteJson.ParseMap(json);
                if (v == null) throw new FormatException("vectors.json isn't a JSON object.");
                double protocol;
                Expect(v.Number("protocol", out protocol) && protocol == RemoteMessages.Protocol, "protocol is 1");
                RemoteJson.Map labels = v.Child("labels");
                Same(labels.Text("pair"), RemoteCrypto.PairLabel, "pair label");
                Same(labels.Text("confirm"), RemoteCrypto.ConfirmLabel, "confirm label");
                Same(labels.Text("hello"), RemoteCrypto.HelloLabel, "hello label");
                Same(labels.Text("session"), RemoteCrypto.SessionLabel, "session label");

                inputs = v.Child("inputs");
                int[] lengths = { 16, 32, 16, 32, 16, 16, 32, 32, 16, 16 };
                string[] names = { "room", "pcToken", "code", "pairingKey", "pairNonce", "deviceId", "deviceToken", "deviceKey", "phoneNonce", "pcNonce" };
                for (int i = 0; i < names.Length; i++)
                {
                    byte[] raw = Input(names[i]);
                    Same(RemoteCrypto.B64u(raw), InputB64u(names[i]), names[i] + " in base64url");
                    byte[] back = RemoteCrypto.FromB64u(InputB64u(names[i]), lengths[i]);
                    Expect(back != null && Hex(back) == Hex(raw), names[i] + " decodes back from base64url");
                }
                // The strict decoder turns away padding, the other alphabet, stray bits and wrong lengths.
                string room = InputB64u("room");
                Expect(RemoteCrypto.FromB64u(room + "==", 16) == null, "padding is refused");
                Expect(RemoteCrypto.FromB64u("+" + room.Substring(1), 16) == null && RemoteCrypto.FromB64u("/" + room.Substring(1), 16) == null, "+ and / are refused");
                Expect(RemoteCrypto.FromB64u(room.Substring(0, room.Length - 1) + "x", 16) == null, "non-canonical trailing bits are refused");
                Expect(RemoteCrypto.FromB64u(room, 32) == null, "a wrong length is refused");

                Same(RemoteCrypto.QrUrl(InputB64u("room"), InputB64u("code"), InputB64u("pairingKey")), v.Text("qrUrl"), "QR URL");
                RemoteJson.Map hashes = v.Child("hashes");
                Same(RemoteCrypto.Hash(Input("code")), hashes.Text("codeHashB64u"), "SHA-256 of the code");
                Same(RemoteCrypto.Hash(Input("pcToken")), hashes.Text("pcTokenHashB64u"), "SHA-256 of the PC token");
                Same(RemoteCrypto.Hash(Input("deviceToken")), hashes.Text("deviceTokenHashB64u"), "SHA-256 of the device token");

                RemoteJson.Map rfc = v.Child("hkdfRfc5869Case1");
                Same(Hex(RemoteCrypto.Hkdf(FromHex(rfc.Text("ikm")), FromHex(rfc.Text("salt")), FromHex(rfc.Text("info")), Int(rfc, "length"))), rfc.Text("okm"), "HKDF, RFC 5869 case 1");

                RemoteJson.Map confirm = v.Child("confirm");
                Same(Hex(RemoteCrypto.Hkdf(Input("pairingKey"), Input("pairNonce"), RemoteCrypto.Ascii(RemoteCrypto.ConfirmLabel), 4)), confirm.Text("okm"), "confirmation HKDF");
                Same(RemoteCrypto.Confirm(Input("pairingKey"), Input("pairNonce")), confirm.Text("code"), "confirmation code");

                Context("pair", v.Child("pairKeys"), Input("pairingKey"), Input("pairNonce"), RemoteCrypto.PairLabel);
                Context("hello", v.Child("helloKeys"), Input("deviceKey"), Input("deviceId"), RemoteCrypto.HelloLabel);
                Context("session", v.Child("sessionKeys"), Input("deviceKey"), RemoteCrypto.Join(Input("phoneNonce"), Input("pcNonce")), RemoteCrypto.SessionLabel);
                RemoteKeys split = RemoteCrypto.SessionKeys(Input("deviceKey"), Input("phoneNonce"), Input("pcNonce"));
                Same(Hex(split.PcToPhone.Mac), v.Child("sessionKeys").Child("pcToPhone").Text("mac"), "SessionKeys() splits the same way");

                Dictionary<string, string> envelopes = new Dictionary<string, string>();
                foreach (object item in v.Items("envelopes"))
                {
                    RemoteJson.Map entry = (RemoteJson.Map)item;
                    string name = entry.Text("name");
                    RemoteKey key = Key(entry.Text("keys"));
                    byte kind = (byte)Int(entry, "kind"), dir = (byte)Int(entry, "dir");
                    uint seq = Seq(entry);
                    byte[] plain = FromHex(entry.Text("plaintextHex"));
                    Same(Hex(Encoding.UTF8.GetBytes(entry.Text("plaintext"))), entry.Text("plaintextHex"), name + " plaintext is its UTF-8");
                    Same(Hex(RemoteCrypto.Seal(key, kind, dir, seq, plain, FromHex(entry.Text("iv")))), entry.Text("envelope"), name + " seals to the vector");
                    byte[] opened = RemoteCrypto.Open(key, kind, dir, seq, FromHex(entry.Text("envelope")));
                    Expect(opened != null && Hex(opened) == entry.Text("plaintextHex"), name + " opens back to its plaintext");
                    RemoteJson.Map message = opened == null ? null : RemoteJson.Message(opened);
                    Expect(message != null && name.StartsWith(message.Text("type"), StringComparison.Ordinal), name + " reads as a message of its type");
                    envelopes[name.Split(' ')[0]] = entry.Text("envelope");
                    // The PC's own messages come out exactly as the reference writes them.
                    string ours = Ours(message);
                    if (ours != null) Same(ours, entry.Text("plaintext"), name + " is what the PC writes");
                }
                // A PC channel seals at 0, 1, 2 and opens a phone's frames only in order.
                RemoteChannel channel = new RemoteChannel(RemoteCrypto.SessionKeys(Input("deviceKey"), Input("phoneNonce"), Input("pcNonce")), RemoteCrypto.KindSession);
                byte[] typing = channel.Seal(RemoteMessages.Typing(true, null), FromHex("606162636465666768696a6b6c6d6e6f"));
                Same(Hex(typing), FindEnvelope(v, "chat.typing"), "a session channel's first frame is the chat.typing vector");
                Expect(RemoteCrypto.SeqOf(channel.Seal(RemoteMessages.Typing(false, null)), 0) == 1, "a session channel's second frame is seq 1");
                byte[] send = FromHex(envelopes["chat.send"]);
                Expect(channel.Open(send, 0, send.Length) != null, "a session channel opens the chat.send vector at seq 0");
                Expect(channel.Open(send, 0, send.Length) == null, "a session channel refuses the same frame again");

                foreach (object item in v.Items("rejects"))
                {
                    RemoteJson.Map entry = (RemoteJson.Map)item;
                    string reason;
                    byte[] frame = FromHex(entry.Text("envelope"));
                    byte[] opened = RemoteCrypto.Open(Key(entry.Text("keys")), (byte)Int(entry, "kind"), (byte)Int(entry, "dir"), Seq(entry), frame, 0, frame.Length, out reason);
                    Expect(opened == null, "rejected: " + entry.Text("name"));
                    Same(reason, entry.Text("reason"), "rejected for its reason: " + entry.Text("name"));
                }

                RemoteJson.Map frames = v.Child("frames");
                byte[] fromRelay = FromHex(frames.Text("relayToPcChatSend"));
                byte[] device = new byte[RemoteCrypto.IdBytes];
                Buffer.BlockCopy(fromRelay, 0, device, 0, device.Length);
                Same(RemoteCrypto.B64u(device), InputB64u("deviceId"), "a relay frame starts with the device id");
                string why;
                byte[] chat = RemoteCrypto.Open(Key("session.phoneToPc"), RemoteCrypto.KindSession, RemoteCrypto.PhoneToPc, 0, fromRelay, RemoteCrypto.IdBytes, fromRelay.Length - RemoteCrypto.IdBytes, out why);
                Expect(chat != null && RemoteJson.Message(chat).Text("text") == "Hello, Capri!", "a relay frame's envelope opens after the id");
                Same(Hex(RemoteCrypto.Join(Input("deviceId"), typing)), frames.Text("pcToRelayChatTyping"), "a PC frame is the device id, then the envelope");

                RemoteJson.Map controls = v.Child("controls");
                Same(RemoteMessages.Auth(InputB64u("pcToken")), controls.Text("pcAuth"), "the PC's auth");
                Same(RemoteMessages.Code(hashes.Text("codeHashB64u"), 600), controls.Text("pcCode"), "the PC's code");
                RemoteJson.Map pending = RemoteJson.ParseMap(controls.Text("relayPending"));
                Same(RemoteMessages.Admit(pending.Text("id"), InputB64u("deviceId"), hashes.Text("deviceTokenHashB64u"), RemoteCrypto.B64u(FromHex(envelopes["pair-allow"]))), controls.Text("pcAdmit"), "the PC's admit");
                Same(pending.Text("env"), RemoteCrypto.B64u(FromHex(envelopes["pair-request"])), "a pending's env is the pair-request");
                Same(RemoteJson.ParseMap(controls.Text("phonePair")).Text("nonce"), InputB64u("pairNonce"), "a phone's pair carries its nonce");
                Same(RemoteMessages.Ping, "{\"t\":\"ping\"}", "the heartbeat is exactly 12 characters");
                Expect(RemoteMessages.Ping.Length == 12, "the heartbeat is 12 characters long");
            }

            private static string FindEnvelope(RemoteJson.Map v, string prefix)
            {
                foreach (object item in v.Items("envelopes"))
                {
                    RemoteJson.Map entry = (RemoteJson.Map)item;
                    if (entry.Text("name").StartsWith(prefix, StringComparison.Ordinal)) return entry.Text("envelope");
                }
                return null;
            }

            // The same message as the PC would write it, for the ones the PC sends; null for a phone's.
            private static string Ours(RemoteJson.Map message)
            {
                if (message == null) return null;
                switch (message.Text("type"))
                {
                    case "pair-allow": return RemoteMessages.PairAllow(message.Text("device"), message.Text("token"), message.Text("key"), message.Text("name"), message.Text("app"));
                    case "pair-deny": return RemoteMessages.PairDeny(message.Text("reason"));
                    case "hello-ok":
                        List<string> features = new List<string>();
                        foreach (object feature in message.Items("features")) features.Add((string)feature);
                        return RemoteMessages.HelloOk(message.Text("nonce"), message.Text("echo"), message.Text("name"), message.Text("app"), features.ToArray());
                    case "chat.typing": return RemoteMessages.Typing(message.Is("on", true), message.Text("re"));
                    case "chat.reply": return RemoteMessages.Reply(message.Text("id"), message.Text("re"), message.Text("text"), message.Is("done", true));
                    default: return null;
                }
            }

            private void Context(string name, RemoteJson.Map expected, byte[] ikm, byte[] salt, string label)
            {
                Same(Hex(salt), expected.Text("salt"), name + " salt");
                byte[] okm = RemoteCrypto.Hkdf(ikm, salt, RemoteCrypto.Ascii(label), 128);
                Same(Hex(okm), expected.Text("okm"), name + " keys");
                RemoteKeys keys = RemoteKeys.Split(okm);
                Same(Hex(keys.PhoneToPc.Enc), expected.Child("phoneToPc").Text("enc"), name + " phone to PC AES key");
                Same(Hex(keys.PhoneToPc.Mac), expected.Child("phoneToPc").Text("mac"), name + " phone to PC HMAC key");
                Same(Hex(keys.PcToPhone.Enc), expected.Child("pcToPhone").Text("enc"), name + " PC to phone AES key");
                Same(Hex(keys.PcToPhone.Mac), expected.Child("pcToPhone").Text("mac"), name + " PC to phone HMAC key");
                contexts[name] = keys;
            }
        }
    }
}
