using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using GalHack.Music;

// Renders drones to WAV, seeds rolled fresh unless named, and prints what each took:
//   DroneRender --out <dir> [--seconds 90] [--rate 44100] [--only a,b]   the audition set, or part of it
//   DroneRender --out <dir> --job "name world=ice place=cave day=night seed=12 seconds=120"
// Each is the real engine's output, with only a short fade on the end so a file doesn't stop dead.

string outDir = Arg("--out") ?? ".";
int defaultSeconds = int.Parse(Arg("--seconds") ?? "90");
int rate = int.Parse(Arg("--rate") ?? "44100");
Directory.CreateDirectory(outDir);

string[] audition =
{
    "temperate-1 world=temperate", "temperate-2 world=temperate", "temperate-3 world=temperate tech=0.1",
    "temperate-high-tech world=temperate tech=0.95", "temperate-forest-dusk world=temperate ground=forest day=dusk",
    "temperate-night world=temperate day=night", "temperate-never-settled world=temperate inhabited=false",
    "temperate-red-dwarf world=temperate star=0.05", "temperate-blue-star world=temperate star=0.95",
    "temperate-nuclear world=temperate theme=nuclear", "temperate-exodus world=temperate theme=exodus tech=0.8",
    "arid-1 world=arid heat=0.8 damp=0.1", "arid-2 world=arid heat=0.9 damp=0.05", "arid-canyon-dawn world=arid ground=stone day=dawn",
    "ice-1 world=ice heat=0.1", "ice-2 world=ice heat=0.05", "ice-snow-night world=ice ground=snow day=night heat=0.1",
    "lava-1 world=lava heat=1", "lava-2 world=lava heat=0.95 ground=fire",
    "swamp-1 world=swamp damp=0.95", "swamp-2 world=swamp damp=0.9 heat=0.7",
    "ocean-1 world=ocean damp=0.8", "ocean-2 world=ocean ground=shore day=dusk", "ocean-underwater world=ocean place=underwater",
    "airless-1 world=airless", "airless-2 world=airless star=0.9",
    "place-cave world=temperate place=cave", "place-cave-ice world=ice place=cave heat=0.1", "place-ruin-interior world=temperate place=ruin",
    "place-town-low-tech world=temperate place=town tech=0.1", "place-town-high-tech world=temperate place=town tech=0.9",
    "place-bunker world=temperate place=bunker", "place-ship world=temperate place=ship tech=0.8", "place-base world=arid place=base",
    "space-high-orbit world=temperate place=highorbit", "space-deep-1 world=deepspace place=deepspace", "space-deep-2 world=deepspace place=deepspace",
    "space-nebula world=deepspace place=nebula", "space-galactic-core world=deepspace place=galacticcore",
    "hollow-1 world=hollow seconds=180", "hollow-2 world=hollow seconds=180", "hollow-ruin world=hollow place=ruin seconds=180",
    "hollow-patch world=temperate hollow=0.6 seconds=180",
    "long-arc-12-minutes world=temperate seconds=720",
};

var jobs = new List<string>();
if (Arg("--job") is { } one) jobs.Add(one);
else
{
    string only = Arg("--only");
    foreach (string job in audition)
        if (only == null || Array.IndexOf(only.Split(','), job.Split(' ')[0]) >= 0) jobs.Add(job);
}

foreach (string job in jobs)
{
    string[] parts = job.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var o = new Dictionary<string, string>();
    for (int i = 1; i < parts.Length; i++) o[parts[i].Split('=')[0]] = parts[i].Split('=')[1];
    float F(string key, float fallback) => o.TryGetValue(key, out string v) ? float.Parse(v, CultureInfo.InvariantCulture) : fallback;
    T E<T>(string key) where T : struct, Enum => o.TryGetValue(key, out string v) ? Enum.Parse<T>(v, ignoreCase: true) : default;

    ulong seed = o.TryGetValue("seed", out string s) ? ulong.Parse(s) : (ulong)Random.Shared.NextInt64(1, long.MaxValue);
    var world = new MusicWorld(E<DroneMood>("world"), seed)
    {
        Heat = F("heat", 0.5f),
        Damp = F("damp", 0.5f),
        Star = F("star", 0.5f),
        Tech = F("tech", 0.5f),
        Inhabited = !o.TryGetValue("inhabited", out string inhabited) || inhabited == "true",
        Theme = E<MusicTheme>("theme"),
    };
    var scene = new MusicScene(E<MusicPlace>("place"), E<MusicGround>("ground"), E<DayPart>("day"), F("hollow", 0f));
    int seconds = (int)F("seconds", defaultSeconds);

    var clock = Stopwatch.StartNew();
    var engine = new DroneEngine(world, scene, rate);
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
    WriteWav(Path.Combine(outDir, parts[0] + ".wav"), left, right, rate);
    Console.WriteLine($"{parts[0]}: seed {seed}, made in {made:F0} ms, {cost:F2}% of a core");
}

string Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static void WriteWav(string path, float[] left, float[] right, int rate)
{
    using var f = new BinaryWriter(new BufferedStream(File.Create(path), 1 << 20));
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
