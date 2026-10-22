using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Resq.Sim.Testing;

namespace Resq.Sim.Tests.NUnitAdapter
{
    /// <summary>Runs every [SimTest] method inside the Unity Test Runner (EditMode).</summary>
    public sealed class SimTestsNUnitAdapter
    {
        static IEnumerable<TestCaseData> Cases()
        {
            return typeof(SimTestAttribute).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
                .Where(m => m.GetCustomAttribute<SimTestAttribute>() != null)
                .OrderBy(m => m.DeclaringType?.Name + "." + m.Name)
                .Select(m => new TestCaseData(m).SetName($"{m.DeclaringType?.Name}.{m.Name}"));
        }

        [TestCaseSource(nameof(Cases))]
        public void Run(MethodInfo method)
        {
            object target = method.IsStatic ? null : System.Activator.CreateInstance(method.DeclaringType);
            try { method.Invoke(target, null); }
            catch (TargetInvocationException ex) when (ex.InnerException is CheckFailedException fail)
            {
                Assert.Fail(fail.Message);
            }
        }
    }
}
