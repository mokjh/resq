using System;
using System.Linq;
using Resq.Tests.Shared;

public static class HostCoreHarness
{
    public static int Main()
    {
        int failures = 0;
        Console.Write("{\"kind\":\"standalone_installed_unity_toolchain\",\"editor_execution\":false,\"case_records\":[");
        for (int i = 0; i < CoreSmokeChecks.Names.Length; i++)
        {
            string name = CoreSmokeChecks.Names[i]; bool passed;
            try { CoreSmokeChecks.Run(name); passed = true; }
            catch (Exception error) { Console.Error.WriteLine(name + ": " + error); passed = false; failures++; }
            if (i > 0) Console.Write(",");
            Console.Write("{\"case_id\":\"host-core/" + name + "\",\"status\":\"" + (passed ? "passed" : "failed") + "\"}");
        }
        string[] engineReferences = typeof(Resq.Core.Tick).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name).Where(name => name.StartsWith("UnityEngine") || name.StartsWith("UnityEditor")).ToArray();
        if (engineReferences.Length > 0) { failures++; Console.Error.WriteLine("Core references engine assemblies."); }
        Console.WriteLine("],\"failed_count\":" + failures + ",\"core_engine_reference_count\":" + engineReferences.Length + "}");
        return failures == 0 ? 0 : 1;
    }
}
