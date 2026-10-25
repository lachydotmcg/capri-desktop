using System.Runtime.Serialization;
namespace CapriDesktop
{
    [DataContract]
    internal sealed class Preferences
    {
        [DataMember] public int X { get; set; }
        [DataMember] public int Y { get; set; }
        [DataMember] public bool HasPosition { get; set; }
        [DataMember] public int Size = 240;
        [DataMember] public bool Motion = true;
        [DataMember] public bool CursorReactions { get; set; }
        [DataMember] public bool SmoothMotion { get; set; }
        [DataMember] public bool Autonomous { get; set; }
        [DataMember] public bool PauseFullscreen { get; set; }
        [DataMember] public bool EcoMode { get; set; }
        [DataMember] public int MovementSpeed { get; set; }
        [DataMember] public int GravityScale { get; set; }
        [DataMember] public bool OnTop = true;
        [DataMember] public long UpdateCheckedAt { get; set; }
        [DataMember] public string Model { get; set; }
        [DataMember] public bool Welcomed { get; set; }
        [DataMember] public bool NoSitting { get; set; }
        [DataMember] public string VoiceKey { get; set; }
        [DataMember] public string VoiceId { get; set; }
        [DataMember] public string VoiceModel { get; set; }
        [DataMember] public string AgentId { get; set; }
        [DataMember] public int AgentVersion { get; set; }
        [OnDeserializing] private void Defaults(StreamingContext context) { Size = 240; Motion = true; OnTop = true; }
        internal void Normalize()
        {
            if (Size != 170 && Size != 240 && Size != 320) Size = 240;
            if (MovementSpeed < 25 || MovementSpeed > 90) MovementSpeed = 50;
            if (GravityScale < 6 || GravityScale > 16) GravityScale = 11;
            if (X < -100000 || X > 100000 || Y < -100000 || Y > 100000) { HasPosition = false; X = Y = 0; }
        }
    }

}
