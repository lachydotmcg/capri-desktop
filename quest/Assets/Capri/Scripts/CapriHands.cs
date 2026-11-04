// Your two hands, whether you're holding Touch controllers or using hand tracking.
// "Grabbing" is the grip button on a controller, or a pinch with a tracked hand.
using UnityEngine;

namespace CapriRoom
{
    public sealed class HandState
    {
        public bool Left;
        public bool Tracked;
        public Vector3 Position;
        public Vector3 Velocity;
        public bool Grabbing, GrabStarted, GrabEnded;
        public float Thumbstick;        // up/down on this controller's stick (0 with tracked hands)
        public bool ButtonOneDown;      // A or X
        public bool ButtonTwoDown;      // B or Y
        // The laser pointer: where it starts and which way it points, and a click (trigger, or a pinch with a tracked hand).
        public Vector3 PointFrom, PointAlong;
        public bool Clicked;
        public bool Controller;

        private readonly Vector3[] history = new Vector3[6];
        private readonly float[] times = new float[6];
        private int next;

        public void Track(Vector3 position, float now)
        {
            Position = position;
            history[next] = position;
            times[next] = now;
            int oldest = (next + 1) % history.Length;
            next = oldest;
            float span = now - times[oldest];
            Velocity = span > 0.0001f && times[oldest] > 0 ? (position - history[oldest]) / span : Vector3.zero;
        }
    }

    [DefaultExecutionOrder(-50)]   // read your hands before Capri reacts to them
    public class CapriHands : MonoBehaviour
    {
        public HandState LeftHand = new HandState { Left = true };
        public HandState RightHand = new HandState { Left = false };
        public Transform Head { get; private set; }

        private OVRCameraRig rig;
        private OVRHand leftTracked, rightTracked;
        private float nextLookForHands;

        public HandState[] Both { get { return new[] { LeftHand, RightHand }; } }

        private void Update()
        {
            if (rig == null)
            {
                rig = FindFirstObjectByType<OVRCameraRig>();
                if (rig == null) return;
            }
            Head = rig.centerEyeAnchor;
            if ((leftTracked == null || rightTracked == null) && Time.time >= nextLookForHands)
            {
                nextLookForHands = Time.time + 2f;
                foreach (OVRHand hand in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
                {
                    if (hand.GetHand() == OVRPlugin.Hand.HandLeft) leftTracked = hand;
                    else if (hand.GetHand() == OVRPlugin.Hand.HandRight) rightTracked = hand;
                }
            }
            Read(LeftHand, rig.leftHandAnchor, leftTracked, OVRInput.Controller.LTouch);
            Read(RightHand, rig.rightHandAnchor, rightTracked, OVRInput.Controller.RTouch);
        }

        private void Read(HandState state, Transform anchor, OVRHand tracked, OVRInput.Controller controller)
        {
            bool controllerOn = OVRInput.IsControllerConnected(controller);
            bool handOn = tracked != null && tracked.IsTracked && tracked.IsDataHighConfidence;
            state.Tracked = controllerOn || handOn;
            state.Controller = controllerOn;
            state.Track(anchor.position, Time.time);
            if (!controllerOn && handOn && tracked.IsPointerPoseValid && tracked.PointerPose != null)
            {
                state.PointFrom = tracked.PointerPose.position;
                state.PointAlong = tracked.PointerPose.forward;
            }
            else
            {
                state.PointFrom = anchor.position;
                state.PointAlong = anchor.forward;
            }

            bool was = state.Grabbing;
            if (controllerOn)
            {
                float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller);
                state.Grabbing = was ? grip > 0.35f : grip > 0.6f;
                state.Thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controller).y;
                state.ButtonOneDown = OVRInput.GetDown(OVRInput.Button.One, controller);
                state.ButtonTwoDown = OVRInput.GetDown(OVRInput.Button.Two, controller);
                state.Clicked = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller);
            }
            else
            {
                float pinch = handOn ? tracked.GetFingerPinchStrength(OVRHand.HandFinger.Index) : 0f;
                state.Grabbing = was ? pinch > 0.5f : pinch > 0.85f;
                state.Thumbstick = 0;
                state.ButtonOneDown = state.ButtonTwoDown = false;
            }
            if (!state.Tracked) state.Grabbing = false;
            state.GrabStarted = state.Grabbing && !was;
            state.GrabEnded = !state.Grabbing && was;
            if (!controllerOn) state.Clicked = state.GrabStarted;   // with tracked hands, a pinch clicks
        }
    }
}
