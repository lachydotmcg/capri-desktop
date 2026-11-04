// Her ears: listens on your Quest's microphone, notices when you start and stop talking, and turns what you said into text
// with Fish Audio (or OpenAI, if that's your brain and there's no Fish Audio key). X mutes and unmutes her mic.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CapriRoom
{
    public class CapriEars : MonoBehaviour
    {
        private const int Rate = 16000;
        private const float PreRoll = 0.35f, EndSilence = 0.9f, MinSpeech = 0.45f, MaxSpeech = 15f;

        [Serializable] private class Heard { public string text; }
        [Serializable] private class Problem { public string message; public string detail; public string status; }

        // The reason from an error reply, short enough for her bubble.
        private static string Why(string body)
        {
            string why = "";
            try
            {
                Problem p = JsonUtility.FromJson<Problem>(body);
                why = p == null ? "" : !string.IsNullOrEmpty(p.message) ? p.message : !string.IsNullOrEmpty(p.detail) ? p.detail : p.status ?? "";
            }
            catch (Exception) { why = body ?? ""; }
            why = why.Replace("\n", " ").Trim();
            return why.Length > 90 ? why.Substring(0, 90) + "..." : why;
        }

        public bool Listening { get; private set; }        // mic is on and she's waiting for you
        public bool Hearing { get; private set; }          // you're talking right now
        public bool Busy { get; private set; }             // turning your words into text
        public Func<bool> Paused = delegate { return false; };   // true while she's talking or thinking, so she doesn't hear herself
        public Action<string> Said;                          // what you said, as text
        public Action<string> Trouble;                       // something went wrong, in her words

        private string device;
        private AudioClip mic;
        private int readAt;
        private float floor = 0.01f, quietFor, speechFor;
        private readonly List<float> speech = new List<float>();
        private readonly Queue<float[]> recent = new Queue<float[]>();
        private int recentSamples;
        private bool asked;

        public bool Ready { get { return !string.IsNullOrEmpty(Key); } }

        // The key for whichever service is set to hear you (VOICE > Hearing).
        private static string Key
        {
            get
            {
                switch (CapriConfig.Hearing)
                {
                    case 0: return CapriConfig.GroqKey;
                    case 1: return CapriConfig.FishKey;
                    default: return CapriConfig.Brain == 2 ? CapriConfig.BrainKey : "";
                }
            }
        }
        private static string Service { get { return CapriConfig.Hearings[Mathf.Clamp(CapriConfig.Hearing, 0, 2)].Replace(" (free)", ""); } }

        private void Update()
        {
            bool want = !CapriConfig.MicMuted && Ready;
            if (want && mic == null) StartMic();
            else if (!want && mic != null) StopMic();
            if (mic == null) { Listening = Hearing = false; return; }
            Listening = true;
            ReadMic();
        }

        private void StartMic()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                if (!asked) { asked = true; UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone); }
                return;
            }
