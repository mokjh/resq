using System;
using Resq.Core;

namespace Resq.Tests.Shared
{
    // Shared by actual Unity EditMode tests and the bounded installed-toolchain harness.
    public static class CoreSmokeChecks
    {
        public static void Run(string name)
        {
            switch (name)
            {
                case "zero": Require(default(Tick).Value == 0 && new Tick(0).Value == 0); break;
                case "maximum": Require(new Tick(Tick.Maximum).Value == 1000000); break;
                case "negative": Reject(-1); Reject(long.MinValue); break;
                case "over_maximum": Reject(1000001); Reject(long.MaxValue); break;
                case "equality":
                    Require(new Tick(17) == new Tick(17) && new Tick(17) != new Tick(18));
                    Require(new Tick(17).GetHashCode() == new Tick(17).GetHashCode());
                    Require(!new Tick(17).Equals((object)17)); break;
                case "ordering": Require(new Tick(16).CompareTo(new Tick(17)) < 0 && new Tick(18).CompareTo(new Tick(17)) > 0); break;
                case "isolation":
                    var original = new Tick(33); var copy = original; copy = new Tick(34);
                    Require(original.Value == 33 && copy.Value == 34 && original.ToString() == "33"); break;
                default: throw new ArgumentException("Unknown native check: " + name);
            }
        }
        public static readonly string[] Names = { "zero", "maximum", "negative", "over_maximum", "equality", "ordering", "isolation" };
        private static void Require(bool value) { if (!value) throw new InvalidOperationException("Core invariant failed."); }
        private static void Reject(long value)
        {
            try { new Tick(value); }
            catch (ArgumentOutOfRangeException) { return; }
            throw new InvalidOperationException("Invalid tick accepted: " + value);
        }
    }
}
