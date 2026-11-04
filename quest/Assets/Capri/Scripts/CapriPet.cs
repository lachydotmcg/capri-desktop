// Capri in your room: she glitches in near you, walks around your floor, sits on the edges of your furniture,
// naps when you ignore her, reacts when you pet, poke, grab or throw her, and talks with you through your headset's mic.
//
// Controls
//   Grab her:    grip button (controllers) or pinch (hand tracking) with your hand near her. Let go to drop or throw her.
//   Resize her:  while holding her with a controller, push its thumbstick up or down.
//   Pet her:     move your hand gently near her head.
//   Talk:        just talk. She listens on the headset's mic (needs a Fish Audio key, or OpenAI as her brain).
//   Mute mic:    X.
//   Bop:         A, while she's sitting on a ledge.
//   Come here:   B.
//   Settings:    Y opens her settings: BRAIN, VOICE, YOU and MOVES.
//   Put her down next to the edge of a table, couch or bed and she sits on it.
using System;
using UnityEngine;

namespace CapriRoom
{
    [RequireComponent(typeof(RoomSense), typeof(CapriHands))]
    public class CapriPet : MonoBehaviour
    {
        [Header("Size and movement")]
        [Tooltip("Her height in metres. You can also change it in the headset by holding her and pushing the thumbstick.")]
        [Range(0.2f, 1.7f)] public float size = 0.5f;
        public float walkSpeed = 0.35f;
        [Tooltip("She won't wander further than this from you.")]
        public float stayWithin = 2.5f;

        [Header("Habits (Wander, Sleep and Pop-ins are set in the headset: Y opens her settings)")]
        [Range(0, 1)] public float chanceToSitOnFurniture = 0.35f;
        public Vector2 secondsSittingOnFurniture = new Vector2(20, 60);

        private enum Mode { Waiting, GlitchingOut, GlitchingIn, Idle, Walking, Perched, Held, Falling, Napping, Waking, Dancing, Reacting }

        private static readonly string[] PetLines =
        {
            "Heh. Okay, I'll allow it.", "Pets logged. Morale up twelve percent.", "Careful, choom. I bite viruses.",
            "Head pats? On my chrome? Bold.", "Okay, that's nice. Don't tell anyone.", "System status: very patted.",
            "You're gonna wear a groove in my firmware, choom.", "Easy on the hair. It took three patches to get it like this.",
            "Affection detected. Sending some back. Hi.", "Keep going. This beats defragging.", "Hehe. My cooling fans just kicked in.",
            "Ten out of ten pats. Would get patted again.", "Is this a bribe? Because it's working.", "Mmm. If I start glitch-purring, that's on you."
        };
        private static readonly string[] PokeLines = { "Hey! Personal space, choom.", "Ow. Rude.", "Poke detected. Filing a complaint.", "Do that again and I'm deleting your saves." };
        private static readonly string[] TossLines = { "Did you just throw me?!", "Landing confirmed. Mostly.", "Okay. Never again.", "Ten out of ten. Do it again." };
        private static readonly string[] HoldLines = { "Put me down, choom!", "Hey! Where are we going?", "Careful, I'm fragile code." };
        private static readonly string[] WakeLines = { "Mm? I'm up. I'm up.", "Was I asleep? Don't tell anyone.", "Rebooting... hi." };

        // How far her feet hang below your hand when you hold her (from the dangle art, in sizes).
        private const float DangleDrop = 0.907f;
        private const string SizeKey = "capri.size";

        private RoomSense room;
        private CapriHands hands;
        private Transform body;
        private SpriteRenderer sprite, mouth, shadow;
        private SpeechBubble bubble;
        private AudioSource voice;

        private Mode mode = Mode.Waiting;
        private CapriClip clip;
        private bool looping;
        private float clipStarted;
        private Action clipDone;

        private Surface standingOn;            // null = the floor
        private Vector3 walkTarget;
        private Surface teleportTarget;
        private float nextWander, perchUntil, nextBlink, lastAttention, gazeHeld, petHeld, reactCooldown;
        private Vector3 velocity;
        private HandState holder;
        private float fallStarted, nextShadowCheck, shadowY, nextMouthFrame;
        private bool greeted, wasHearing;
        private CapriSettings settings;
        private CapriBrain brain;
        private CapriVoice speaker;
        private CapriEars ears;
        private float nextPop;

