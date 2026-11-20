using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private void SmokeSpend(List<string> checks, string output)
        {
            Directory.CreateDirectory(output);
            string folderWas = Spend.Folder, openAiWas = Brain.OpenAi, anthropicWas = Brain.Anthropic;
            System.Net.Sockets.TcpListener openAiFake = null, claudeFake = null;
            try
            {
                Spend.Folder = output;
                Spend.Forget();
                if (File.Exists(Path.Combine(output, Spend.File))) File.Delete(Path.Combine(output, Spend.File));

                DateTime peak = new DateTime(2026, 11, 16, 2, 0, 0, DateTimeKind.Utc), calm = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
                Price sonnet = Spend.PriceOf("anthropic", "claude-sonnet-5-5", calm), haiku = Spend.PriceOf("anthropic", "claude-haiku-5-5", calm);
                if (sonnet == null || sonnet.Of(1000000, 0, 0, 1000000) != 12m || sonnet.Of(0, 1000000, 1000000, 0) != 2.6m) throw new Exception("Sonnet 5.5's list price is wrong.");
                if (haiku == null || haiku.Of(2000000, 10000000, 0, 1000000) != 0.8m) throw new Exception("Haiku 5.5's list price is wrong.");
                if (Spend.PriceOf("openai", "gpt-6.1-terra", calm).Of(1000000, 0, 0, 1000000) != 3m || Spend.PriceOf("google", "gemini-3.1-flash-lite", calm).Of(1000000, 0, 0, 1000000) != 1.75m) throw new Exception("OpenAI's or Gemini's list price is wrong.");
                if (Spend.PriceOf("deepseek", "deepseek-flash", peak).Of(1000000, 0, 0, 1000000) != 1.5m || Spend.PriceOf("deepseek", "deepseek-flash", calm).Of(1000000, 0, 0, 1000000) != 0.75m) throw new Exception("DeepSeek's off-peak half price is wrong.");
                if (!Spend.Free("nvidia", "meta/llama") || !Spend.Free("openrouter", "example/model:free") || Spend.PriceOf("openai", "gpt-unheard-of", calm) != null) throw new Exception("Free or unknown models were priced.");
                if (Spend.PriceOf("openrouter", "anthropic/claude-haiku-5-5", calm) == null) throw new Exception("An OpenRouter model without a listed price didn't fall back to its maker's.");
                checks.Add("PASS spend prices: Claude, OpenAI, Gemini and DeepSeek list prices per model with cache reads and writes, DeepSeek's off-peak half price, NVIDIA and :free models at nothing, and unknown models left unpriced");

                Spend.Add("anthropic", "claude-sonnet-5-5", 1000, 0, 0, 500);
                Spend.Add("anthropic", "claude-sonnet-5-5", 1000, 0, 0, 500);
                Spend.Add("openrouter", "example/paid", 10, 0, 0, 10, 0.25);
                Spend.Add("other:api.example.com", "mystery-1", 10, 0, 0, 10);
                List<SpendRow> today = Spend.Today();
                decimal total = Spend.Total(today);
                if (today.Count != 3 || total != 0.264m || today[0].Model != "example/paid" || today[1].Asks != 2) throw new Exception("Today's spend added up wrong: " + total + " over " + today.Count + " models.");
                SpendRow unknown = today.Find(delegate(SpendRow row) { return row.Model == "mystery-1"; });
                if (unknown == null || unknown.Unpriced != 1 || unknown.Cost != 0 || Spend.Company(unknown.Kind) != "api.example.com") throw new Exception("An unpriced provider wasn't kept apart.");
                Spend.Forget();
                if (Spend.Total(Spend.Today()) != total || Spend.Month().Count != 3 || Spend.Line() != "$0.26 today · $0.26 this month") throw new Exception("Spend didn't come back the same from spend.tsv: " + Spend.Line());
                if (Spend.Money(0.004m) != "<$0.01" || Spend.Money(1234.5m) != "$1,234.50" || Spend.Tokens(1260) != "1.3K" || Spend.Tokens(12600000) != "13M") throw new Exception("Money or tokens read wrong.");
                checks.Add("PASS spend ledger: each model's asks, tokens and cost add up by day in spend.tsv, read back the same after a restart, and the Mainframe line says today's and this month's");

                List<string> openAiAsked = new List<string>();
                string terraReply = "{\"model\":\"gpt-6.1-terra\",\"choices\":[{\"message\":{\"content\":\"Hi.\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":40,\"prompt_tokens_details\":{\"cached_tokens\":200}}}";
                string solReply = "{\"model\":\"gpt-6.2-sol\",\"choices\":[{\"message\":{\"content\":\"Hello.\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":10}}";
                Brain.OpenAi = FakeServer(new[] { terraReply, "!404 {\"error\":{\"message\":\"The model gpt-6.1-terra does not exist\"}}", solReply }, openAiAsked, out openAiFake);
                Brain.ReadCloudModels("openai", "{\"data\":[{\"id\":\"gpt-6.2-sol\"},{\"id\":\"gpt-6-terra\"},{\"id\":\"gpt-6.1-terra\"},{\"id\":\"gpt-6.2-luna\"}]}");
                Brain thrifty = new Brain { Model = "gpt-6.2-sol", Remote = new Cloud("sk-spendcheck", "gpt-6.2-sol") };
                if (thrifty.SmallJobs != "gpt-6.1-terra" || new Brain { Model = "gpt-6.1-terra", Remote = new Cloud("sk-spendcheck", "gpt-6.1-terra") }.SmallJobs != null) throw new Exception("OpenAI's small jobs didn't pick its newest terra: " + thrifty.SmallJobs);
                if (thrifty.Quick("Be brief.", "Say hi.", 60) != "Hi." || openAiAsked.Count != 1 || !openAiAsked[0].Contains("\"model\":\"gpt-6.1-terra\"")) throw new Exception("A quick OpenAI ask didn't go to the cheaper model.");
                SpendRow terra = Spend.Today().Find(delegate(SpendRow row) { return row.Model == "gpt-6.1-terra"; });
                if (terra == null || terra.Fresh != 1000 || terra.Cached != 200 || terra.Written != 40 || terra.Cost != 0.000605m) throw new Exception("The cheaper model's tokens weren't counted: " + (terra == null ? "missing" : terra.Fresh + "/" + terra.Cached + "/" + terra.Written + " " + terra.Cost));
                if (thrifty.Quick("Be brief.", "Say hello.", 60) != "Hello." || openAiAsked.Count != 3 || !openAiAsked[2].Contains("\"model\":\"gpt-6.2-sol\"") || thrifty.SmallJobs != null) throw new Exception("A cheaper model that isn't on the key didn't hand the ask back to her picked brain.");
                checks.Add("PASS small jobs on OpenAI: a quick ask goes to the newest terra on the key, its tokens are counted, and a terra the key can't use hands the ask back to her picked model for good");

                List<string> claudeAsked = new List<string>();
                Brain.Anthropic = FakeServer(new[] { "{\"model\":\"claude-haiku-5-5\",\"content\":[{\"type\":\"text\",\"text\":\"Bonjour.\"}],\"stop_reason\":\"end_turn\",\"usage\":{\"input_tokens\":400,\"output_tokens\":20,\"cache_read_input_tokens\":1000,\"cache_creation_input_tokens\":0}}", "!402 {\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"Your credit balance is too low to access the Anthropic API.\"}}" }, claudeAsked, out claudeFake);
                Brain opus = new Brain { Model = "claude-opus-5-5", Remote = new Cloud("sk-ant-spendcheck", "claude-opus-5-5") };
                if (opus.SmallJobs != Brain.Fastest || new Brain { Model = Brain.Fastest, Remote = new Cloud("sk-ant-spendcheck", Brain.Fastest) }.SmallJobs != null) throw new Exception("Claude's small jobs didn't name Haiku.");
                if (opus.SmallJob("Translate.", "1. Hello", 300) != "Bonjour." || claudeAsked.Count != 1 || !claudeAsked[0].Contains("\"model\":\"" + Brain.Fastest + "\"")) throw new Exception("A translation didn't go to Haiku.");
                SpendRow haikuRow = Spend.Today().Find(delegate(SpendRow row) { return row.Model == Brain.Fastest; });
                if (haikuRow == null || haikuRow.Cached != 1000 || haikuRow.Cost != 0.00006m) throw new Exception("Haiku's tokens weren't counted with its cache reads.");
                string broke = null;
                try { opus.SmallJob("Translate.", "1. Bye", 300); }
                catch (BrainException error) { broke = error.Message; }
                if (broke != Brain.NoCredits || claudeAsked.Count != 2 || opus.SmallJobs != Brain.Fastest) throw new Exception("A key out of credits gave up on Haiku for good, or asked again: " + broke);
                checks.Add("PASS small jobs on Claude: translations and summaries go to Haiku when a bigger Claude is picked, with its cache reads counted at their price, and a key out of credits says so without giving up on Haiku");

                List<SpendRow> made = new List<SpendRow>
                {
                    new SpendRow { Kind = "anthropic", Model = "claude-opus-5-5", Asks = 1284, Fresh = 9800000, Cached = 41000000, Written = 820000, Cost = 1234.56m },
                    new SpendRow { Kind = "openrouter", Model = "anthropic/claude-sonnet-5-5-with-a-very-long-name", Asks = 312, Fresh = 1200000, Written = 90000, Cost = 3.4m },
                    new SpendRow { Kind = "anthropic", Model = Brain.Fastest, Asks = 9999, Fresh = 3000000, Cached = 12000000, Written = 400000, Cost = 0.62m },
                    new SpendRow { Kind = "other:api.example.com", Model = "mystery-1", Asks = 4, Fresh = 900, Written = 80, Unpriced = 4 },
                    new SpendRow { Kind = "nvidia", Model = "nvidia/nemotron-3.5-super-vl-120b-a12b", Asks = 40, Fresh = 80000, Written = 4000 }
                };
                foreach (float zoom in new[] { 1f, 1.5f, 2f })
                {
                    string crowded = mainframe.SpendSheetAt(zoom, made.GetRange(0, 2), made, "claude-opus-5-5", Brain.Fastest, Path.Combine(output, "spend-" + (int)(zoom * 100) + ".png"));
                    if (crowded != null) throw new Exception("The Spend panel at " + (int)(zoom * 100) + "%: " + crowded);
                }
                if (mainframe.SpendSheetAt(1f, new List<SpendRow>(), new List<SpendRow>(), null, null, null) != null) throw new Exception("An empty Spend panel didn't fit.");
                checks.Add("PASS spend panel: today, this month and each model's asks, tokens and cost fit at 100%, 150% and 200%, with free models marked free and unpriced ones marked ?");

                string docked = mainframe.SpendDocked();
                if (docked != null) throw new Exception(docked);
                if (string.IsNullOrEmpty(mainframe.SpendLine) || mainframe.SpendLine.IndexOf("today", StringComparison.Ordinal) < 0) throw new Exception("The Mainframe's spend line is empty: " + mainframe.SpendLine);
                checks.Add("PASS Spend key: opens its panel docked to the Mainframe in place of Resources, lights up while it is open, and closes it again");
            }
            finally
            {
                Brain.OpenAi = openAiWas;
                Brain.Anthropic = anthropicWas;
                if (openAiFake != null) openAiFake.Stop();
                if (claudeFake != null) claudeFake.Stop();
                Spend.Folder = folderWas;
                Spend.Forget();
            }
        }
    }
}
