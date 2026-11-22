// SPDX-License-Identifier: MIT
// Pure code (no game references).
using System;

namespace Hearth
{
    public enum RngAlgorithm { SplitMix64 = 0, Pcg32 = 1, Xoshiro256 = 2, ChaCha8 = 3 }

    /// <summary>
    /// Counter-based random stream: value n of a stream is a pure function of (seed, streamId, n).
    /// That makes results reproducible, independent of call order between systems, and safe for multiplayer.
    /// </summary>
    public sealed class RngStream
    {
        public ulong Seed { get; private set; }
        public ulong StreamId { get; private set; }
        public ulong Counter;
        public RngAlgorithm Algorithm;

        public RngStream(ulong seed, ulong streamId, ulong counter = 0, RngAlgorithm algo = RngAlgorithm.SplitMix64)
        { Seed = seed; StreamId = streamId; Counter = counter; Algorithm = algo; }

        /// <summary>Re-points this instance at another stream (used to avoid allocations for throw-away streams).</summary>
        public void Reseed(ulong seed, ulong streamId, ulong counter, RngAlgorithm algo)
        { Seed = seed; StreamId = streamId; Counter = counter; Algorithm = algo; }

        public static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static ulong HashString(string s)
        {
            // FNV-1a 64 — stable across platforms and runs (string.GetHashCode is not).
            ulong h = 14695981039346656037UL;
            foreach (char c in s ?? "") { h ^= c; h *= 1099511628211UL; }
            return h;
        }

        public ulong NextULong()
        {
            ulong n = Counter++;
            switch (Algorithm)
            {
                case RngAlgorithm.Pcg32:
                {
                    ulong state = Mix(Seed ^ StreamId) + n * 6364136223846793005UL;
                    ulong a = Pcg(state), b = Pcg(state ^ 0xDA3E39CB94B95BDBUL);
                    return (a << 32) | b;
                }
                case RngAlgorithm.Xoshiro256:
                {
                    ulong s0 = Mix(Seed + n), s1 = Mix(StreamId + n * 0x9E37UL), s2 = Mix(s0 ^ s1), s3 = Mix(s2 + n);
                    ulong r = Rotl(s1 * 5, 7) * 9;
                    return r ^ s3 ^ s0;
                }
                case RngAlgorithm.ChaCha8:
                    return ChaChaLite(Seed, StreamId, n);
                default:
                    return Mix(Seed ^ Mix(StreamId ^ Mix(n)));
            }
        }

        /// <summary>Uniform in [0,1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        public float NextFloat() => (float)NextDouble();
        public bool Chance(double p) => p >= 1 || (p > 0 && NextDouble() < p);
        /// <summary>Uniform int in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            ulong span = (ulong)((long)maxExclusive - min); // long maths: the span may exceed int.MaxValue
            return (int)(min + (long)(NextULong() % span));
        }
        public double Range(double min, double max) => min + (max - min) * NextDouble();
        /// <summary>Standard normal (Box-Muller).</summary>
        public double Gaussian(double mean = 0, double sd = 1)
        {
            double u1 = Math.Max(1e-12, NextDouble()), u2 = NextDouble();
            return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
        public int Poisson(double lambda)
        {
            if (lambda <= 0) return 0;
            if (lambda > 30) return Math.Max(0, (int)Math.Round(Gaussian(lambda, Math.Sqrt(lambda))));
            double l = Math.Exp(-lambda), p = 1; int k = 0;
            do { k++; p *= NextDouble(); } while (p > l);
            return k - 1;
        }

        static ulong Pcg(ulong state)
        {
            uint xorshifted = (uint)(((state >> 18) ^ state) >> 27);
            int rot = (int)(state >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }
        static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        static uint R(uint x, int n) => (x << n) | (x >> (32 - n));
        static void QR(ref uint a, ref uint b, ref uint c, ref uint d)
        { a += b; d ^= a; d = R(d, 16); c += d; b ^= c; b = R(b, 12); a += b; d ^= a; d = R(d, 8); c += d; b ^= c; b = R(b, 7); }
        /// <summary>ChaCha with 8 rounds over (key=seed/stream, counter=n); returns the first 64 bits of the block.</summary>
        [ThreadStatic] static uint[] chaS, chaX;
        static ulong ChaChaLite(ulong seed, ulong stream, ulong n)
        {
            var s = chaS ?? (chaS = new uint[16]);
            var x = chaX ?? (chaX = new uint[16]);
            ulong ms = Mix(seed), mt = Mix(stream);
            s[0] = 0x61707865; s[1] = 0x3320646e; s[2] = 0x79622d32; s[3] = 0x6b206574;
            s[4] = (uint)seed; s[5] = (uint)(seed >> 32); s[6] = (uint)stream; s[7] = (uint)(stream >> 32);
            s[8] = (uint)ms; s[9] = (uint)(ms >> 32); s[10] = (uint)mt; s[11] = (uint)(mt >> 32);
            s[12] = (uint)n; s[13] = (uint)(n >> 32); s[14] = 0x4845_4152; s[15] = 0x5448_0001;
            Array.Copy(s, x, 16);
            for (int i = 0; i < 4; i++)
            {
                QR(ref x[0], ref x[4], ref x[8], ref x[12]); QR(ref x[1], ref x[5], ref x[9], ref x[13]);
                QR(ref x[2], ref x[6], ref x[10], ref x[14]); QR(ref x[3], ref x[7], ref x[11], ref x[15]);
                QR(ref x[0], ref x[5], ref x[10], ref x[15]); QR(ref x[1], ref x[6], ref x[11], ref x[12]);
                QR(ref x[2], ref x[7], ref x[8], ref x[13]); QR(ref x[3], ref x[4], ref x[9], ref x[14]);
            }
            return ((ulong)(x[1] + s[1]) << 32) | (x[0] + s[0]);
        }
    }
}