        private bool Wandering { get { return CapriConfig.Wander != 0; } }
        private Vector2 secondsBetweenWanders { get { return CapriConfig.WanderGaps[Mathf.Clamp(CapriConfig.Wander, 0, 3)]; } }
        private float napAfterSeconds { get { float s = CapriConfig.SleepSeconds[Mathf.Clamp(CapriConfig.Sleep, 0, 4)]; return s <= 0 ? float.MaxValue : s; } }

        private void Start()
        {
            room = GetComponent<RoomSense>();
            hands = GetComponent<CapriHands>();
            size = PlayerPrefs.GetFloat(SizeKey, size);
            settings = CapriSettings.Make(this);
            brain = gameObject.AddComponent<CapriBrain>();
            speaker = gameObject.AddComponent<CapriVoice>();
            ears = gameObject.AddComponent<CapriEars>();
            ears.Paused = delegate { return voice.isPlaying || brain.Busy || speaker.Busy || mode == Mode.Waiting; };
            ears.Said = Heard;
            ears.Trouble = line => Say(line);
            nextPop = Time.time + Gap(CapriConfig.PopGaps[Mathf.Clamp(CapriConfig.PopIns, 0, 2)]);

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            sprite = body.gameObject.AddComponent<SpriteRenderer>();
            GameObject mouthObject = new GameObject("Mouth");
            mouthObject.transform.SetParent(body, false);
            mouth = mouthObject.AddComponent<SpriteRenderer>();
            mouth.sortingOrder = 1;
            mouth.enabled = false;

            GameObject shadowObject = new GameObject("Shadow");
            shadowObject.transform.SetParent(transform, false);
            shadowObject.transform.localRotation = Quaternion.Euler(90, 0, 0);
            shadow = shadowObject.AddComponent<SpriteRenderer>();
            shadow.sprite = BlobSprite();
            shadow.sortingOrder = -1;

            bubble = SpeechBubble.Make(transform);
            voice = gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 1;           // her voice comes from where she is
            voice.minDistance = 0.5f;
            voice.maxDistance = 8;
            speaker.Attach(voice);

            SetVisible(false);
        }

        private void Update()
        {
            if (hands.Head == null) return;
            if (mode == Mode.Waiting)
            {
                if (!room.Ready) return;
                Vector3 front = hands.Head.position + RoomSense.Flat(hands.Head.forward).normalized * 1.1f;
                GlitchIn(new Surface { Point = room.FloorSpot(front, 0.4f) });
                return;
            }

            Animate();
            Interact();
            if (settings.IsOpen) settings.Point(hands.Both);
            Listen();
            PopIn();

            switch (mode)
            {
                case Mode.Idle: Idle(); break;
                case Mode.Walking: Walk(); break;
                case Mode.Perched: Perched(); break;
                case Mode.Held: Held(); break;
                case Mode.Falling: Fall(); break;
                case Mode.Napping: Nap(); break;
            }

            FaceYou();
            PlaceShadow();
            Mouth();
        }

        // ---------- Animation ----------

        private void Play(string name, bool loop, Action done = null)
        {
            clip = CapriClips.Get(name);
            looping = loop;
            clipStarted = Time.time;
            clipDone = done;
            sprite.sprite = clip.Frames[0];
        }

        private void Animate()
        {
            if (clip == null) return;
            int frame = (int)((Time.time - clipStarted) * clip.Fps);
            if (frame >= clip.Frames.Length)
            {
                if (looping) frame %= clip.Frames.Length;
                else
                {
                    sprite.sprite = clip.Frames[clip.Frames.Length - 1];
                    Action done = clipDone;
                    clipDone = null;
                    if (done != null) done();
                    return;
                }
            }
            sprite.sprite = clip.Frames[frame];
            // Her art is drawn so one unit is her size; keep the body scaled to it and lifted when she dangles.
            body.localScale = Vector3.one * size;
            body.localPosition = clip.Name == "dangle" ? Vector3.up * DangleDrop * size : Vector3.zero;
        }

        private void Rest()
        {
            if (standingOn != null && standingOn.Edge) { mode = Mode.Perched; Play("perch", true); }
            else { mode = Mode.Idle; Play("idle", true); }
            nextBlink = Time.time + UnityEngine.Random.Range(2.5f, 6f);
        }

        // ---------- Getting around ----------

