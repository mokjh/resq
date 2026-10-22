using NUnit.Framework;

namespace Resq.Tests.Probe
{
    [TestFixture, Category("probe-instrumentation")]
    public sealed class ProbeRecorderTests
    {
        [TestCaseSource(typeof(ProbeProtocolChecks), nameof(ProbeProtocolChecks.Names))]
        public void SharedProtocolSafety(string name) => ProbeProtocolChecks.Run(name);
    }
}
