using System;
using System.Diagnostics;
using System.IO;
using GalHack.Music;

// Renders one WAV per mood, seeds rolled fresh unless named, and prints what each took:
//   dotnet run -c Release --project shared/Music/Render -- --out <dir> [--seconds 90] [--rate 44100]
//   ... -- --out <dir> --only temperate-1=temperate:12345   (name=mood[:seed], comma separated)
// Each is the real engine's output, with only a short fade on the end so a file doesn't stop dead.

string outDir = Arg("--out") ?? ".";
int seconds = int.Parse(Arg("--seconds") ?? "90");
int rate = int.Parse(Arg("--rate") ?? "44100");
string only = Arg("--only") ?? "temperate-1=temperate,temperate-2=temperate,arid=arid,ice=ice,lava=lava,swamp=swamp,ocean=ocean,airless=airless,deep-space=deepspace,hollow=hollow";
Directory.CreateDirectory(outDir);

foreach (string job in only.Split(','))
{
    string[] parts = job.Split('=', 2), moodSeed = parts[1].Split(':');
    DroneMood mood = Enum.Parse<DroneMood>(moodSeed[0], ignoreCase: true);
    ulong seed = moodSeed.Length > 1 ? ulong.Parse(moodSeed[1]) : (ulong)Random.Shared.NextInt64(1, long.MaxValue);

    var clock = Stopwatch.StartNew();
    var engine = new DroneEngine(mood, seed, rate);
    double made = clock.Elapsed.TotalMilliseconds;
    int frames = seconds * rate;
    var left = new float[frames];
    var right = new float[frames];
    clock.Restart();
    for (int at = 0; at < frames; at += 1024)
        engine.Render(left, right, at, Math.Min(1024, frames - at));
    double cost = clock.Elapsed.TotalSeconds / seconds * 100.0;

    int tail = Math.Min(frames, 4 * rate);
    for (int i = 0; i < tail; i++)
    {
        float x = (float)i / tail, g = 1f - x * x * (3f - 2f * x);
        left[frames - tail + i] *= g;
        right[frames - tail + i] *= g;
    }
    string path = Path.Combine(outDir, parts[0] + ".wav");
    WriteWav(path, left, right, rate);
    Console.WriteLine($"{parts[0]}: {mood} seed {seed}, made in {made:F0} ms, {cost:F2}% of a core");
}

string Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static void WriteWav(string path, float[] left, float[] right, int rate)
{
    using var f = new BinaryWriter(File.Create(path));
    int bytes = left.Length * 4;
    f.Write("RIFF"u8);
    f.Write(36 + bytes);
    f.Write("WAVEfmt "u8);
    f.Write(16);
    f.Write((short)1);
    f.Write((short)2);
    f.Write(rate);
    f.Write(rate * 4);
    f.Write((short)4);
    f.Write((short)16);
    f.Write("data"u8);
    f.Write(bytes);
    var rng = new Random(1);
    for (int i = 0; i < left.Length; i++)
    {
        f.Write(Sample(left[i], rng));
        f.Write(Sample(right[i], rng));
    }
}

// Triangular dither: the quiet tails stay smooth at 16 bits.
static short Sample(float x, Random rng)
{
    double dither = (rng.NextDouble() - rng.NextDouble()) / 32768.0;
    return (short)Math.Round(Math.Clamp(x + dither, -1.0, 1.0) * 32767.0);
}