        private void GlitchIn(Surface to)
        {
            SetVisible(true);
            standingOn = to.Raised ? to : null;
            transform.position = to.Point;
            mode = Mode.GlitchingIn;
            bool quietly = !greeted && !CapriConfig.GlitchIn;   // "Glitch in at startup" off: she's just there
            Play(quietly ? (to.Edge ? "perch" : "idle") : to.Edge ? "perch_arrive" : "arrive", false, delegate
            {
                Rest();
                if (to.Edge) perchUntil = Time.time + UnityEngine.Random.Range(secondsSittingOnFurniture.x, secondsSittingOnFurniture.y);
                nextWander = Time.time + UnityEngine.Random.Range(secondsBetweenWanders.x, secondsBetweenWanders.y);
                if (!greeted) { greeted = true; Greet(); }
            });
        }

        private void GlitchTo(Surface to)
        {
            teleportTarget = to;
            mode = Mode.GlitchingOut;
            bool perched = standingOn != null && standingOn.Edge;
            Play(perched ? "perch_leave" : "leave", false, delegate { GlitchIn(teleportTarget); });
        }

        private void ComeHere()
        {
            Vector3 front = hands.Head.position + RoomSense.Flat(hands.Head.forward).normalized * 0.9f;
            Say("Coming, choom.");
            GlitchTo(new Surface { Point = room.FloorSpot(front, 0.3f) });
        }

        private void Wander()
        {
            nextWander = Time.time + UnityEngine.Random.Range(secondsBetweenWanders.x, secondsBetweenWanders.y);
            Vector3 you = hands.Head.position;
            if (UnityEngine.Random.value < chanceToSitOnFurniture)
            {
                Surface seat = room.FurnitureSpot(you, stayWithin + 0.5f);
                if (seat != null) { GlitchTo(seat); return; }
            }
            Vector3 spot = room.FloorSpot(you, stayWithin);
            if (standingOn != null) { GlitchTo(new Surface { Point = spot }); return; }
            walkTarget = spot;
            mode = Mode.Walking;
            Play("walk", true);
        }

        private void Idle()
        {
            if (Time.time > nextBlink)
            {
                nextBlink = Time.time + UnityEngine.Random.Range(2.5f, 6f);
                Play(UnityEngine.Random.value < 0.15f ? "blink_glitch" : "blink", false, delegate { if (mode == Mode.Idle) Play("idle", true); });
            }
            if (Time.time - lastAttention > napAfterSeconds && standingOn == null)
            {
                mode = Mode.Napping;
                Play("sleep", true);
                return;
            }
            // Follow you if you walk off.
            if (!Wandering) return;
            if (RoomSense.Flat(transform.position - hands.Head.position).magnitude > stayWithin + 1.5f) { nextWander = 0; }
            if (Time.time > nextWander && !voice.isPlaying) Wander();
        }

        private void Walk()
        {
            Vector3 to = walkTarget - transform.position;
            to.y = 0;
            float step = walkSpeed * size / 0.5f * Time.deltaTime;   // bigger Capri, longer steps
            if (to.magnitude <= step) { transform.position = new Vector3(walkTarget.x, transform.position.y, walkTarget.z); Rest(); return; }
            transform.position += to.normalized * step;
        }

        private void Perched()
        {
            if (clip != null && clip.Name == "perch_music") return;   // bopping along; A or X stops it
            if (Time.time > nextBlink)
            {
                nextBlink = Time.time + UnityEngine.Random.Range(2.5f, 6f);
                Play(UnityEngine.Random.value < 0.15f ? "perch_blink_glitch" : "perch_blink", false, delegate { if (mode == Mode.Perched) Play("perch", true); });
            }
            if (Wandering && Time.time > perchUntil && Time.time - lastAttention > 5) GlitchTo(new Surface { Point = room.FloorSpot(hands.Head.position, stayWithin) });
        }

        private void Nap()
        {
            if (Time.time - lastAttention < 1)
            {
                mode = Mode.Waking;
                Play("wake", false, delegate { Rest(); Say(Pick(WakeLines)); });
            }
        }

        // ---------- You and her ----------

        private Vector3 HeadPoint { get { return transform.position + Vector3.up * size * (standingOn != null && standingOn.Edge ? 0.45f : 0.82f); } }
        private Vector3 MiddlePoint { get { return transform.position + Vector3.up * size * 0.5f; } }

