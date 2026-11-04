// Her voice: Fish Audio speaks her replies with your voice model, then her Pitch, Robot and Glitch settings colour it.
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CapriRoom
{
    public class CapriVoice : MonoBehaviour
    {
        private const int Rate = 24000;
        public AudioSource Source { get; private set; }
        public bool Busy { get; private set; }
        public bool Ready { get { return CapriConfig.VoiceService == 1 && !string.IsNullOrEmpty(CapriConfig.FishKey); } }

        // Robot and Glitch, read by the audio thread.
        private volatile float robot, glitch, boost = 1;
        private double phase;
        private readonly System.Random dice = new System.Random();
        private float[] held = new float[0];
        private int stutterLeft, silentLeft;

        public void Attach(AudioSource source)
        {
            Source = source;
            Apply();
        }

        public void Apply()
        {
            if (Source == null) return;
            float volume = CapriConfig.VolumeValues[Clamp(CapriConfig.Volume, CapriConfig.VolumeValues)];
            Source.volume = Mathf.Min(1f, volume);
            boost = Mathf.Max(1f, volume);   // above 100% she's turned up in the audio filter
            Source.pitch = CapriConfig.PitchValues[Clamp(CapriConfig.Pitch, CapriConfig.PitchValues)];
            robot = new[] { 0f, .3f, .6f, .9f }[Mathf.Clamp(CapriConfig.Robot, 0, 3)];
            glitch = new[] { 0f, .02f, .06f, .14f }[Mathf.Clamp(CapriConfig.Glitch, 0, 3)];
        }

        public void Speak(string text, Action<AudioClip> done, Action<string> failed)
        {
            if (!Ready) { failed(null); return; }
            StartCoroutine(Ask(text, done, failed));
        }

        private IEnumerator Ask(string text, Action<AudioClip> done, Action<string> failed)
        {
            Busy = true;
            float speed = CapriConfig.SpeedValues[Clamp(CapriConfig.Speed, CapriConfig.SpeedValues)];
            StringBuilder body = new StringBuilder("{\"text\":").Append(CapriBrain.Quote(text));
            string voice = CapriConfig.FishVoiceId;
            if (!string.IsNullOrEmpty(voice)) body.Append(",\"reference_id\":").Append(CapriBrain.Quote(voice));
            body.Append(",\"format\":\"wav\",\"sample_rate\":").Append(Rate);
            body.Append(",\"prosody\":{\"speed\":").Append(speed.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(",\"volume\":0}}");

            using (UnityWebRequest web = new UnityWebRequest("https://api.fish.audio/v1/tts", "POST"))
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Content-Type", "application/json");
                web.SetRequestHeader("Authorization", "Bearer " + CapriConfig.FishKey);
                web.SetRequestHeader("model", CapriConfig.FishModel);
                web.timeout = 45;
                yield return web.SendWebRequest();
                Busy = false;
                AudioClip clip = web.result == UnityWebRequest.Result.Success ? Wav(web.downloadHandler.data) : null;
                if (clip == null)
                {
                    long code = web.responseCode;
                    Debug.Log("[Capri] Fish Audio failed: HTTP " + code + " " + web.error + " " + (web.downloadHandler != null ? web.downloadHandler.text : ""));
                    failed(code == 401 || code == 403 ? "Fish Audio won't take that key. Press Y and check it under VOICE."
                        : code == 404 ? "Fish Audio can't find that voice. Press Y and check the voice ID under VOICE."
                        : code == 402 ? "Fish Audio says your account's out of credit."
                        : "My voice glitched out. Fish Audio didn't answer.");
                    yield break;
                }
                done(clip);
            }
        }

        // Turns a 16-bit PCM WAV file into an AudioClip.
        public static AudioClip Wav(byte[] data)
        {
            if (data == null || data.Length < 44 || Encoding.ASCII.GetString(data, 0, 4) != "RIFF") return null;
            int channels = 1, rate = Rate, bits = 16, at = 12;
            while (at + 8 <= data.Length)
            {
                string id = Encoding.ASCII.GetString(data, at, 4);
                int size = BitConverter.ToInt32(data, at + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(data, at + 10);
                    rate = BitConverter.ToInt32(data, at + 12);
                    bits = BitConverter.ToInt16(data, at + 22);
                }
                else if (id == "data")
                {
                    if (bits != 16) return null;
                    if (size <= 0 || at + 8 + size > data.Length) size = data.Length - at - 8;   // streamed WAVs can leave the size open
                    int count = size / 2;
                    float[] samples = new float[count];
                    for (int i = 0; i < count; i++) samples[i] = BitConverter.ToInt16(data, at + 8 + i * 2) / 32768f;
                    AudioClip clip = AudioClip.Create("capri-line", count / channels, channels, rate, false);
                    clip.SetData(samples, 0);
                    return clip;
                }
                at += 8 + size + (size & 1);
            }
            return null;
        }

        // Makes a 16-bit mono WAV file from microphone samples.
        public static byte[] ToWav(float[] samples, int rate)
        {
            byte[] wav = new byte[44 + samples.Length * 2];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
            BitConverter.GetBytes(36 + samples.Length * 2).CopyTo(wav, 4);
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);
            BitConverter.GetBytes((short)1).CopyTo(wav, 22);
            BitConverter.GetBytes(rate).CopyTo(wav, 24);
            BitConverter.GetBytes(rate * 2).CopyTo(wav, 28);
            BitConverter.GetBytes((short)2).CopyTo(wav, 32);
            BitConverter.GetBytes((short)16).CopyTo(wav, 34);
            Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
            BitConverter.GetBytes(samples.Length * 2).CopyTo(wav, 40);
            for (int i = 0; i < samples.Length; i++)
                BitConverter.GetBytes((short)Mathf.Clamp(samples[i] * 32767f, -32768f, 32767f)).CopyTo(wav, 44 + i * 2);
            return wav;
        }

        // Robot (a ring modulator) and Glitch (stutters and drop-outs), on the audio thread.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            float r = robot, g = glitch, b = boost;
            if (b > 1) for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i] * b, -1f, 1f);
            if (r <= 0 && g <= 0) return;
            if (r > 0)
            {
                double step = 2 * Math.PI * 70.0 / AudioSettings.outputSampleRate;
                for (int i = 0; i < data.Length; i += channels)
                {
                    float ring = (float)Math.Sin(phase);
                    phase += step;
                    for (int c = 0; c < channels; c++) data[i + c] = data[i + c] * (1 - r) + data[i + c] * ring * r * 1.6f;
                }
                if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
            }
            if (g > 0)
            {
                if (held.Length != data.Length) held = new float[data.Length];
                if (stutterLeft == 0 && silentLeft == 0)
                {
                    double roll = dice.NextDouble();
                    if (roll < g) stutterLeft = 2 + dice.Next(3);
                    else if (roll < g * 1.4) silentLeft = 1;
                    else Array.Copy(data, held, data.Length);
                }
                if (stutterLeft > 0) { Array.Copy(held, data, data.Length); stutterLeft--; }
                else if (silentLeft > 0) { Array.Clear(data, 0, data.Length); silentLeft--; }
            }
        }

        private static int Clamp(int i, Array list) { return Mathf.Clamp(i, 0, list.Length - 1); }
    }
}
