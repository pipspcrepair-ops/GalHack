namespace Planets.Gen;

/// <summary>
/// A tiny deterministic random number generator (SplitMix64). System.Random's algorithm is an
/// implementation detail of .NET and has changed between versions, so world seeds never touch it.
/// </summary>
public struct Rng
{
    ulong _state;

    public Rng(ulong seed) { _state = seed; }

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Hash.Mix(_state);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float NextFloat() => (NextULong() >> 40) * (1f / (1 << 24));

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>Uniform integer in [0, count).</summary>
    public int Next(int count) => (int)((NextULong() >> 33) % (ulong)count);
}

/// <summary>Integer hashing for position-keyed randomness (craters, volcanoes, scatter).</summary>
public static class Hash
{
    /// <summary>SplitMix64 finaliser: a cheap, well-mixed 64-bit hash.</summary>
    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static ulong Of(ulong seed, long a, long b = 0, long c = 0) =>
        Mix(seed ^ Mix((ulong)a + 0x9E3779B97F4A7C15UL ^ Mix((ulong)b + 0x632BE59BD9B4E019UL ^ Mix((ulong)c))));

    /// <summary>The k-th independent uniform value in [0, 1) drawn from one hash.</summary>
    public static float Unit(ulong hash, int k = 0) =>
        (Mix(hash + (ulong)k * 0x9E3779B97F4A7C15UL) >> 40) * (1f / (1 << 24));
}
