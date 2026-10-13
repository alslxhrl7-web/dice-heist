using System;

namespace DiceHeist.Core
{
    public interface IRandom
    {
        /// <summary>0 이상 maxExclusive 미만의 정수.</summary>
        int Next(int maxExclusive);

        /// <summary>0 이상 1 미만의 실수.</summary>
        double NextDouble();
    }

    public static class RandomExtensions
    {
        public static int D6(this IRandom rng) => 1 + rng.Next(6);
    }

    /// <summary>
    /// SplitMix64. System.Random은 런타임 버전에 따라 수열이 달라지므로,
    /// 클라이언트와 서버가 같은 시드로 같은 판을 재현할 수 있게 직접 구현한다.
    /// </summary>
    public sealed class SeededRandom : IRandom
    {
        ulong _state;

        public SeededRandom(long seed)
        {
            _state = unchecked((ulong)seed);
        }

        ulong NextUInt64()
        {
            unchecked
            {
                ulong z = _state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            ulong bound = (ulong)maxExclusive;
            // 나머지 편향이 없도록 bound의 배수 구간 밖의 값은 버린다
            ulong limit = ulong.MaxValue - ulong.MaxValue % bound;
            ulong r;
            do r = NextUInt64(); while (r >= limit);
            return (int)(r % bound);
        }

        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
    }
}
