using System;
using System.Diagnostics;
using System.IO;
using System.Text;

internal static class StandaloneCheck
{
    private static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static int Main(string[] args)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "QuotaRings-test-" + Guid.NewGuid().ToString("N"));
        string previousCli = Environment.GetEnvironmentVariable("CODEX_QUOTA_CODEX_EXE");
        Encoding previousInputEncoding = Console.InputEncoding;
        try
        {
            Directory.CreateDirectory(scratch);
            var start = new ProcessStartInfo();
            CodexQuotaRings.EnsureChildEnvironment(start);
            start.EnvironmentVariables.Remove("HOME");
            start.EnvironmentVariables.Remove("CODEX_HOME");
            start.EnvironmentVariables["CODEX_APP_TOOLS_PIPE_PATH"] = "unavailable-desktop-pipe";
            start.EnvironmentVariables["CODEX_THREAD_ID"] = "desktop-task";
            CodexQuotaRings.ConfigureQueryEnvironment(start, scratch);
            Assert(start.EnvironmentVariables["HOME"] == scratch, "Detached HOME missing");
            Assert(start.EnvironmentVariables["CODEX_HOME"] == Path.Combine(scratch, ".codex"), "Detached CODEX_HOME missing");
            Assert(start.WorkingDirectory == scratch, "Working directory must not depend on the caller workspace");
            Assert(start.EnvironmentVariables["CODEX_APP_TOOLS_PIPE_PATH"] == null && start.EnvironmentVariables["CODEX_THREAD_ID"] == null, "Desktop IPC/task identity inherited");
            start.EnvironmentVariables["HOME"] = "custom-home";
            start.EnvironmentVariables["CODEX_HOME"] = "custom-codex-profile";
            CodexQuotaRings.ConfigureQueryEnvironment(start, scratch);
            Assert(start.EnvironmentVariables["HOME"] == "custom-home" && start.EnvironmentVariables["CODEX_HOME"] == "custom-codex-profile", "Explicit profile overwritten");

            // A separate protocol fixture supplies quota responses. No desktop
            // process, app bridge, credentials or network is used by this test.
            Environment.SetEnvironmentVariable("CODEX_QUOTA_CODEX_EXE", Path.GetFullPath(args[0]));
            QuotaResult result = CodexQuotaRings.Query();
            Assert(result.FiveHours != null && result.FiveHours.Remaining == 79 && result.Weekly != null && result.Weekly.Remaining == 88, "Standalone RPC failed");
            // A UTF-8 console can otherwise make Process.StandardInput emit a
            // preamble. Exercise it as well as a legacy Windows console page.
            foreach (Encoding encoding in new Encoding[] { Encoding.UTF8, Encoding.GetEncoding(1252) })
            {
                Console.InputEncoding = encoding;
                QuotaResult encodedResult = CodexQuotaRings.Query();
                Assert(encodedResult.FiveHours != null && encodedResult.FiveHours.Remaining == 79, "RPC depends on console input encoding");
            }
            foreach (Process process in Process.GetProcessesByName("FakeCodex"))
                using (process) Assert(process.HasExited, "CLI child left running between refreshes");

            string cache = Path.Combine(scratch, "quota-cache.json");
            CodexQuotaRings.SaveCache(result, cache);
            QuotaResult restored = CodexQuotaRings.LoadCache(cache);
            Assert(restored != null && restored.FiveHours.Remaining == 79 && restored.Weekly.Remaining == 88 && Math.Abs((restored.CheckedAt - result.CheckedAt).TotalMilliseconds) < 1, "Cache round trip failed");
            result.FiveHours.Remaining = 65;
            CodexQuotaRings.SaveCache(result, cache);
            Assert(CodexQuotaRings.LoadCache(cache).FiveHours.Remaining == 65, "Cache replacement failed");
            File.WriteAllText(cache, "{invalid}");
            Assert(CodexQuotaRings.LoadCache(cache) == null, "Invalid cache must be ignored");
            result.FiveHours.Remaining = 101;
            CodexQuotaRings.SaveCache(result, cache);
            Assert(CodexQuotaRings.LoadCache(cache) == null, "Invalid percent accepted");
            Console.WriteLine("PASS: detached profile; explicit profile preserved; standalone read-only RPC; child cleanup; persistent cache");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_QUOTA_CODEX_EXE", previousCli);
            Console.InputEncoding = previousInputEncoding;
            // Only remove files created here; no recursive filesystem deletion.
            if (Directory.Exists(scratch))
            {
                foreach (string file in Directory.GetFiles(scratch)) File.Delete(file);
                Directory.Delete(scratch);
            }
        }
    }
}
