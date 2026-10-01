using System;
using System.Web.Script.Serialization;
using System.Collections.Generic;

internal static class FakeCodex
{
    private static int Main(string[] args)
    {
        if (String.Join(" ", args) != "app-server --listen stdio://" ||
            String.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME")) ||
            String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEX_HOME"))) return 1;
        var json = new JavaScriptSerializer();
        string[] expected = { "initialize", "initialized", "account/rateLimits/read" };
        foreach (string method in expected)
        {
            string line = Console.ReadLine();
            if (line == null) return 2;
            var request = json.DeserializeObject(line) as Dictionary<string, object>;
            // Strict whitelist: no thread/turn creation or inference requests.
            if (request == null || Convert.ToString(request["method"]) != method) return 3;
            if (method == "initialize") Console.WriteLine("{\"id\":1,\"result\":{}}");
            if (method == "account/rateLimits/read")
                Console.WriteLine("{\"id\":2,\"result\":{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":21,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":12,\"windowDurationMins\":10080}}}}}");
        }
        // Query must terminate the short-lived helper after getting its result.
        while (Console.ReadLine() != null) return 4;
        return 0;
    }
}
