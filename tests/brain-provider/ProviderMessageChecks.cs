using System;
using CapriDesktop;

internal static class ProviderMessageChecks
{
    private static int failures;

    private static void Check(string name, string got, string want)
    {
        bool good = got == want;
        Console.WriteLine("{0} {1} [{2}]", good ? "PASS" : "FAIL", name, good ? "exact" : "got: " + got);
        if (!good) failures++;
    }

    internal static int Run()
    {
        // Brain.Meaning is the provider taxonomy: a status and body land on one plain sentence, or null when nothing applies.
        // Expected values are literals, so a wording change to the source is a conscious test change too.
        Check("brain 451 -> Blocked", Brain.Meaning(451, ""), "The service is blocked where you are, so a new key won't help. Pick another service under Free key, or Built-in under BRAIN, which runs on your PC.");
        Check("brain 403 HTML page -> Blocked", Brain.Meaning(403, "<html>no access</html>"), "The service is blocked where you are, so a new key won't help. Pick another service under Free key, or Built-in under BRAIN, which runs on your PC.");
        Check("brain 429 rate limit -> Busy", Brain.Meaning(429, "rate limit exceeded"), "Too many requests. Wait a minute, then try again.");
        Check("brain 413 tokens per minute -> TooBig", Brain.Meaning(413, "tokens per minute"), "Too big for this key's per-minute limit. Wait a minute, or use a bigger plan or another provider.");
        Check("brain 402 -> NoCredits", Brain.Meaning(402, ""), "No credits on this key. Add credits on the service's site, or press Free key for a free NVIDIA key.");
        Check("brain 404 -> Gone", Brain.Meaning(404, ""), "That model isn't on your key. Pick another one.");
        Check("brain 500 -> Down", Brain.Meaning(500, ""), "The service failed on its end. Pick another model or retry.");
        Check("brain 401 NVIDIA -> Rejected names it", Brain.Meaning(401, "", company: "NVIDIA"), "NVIDIA rejected the key for my brain. Press Forget under BRAIN in Capri Mainframe, then paste it again.");
        Check("brain 401 openai -> PlusRejected", Brain.Meaning(401, "", openai: true), "Key rejected. ChatGPT Plus doesn't include API use, so make a key with credits at platform.openai.com.");
        Check("brain 403 openai -> PlusRejected", Brain.Meaning(403, "", openai: true), "Key rejected. ChatGPT Plus doesn't include API use, so make a key with credits at platform.openai.com.");
        Check("brain google 400 location -> Unserved", Brain.Meaning(400, "location is not supported", google: true), "Google doesn't serve Gemini where you are. Press Free key for a free NVIDIA key.");
        Check("brain google 401 -> NewKey", Brain.Meaning(401, "", google: true), "Key rejected. Make a new Google AI Studio key and paste it.");
        Check("brain openai 429 spend limit -> OpenAiCapped", Brain.Meaning(429, "organization_spend_limit_exceeded", openai: true), "Your OpenAI key hit a monthly spend or usage limit. Raise it under Limits at platform.openai.com, or wait for it to reset next month.");
        Check("brain openai 429 insufficient_quota -> OpenAiNoCredits", Brain.Meaning(429, "insufficient_quota", openai: true), "No credits on your OpenAI key. ChatGPT Plus doesn't include API use, so add a payment method and credits under Billing at platform.openai.com, or press Free key for a free NVIDIA key.");
        Check("brain google daily quota -> DailyGone", Brain.Meaning(429, "GenerateRequestsPerDayPerProjectPerModel-FreeTier", google: true), "Your Gemini key has used up today's requests. They reset tomorrow, or add another key under BRAIN in Capri Mainframe.");
        Check("brain unknown status -> null", Brain.Meaning(418, "teapot"), (string)null);
        // The standing sentences the Mainframe and errors.txt quote.
        Check("NotAKey names the real services", Brain.NotAKey, "That isn't a Gemini, OpenRouter, NVIDIA, OpenAI, Claude, DeepSeek or xAI key.");
        Check("NoFreeModels points at Refresh or MAX INTELLIGENCE", Brain.NoFreeModels, "OpenRouter has no free chat models on this key. Try Refresh or MAX INTELLIGENCE.");
        return failures;
    }
}
