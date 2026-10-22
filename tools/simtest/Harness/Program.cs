using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Resq.Sim.Testing;

namespace Resq.Sim.Headless
{
    /// <summary>Headless runner: discovers [SimTest] methods in this assembly and runs them.</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--lab") return Lab.Run(args.Skip(1).ToArray());
            string filter = args.FirstOrDefault(a => !a.StartsWith("--"));
            bool verbose = args.Contains("--verbose");
            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                .Where(m => m.GetCustomAttribute<SimTestAttribute>() != null)
                .Where(m => filter == null || FullName(m).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(FullName, StringComparer.Ordinal)
                .ToList();

            int passed = 0;
            var failures = new List<string>();
            var total = Stopwatch.StartNew();
            foreach (var method in tests)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    object target = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType);
                    method.Invoke(target, null);
                    passed++;
                    if (verbose) Console.WriteLine($"  ok   {FullName(method)} ({sw.Elapsed.TotalMilliseconds:F1} ms)");
                }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    string detail = inner is CheckFailedException ? inner.Message : inner.ToString();
                    failures.Add($"{FullName(method)}: {detail}");
                    Console.WriteLine($"  FAIL {FullName(method)}: {detail}");
                }
            }

            Console.WriteLine($"{passed}/{tests.Count} passed in {total.Elapsed.TotalSeconds:F2}s" +
                              (failures.Count > 0 ? $" — {failures.Count} FAILED" : ""));
            return failures.Count == 0 && tests.Count > 0 ? 0 : 1;
        }

        static string FullName(MethodInfo m) => $"{m.DeclaringType?.Name}.{m.Name}";
    }
}
