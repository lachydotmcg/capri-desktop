using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    // One sound an app has in Windows' volume mixer: its program, its own volume from 0 to 1, whether it's muted, and whether
    // it's playing right now.
    internal sealed class AppSound
    {
        internal string App;
        internal int Id;
        internal float Level;
        internal bool Muted;
        internal bool Playing;
        internal float Peak;
    }

    // Each app's own volume in Windows' volume mixer, on every speaker it plays through, apart from the PC's volume, so "turn the
    // game down" or "mute Spotify" is one call (Lachy, 3 Nov: "ensure they have the tools they need to perform everything themself").
    internal static class Mixer
    {
        // Windows quiets every other app while one is on a call, and can count her mic or her voice as a call (Neco Arc, 3 Nov:
        // "everything else on computer gets muted"). Each of her sounds opts out before it starts, and a failure never stops it.
        // The smoke test swaps in its own sessions and opt-out.
        internal static readonly Guid SessionId = new Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD");
        internal static Func<string, int, object> Session = OpenSession;
        internal static Func<object, bool, int> Ducking = delegate(object session, bool optOut) { return ((ISound)session).SetDuckingPreference(optOut); };

        // A WASAPI sound of hers, after Initialize and before Start.
        internal static void NoDucking(ISoundClient client)
        {
            object session = null;
            try
            {
                Guid id = SessionId;
                if (client.GetService(ref id, out session) >= 0 && session != null) Ducking(session, true);
            }
            catch (Exception) { }
            finally { Drop(session); }
        }

        // A wave API sound of hers plays in her default session on its device: Windows' default when there's no id.
        internal static void NoDucking(string id, int flow)
        {
            object session = null;
            try
            {
                session = Session(id, flow);
                if (session != null) Ducking(session, true);
            }
            catch (Exception) { }
            finally { Drop(session); }
        }

        private static object OpenSession(string id, int flow)
        {
            ISoundDevices devices = null;
            ISoundDevice device = null;
            object manager = null;
            IntPtr control = IntPtr.Zero;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                int result = string.IsNullOrEmpty(id) ? devices.GetDefaultAudioEndpoint(flow, 0, out device) : devices.GetDevice(id, out device);
                if (result < 0 || device == null) return null;
                Guid managerId = typeof(ISoundManager).GUID;
                if (device.Activate(ref managerId, 23, IntPtr.Zero, out manager) < 0 || manager == null) return null;
                if (((ISoundManager)manager).GetAudioSessionControl(IntPtr.Zero, 0, out control) < 0 || control == IntPtr.Zero) return null;
                return Marshal.GetObjectForIUnknown(control);
            }
            finally
            {
                if (control != IntPtr.Zero) Marshal.Release(control);
                Drop(manager); Drop(device); Drop(devices);
            }
        }

        // Every app's sounds in the mixer right now, or null when Windows won't say. An app only has one once it has made sound.
        internal static List<AppSound> Sounds()
        {
            List<AppSound> found = new List<AppSound>();
            return Walk(delegate(AppSound sound, ISoundVolume volume) { found.Add(sound); }) ? found : null;
        }

        // Sets the volume, the mute or both on every sound the process has, and says whether Windows took each one.
        internal static bool Set(int id, float? level, bool? muted)
        {
            bool any = false, took = true;
            bool walked = Walk(delegate(AppSound sound, ISoundVolume volume)
            {
                if (sound.Id != id) return;
                any = true;
                if (level.HasValue && volume.SetMasterVolume(Math.Max(0f, Math.Min(1f, level.Value)), IntPtr.Zero) != 0) took = false;
                if (muted.HasValue && volume.SetMute(muted.Value, IntPtr.Zero) != 0) took = false;
            });
            return walked && any && took;
        }

        // Goes through every sound on every speaker that's plugged in, skipping Windows' own system sounds and ones that ended.
        private static bool Walk(Action<AppSound, ISoundVolume> each)
        {
            ISoundDevices devices = null;
            ISpeakerList speakers = null;
            IntPtr listed = IntPtr.Zero;
            Dictionary<int, string> names = new Dictionary<int, string>();
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                if (devices.EnumAudioEndpoints(0, 1, out listed) < 0 || listed == IntPtr.Zero) return false;
                speakers = (ISpeakerList)Marshal.GetObjectForIUnknown(listed);
                int count;
                if (speakers.GetCount(out count) < 0) return false;
                for (int at = 0; at < count; at++)
                {
                    ISoundDevice speaker = null;
                    object manager = null;
                    ISoundList sounds = null;
                    try
                    {
                        if (speakers.Item(at, out speaker) < 0 || speaker == null) continue;
                        Guid managerId = typeof(ISoundManager).GUID;
                        if (speaker.Activate(ref managerId, 23, IntPtr.Zero, out manager) < 0 || manager == null) continue;
                        if (((ISoundManager)manager).GetSessionEnumerator(out sounds) < 0 || sounds == null) continue;
                        int soundCount;
                        if (sounds.GetCount(out soundCount) < 0) continue;
                        for (int one = 0; one < soundCount; one++)
                        {
                            object control = null;
                            try
                            {
                                if (sounds.GetSession(one, out control) < 0 || control == null) continue;
                                ISound sound = control as ISound;
                                ISoundVolume volume = control as ISoundVolume;
                                int id, state;
                                float level;
                                bool muted;
                                if (sound == null || volume == null || sound.GetProcessId(out id) < 0 || id == 0 || sound.GetState(out state) < 0 || state == 2) continue;
                                if (volume.GetMasterVolume(out level) != 0 || volume.GetMute(out muted) != 0) continue;
                                string name;
                                if (!names.TryGetValue(id, out name))
                                {
                                    try { using (Process process = Process.GetProcessById(id)) name = process.ProcessName + ".exe"; }
                                    catch (Exception) { name = null; }
                                    names[id] = name;
                                }
                                if (name == null) continue;
                                float peak = 0;
                                ISoundMeter meter = control as ISoundMeter;
                                if (meter != null) meter.GetPeakValue(out peak);
                                each(new AppSound { App = name, Id = id, Level = level, Muted = muted, Playing = state == 1, Peak = peak }, volume);
                            }
                            catch (Exception) { }
                            finally { Drop(control); }
                        }
                    }
                    catch (Exception) { }
                    finally { Drop(sounds); Drop(manager); Drop(speaker); }
                }
                return true;
            }
            catch (Exception) { return false; }
            finally
            {
                Drop(speakers);
                if (listed != IntPtr.Zero) { try { Marshal.Release(listed); } catch (Exception) { } }
                Drop(devices);
            }
        }

        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISoundMeter
        {
            [PreserveSig] int GetPeakValue(out float peak);
        }

        private static void Drop(object value)
        {
            if (value == null) return;
            try { Marshal.ReleaseComObject(value); }
            catch (Exception) { }
        }

        // IMMDeviceCollection.
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpeakerList
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out ISoundDevice device);
        }

        // IAudioSessionManager2, up to GetSessionEnumerator.
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISoundManager
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr session, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr session, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out ISoundList sessions);
        }

        // IAudioSessionEnumerator.
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISoundList
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }

        // IAudioSessionControl2.
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISound
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
            [PreserveSig] int GetGroupingParam(out Guid group);
            [PreserveSig] int SetGroupingParam(ref Guid group, IntPtr context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int GetSessionIdentifier(out IntPtr identifier);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr identifier);
            [PreserveSig] int GetProcessId(out int processId);
            [PreserveSig] int IsSystemSoundsSession();
            [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }
    }
}
