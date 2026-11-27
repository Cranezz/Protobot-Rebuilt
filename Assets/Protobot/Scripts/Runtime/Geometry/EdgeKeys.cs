using System.Collections.Generic;

namespace Protobot.Geometry
{
    /// <summary>
    /// A mesh edge as one long (the smaller vertex index in the high half), with a hash that spreads.
    /// long.GetHashCode is the two halves XORed together, so neighbouring vertex indices share a few thousand
    /// hash codes: on a 116,000-vertex mesh (the Override goal) 696,000 edge uses fell on 14,962 codes and filling
    /// a dictionary took about 4 minutes, which froze the app when the Measure tool first looked at the part.
    /// Use <c>new Dictionary&lt;long, T&gt;(EdgeKeys.Comparer)</c>.
    /// </summary>
    public static class EdgeKeys
    {
        public static long Of(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        public static readonly IEqualityComparer<long> Comparer = new Mixed();

        private sealed class Mixed : IEqualityComparer<long>
        {
            public bool Equals(long x, long y) => x == y;

            // SplitMix64's finaliser: every bit of the key moves every bit of the hash.
            public int GetHashCode(long key)
            {
                var z = (ulong)key;
                z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
                z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
                z ^= z >> 31;
                return (int)z ^ (int)(z >> 32);
            }
        }
    }
}