        private void Interact()
        {
            bool busy = mode == Mode.GlitchingIn || mode == Mode.GlitchingOut;
            Vector3 eye = hands.Head.position;

            // Looking at her up close counts as attention (and wakes her after a couple of seconds).
            Vector3 toHer = MiddlePoint - eye;
            bool looking = toHer.magnitude < 2f && Vector3.Angle(hands.Head.forward, toHer) < 12f;
            gazeHeld = looking ? gazeHeld + Time.deltaTime : 0;
            if (mode != Mode.Napping && looking) lastAttention = Time.time;
            if (mode == Mode.Napping && gazeHeld > 2f) lastAttention = Time.time;

            foreach (HandState hand in hands.Both)
            {
                if (!hand.Tracked) continue;
                float reach = Mathf.Max(0.18f, size * 0.45f);
                float fromMiddle = Vector3.Distance(hand.Position, MiddlePoint);
                if (fromMiddle < 0.6f) lastAttention = Time.time;

                // Y (left controller) opens and closes her settings. X mutes and unmutes her mic.
                if (hand.ButtonTwoDown && hand.Left) { settings.Toggle(hands.Head); return; }
                if (hand.ButtonOneDown && hand.Left) { ToggleMic(); return; }

                if (busy) continue;

                if (hand.ButtonTwoDown && !hand.Left && mode != Mode.Held) { ComeHere(); return; }
                if (hand.ButtonOneDown && !hand.Left && mode == Mode.Perched) { ToggleDance(); return; }   // no standing dance, only bopping on a ledge

                if (mode != Mode.Held && mode != Mode.Falling && hand.GrabStarted && fromMiddle < reach + 0.1f)
                {
                    PickUp(hand);
                    return;
                }

                if (mode == Mode.Held || mode == Mode.Falling || mode == Mode.Napping) continue;
                float speed = hand.Velocity.magnitude;
                float fromHead = Vector3.Distance(hand.Position, HeadPoint);
                if (speed > 1.6f && fromMiddle < reach && Time.time > reactCooldown) { React("glitch", "perch_glitch", Pick(PokeLines)); return; }
                if (!hand.Grabbing && fromHead < Mathf.Max(0.15f, size * 0.3f) && speed > 0.08f && speed < 1.2f)
                {
                    petHeld += Time.deltaTime;
                    if (petHeld > 0.6f && Time.time > reactCooldown) { petHeld = 0; React("grin", "perch", Pick(PetLines)); return; }
                }
            }
        }

        private void React(string standingClip, string perchedClip, string line)
        {
            reactCooldown = Time.time + 4f;
            Say(line);
            bool perched = standingOn != null && standingOn.Edge;
            if (perched) { Play(perchedClip, false, delegate { if (mode == Mode.Perched) Play("perch", true); }); return; }
            mode = Mode.Reacting;
            Play(standingClip, false, Rest);
        }

        private void ToggleDance()
        {
            lastAttention = Time.time;
            bool perched = standingOn != null && standingOn.Edge;
            if (!perched) return;
            mode = Mode.Perched;   // she bops along sitting down
            Play(clip != null && clip.Name == "perch_music" ? "perch" : "perch_music", true);
        }

        private void PickUp(HandState hand)
        {
            holder = hand;
            mode = Mode.Held;
            standingOn = null;
            Play("dangle", true);
            Say(Pick(HoldLines));
        }

        private void Held()
        {
            if (!holder.Tracked || holder.GrabEnded || !holder.Grabbing)
            {
                velocity = Vector3.ClampMagnitude(holder.Velocity, 6f);
                // Put down gently beside the edge of a table, couch or bed: she sits on it.
                if (velocity.magnitude < 1.2f)
                {
                    Surface ledge = room.LedgeNear(transform.position + Vector3.up * DangleDrop * size * 0.5f, 0.3f);
                    if (ledge != null) { SitOn(ledge); return; }
                }
                fallStarted = Time.time;
                mode = Mode.Falling;
                return;
            }
            transform.position = holder.Position - Vector3.up * DangleDrop * size;
            if (Mathf.Abs(holder.Thumbstick) > 0.3f)
            {
                size = Mathf.Clamp(size * (1 + holder.Thumbstick * Time.deltaTime), 0.2f, 1.7f);
                PlayerPrefs.SetFloat(SizeKey, size);
            }
            lastAttention = Time.time;
        }

