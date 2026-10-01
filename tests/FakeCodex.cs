using System;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Text;
using System.IO;

internal static class FakeCodex
{
    private static int Main(string[] args)
    {
        // Decode redirected input directly: a headless .NET child does not
        // necessarily inherit the parent's console code page. Detect an initial
        // encoding preamble, as StreamReader normally does for UTF-8 streams.
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), true);
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        if (String.Join(" ", args) != "app-server --listen stdio://" ||
            String.IsNullOrEmpty(Environment.GetEnvironmentVariable("HOME")) ||
            String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEX_HOME"))) return 1;
        var json = new JavaScriptSerializer();
        string[] expected = { "initialize", "initialized", "account/rateLimits/read" };
        foreach (string method in expected)
        {
            string line = input.ReadLine();
            if (line == null) return 2;
            if (!line.StartsWith("{", StringComparison.Ordinal))
                throw new Exception("Expected a JSON object on the RPC input pipe");
            var request = json.DeserializeObject(line) as Dictionary<string, object>;
            // Strict whitelist: no thread/turn creation or inference requests.
            if (request == null || Convert.ToString(request["method"]) != method) return 3;
            if (method == "initialize") output.WriteLine("{\"id\":1,\"result\":{}}");
            if (method == "account/rateLimits/read")
                output.WriteLine("{\"id\":2,\"result\":{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":21,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":12,\"windowDurationMins\":10080}}}}}");
        }
        // Query must terminate the short-lived helper after getting its result.
        while (input.ReadLine() != null) return 4;
        return 0;
    }
}
