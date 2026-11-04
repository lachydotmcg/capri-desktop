// Capri's settings panel. Y on the left controller opens and closes it.
// Tabs: BRAIN, VOICE, YOU and MOVES, the desktop app's settings that make sense in a headset.
// Point at a button with the laser and pull the trigger (or pinch with a tracked hand) to press it.
// Edit opens her own keyboard with a text bar, Paste (from the Quest clipboard) and Clear.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CapriRoom
{
    public class CapriSettings : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        private sealed class Button
        {
            public Transform Box;
            public SpriteRenderer Back;
            public Color Normal;
            public Action Click;
            public bool Touched;
        }

        private const float Wide = 0.52f, RowGap = 0.036f, Top = 0.085f;
        private static readonly Color Panel = new Color(0.05f, 0.02f, 0.08f, 0.94f);
        private static readonly Color Ink = new Color(0.62f, 1f, 0.78f);
        private static readonly Color Pink = new Color(1f, 0.16f, 0.42f);
        private static readonly Color Dim = new Color(0.22f, 0.2f, 0.28f);
        private static readonly Color Hover = new Color(0.45f, 0.4f, 0.55f);
        private static readonly string[] Tabs = { "BRAIN", "VOICE", "YOU", "MOVES" };

        private CapriPet pet;
        private Transform rows;
        private SpriteRenderer back;
        private TextMeshPro hint;
        private readonly List<Button> buttons = new List<Button>();
        private int tab;
        private float cooldown;
        // Her keyboard.
        private bool editing, shift, reveal, secret;
        private string text = "", editLabel = "";
        private Action<string> typed;
        private TextMeshPro bar;
        // The laser pointers, one per hand.
        private LineRenderer[] lasers;
        private SpriteRenderer[] dots;

        public static CapriSettings Make(CapriPet pet)
        {
            GameObject root = new GameObject("Capri settings");
            CapriSettings settings = root.AddComponent<CapriSettings>();
            settings.pet = pet;
            settings.back = Box(root.transform, Vector3.zero, new Vector2(Wide, 0.3f), Panel, 0);
            settings.rows = new GameObject("Rows").transform;
            settings.rows.SetParent(root.transform, false);
            settings.MakeLasers();
            root.SetActive(false);
            return settings;
        }

        public void Toggle(Transform head)
        {
            if (IsOpen) { Close(); return; }
            Vector3 forward = head.forward; forward.y = 0;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            transform.position = head.position + forward * 0.5f + Vector3.down * 0.15f;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            gameObject.SetActive(true);
            IsOpen = true;
            editing = false;
            Rebuild();
        }

        public void Close()
        {
            gameObject.SetActive(false);
            IsOpen = false;
            editing = false;
        }

        public void Refresh() { if (IsOpen) Rebuild(); }

        // ---------- the tabs ----------

        private void Rebuild()
        {
            foreach (Transform child in rows) Destroy(child.gameObject);
            buttons.Clear();
            if (editing) { BuildKeyboard(); return; }
            int row = 0;

            // Title and tabs.
            Words(rows, new Vector3(-Wide / 2 + 0.015f, Top + 0.045f, -0.002f), "CAPRI SETTINGS", 0.2f, Pink, TextAlignmentOptions.Left, 0.3f);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int pick = i;
                Pressable(new Vector3(-Wide / 2 + 0.065f + i * 0.105f, Top + 0.008f, -0.002f), new Vector2(0.095f, 0.03f), Tabs[i], i == tab ? Pink : Dim, delegate { tab = pick; Rebuild(); });
            }
            Pressable(new Vector3(Wide / 2 - 0.03f, Top + 0.045f, -0.002f), new Vector2(0.04f, 0.028f), "X", Dim, Close);

            switch (tab)
            {
                case 0:
                    Choice(ref row, "Brain", CapriConfig.Brains, CapriConfig.Brain, v => { CapriConfig.Brain = v; });
                    if (CapriConfig.Brain != 0)
                    {
                        Field(ref row, CapriConfig.Brain == 1 ? "NVIDIA key" : "API key", CapriConfig.Masked(CapriConfig.BrainKey), true, v => CapriConfig.BrainKey = v);
                        Field(ref row, "Model", CapriConfig.BrainModel, false, v => CapriConfig.BrainModel = v);
                    }
                    Choice(ref row, "Replies", CapriConfig.Lengths, CapriConfig.Replies, v => CapriConfig.Replies = v);
                    Choice(ref row, "Memory", CapriConfig.Lengths, CapriConfig.Memory, v => CapriConfig.Memory = v);
                    Toggle(ref row, "Remember our chats", CapriConfig.RememberChats, v => CapriConfig.RememberChats = v);
                    Do(ref row, "Chat history", "Forget", pet.ForgetChats);
                    break;
                case 1:
                    Choice(ref row, "Voice", CapriConfig.Voices, CapriConfig.VoiceService, v => CapriConfig.VoiceService = v);
                    Field(ref row, "Fish Audio key", CapriConfig.Masked(CapriConfig.FishKey), true, v => CapriConfig.FishKey = v);
                    Field(ref row, "Voice ID or link", string.IsNullOrEmpty(CapriConfig.FishVoice) ? "(Fish default)" : CapriConfig.FishVoiceId, false, v => CapriConfig.FishVoice = v);
                    Field(ref row, "Fish model", CapriConfig.FishModel, false, v => CapriConfig.FishModel = v);
                    Choice(ref row, "Hearing", CapriConfig.Hearings, CapriConfig.Hearing, v => CapriConfig.Hearing = v);
                    if (CapriConfig.Hearing == 0) Field(ref row, "Groq key (free)", CapriConfig.Masked(CapriConfig.GroqKey), true, v => CapriConfig.GroqKey = v);
                    else if (CapriConfig.Hearing == 2 && CapriConfig.Brain != 2) Do(ref row, "OpenAI hearing", "Needs OpenAI brain", delegate { });
                    Choice(ref row, "Volume", CapriConfig.VolumeNames, CapriConfig.Volume, v => CapriConfig.Volume = v);
                    Choice(ref row, "Pitch", CapriConfig.PitchNames, CapriConfig.Pitch, v => CapriConfig.Pitch = v);
                    Choice(ref row, "Speed", CapriConfig.SpeedNames, CapriConfig.Speed, v => CapriConfig.Speed = v);
                    Choice(ref row, "Robot", CapriConfig.AmountNames, CapriConfig.Robot, v => CapriConfig.Robot = v);
                    Choice(ref row, "Glitch", CapriConfig.AmountNames, CapriConfig.Glitch, v => CapriConfig.Glitch = v);
                    Do(ref row, "Hear her voice", "Test", pet.TestVoice);
                    break;
                case 2:
                    Toggle(ref row, "Microphone (X mutes)", !CapriConfig.MicMuted, v => CapriConfig.MicMuted = !v);
                    Choice(ref row, "Language", CapriConfig.LanguageNames, CapriConfig.Language, v => CapriConfig.Language = v);
                    Field(ref row, "What should she call you?", string.IsNullOrEmpty(CapriConfig.CallMe) ? "(not set)" : CapriConfig.CallMe, false, v => CapriConfig.CallMe = v);
                    break;
                case 3:
                    Choice(ref row, "Wander", CapriConfig.WanderNames, CapriConfig.Wander, v => CapriConfig.Wander = v);
                    Choice(ref row, "Sleep", CapriConfig.SleepNames, CapriConfig.Sleep, v => CapriConfig.Sleep = v);
                    Choice(ref row, "Pop-ins", CapriConfig.PopNames, CapriConfig.PopIns, v => CapriConfig.PopIns = v);
                    Choice(ref row, "Size", CapriConfig.SizeNames, pet.SizeIndex, pet.SetSizeIndex);
                    Choice(ref row, "Bubble", CapriConfig.BubbleNames, CapriConfig.Bubble, v => CapriConfig.Bubble = v);
                    Toggle(ref row, "Glitch in at startup", CapriConfig.GlitchIn, v => CapriConfig.GlitchIn = v);
                    break;
            }

            // Size the panel to its rows, with the hint underneath.
            float bottom = Top - 0.035f - row * RowGap;
            hint = Words(rows, new Vector3(0, bottom - 0.005f, -0.002f), "Point and pull the trigger (or pinch)  ·  Y closes", 0.12f, new Color(1, 1, 1, 0.55f), TextAlignmentOptions.Center, Wide);
            float height = Top + 0.07f - (bottom - 0.025f);
            back.transform.localScale = new Vector3(Wide, height, 1);
            back.transform.localPosition = new Vector3(0, Top + 0.07f - height / 2, 0);
        }

        private float RowY(int row) { return Top - 0.04f - row * RowGap; }

        private void Label(int row, string text)
        {
            Words(rows, new Vector3(-Wide / 2 + 0.015f, RowY(row), -0.002f), text, 0.15f, Ink, TextAlignmentOptions.Left, 0.22f);
        }

        private void Choice(ref int row, string label, string[] names, int index, Action<int> set)
        {
            Label(row, label);
            int at = Mathf.Clamp(index, 0, names.Length - 1);
            float y = RowY(row);
            Pressable(new Vector3(0.025f, y, -0.002f), new Vector2(0.03f, 0.028f), "<", Dim, delegate { set((at + names.Length - 1) % names.Length); Changed(); });
            Words(rows, new Vector3(0.125f, y, -0.002f), names[at], 0.14f, Color.white, TextAlignmentOptions.Center, 0.16f);
            Pressable(new Vector3(0.225f, y, -0.002f), new Vector2(0.03f, 0.028f), ">", Dim, delegate { set((at + 1) % names.Length); Changed(); });
            row++;
        }

        private void Toggle(ref int row, string label, bool on, Action<bool> set)
        {
            Label(row, label);
            Pressable(new Vector3(0.125f, RowY(row), -0.002f), new Vector2(0.07f, 0.028f), on ? "ON" : "OFF", on ? Pink : Dim, delegate { set(!on); Changed(); });
            row++;
        }

        private void Field(ref int row, string label, string shown, bool secret, Action<string> set)
        {
            Label(row, label);
            float y = RowY(row);
            Words(rows, new Vector3(0.09f, y, -0.002f), Short(shown), 0.12f, Color.white, TextAlignmentOptions.Center, 0.17f);
            Pressable(new Vector3(0.215f, y, -0.002f), new Vector2(0.05f, 0.028f), "Edit", Dim, delegate { Type(secret ? "" : shown.StartsWith("(") ? "" : shown, secret, label, set); });
            row++;
        }

        private void Do(ref int row, string label, string button, Action act)
        {
            Label(row, label);
            Pressable(new Vector3(0.125f, RowY(row), -0.002f), new Vector2(0.09f, 0.028f), button, Dim, act);
            row++;
        }

        private void Changed()
        {
            pet.SettingsChanged();
            Rebuild();
        }

        private static string Short(string s) { return s != null && s.Length > 22 ? s.Substring(0, 20) + "…" : s; }

        // ---------- her keyboard ----------

        private static readonly string[] KeyRows = { "1234567890-", "qwertyuiop", "asdfghjkl_", "zxcvbnm./:" };
        private const float KeyPitch = 0.042f, KeySize = 0.038f;

        private void Type(string start, bool isSecret, string label, Action<string> set)
        {
            typed = set;
            text = start ?? "";
            secret = isSecret;
            reveal = false;
            shift = false;
            editLabel = label;
            editing = true;
            Rebuild();
        }

        private void BuildKeyboard()
        {
            float top = Top + 0.045f;
            Words(rows, new Vector3(-Wide / 2 + 0.015f, top, -0.002f), editLabel.ToUpperInvariant(), 0.17f, Pink, TextAlignmentOptions.Left, 0.4f);

            // The text bar.
            Box(rows, new Vector3(-0.03f, top - 0.04f, -0.001f), new Vector2(Wide - 0.09f, 0.036f), new Color(0, 0, 0, 0.6f), 1);
            bar = Words(rows, new Vector3(-Wide / 2 + 0.02f, top - 0.04f, -0.003f), "", 0.14f, Color.white, TextAlignmentOptions.Left, Wide - 0.11f);
            bar.overflowMode = TextOverflowModes.Truncate;
            if (secret) Pressable(new Vector3(Wide / 2 - 0.035f, top - 0.04f, -0.002f), new Vector2(0.055f, 0.03f), reveal ? "Hide" : "Show", Dim, delegate { reveal = !reveal; ShowText(); Rebuild(); });
            ShowText();

            // The keys.
            float y = top - 0.09f;
            for (int r = 0; r < KeyRows.Length; r++)
            {
                string keys = KeyRows[r];
                float startX = -(keys.Length - 1) * KeyPitch / 2 + (r == 3 ? KeyPitch / 2 : 0);
                if (r == 3) Pressable(new Vector3(startX - KeyPitch, y, -0.002f), new Vector2(KeySize, KeySize), "Shift", shift ? Pink : Dim, delegate { shift = !shift; Rebuild(); });
                for (int k = 0; k < keys.Length; k++)
                {
                    char c = shift ? char.ToUpperInvariant(keys[k]) : keys[k];
                    Pressable(new Vector3(startX + k * KeyPitch, y, -0.002f), new Vector2(KeySize, KeySize), c.ToString(), Dim, delegate { Add(c.ToString()); });
                }
                y -= KeyPitch;
            }

            // Paste, Space, Backspace, Clear, Cancel, Done.
            float x = -Wide / 2 + 0.01f;
            x = Wide_Key(x, y, 0.07f, "Paste", Dim, Paste);
            x = Wide_Key(x, y, 0.13f, "Space", Dim, delegate { Add(" "); });
            x = Wide_Key(x, y, 0.06f, "<-", Dim, delegate { if (text.Length > 0) { text = text.Substring(0, text.Length - 1); ShowText(); } });
            x = Wide_Key(x, y, 0.065f, "Clear", Dim, delegate { text = ""; ShowText(); });
            x = Wide_Key(x, y, 0.07f, "Cancel", Dim, delegate { editing = false; Rebuild(); });
            Wide_Key(x, y, 0.08f, "Done", Pink, Finish);

            float bottom = y - 0.035f;
            Words(rows, new Vector3(0, bottom, -0.002f), "Copy a key in the Quest browser, then press Paste", 0.11f, new Color(1, 1, 1, 0.55f), TextAlignmentOptions.Center, Wide);
            float height = Top + 0.07f - (bottom - 0.02f);
            back.transform.localScale = new Vector3(Wide, height, 1);
            back.transform.localPosition = new Vector3(0, Top + 0.07f - height / 2, 0);
        }

        private float Wide_Key(float x, float y, float width, string label, Color color, Action click)
        {
            Pressable(new Vector3(x + width / 2, y, -0.002f), new Vector2(width, KeySize), label, color, click);
            return x + width + 0.008f;
        }

        private void Add(string more)
        {
            text += more;
            if (shift) { shift = false; Rebuild(); return; }
            ShowText();
        }

        private void Paste()
        {
            string clip = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clip)) { pet.Say("Your clipboard's empty, choom."); return; }
            text += clip.Trim().Replace("\n", "").Replace("\r", "");
            ShowText();
        }

        private void Finish()
        {
            editing = false;
            if (typed != null && text.Trim().Length > 0) typed(text.Trim());
            typed = null;
            Changed();
        }

        private void ShowText()
        {
            if (bar == null) return;
            string shown = secret && !reveal ? new string('•', text.Length) : text;
            // Keep the end of long text in view.
            if (shown.Length > 40) shown = "…" + shown.Substring(shown.Length - 39);
            bar.text = shown + "<color=#FF2A6B>|</color>";
        }

        // ---------- the laser pointer ----------

        private void MakeLasers()
        {
            lasers = new LineRenderer[2];
            dots = new SpriteRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                GameObject line = new GameObject("Laser");
                line.transform.SetParent(transform, false);
                lasers[i] = line.AddComponent<LineRenderer>();
                lasers[i].sharedMaterial = back.sharedMaterial;   // the sprite material: unlit, takes vertex colours
                lasers[i].positionCount = 2;
                lasers[i].useWorldSpace = true;
                lasers[i].widthMultiplier = 0.003f;
                lasers[i].startColor = new Color(1f, 0.16f, 0.42f, 0.9f);
                lasers[i].endColor = new Color(1f, 0.16f, 0.42f, 0.25f);
                lasers[i].sortingOrder = 40;

                GameObject dot = new GameObject("Cursor");
                dot.transform.SetParent(transform, false);
                dots[i] = dot.AddComponent<SpriteRenderer>();
                Texture2D white = Texture2D.whiteTexture;
                dots[i].sprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
                dots[i].color = Color.white;
                dots[i].sortingOrder = 41;
                dot.transform.localScale = new Vector3(0.008f, 0.008f, 1);
            }
        }

        // Called by Capri each frame while the panel is open, with your hands.
        public void Point(HandState[] hands)
        {
            Plane panel = new Plane(transform.forward, transform.position);
            Button pressed = null;
            foreach (Button b in buttons) if (b.Back != null) b.Back.color = b.Normal;
            for (int i = 0; i < hands.Length && i < 2; i++)
            {
                HandState hand = hands[i];
                LineRenderer laser = lasers[i];
                SpriteRenderer dot = dots[i];
                if (!hand.Tracked) { laser.enabled = false; dot.enabled = false; continue; }
                Ray ray = new Ray(hand.PointFrom, hand.PointAlong);
                float distance;
                bool hit = panel.Raycast(ray, out distance) && distance < 3f;
                Vector3 end = hit ? ray.GetPoint(distance) : ray.GetPoint(0.6f);
                if (hit)
                {
                    Vector3 local = back.transform.InverseTransformPoint(end);
                    hit = Mathf.Abs(local.x) <= 0.55f && Mathf.Abs(local.y) <= 0.55f;   // on the panel
                }
                laser.enabled = true;
                laser.SetPosition(0, ray.origin);
                laser.SetPosition(1, end);
                dot.enabled = hit;
                if (!hit) continue;
                dot.transform.position = end - transform.forward * 0.004f;
                dot.transform.rotation = transform.rotation;

                foreach (Button b in buttons)
                {
                    if (b.Box == null) continue;
                    Vector3 on = b.Box.InverseTransformPoint(end);
                    if (Mathf.Abs(on.x) > 0.5f || Mathf.Abs(on.y) > 0.5f) continue;
                    if (b.Back != null) b.Back.color = Hover;
                    if (hand.Clicked && Time.time > cooldown) pressed = b;
                    break;
                }
            }
            if (pressed != null)
            {
                cooldown = Time.time + 0.15f;
                pressed.Click();
            }
        }

        // ---------- building blocks ----------

        private void Pressable(Vector3 at, Vector2 size, string text, Color color, Action click)
        {
            SpriteRenderer box = Box(rows, at, size, color, 2);
            // The box's z scale sets how far in front or behind the panel a touch still counts (InverseTransformPoint divides by it).
            box.transform.localScale = new Vector3(size.x, size.y, 0.01f);
            Words(rows, at + new Vector3(0, 0, -0.002f), text, 0.13f, Color.white, TextAlignmentOptions.Center, size.x);
            buttons.Add(new Button { Box = box.transform, Back = box, Normal = color, Click = click });
        }

        private static SpriteRenderer Box(Transform parent, Vector3 at, Vector2 size, Color color, int order)
        {
            GameObject go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            SpriteRenderer box = go.AddComponent<SpriteRenderer>();
            Texture2D white = Texture2D.whiteTexture;
            box.sprite = Sprite.Create(white, new Rect(0, 0, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
            box.color = color;
            box.sortingOrder = 20 + order;
            return box;
        }

        private static TextMeshPro Words(Transform parent, Vector3 at, string text, float size, Color color, TextAlignmentOptions align, float width)
        {
            GameObject go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            TextMeshPro words = go.AddComponent<TextMeshPro>();
            words.text = text;
            words.fontSize = size;
            words.color = color;
            words.alignment = align;
            words.textWrappingMode = TextWrappingModes.NoWrap;
            words.overflowMode = TextOverflowModes.Ellipsis;
            words.rectTransform.sizeDelta = new Vector2(width, 0.03f);
            if (align == TextAlignmentOptions.Left) words.rectTransform.pivot = new Vector2(0, 0.5f);
            words.sortingOrder = 30;
            return words;
        }
    }
}
