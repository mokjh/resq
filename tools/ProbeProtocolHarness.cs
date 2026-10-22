using System;
using Resq.Tests.Probe;

// Standalone synthetic protocol checks. Never emits F04/F05 acceptance or device evidence.
internal static class ProbeProtocolHarness
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--synthetic-sample-json")
        {
            Console.WriteLine(ProbeProtocolChecks.SyntheticSampleJson());
            return 0;
        }
        if (args.Length != 0)
        { Console.Error.WriteLine("Usage: ProbeProtocolHarness [--synthetic-sample-json]"); return 2; }
        int failed = 0;
        foreach (string name in ProbeProtocolChecks.Names)
        {
            try { ProbeProtocolChecks.Run(name); Console.WriteLine("PASS synthetic protocol: " + name); }
            catch (Exception error) { failed++; Console.Error.WriteLine("FAIL synthetic protocol: " + name + " " + error); }
        }
        Console.WriteLine("Synthetic protocol checks: " + (ProbeProtocolChecks.Names.Length - failed) +
            " passed, " + failed + " failed; no Unity Editor, SDK, device or human qualification.");
        return failed == 0 ? 0 : 1;
    }
}