        private void Fall()
        {
            velocity += Physics.gravity * Time.deltaTime;
            Vector3 next = transform.position + velocity * Time.deltaTime;
            float ground = room.GroundBelow(next + Vector3.up * 0.3f);
            if (next.y <= ground && velocity.y <= 0)
            {
                next.y = ground;
                transform.position = next;
                bool thrown = Time.time - fallStarted > 0.35f;
                if (!room.Inside(next)) { ComeHere(); return; }
                if (ground > room.FloorY + 0.15f)
                {
                    Surface ledge = room.LedgeNear(next, 0.2f);   // landed near the edge of furniture: sit on it
                    if (ledge != null) { if (thrown) Say(Pick(TossLines)); SitOn(ledge); return; }
                }
                standingOn = ground > room.FloorY + 0.15f ? new Surface { Point = next, Raised = true } : null;
                mode = Mode.Reacting;
                if (thrown) Say(Pick(TossLines));
                Play("glitch", false, Rest);
                return;
            }
            transform.position = next;
            if (Time.time - fallStarted > 4f) ComeHere();   // lost somewhere: bring her back
        }

        private void SitOn(Surface ledge)
        {
            standingOn = ledge;
            transform.position = ledge.Point;
            mode = Mode.Perched;
            // She stays a good while where you put her.
            perchUntil = Time.time + 2 * UnityEngine.Random.Range(secondsSittingOnFurniture.x, secondsSittingOnFurniture.y);
            nextBlink = Time.time + UnityEngine.Random.Range(2.5f, 6f);
            Play("perch_glitch", false, delegate { if (mode == Mode.Perched) Play("perch", true); });
        }

        // ---------- Settings ----------

        public int SizeIndex
        {
            get
            {
                int best = 0;
                for (int i = 1; i < CapriConfig.SizeValues.Length; i++)
                    if (Mathf.Abs(CapriConfig.SizeValues[i] - size) < Mathf.Abs(CapriConfig.SizeValues[best] - size)) best = i;
                return best;
            }
        }

        public void SetSizeIndex(int index)
        {
            size = CapriConfig.SizeValues[Mathf.Clamp(index, 0, CapriConfig.SizeValues.Length - 1)];
            PlayerPrefs.SetFloat(SizeKey, size);
        }

        public void SettingsChanged()
        {
            speaker.Apply();
            nextWander = Mathf.Min(nextWander, Time.time + Gap(secondsBetweenWanders));
            nextPop = Time.time + Gap(CapriConfig.PopGaps[Mathf.Clamp(CapriConfig.PopIns, 0, 2)]);
        }

        public void ForgetChats()
        {
            brain.Forget();
            Say("Wiped. Who are you again? Kidding.");
        }

        public void TestVoice()
        {
            if (!speaker.Ready) { Say("Pick Fish Audio and add its key under VOICE first."); return; }
            Talk("Testing, testing. Do I sound preem from over here?");
        }

        private void ToggleMic()
        {
            CapriConfig.MicMuted = !CapriConfig.MicMuted;
            settings.Refresh();
            if (CapriConfig.MicMuted) Say("Mic muted. I'll just read your lips. Kidding.");
            else if (!ears.Ready) Say("Mic's on, but I need a key to hear you. Press Y, then VOICE > Hearing. Groq's is free.");
            else Say("I'm listening, choom.");
        }

        // ---------- Conversation ----------

        // Shows a little "..." while you're talking, so you know she heard you start.
        private void Listen()
        {
            if (ears.Hearing && !wasHearing && !voice.isPlaying) bubble.Say("...", 1.5f);
            wasHearing = ears.Hearing;
        }

        private void Heard(string said)
        {
            lastAttention = Time.time;
            if (!brain.Ready) { Say("My brain's offline. Press Y and set one up under BRAIN."); return; }
            if (mode == Mode.Napping) { mode = Mode.Waking; Play("wake", false, Rest); }
            bubble.Say("...", 20);
            brain.Reply(said, false, Talk, line => Say(line));
        }

        // She speaks up on her own now and then (Pop-ins).
        private void PopIn()
        {
            if (CapriConfig.PopIns == 0 || Time.time < nextPop) return;
            nextPop = Time.time + Gap(CapriConfig.PopGaps[Mathf.Clamp(CapriConfig.PopIns, 0, 2)]);
            if (!brain.Ready || brain.Busy || voice.isPlaying || ears.Hearing || mode == Mode.Napping || mode == Mode.Held || mode == Mode.Falling) return;
            brain.Reply(null, true, Talk, line => Debug.Log("[Capri] Pop-in failed: " + line));
        }

