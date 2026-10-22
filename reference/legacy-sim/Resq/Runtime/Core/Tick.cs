using System;
using System.Globalization;

namespace Resq.Core
{
    // Frozen tick domain, specification/03_RUNTIME_CONTRACTS.md section 1.
    // This value has no clock policy; operation/scenario advancement belongs to C02.
    public readonly struct Tick : IEquatable<Tick>, IComparable<Tick>
    {
        public const long Maximum = 1000000;
        public long Value { get; }

        public Tick(long value)
        {
            if (value < 0 || value > Maximum)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Tick outside 0..1000000.");
            Value = value;
        }

        public bool Equals(Tick other) => Value == other.Value;
        public override bool Equals(object other) => other is Tick tick && Equals(tick);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(Tick other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        public static bool operator ==(Tick left, Tick right) => left.Equals(right);
        public static bool operator !=(Tick left, Tick right) => !left.Equals(right);
    }
}
