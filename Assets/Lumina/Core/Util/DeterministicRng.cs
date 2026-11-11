using System;

namespace Lumina.Core.Util
{
    /// <summary>
    /// 재현 가능한 난수(xorshift64*). 전투 판정·효과음 합성·테스트가 같은 시드에서 같은 결과를 내도록 한다.
    /// 가챠 서버 난수 용도가 아니다(서버 트랜잭션은 미구현).
    /// </summary>
    public sealed class DeterministicRng
    {
        private ulong _state;

        public DeterministicRng(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
            NextUInt();
        }

        public ulong State
        {
            get => _state;
            set => _state = value == 0 ? 0x9E3779B97F4A7C15UL : value;
        }

        public uint NextUInt()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return (uint)((_state * 0x2545F4914F6CDD1DUL) >> 32);
        }

        /// <summary>[0,1) 범위 float.</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>[-1,1) 범위 float (오디오 노이즈용).</summary>
        public float NextSigned() => NextFloat() * 2f - 1f;

        public double NextDouble() => NextUInt() / 4294967296.0;

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public bool Chance(float probability) => NextFloat() < probability;

        public static ulong Hash(params int[] values)
        {
            ulong h = 1469598103934665603UL;
            foreach (int v in values)
            {
                h ^= (uint)v;
                h *= 1099511628211UL;
            }
            return h == 0 ? 1UL : h;
        }
    }
}