        // Says a line out loud with Fish Audio if it's set up, and always in her bubble.
        private void Talk(string line)
        {
            if (mode == Mode.Walking) Rest();   // she stops to talk
            if (!speaker.Ready) { Say(line); return; }
            bubble.Say(line, 20);
            speaker.Speak(line, delegate (AudioClip clip)
            {
                voice.clip = clip;
                voice.Play();
                if (mode == Mode.Idle && this.clip != null && this.clip.Name != "idle") Play("idle", true);
                Say(line, clip.length / Mathf.Max(0.5f, voice.pitch) + 1.5f);
            }, delegate (string problem)
            {
                Say(line);
                if (problem != null) Debug.Log("[Capri] " + problem);
                if (problem != null) StartCoroutineLater(problem);
            });
        }

        private void StartCoroutineLater(string problem) { StartCoroutine(SayLater(problem, CapriConfig.BubbleSeconds[Mathf.Clamp(CapriConfig.Bubble, 0, 2)] + 0.5f)); }

        private System.Collections.IEnumerator SayLater(string line, float wait)
        {
            yield return new WaitForSeconds(wait);
            Say(line);
        }

        private static float Gap(Vector2 range) { return range == Vector2.zero ? float.MaxValue / 4 : UnityEngine.Random.Range(range.x, range.y); }

        // ---------- Talking ----------

        private void Greet()
        {
            int hour = DateTime.Now.Hour;
            string name = hour >= 5 && hour < 12 ? "morning" : hour >= 22 || hour < 5 ? "late" : "miss";
            string line = name == "morning" ? "Good morning, choom." : name == "late" ? "Isn't it late, choom?" : "Miss me, choom?";
            if (speaker.Ready) { Talk(line); return; }   // her greeting in your Fish Audio voice
            AudioClip hello = Resources.Load<AudioClip>("CapriVoice/hello-" + name);
            Say(line, hello != null ? hello.length : 3);
            if (hello != null) voice.PlayOneShot(hello);
        }

        public void Say(string line, float seconds = 0)
        {
            float setting = CapriConfig.BubbleSeconds[Mathf.Clamp(CapriConfig.Bubble, 0, 2)];
            bubble.Say(line, Mathf.Max(seconds, setting + line.Length * 0.03f));
        }

        private void Mouth()
        {
            bool talking = voice.isPlaying && clip != null && clip.Name == "idle";
            mouth.enabled = talking;
            if (!talking || Time.time < nextMouthFrame) return;
            nextMouthFrame = Time.time + 1f / 12f;
            CapriClip shapes = CapriClips.Get("mouth");
            mouth.sprite = shapes.Frames[UnityEngine.Random.Range(0, shapes.Frames.Length)];
        }

        // ---------- Looks ----------

        private void FaceYou()
        {
            Vector3 away = RoomSense.Flat(transform.position - hands.Head.position);
            if (away.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            bubble.transform.position = transform.position + Vector3.up * (size * (standingOn != null && standingOn.Edge ? 0.75f : 1.08f) + 0.08f);
            bubble.Face(hands.Head.position);
        }

        private void PlaceShadow()
        {
            if (Time.time > nextShadowCheck)
            {
                nextShadowCheck = Time.time + 0.1f;
                shadowY = mode == Mode.Held || mode == Mode.Falling ? room.GroundBelow(transform.position + Vector3.up * 0.3f) : transform.position.y;
            }
            bool perched = standingOn != null && standingOn.Edge;
            shadow.enabled = sprite.enabled && !perched;
            shadow.transform.position = new Vector3(transform.position.x, shadowY + 0.004f, transform.position.z);
            float height = Mathf.Max(0, transform.position.y - shadowY);
            float spread = size * 0.55f * Mathf.Clamp(1 - height, 0.4f, 1f);
            shadow.transform.localScale = new Vector3(spread, spread * 0.5f, 1);
            shadow.color = new Color(0, 0, 0, 0.45f * Mathf.Clamp(1 - height, 0.2f, 1f));
        }

        private void SetVisible(bool on) { sprite.enabled = on; shadow.enabled = on; }

        private static Sprite BlobSprite()
        {
            const int n = 64;
            Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - d * d)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        }

        private static string Pick(string[] lines) { return lines[UnityEngine.Random.Range(0, lines.Length)]; }
    }
}
