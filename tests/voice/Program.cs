using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

var assembly = Assembly.Load("Capri");
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
Type Type(string name) => assembly.GetType("CapriDesktop." + name, true)!;
object New(string name, params object[] args) => Activator.CreateInstance(Type(name), flags & ~BindingFlags.Static, null, args, null)!;
object? Call(object? obj, string type, string method, params object?[] args)
{
    try { return (Type(type).GetMethod(method, flags, args.Select(x => x?.GetType() ?? typeof(object)).ToArray()) ?? Type(type).GetMethods(flags).First(m => m.Name == method && m.GetParameters().Length == args.Length)).Invoke(obj, args); }
    catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
}
object? Field(object? obj, string type, string field) => Type(type).GetField(field, flags)!.GetValue(obj);
void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
if (args.Length > 0 && args[0] == "--audio")
{
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CAPRI_FIXTURE_SOURCE_MODULE"))) throw new Exception("A private audio fixture is required.");
    var missing = New("Ears");
    Type("Ears").GetField("Chosen",flags)!.SetValue(missing,"capri_missing_fixture");
    bool refused = false;
    try { await (Task)Call(missing,"Ears","Start")!; } catch (Exception) { refused = true; }
    Check(refused && !(bool)Type("Ears").GetProperty("Listening",flags)!.GetValue(missing)!,"missing selected source fails without capturing the default");
    ((IDisposable)missing).Dispose();
    var raced = New("Ears");
    Type("Ears").GetField("Chosen",flags)!.SetValue(raced,"capri_fixture_input");
    Task starting=(Task)Call(raced,"Ears","Start")!;
    Call(raced,"Ears","Stop");
    try { await starting; } catch (OperationCanceledException) { }
    Check(!(bool)Type("Ears").GetProperty("Listening",flags)!.GetValue(raced)!,"Stop during asynchronous device discovery cannot open a microphone later");
    ((IDisposable)raced).Dispose();
    var mic = New("Ears");
    Type("Ears").GetField("Chosen",flags)!.SetValue(mic,"capri_fixture_input");
    var sound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int chunks=0;
    Action<short[]> heard = samples => { Interlocked.Increment(ref chunks); if (samples.Any(s=>Math.Abs((int)s)>1000)) sound.TrySetResult(); };
    Action<string> failed = message => { Console.WriteLine("device_failure="+message); disconnected.TrySetResult(); };
    Type("Ears").GetEvent("Heard",flags)!.GetAddMethod(true)!.Invoke(mic,new object[]{heard});
    Type("Ears").GetEvent("Failed",flags)!.GetAddMethod(true)!.Invoke(mic,new object[]{failed});
    await (Task)Call(mic,"Ears","Start")!;
    Check((bool)Type("Ears").GetProperty("Listening",flags)!.GetValue(mic)!,"native capture opens explicit software source");
    Call(null,"SpeechAudio","Play",File.ReadAllBytes(args[1]));
    await sound.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Check(chunks>0,"libpulse captures 16 kHz chunks from synthesized software-sink audio");
    Call(null,"SpeechAudio","Stop");
    Check(!(bool)Type("SpeechAudio").GetProperty("Busy",flags)!.GetValue(null)!,"Stop releases playback ownership");
    bool drained = SpinWait.SpinUntil(() =>
    {
        string inputs=(string)Call(null,"SpeechAudio","Run","pactl",new[]{"--format=json","list","sink-inputs"})!;
        using var json=JsonDocument.Parse(inputs); return json.RootElement.GetArrayLength()==0;
    },2000);
    Check(drained,"Stop removes the actual native playback stream");
    Type("Ears").GetField("Muted",flags)!.SetValue(mic,true);
    bool silent=false;
    Action<short[]> muted = samples => silent = samples.All(s=>s==0);
    Type("Ears").GetEvent("Heard",flags)!.GetAddMethod(true)!.Invoke(mic,new object[]{muted});
    Call(mic,"Ears","Feed",Sound(4000));
    Check(silent,"muting suppresses microphone samples");
    Call(null,"SpeechAudio","Run","pactl",new[]{"unload-module",Environment.GetEnvironmentVariable("CAPRI_FIXTURE_SOURCE_MODULE")!});
    await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(!(bool)Type("Ears").GetProperty("Listening",flags)!.GetValue(mic)!,"source removal fails capture instead of migrating it");
    ((IDisposable)mic).Dispose();
    Call(null,"SpeechAudio","Run","pactl",new[]{"load-module","module-remap-source","source_name=capri_fixture_input","master=capri_fixture.monitor"});
    var recovered=New("Ears"); Type("Ears").GetField("Chosen",flags)!.SetValue(recovered,"capri_fixture_input");
    await (Task)Call(recovered,"Ears","Start")!;
    Check((bool)Type("Ears").GetProperty("Listening",flags)!.GetValue(recovered)!,"explicit retry opens the reconnected source");
    ((IDisposable)recovered).Dispose();
    return;
}
// A local server contract is exercised without a hosted account or microphone.
var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
listener.Start();
int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
byte[] serverWave = (byte[])Call(null,"Voice","Wave",Enumerable.Repeat((short)1000,2400).ToArray(),24000)!;
string received = "";
var responseTask = Task.Run(async () =>
{
    using var client = await listener.AcceptTcpClientAsync();
    using var stream = client.GetStream();
    using var reader = new StreamReader(stream,System.Text.Encoding.UTF8,false,1024,true);
    string? line; int length=0;
    while (!string.IsNullOrEmpty(line=await reader.ReadLineAsync()))
    {
        received += line+"\n";
        if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)) length=int.Parse(line.Split(':')[1]);
    }
    char[] body=new char[length]; await reader.ReadBlockAsync(body); received+=new string(body);
    byte[] header=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: audio/wav\r\nContent-Length: "+serverWave.Length+"\r\nConnection: close\r\n\r\n");
    await stream.WriteAsync(header); await stream.WriteAsync(serverWave);
});
var custom=New("VoiceServer");
Type("VoiceServer").GetField("Url",flags)!.SetValue(custom,"http://127.0.0.1:"+port+"/v1");
Type("VoiceServer").GetField("Model",flags)!.SetValue(custom,"fixture");
byte[] served=(byte[])Call(custom,"VoiceServer","Speak","Local voice fixture.")!;
await responseTask.WaitAsync(TimeSpan.FromSeconds(5)); listener.Stop();
Check(served.Length==serverWave.Length && received.Contains("POST /v1/audio/speech") && received.Contains("fixture") && received.Contains("Local voice fixture."),"local OpenAI speech request and PCM response contract");
var slowListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);
slowListener.Start(); int slowPort=((System.Net.IPEndPoint)slowListener.LocalEndpoint).Port;
var stalledResponse=Task.Run(async () => { using var connection=await slowListener.AcceptTcpClientAsync(); await Task.Delay(400); });
Type("VoiceServer").GetField("Url",flags)!.SetValue(custom,"http://127.0.0.1:"+slowPort+"/v1");
using(var abort=new CancellationTokenSource())
{
    using var work=(IDisposable)New("SpeechWork",abort.Token); abort.CancelAfter(100);
    bool interrupted=false; var elapsed=System.Diagnostics.Stopwatch.StartNew();
    try { Call(custom,"VoiceServer","Speak","Cancel this server request."); } catch (Exception) when (abort.IsCancellationRequested) { interrupted=true; }
    Check(interrupted && elapsed.ElapsedMilliseconds<2000,"Stop aborts an in-flight local speech HTTP request");
}
await stalledResponse.WaitAsync(TimeSpan.FromSeconds(3)); slowListener.Stop();
// No model is needed to prove that the selected HD route starts its engine and stays off Windows APIs.
if (OperatingSystem.IsLinux())
{
    string fixture = Path.Combine(Path.GetTempPath(), "capri-hd-check-" + Guid.NewGuid().ToString("N"));
    object hd = New("QwenVoice", fixture);
    var spawnField = Type("QwenVoice").GetField("Spawn", flags)!;
    object originalSpawn = spawnField.GetValue(null)!;
    var children = new List<System.Diagnostics.Process>();
    int spawned = 0;
    try
    {
        string engine = (string)Field(hd, "QwenVoice", "engine")!;
        string model = (string)Field(hd, "QwenVoice", "folder")!;
        Directory.CreateDirectory(engine); Directory.CreateDirectory(model);
        File.WriteAllText(Path.Combine(engine, "tts-server"), "fixture only");
        foreach (string name in new[] { "ModelFile", "CodecFile" }) File.WriteAllText(Path.Combine(model, (string)Field(null, "QwenVoice", name)!), "fixture only");
        spawnField.SetValue(null, new Func<System.Diagnostics.ProcessStartInfo, System.Diagnostics.Process>(_ =>
        {
            spawned++;
            var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/true") { UseShellExecute = false })!;
            children.Add(child); return child;
        }));
        bool exited = false;
        try { Call(hd, "QwenVoice", "Warm"); }
        catch (Exception error) when (error.GetType().Name == "VoiceException") { exited = true; }
        Check(exited && spawned == 1, "Linux HD startup reports an exited engine without invoking kernel32");
        object pet = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Type("Pet"));
        object settings = New("Settings");
        void Set(object value, string field, object data) => value.GetType().GetField(field, flags)!.SetValue(value, data);
        Set(settings, "LocalStyle", Field(null, "QwenVoice", "Style")!);
        Set(pet, "Saved", settings); Set(pet, "voice", New("Voice")); Set(pet, "localVoice", New("LocalVoice", fixture)); Set(pet, "hdVoice", hd);
        try { Call(pet, "Pet", "MakeSpeech", "Disposable HD routing fixture.", CancellationToken.None); }
        catch (Exception error) when (error.GetType().Name == "VoiceException") { }
        Check(spawned == 2, "selected installed HD speech starts its engine before synthesis");
        // Use the actual routed menu event: toggling speech off must remain available on every cross platform.
        object menuVoice = Field(pet, "Pet", "voice")!;
        Set(menuVoice, "Key", "fixture-key"); Set(menuVoice, "VoiceId", "fixture"); Set(settings, "LocalStyle", null!);
        var menu = new Avalonia.Controls.MenuItem();
        Set(pet, "voiceMenu", menu); Set(pet, "voices", New("VoiceListing")); Set(pet, "localEars", New("LocalEars", fixture)); Set(pet, "activity", New("VoiceActivity")); Set(pet, "speechJob", new CancellationTokenSource());
        var smoke = Type("Program").GetField("Smoke", flags)!; object? originalSmoke = smoke.GetValue(null); smoke.SetValue(null, fixture);
        try
        {
            Call(pet, "Pet", "RefreshVoice");
            var use = menu.Items.OfType<Avalonia.Controls.MenuItem>().Single(item => (string?)item.Header == "Use ElevenLabs");
            use.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
            Check((bool)Field(settings, "Settings", "Silent")!, "clicking selected ElevenLabs turns speech off");
            object sessionPet = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Type("Pet"));
            object sessionMic = New("Ears");
            Set(sessionPet, "Saved", New("Settings")); Set(sessionPet, "voice", New("Voice")); Set(sessionPet, "microphone", sessionMic); Set(sessionPet, "activity", New("VoiceActivity"));
            Call(sessionPet, "Pet", "InitializeSpeech");
            Set(sessionPet, "chatTicket", 1); Set(sessionPet, "chatting", true);
            ((Action<string>)Field(sessionMic, "Ears", "Failed")!)("Synthetic disconnected source");
            Set(sessionPet, "chatTicket", 2); Set(sessionPet, "chatting", true);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Check((int)Field(sessionPet, "Pet", "chatTicket")! == 2 && (bool)Field(sessionPet, "Pet", "chatting")!, "an old microphone error cannot close a newer TALK session");
        }
        finally { smoke.SetValue(null, originalSmoke); }
    }
    finally
    {
        ((IDisposable)hd).Dispose(); spawnField.SetValue(null, originalSpawn);
        foreach (var child in children) child.Dispose();
        if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
    }
}
var vad = New("VoiceActivity");
short[] Sound(int amplitude) => Enumerable.Repeat((short)amplitude,1600).ToArray();
short[] quiet = Sound(0);
for (int i=0;i<20;i++) Check(Call(vad,"VoiceActivity","Feed",quiet,false)==null,"silence does not open an utterance");
Call(vad,"VoiceActivity","Feed",Sound(3000),false); Call(vad,"VoiceActivity","Feed",Sound(6000),false); Call(vad,"VoiceActivity","Feed",Sound(3000),false);
short[]? result = null;
for(int i=0;i<9;i++) result=(short[]?)Call(vad,"VoiceActivity","Feed",quiet,false);
Check(result != null && result.Length >= 12*1600,"three voiced chunks and nine quiet chunks produce an endpoint");
Call(vad,"VoiceActivity","Feed",Sound(3000),false); Call(vad,"VoiceActivity","Feed",Sound(3000),true);
for(int i=0;i<9;i++) Check(Call(vad,"VoiceActivity","Feed",quiet,false)==null,"held audio cannot become an utterance");
Call(vad,"VoiceActivity","Reset");
bool capped=false;
for(int i=0;i<151;i++) { Call(vad,"VoiceActivity","Feed",Sound(5000),false); capped |= (bool)Field(vad,"VoiceActivity","Capped")!; }
Check(capped,"15 seconds of continuous noise is capped");
Check(Marshal.SizeOf(Type("LocalVoice+Config"))==448,"TTS struct matches pinned 1.13.8 Linux C header");
Check(Marshal.SizeOf(Type("LocalVoice+ModelConfig"))==416,"TTS model struct matches pinned header");
Check(Marshal.OffsetOf(Type("LocalVoice+ModelConfig"),"Kokoro").ToInt32()==128,"Kokoro offset matches pinned header");
if(args.Length == 0) return;
string profile = Path.GetFullPath(args[0]);
var voice = New("LocalVoice",profile);
Check((bool)Type("LocalVoice").GetProperty("Installed",flags)!.GetValue(voice)!,"exact local voice profile is installed");
var watch=System.Diagnostics.Stopwatch.StartNew();
byte[] wav=(byte[])Call(voice,"LocalVoice","Speak","The quick brown fox jumps over the lazy dog.")!;
File.WriteAllBytes(Path.Combine(profile,"speech.wav"),wav);
Check(wav.Length>10000,"fresh Kokoro synthesis returns PCM audio");
Console.WriteLine(JsonSerializer.Serialize(new { synthesis_seconds=watch.Elapsed.TotalSeconds,wav_bytes=wav.Length,output=Path.Combine(profile,"speech.wav") }));
using(var cancel=new CancellationTokenSource())
{
    using var work=(IDisposable)New("SpeechWork",cancel.Token);
    cancel.Cancel(); bool stopped=false;
    try { Call(voice,"LocalVoice","Speak","This line must be cancelled."); } catch(OperationCanceledException) { stopped=true; }
    Check(stopped,"cancelled synthesis never returns audio");
}
using(var cancel=new CancellationTokenSource())
{
    using var work=(IDisposable)New("SpeechWork",cancel.Token);
    cancel.CancelAfter(50); bool stopped=false; watch.Restart();
    try { Call(voice,"LocalVoice","Speak",string.Join(" ",Enumerable.Repeat("This is a sentence to test cancellation during generation.",8))); } catch(OperationCanceledException) { stopped=true; }
    Check(stopped,"stop during native synthesis discards partial output");
    Console.WriteLine("native_cancel_seconds="+watch.Elapsed.TotalSeconds);
}
var ears=New("LocalEars",profile);
if((bool)Type("LocalEars").GetProperty("Ready",flags)!.GetValue(ears)!)
{
    int rate=0;
    object[] decodeArgs={wav,rate};
    short[] pcm=(short[])Type("Voice").GetMethod("Samples",flags)!.Invoke(null,decodeArgs)!;
    int sourceRate=(int)decodeArgs[1];
    short[] resampled=(short[])Call(null,"VoiceFx","Resample",pcm,sourceRate/16000.0)!;
    watch.Restart();
    string text=(string)Call(ears,"LocalEars","Hear",resampled)!;
    Check(text.Contains("quick",StringComparison.OrdinalIgnoreCase)&&text.Contains("fox",StringComparison.OrdinalIgnoreCase),"local Parakeet recognizes fresh Kokoro fixture");
    Console.WriteLine(JsonSerializer.Serialize(new { asr_seconds=watch.Elapsed.TotalSeconds, transcript=text }));
}
else throw new Exception("ASR fixture profile is missing; this is not a pass.");
((IDisposable)ears).Dispose(); ((IDisposable)voice).Dispose();
