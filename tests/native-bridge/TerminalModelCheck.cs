using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using CapriDesktop;

internal static class TerminalModelCheck
{
    internal static int Run(string settings, string manifestPath, string evidence)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(settings));
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (Directory.Exists(evidence)) throw new Exception("Use a fresh evidence directory.");
        Directory.CreateDirectory(evidence);
        var c=config.RootElement;
        string Get(string key)=>c.GetProperty(key).GetString();
        var plan=new PlanBrain(Get("PlanPython"),Get("PlanAccounts"),Get("PlanModel"),Get("PlanVisionModel"),Get("PlanEffort"),Get("PlanVisionEffort"),1024,1024,c.GetProperty("OwnPlanAccount").GetBoolean());
        var brain=new Brain{Provider=plan,Model=Get("PlanModel")};
        Security.Pick(Security.Trusted); Limbs.Attach(brain);
        // Only a public disposable window inventory is exposed to the model. Execution still
        // uses the production native selector and identity checks; no owner history is loaded.
        var windows=Hands.Windows;
        Hands.Windows=()=>windows().Where(w=>w.Title.StartsWith("Capri terminal ",StringComparison.Ordinal)||w.Title.StartsWith("Capri public backdrop ",StringComparison.Ordinal)).ToList();
        var act=brain.Act;var actions=new List<object>();string lastJob=null;
        brain.Act=(tool,args)=>
        {
            string result=act(tool,args);
            if (tool=="run_terminal")
            {
                try { using var j=JsonDocument.Parse(result);lastJob=j.RootElement.GetProperty("job_id").GetString(); }catch(JsonException){}catch(KeyNotFoundException){}
            }
            actions.Add(new{tool,command=args?.Command,cwd=args?.Cwd,name=args?.Name,action=args?.Action,job_id=args?.JobId,asked=args?.Asked,result});
            File.WriteAllText(Path.Combine(evidence,"actions.json"),JsonSerializer.Serialize(actions,new JsonSerializerOptions{WriteIndented=true}));
            return result;
        };
        var replies=new List<object>();
        try
        {
            foreach (var row in manifest.RootElement.GetProperty("prompts").EnumerateArray())
            {
                string prompt=row.GetString().Replace("{job_id}",lastJob??"missing");
                Console.WriteLine("REQUEST "+prompt);
                string reply=brain.Ask(prompt);
                replies.Add(new{prompt,reply});File.WriteAllText(Path.Combine(evidence,"replies.json"),JsonSerializer.Serialize(replies,new JsonSerializerOptions{WriteIndented=true}));
                Console.WriteLine("REPLY "+reply);
                if (lastJob!=null)
                {
                    Thread.Sleep(500);
                    File.AppendAllText(Path.Combine(evidence,"observed-jobs.jsonl"),TerminalJobs.Control(new(){Asked="read job",Action="read",JobId=lastJob})+"\n");
                }
            }
        }
        finally {plan.Cancel();TerminalJobs.StopAll();Hands.Windows=windows;}
        return 0;
    }
}
