using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;

namespace CapriDesktop
{
    // A small command line for the Omarchy bridge, so the plugin contract can be tested without the shell.
    internal static class CompanionCli
    {
        internal static int Run(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Use --companion state, say, quiet, react, guard, ambient or show.");
                return 2;
            }
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capri");
            string file = Path.Combine(folder, CompanionBridge.Discovery);
            int port;
            string token;
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 4096)
                {
                    Console.Error.WriteLine("Capri's Omarchy integration is off.");
                    return 1;
                }
                Dictionary<string, object> route = HuskJson.Read(File.ReadAllText(file)) as Dictionary<string, object>;
                HuskNumber number = HuskJson.Get(route, "port") as HuskNumber;
                token = HuskJson.Text(route, "token");
                if (number == null || !int.TryParse(number.Text, out port) || port < 1 || port > 65535 || token.Length < 32)
                {
                    Console.Error.WriteLine("Capri's Omarchy listener is unreadable.");
                    return 1;
                }
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Capri's Omarchy listener is unreadable.");
                return 1;
            }
            string command = args[1];
            Dictionary<string, object> payload;
            if (command == "state") payload = HuskJson.Map("kind", "state");
            else if (command == "say" && args.Length >= 3) payload = HuskJson.Map("kind", "say", "text", args[2], "source", "user");
            else if (command == "quiet" && args.Length >= 3) payload = HuskJson.Map("kind", "quiet", "on", args[2] == "on" || args[2] == "true");
            else if (command == "react" && args.Length >= 3) payload = HuskJson.Map("kind", "react", "name", args[2]);
            else if (command == "guard" && args.Length >= 4) payload = HuskJson.Map("kind", "guard", "name", args[2], "on", args[3] == "on" || args[3] == "true");
            else if (command == "ambient" && args.Length >= 3) payload = HuskJson.Map("kind", "ambient", "event", args[2], "category", args.Length >= 4 ? args[3] : "");
            else if (command == "show" && args.Length >= 3) payload = HuskJson.Map("kind", "show", "target", args[2]);
            else
            {
                Console.Error.WriteLine("That companion command is incomplete.");
                return 2;
            }
            try
            {
                using (HttpClient client = new HttpClient(new HttpClientHandler { UseProxy = false }))
                {
                    client.Timeout = TimeSpan.FromSeconds(2);
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + port + "/event");
                    request.Headers.TryAddWithoutValidation("X-Capri-Token", token);
                    request.Content = new StringContent(HuskJson.Write(payload), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = client.SendAsync(request).GetAwaiter().GetResult())
                    {
                        Console.WriteLine(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        return response.IsSuccessStatusCode ? 0 : 1;
                    }
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Capri didn't answer: " + error.Message);
                return 1;
            }
        }
    }
}
