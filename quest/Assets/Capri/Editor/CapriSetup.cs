// Editor helpers: sets up Capri's sprite sheets when they're imported, and adds a menu item that puts her in the scene.
using UnityEditor;
using UnityEngine;

namespace CapriRoom.EditorTools
{
    public class CapriTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Capri/Resources/CapriSprites/")) return;
            TextureImporter importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;   // keep exact pixels so frames line up
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;                         // less shimmer when she's across the room
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }
    }

    // Makes sure the Quest build can open the system keyboard (for her settings) and reach the internet (for her brain and voice).
    [InitializeOnLoad]
    public static class CapriProjectSettings
    {
        static CapriProjectSettings() { EditorApplication.delayCall += Apply; }

        [MenuItem("Capri/Fix Quest settings for talking")]
        public static void Apply()
        {
            PlayerSettings.Android.forceInternetPermission = true;
            // Meta's "Requires System Keyboard" setting, set by name so this still compiles if Meta moves it.
            try
            {
                System.Type type = System.Type.GetType("OVRProjectConfig, Oculus.VR.Editor") ?? FindType("OVRProjectConfig");
                if (type == null) return;
                object config = type.GetMethod("GetProjectConfig", new System.Type[0]) != null
                    ? type.GetMethod("GetProjectConfig", new System.Type[0]).Invoke(null, null)
                    : null;
                if (config == null) return;
                System.Reflection.FieldInfo keyboard = type.GetField("requiresSystemKeyboard");
                if (keyboard == null || (bool)keyboard.GetValue(config)) return;
                keyboard.SetValue(config, true);
                System.Reflection.MethodInfo commit = type.GetMethod("CommitProjectConfig");
                if (commit != null) commit.Invoke(null, new[] { config });
                Debug.Log("[Capri] Turned on Requires System Keyboard so her settings can open the Quest keyboard.");
            }
            catch (System.Exception problem) { Debug.LogWarning("[Capri] Couldn't turn on the Quest keyboard setting: " + problem.Message); }
        }

        private static System.Type FindType(string name)
        {
            foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type found = assembly.GetType(name);
                if (found != null) return found;
            }
            return null;
        }
    }

    public static class CapriMenu
    {
        [MenuItem("Capri/Add Capri to Scene")]
        public static void AddToScene()
        {
            CapriPet existing = Object.FindFirstObjectByType<CapriPet>();
            if (existing != null)
            {
                Selection.activeObject = existing.gameObject;
                EditorUtility.DisplayDialog("Capri", "Capri is already in this scene.", "OK");
                return;
            }
            GameObject capri = new GameObject("Capri");
            capri.AddComponent<RoomSense>();
            capri.AddComponent<CapriHands>();
            capri.AddComponent<CapriPet>();
            Undo.RegisterCreatedObjectUndo(capri, "Add Capri");
            Selection.activeObject = capri;
            if (Object.FindFirstObjectByType<OVRCameraRig>() == null)
                EditorUtility.DisplayDialog("Capri", "Capri is in. This scene has no Camera Rig yet, so add the Camera Rig, Passthrough and MR Utility Kit building blocks too (Meta > Tools > Building Blocks).", "OK");
        }
    }
}
