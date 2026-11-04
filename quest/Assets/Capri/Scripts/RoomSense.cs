// Everything Capri knows about your real room comes through here, from Meta's MR Utility Kit (MRUK).
// It reads the Space Setup you did on your Quest 3: floor, walls, tables, couches, beds and shelves.
// If no room loads (no Space Setup, permission denied, or running in the editor), she falls back to a flat floor
// at the height in "fallbackFloorY", so she still works, just without furniture.
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace CapriRoom
{
    public sealed class Surface
    {
        public Vector3 Point;        // where she stands or sits
        public bool Raised;          // on furniture, not the floor
        public bool Edge;            // on the edge of the furniture, so she can sit with her legs hanging
        public Vector3 EdgeOutward;  // which way the edge faces (her legs hang that way)
    }

    public class RoomSense : MonoBehaviour
    {
        [Tooltip("Floor height to use if no room scan loads. 0 works when the camera rig's tracking origin is Floor Level.")]
        public float fallbackFloorY = 0f;
        [Tooltip("How long to wait for the room scan before using the flat floor.")]
        public float waitForRoomSeconds = 8f;

        private const MRUKAnchor.SceneLabels Furniture =
            MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.BED |
            MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.OTHER;
        private const MRUKAnchor.SceneLabels Ground = Furniture | MRUKAnchor.SceneLabels.FLOOR;

        public bool Ready { get; private set; }
        public bool HasRoom { get { return room != null; } }
        private MRUKRoom room;
        private float started;
        private bool listening;

        private const string ScenePermission = "com.oculus.permission.USE_SCENE";

        private void Start()
        {
            started = Time.time;
            // Heights are measured from your real floor.
            if (OVRManager.instance != null) OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            Listen();
#if UNITY_ANDROID && !UNITY_EDITOR
            // Ask for "spatial data" so she can see your Space Setup, then load the room once you say yes.
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += delegate
                {
                    started = Time.time;
                    Ready = false;
                    if (MRUK.Instance != null) _ = MRUK.Instance.LoadSceneFromDevice();
                };
                UnityEngine.Android.Permission.RequestUserPermission(ScenePermission, callbacks);
            }
#endif
        }

        private void Listen()
        {
            if (listening || MRUK.Instance == null) return;
            listening = true;
            MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
        }

        private void OnSceneLoaded()
        {
            room = MRUK.Instance.GetCurrentRoom();
            Ready = true;
            if (room == null) Debug.Log("[Capri] No room found. Using a flat floor.");
            else Debug.Log("[Capri] Room loaded with " + room.Anchors.Count + " things in it.");
        }

        private void Update()
        {
            Listen();
            if (!Ready && Time.time - started > waitForRoomSeconds)
            {
                Ready = true;
                Debug.Log("[Capri] Room didn't load in time. Using a flat floor.");
            }
        }

        public float FloorY
        {
            get { return room != null && room.FloorAnchor != null ? room.FloorAnchor.transform.position.y : fallbackFloorY; }
        }

        // The highest walkable surface under a point (furniture top or floor).
        public float GroundBelow(Vector3 point)
        {
            if (room == null) return fallbackFloorY;
            Ray down = new Ray(point + Vector3.up * 0.05f, Vector3.down);
            MRUKAnchor hit;
            Vector3 normal;
            Pose pose = room.GetBestPoseFromRaycast(down, 4f, new LabelFilter(Ground), out hit, out normal);
            return hit != null ? pose.position.y : FloorY;
        }

        public bool Inside(Vector3 point)
        {
            return room == null || room.IsPositionInRoom(point);
        }

        // A random spot on the floor within maxDistance of "near", not inside furniture.
        public Vector3 FloorSpot(Vector3 near, float maxDistance)
        {
            if (room != null)
            {
                for (int i = 0; i < 16; i++)
                {
                    Vector3 pos, normal;
                    if (!room.GenerateRandomPositionOnSurface(MRUK.SurfaceType.FACING_UP, 0.25f, new LabelFilter(MRUKAnchor.SceneLabels.FLOOR), out pos, out normal)) continue;
                    if (Flat(pos - near).magnitude > maxDistance) continue;
                    if (room.IsPositionInSceneVolume(pos + Vector3.up * 0.15f)) continue;
                    return pos;
                }
            }
            Vector2 offset = Random.insideUnitCircle * maxDistance;
            return new Vector3(near.x + offset.x, FloorY, near.z + offset.y);
        }

        private List<MRUKAnchor> Tops()
        {
            List<MRUKAnchor> tops = new List<MRUKAnchor>();
            if (room == null) return tops;
            foreach (MRUKAnchor anchor in room.Anchors)
            {
                if ((anchor.Label & Furniture) == 0 || !anchor.PlaneRect.HasValue) continue;
                if (Vector3.Dot(anchor.transform.forward, Vector3.up) < 0.9f) continue;   // only flat tops facing up
                tops.Add(anchor);
            }
            return tops;
        }

        // A furniture top near the user. If it can, picks the edge closest to the user so she can sit on it.
        public Surface FurnitureSpot(Vector3 user, float maxDistance)
        {
            List<MRUKAnchor> tops = Tops().FindAll(top => Flat(top.transform.position - user).magnitude <= maxDistance);
            if (tops.Count == 0) return null;
            float distance;
            return EdgeOf(tops[Random.Range(0, tops.Count)], user, out distance);
        }

        // The furniture edge closest to where you put her down, if it's within reach and about level with her feet.
        public Surface LedgeNear(Vector3 feet, float reach)
        {
            Surface best = null;
            float bestDistance = reach;
            foreach (MRUKAnchor top in Tops())
            {
                float height = top.transform.InverseTransformPoint(feet).z;   // how far above the top her feet are
                if (height < -0.35f || height > 0.6f) continue;
                float distance;
                Surface edge = EdgeOf(top, feet, out distance);
                if (distance <= bestDistance) { best = edge; bestDistance = distance; }
            }
            return best;
        }

        // The point on a top's edge nearest to "near", 3 cm in from the edge, and how far "near" is from it across the surface.
        private static Surface EdgeOf(MRUKAnchor top, Vector3 near, out float distance)
        {
            // Work in the top's own flat coordinates: x and y across the surface, z out of it.
            Rect rect = top.PlaneRect.Value;
            Vector3 local = top.transform.InverseTransformPoint(near);
            Vector2 at = new Vector2(local.x, local.y);
            Vector2 inside = new Vector2(Mathf.Clamp(at.x, rect.xMin, rect.xMax), Mathf.Clamp(at.y, rect.yMin, rect.yMax));
            // Push to the nearest side.
            float left = inside.x - rect.xMin, right = rect.xMax - inside.x, bottom = inside.y - rect.yMin, topSide = rect.yMax - inside.y;
            float least = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, topSide));
            Vector2 outward;
            if (least == left) { inside.x = rect.xMin; outward = Vector2.left; }
            else if (least == right) { inside.x = rect.xMax; outward = Vector2.right; }
            else if (least == bottom) { inside.y = rect.yMin; outward = Vector2.down; }
            else { inside.y = rect.yMax; outward = Vector2.up; }
            distance = Vector2.Distance(at, inside);
            // Keep her a little way in from the corners (unless the top is tiny), then 3 cm in from the edge.
            if (rect.width > 0.3f) inside.x = Mathf.Clamp(inside.x, rect.xMin + 0.12f, rect.xMax - 0.12f);
            if (rect.height > 0.3f) inside.y = Mathf.Clamp(inside.y, rect.yMin + 0.12f, rect.yMax - 0.12f);
            if (outward.x != 0) inside.x = outward.x < 0 ? rect.xMin + 0.03f : rect.xMax - 0.03f;
            else inside.y = outward.y < 0 ? rect.yMin + 0.03f : rect.yMax - 0.03f;
            Vector3 outwardWorld = Flat(top.transform.TransformDirection(new Vector3(outward.x, outward.y, 0))).normalized;
            Vector3 point = top.transform.TransformPoint(new Vector3(inside.x, inside.y, 0));
            return new Surface { Point = point, Raised = true, Edge = true, EdgeOutward = outwardWorld };
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    }
}
