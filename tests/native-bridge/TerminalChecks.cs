using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using CapriDesktop;

internal static class TerminalChecks
{
    internal static void Run()
    {
        if (!OperatingSystem.IsLinux()) return;
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("Terminal: " + name); checks++; }
        Limbs.Attach(new Brain());
        string kit = Hands.KitFor().Tools;
        Check(Hands.Heard("List running processes and report CPU and memory status using Linux system tools.", out string diagnostic, out ToolArguments diagnosticArgs) && diagnostic == "linux_system" && diagnosticArgs.Action == "overview", "ordinary diagnostic request reaches native tools");
        Check(Hands.Heard("Check the status of my user service pipewire.service. Read only; do not restart it.", out string serviceTool, out ToolArguments serviceArgs) && serviceTool == "linux_system" && serviceArgs.Action == "service_status" && serviceArgs.Name == "pipewire.service", "named user service status reaches read-only native route");
        Check(LinuxRoutes.Request("Restart pipewire.service").Key == null, "read-only service shortcut never infers mutation");
        foreach (string nonUnit in new[] { "Check status of pipewire.service.backup", "Check status of /tmp/pipewire.service", "Show me how to read the status of pipewire.service" })
            Check(LinuxRoutes.Request(nonUnit).Key == null, "service read does not reinterpret " + nonUnit);
        Check(Hands.Valid("linux_system", new() { Action="service_status", Name="pipewire.service" }) && !Hands.Valid("linux_system",new() {Action="service_status"}) && !Hands.Valid("linux_system",new() {Action="shell",Name="x"}), "planned diagnostics validate known actions and required unit names");
        string stopRequest="Stop job 0123456789abcdef0123456789abcdef.";
        Check(Hands.KitFor().Lacking(stopRequest)==null && Hands.Heard(stopRequest,out string stopTool,out ToolArguments stopArgs) && stopTool=="terminal_job", "terminal stop bypasses unrelated Windows mouse-job gap");
        foreach (string name in new[] { "linux_system", "run_terminal", "terminal_job" })
            Check(JsonDocument.Parse(kit).RootElement.EnumerateArray().Any(x => x.GetProperty("function").GetProperty("name").GetString() == name), "compiled catalog exposes " + name);
        Hands.OnMac = true;
        foreach (string name in new[] { "linux_system", "run_terminal", "terminal_job" })
            Check(!JsonDocument.Parse(Hands.KitFor().Tools).RootElement.EnumerateArray().Any(x => x.GetProperty("function").GetProperty("name").GetString() == name), "Mac filters " + name);
        Hands.OnMac = false;
        Check(!Hands.CallTool("run_terminal") && !Hands.CallTool("terminal_job"), "friend calls cannot run shell jobs");
        var calls = new List<KeyValuePair<string, ToolArguments>>();
        PlanBrain.Proposals("{\"text\":\"\",\"calls\":[{\"name\":\"run_terminal\",\"arguments\":{\"command\":\"printf 'héllo world'\",\"cwd\":\"/tmp\",\"visible\":false}}]}",kit,calls);
        Check(calls.Single().Value.Command == "printf 'héllo world'" && calls[0].Value.Visible == false, "typed proposal preserves Unicode quotes and boolean");
        foreach (string args in new[] { "{\"action\":\"read\",\"job_id\":\"x\",\"offset\":-1}", "{\"action\":\"read\",\"job_id\":\"x\",\"offset\":1.5}", "{\"action\":\"kill\",\"job_id\":\"x\"}" })
        {
            bool denied=false; try { PlanBrain.Proposals("{\"text\":\"\",\"calls\":[{\"name\":\"terminal_job\",\"arguments\":"+args+"}]}",kit,new()); } catch(BrainException) {denied=true;} Check(denied,"malformed job proposal rejected");
        }
        foreach (string ask in new[] { "What does the command rm do?", "Explain run rm scratch.txt", "Do not run pwd", "If it fails run pwd", "run pwd and explain rm" })
            Check(TerminalJobs.Intent(new ToolArguments{Asked=ask,Command="rm scratch.txt"}) != null, "non-execution or unrequested destruction rejected");
        Check(TerminalJobs.Intent(new(){Asked="run if true; then printf ok; fi",Command="if true; then printf ok; fi"}) == null,"literal shell conditionals remain supported");
        var repeated=new List<KeyValuePair<string,ToolArguments>>{ new("run_terminal",new(){Command="echo A"}),new("run_terminal",new(){Command="echo A"}),new("run_terminal",new(){Command="echo a"})};Brain.Once(repeated);Check(repeated.Count==2,"case-sensitive terminal deduplication");
        Check(TerminalJobs.Run(new(){Asked="run pwd",Command="pwd",Current=()=>false}).Contains("cancelled"),"stale admission rejected");
        Check(TerminalJobs.Run(new(){Asked="run pwd",Command="pwd",Cwd="/not-a-real-capri-directory"}).Contains("directory"),"invalid cwd rejected");
        Check(TerminalJobs.Control(new(){Asked="read job",Action="read",JobId="unknown"}).Contains("Unknown"),"unknown ID touches no process");
        string cwd=Path.Combine(Path.GetTempPath(),"capri quote space Ω "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(cwd);
        string previousTemp = Environment.GetEnvironmentVariable("TMPDIR");
        string stateRoot = Path.Combine(cwd, "private-job-state");
        Directory.CreateDirectory(stateRoot);
        Environment.SetEnvironmentVariable("TMPDIR", stateRoot);
        Func<string> previousContext = TerminalJobs.Context;
        try
        {
            var admitted=JsonDocument.Parse(TerminalJobs.Run(new(){Asked="run printf in the test folder",Command="printf 'héllo \"world\"\\n'; pwd; exit 7",Cwd=cwd,Visible=false}));
            string id=admitted.RootElement.GetProperty("job_id").GetString();
            JsonDocument Done(string job)
            {
                for(int i=0;i<150;i++) { var d=JsonDocument.Parse(TerminalJobs.Control(new(){Asked="read job",Action="read",JobId=job}));if(d.RootElement.GetProperty("state").GetString() is "exited" or "stopped" or "failed")return d;d.Dispose();Thread.Sleep(20); }throw new Exception("Job did not end");
            }
            using var result=Done(id);Check(result.RootElement.GetProperty("exit_code").GetInt32()==7,"actual nonzero exit retained");Check(result.RootElement.GetProperty("output").GetString().Contains("héllo \"world\"") && result.RootElement.GetProperty("output").GetString().Contains(cwd),"literal quoting Unicode and cwd observed");
            using var verbose = JsonDocument.Parse(TerminalJobs.Run(new() { Asked="run a public output fixture", Command="printf '%12000s' x", Visible=false }));
            using var bounded = Done(verbose.RootElement.GetProperty("job_id").GetString());
            Check(bounded.RootElement.GetProperty("output").GetString().Length <= 8000 && bounded.RootElement.GetProperty("truncated").GetBoolean(), "returned tail reports truncation even below the raw spool cap");
            using var longjob = JsonDocument.Parse(TerminalJobs.Run(new() { Asked="run sleep", Command="printf started; sleep 30", Visible=false }));
            string longid = longjob.RootElement.GetProperty("job_id").GetString();
            string mismatch = TerminalJobs.Control(new() { Asked="stop job " + id, Action="stop", JobId=longid });
            Check(mismatch.Contains("differs"), "original stop target cannot be replaced by a proposed job ID");
            using (var running = JsonDocument.Parse(TerminalJobs.Control(new() { Asked="read job", Action="read", JobId=longid })))
                Check(running.RootElement.GetProperty("state").GetString() == "running", "mismatched stop leaves the other job running");
            string question = "Could you stop job " + longid + "?";
            Check(Hands.Heard(question, out string questionTool, out ToolArguments questionArgs) && questionTool == "terminal_job", "question-mark stop reaches native route");
            using (var observation = JsonDocument.Parse(TerminalJobs.Control(questionArgs)))
                Check(observation.RootElement.GetProperty("state").GetString() is "stopped" or "stopping", "stop returns observed completion or honest pending state");
            using var stopped = Done(longid);
            Check(stopped.RootElement.GetProperty("state").GetString() == "stopped", "question-mark stop ends the owned command");

            using var retiringJob = JsonDocument.Parse(TerminalJobs.Run(new() { Asked="run sleep", Command="sleep 30", Visible=false }));
            Check(Directory.EnumerateDirectories(stateRoot, "capri-terminal-*").Any(), "job state uses the isolated temporary directory");
            TerminalJobs.Shutdown();
            // Simulate a callback queued before retirement, after its account has been retired.
            TerminalJobs.Context = () => throw new IOException("The test account was retired.");
            TerminalJobs.CheckContext();
            Check(!TerminalJobs.StopAll(), "queued callbacks and repeated stop are harmless after shutdown");
            Check(TerminalJobs.Run(new() { Asked="run true", Command="true", Visible=false }).Contains("shutting down"), "shutdown rejects new command admission");
            for (int i = 0; i < 150 && Directory.EnumerateFileSystemEntries(stateRoot).Any(); i++) Thread.Sleep(20);
            Check(!Directory.EnumerateFileSystemEntries(stateRoot).Any(), "shutdown retires private state of completed and active jobs");
        }
        finally
        {
            TerminalJobs.Shutdown();
            TerminalJobs.Context = previousContext;
            Environment.SetEnvironmentVariable("TMPDIR", previousTemp);
            // Keep unexpected files as failure evidence rather than deleting arbitrary command output.
            if (!Directory.EnumerateFileSystemEntries(stateRoot).Any()) Directory.Delete(stateRoot);
            if (!Directory.EnumerateFileSystemEntries(cwd).Any()) Directory.Delete(cwd);
        }
        Console.WriteLine("PASS "+checks+" compiled terminal capability, intent, proposal and lifecycle checks");
    }
}