#endif
            if (Microphone.devices.Length == 0) return;
            device = Microphone.devices[0];
            mic = Microphone.Start(device, true, 30, Rate);
            readAt = 0;
            Debug.Log("[Capri] Listening on " + device);
        }

        private void StopMic()
        {
            Microphone.End(device);
            mic = null;
            speech.Clear();
            recent.Clear();
            recentSamples = 0;
            Hearing = false;
        }

        private void ReadMic()
        {
            int now = Microphone.GetPosition(device);
            if (now < 0 || now == readAt) return;
            int count = now > readAt ? now - readAt : mic.samples - readAt + now;
            float[] chunk = new float[count];
            if (now > readAt) mic.GetData(chunk, readAt);
            else
            {
                float[] tail = new float[mic.samples - readAt];
                mic.GetData(tail, readAt);
                float[] head = new float[now];
                if (now > 0) mic.GetData(head, 0);
                tail.CopyTo(chunk, 0);
                head.CopyTo(chunk, tail.Length);
            }
            readAt = now;

            if (Paused() || Busy)   // ignore her own voice and anything said while she's thinking
            {
                speech.Clear(); Hearing = false; quietFor = speechFor = 0;
                return;
            }

            float sum = 0;
            foreach (float s in chunk) sum += s * s;
            float level = Mathf.Sqrt(sum / Mathf.Max(1, chunk.Length));
            float seconds = chunk.Length / (float)Rate;
            bool loud = level > Mathf.Max(0.015f, floor * 3.5f);
            if (!Hearing) floor = Mathf.Lerp(floor, level, Mathf.Clamp01(seconds * 0.5f));   // learn the room's background noise

            if (!Hearing)
            {
                // Keep a little audio from just before you started, so the first word isn't clipped.
                recent.Enqueue(chunk); recentSamples += chunk.Length;
                while (recentSamples - recent.Peek().Length > PreRoll * Rate) recentSamples -= recent.Dequeue().Length;
                if (loud)
                {
                    Hearing = true; speechFor = 0; quietFor = 0;
                    speech.Clear();
                    foreach (float[] piece in recent) speech.AddRange(piece);
                    recent.Clear(); recentSamples = 0;
                }
                return;
            }

            speech.AddRange(chunk);
            if (loud) { speechFor += seconds; quietFor = 0; }
            else quietFor += seconds;
            float length = speech.Count / (float)Rate;
            if (quietFor >= EndSilence || length >= MaxSpeech)
            {
                Hearing = false;
                float[] said = speech.ToArray();
                speech.Clear();
                if (speechFor >= MinSpeech) StartCoroutine(Transcribe(said));
            }
        }

        private IEnumerator Transcribe(float[] samples)
        {
            Busy = true;
            yield return Send(samples, "transcribe-1-pro", true);
            Busy = false;
        }

        // Sends what you said off to be turned into text. If Fish Audio turns down the pro model, she tries its standard one once.
        private IEnumerator Send(float[] samples, string fishModel, bool mayRetry)
        {
            byte[] wav = CapriVoice.ToWav(samples, Rate);
            bool fish = CapriConfig.Hearing == 1;
            string code = CapriConfig.LanguageCodes[Mathf.Clamp(CapriConfig.Language, 0, CapriConfig.LanguageCodes.Length - 1)];
            List<IMultipartFormSection> form = new List<IMultipartFormSection>();
            UnityWebRequest web;
            if (fish)
            {
                form.Add(new MultipartFormFileSection("audio", wav, "speech.wav", "audio/wav"));
                form.Add(new MultipartFormDataSection("language", code));
                form.Add(new MultipartFormDataSection("ignore_timestamps", "true"));
                form.Add(new MultipartFormDataSection("tag_audio_events", "false"));
                web = UnityWebRequest.Post("https://api.fish.audio/v1/asr", form);
                web.SetRequestHeader("Authorization", "Bearer " + CapriConfig.FishKey);
                web.SetRequestHeader("model", fishModel);
            }
            else
            {
                // Groq and OpenAI take the same OpenAI-style request.
                bool groq = CapriConfig.Hearing == 0;
                form.Add(new MultipartFormFileSection("file", wav, "speech.wav", "audio/wav"));
                form.Add(new MultipartFormDataSection("model", groq ? "whisper-large-v3-turbo" : "gpt-4o-mini-transcribe"));
                form.Add(new MultipartFormDataSection("language", code));
                form.Add(new MultipartFormDataSection("response_format", "json"));
                web = UnityWebRequest.Post(groq ? "https://api.groq.com/openai/v1/audio/transcriptions" : "https://api.openai.com/v1/audio/transcriptions", form);
                web.SetRequestHeader("Authorization", "Bearer " + Key);
            }
            using (web)
            {
                web.timeout = 30;
                yield return web.SendWebRequest();
                string text = null;
                if (web.result == UnityWebRequest.Result.Success)
                {
                    try { text = JsonUtility.FromJson<Heard>(web.downloadHandler.text).text; } catch (Exception) { }
                }
                else
                {
                    long status = web.responseCode;
                    string said = web.downloadHandler != null ? web.downloadHandler.text : "";
                    Debug.Log("[Capri] Speech to text failed: HTTP " + status + " " + web.error + " " + said);
                    if (fish && mayRetry && status >= 400 && status != 401 && status != 403 && status != 429)
                    {
                        yield return Send(samples, "transcribe-1", false);
                        yield break;
                    }
                    string why = Why(said);
                    if (Trouble != null) Trouble(
                        status == 401 || status == 403 ? Service + " won't take that key, so I can't hear you. Press Y and check Hearing under VOICE."
                        : status == 402 ? Service + " needs credit to hear you (HTTP 402). Press Y and set Hearing to Groq (free) under VOICE."
                        : status == 429 ? "Too much talking for " + Service + "'s free limit. Give me a minute, choom."
                        : status == 0 ? "No signal: " + (web.error ?? "couldn't reach " + Service + ".")
                        : Service + " couldn't hear that (HTTP " + status + ")" + (why.Length > 0 ? ": " + why : "."));
                }
                text = text == null ? null : System.Text.RegularExpressions.Regex.Replace(text, @"<\|[^|]*\|>", "").Trim();
                if (!string.IsNullOrEmpty(text) && text.Length > 1 && Said != null) Said(text);
            }
        }
    }
}
