// Loads Capri's animations from Resources/CapriSprites (sheets + clips.json, made from the desktop app's strips).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CapriRoom
{
    public sealed class CapriClip
    {
        public string Name;
        public Sprite[] Frames;
        public float Fps;
        public bool Perched;          // drawn sitting on a ledge, pivot at her seat
        public float Seconds { get { return Frames.Length / Fps; } }
    }

    public static class CapriClips
    {
        [Serializable] private class ClipInfo { public string name; public int count; public float fps; public int cellWidth, cellHeight, columns, rows; public float pivotX, pivotY, unitsHigh; }
        [Serializable] private class ClipList { public ClipInfo[] clips; }

        private static Dictionary<string, CapriClip> loaded;

        public static CapriClip Get(string name)
        {
            if (loaded == null) Load();
            CapriClip clip;
            if (!loaded.TryGetValue(name, out clip)) throw new Exception("Capri's '" + name + "' animation is missing from Resources/CapriSprites.");
            return clip;
        }

        private static void Load()
        {
            loaded = new Dictionary<string, CapriClip>();
            TextAsset json = Resources.Load<TextAsset>("CapriSprites/clips");
            if (json == null) throw new Exception("Resources/CapriSprites/clips.json is missing.");
            foreach (ClipInfo info in JsonUtility.FromJson<ClipList>(json.text).clips)
            {
                Texture2D sheet = Resources.Load<Texture2D>("CapriSprites/" + info.name);
                if (sheet == null) throw new Exception("Resources/CapriSprites/" + info.name + ".png is missing.");
                // One world unit = her size (about her standing height), so the root's scale is her size in metres.
                float pixelsPerUnit = info.cellHeight / info.unitsHigh;
                Sprite[] frames = new Sprite[info.count];
                for (int i = 0; i < info.count; i++)
                {
                    int col = i % info.columns, row = i / info.columns;
                    // Texture rows count up from the bottom.
                    Rect rect = new Rect(col * info.cellWidth, sheet.height - (row + 1) * info.cellHeight, info.cellWidth, info.cellHeight);
                    frames[i] = Sprite.Create(sheet, rect, new Vector2(info.pivotX, info.pivotY), pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    frames[i].name = info.name + "_" + i;
                }
                loaded[info.name] = new CapriClip { Name = info.name, Frames = frames, Fps = info.fps, Perched = info.name.StartsWith("perch") };
            }
        }
    }
}
