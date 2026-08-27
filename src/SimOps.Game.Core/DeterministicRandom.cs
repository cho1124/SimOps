using System;

namespace SimOps.Game.Core;

public enum RandomStream
{
    Encounter = 1,
    Intent = 2,
    Reward = 3,
    Agent = 4,
}

public sealed class DeterministicRandom
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = seed;
    }

    public ulong State => _state;

    public static ulong DeriveSeed(ulong baseSeed, RandomStream stream, int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        unchecked
        {
            var value = baseSeed;
            value ^= (ulong)stream * 0xD2B74407B1CE6E93UL;
            value ^= ((ulong)index + 1UL) * 0xCA5A826395121157UL;
            return Mix(value + GoldenGamma);
        }
    }

    public ulong NextUInt64()
    {
        unchecked
        {
            _state += GoldenGamma;
            return Mix(_state);
        }
    }

    public int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }

        var bound = (ulong)exclusiveMaximum;
        var threshold = unchecked(0UL - bound) % bound;

        while (true)
        {
            var candidate = NextUInt64();
            if (candidate >= threshold)
            {
                return (int)(candidate % bound);
            }
        }
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
