using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using PlanetSurface.Poi;
using PlanetSurface.World;

namespace PlanetSurface;

/// <summary>
/// A fingerprint of what a world is built from, for proving a speed-up changed nothing:
/// --only=Dump --dump=FILE --seeds=1,2,3 [--dsize=16] [--modes=plain,crowded,start]
/// builds each seed in each mode and writes, per world, one hash for each part (places with their layouts, routes, structures, ruins,
/// lots, the rest of the infrastructure). Run it on both sides of a change and compare the files.
/// </summary>
public partial class SurfaceTest
{
    async Task DumpChecks()
    {
        string file = null, seedArg = "1", modeArg = "plain,crowded,start";
        int km = 16;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--dump=")) file = arg["--dump=".Length..];
            else if (arg.StartsWith("--seeds=")) seedArg = arg["--seeds=".Length..];
            else if (arg.StartsWith("--dsize=")) km = int.Parse(arg["--dsize=".Length..]);
            else if (arg.StartsWith("--modes=")) modeArg = arg["--modes=".Length..];
        }
        if (file == null) return;
        var kinds = new[] { Archetype.Temperate, Archetype.Arid, Archetype.Ice };
        var lines = new List<string>();
        int n = 0;
        foreach (string s in seedArg.Split(','))
            foreach (string mode in modeArg.Split(','))
            {
                ulong seed = ulong.Parse(s);
                var spec = PlanetSpec.Roll(seed, kinds[n++ % kinds.Length], 0, km);
                PoiGenerator.Gen.WorldForce.Current = mode == "crowded" ? new PoiGenerator.Gen.WorldForce { Population = 7, PopulationNow = PoiGenerator.Gen.PopulationNow.Settled } : new PoiGenerator.Gen.WorldForce();
                PoiSiting.StartWorld = mode == "start";
                var clock = System.Diagnostics.Stopwatch.StartNew();
                PlanetWorld w = await Task.Run(() => PlanetWorld.Build(spec, Main.Catalog));
                double secs = clock.Elapsed.TotalSeconds;
                PoiSiting.StartWorld = false;
                PoiGenerator.Gen.WorldForce.Current = new PoiGenerator.Gen.WorldForce();
                var infra = w.Infra;
                var parts = new (string, object)[]
                {
                    ("pois", w.Pois),
                    ("routes", infra.Routes), ("structures", infra.Structures), ("ruins", infra.Ruins), ("places", infra.Places),
                    ("lots", infra.Lots), ("planned", infra.PlannedRoutes), ("isolated", infra.Isolated), ("wrecks", infra.Wrecks),
                    ("landing", infra.LandingSpots), ("town", infra.Town), ("city", infra.City), ("power", infra.Power),
                    ("blasts", infra.Blasts), ("craters", infra.Craters), ("targets", infra.Targets), ("grounds", infra.Grounds),
                    ("counts", new int[] { infra.Unrouted, infra.SeaSeparated, infra.TownStreets, infra.NeighbourLanes, infra.Spurs, infra.RailDropped }),
                };
                var sb = new StringBuilder($"{spec.Seed} {km} km {spec.Archetype} {mode}: {w.Profile} | ");
                foreach (var (name, part) in parts)
                {
                    var text = new StringBuilder();
                    int count = 0;
                    try { Print(text, part, 0, new HashSet<object>(ReferenceEqualityComparer.Instance)); count = part is ICollection c ? c.Count : 1; }
                    catch (Exception e) { text.Append("ERR " + e.GetType().Name); }
                    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..12];
                    sb.Append($"{name}[{count}]={hash} ");
                }
                lines.Add(sb.ToString());
                GD.Print($"      dumped {lines[^1][..Math.Min(lines[^1].Length, 120)]} ... {secs:F1} s ({PlanetWorld.LastTimes})");
            }
        File.WriteAllLines(file, lines);
    }

    static readonly HashSet<string> Shallow = new() { "FamilyDef", "RegionPlan", "PoiLibrary", "PoiCatalog", "WorldMap", "PlanetTerrainSource" };

    static void Print(StringBuilder sb, object o, int depth, HashSet<object> path)
    {
        if (o == null) { sb.Append("~;"); return; }
        Type t = o.GetType();
        if (o is float f) { sb.Append(BitConverter.SingleToInt32Bits(f)).Append(';'); return; }
        if (o is double d) { sb.Append(BitConverter.DoubleToInt64Bits(d)).Append(';'); return; }
        if (t.IsPrimitive || o is string || o is decimal || t.IsEnum) { sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)).Append(';'); return; }
        if (o is Delegate || o is GodotObject || o is Type) return;
        if (depth > 9) { sb.Append("..;"); return; }
        if (!t.IsValueType && !path.Add(o)) { sb.Append("^;"); return; }
        try
        {
            if (Shallow.Contains(t.Name)) { sb.Append(t.Name).Append(';'); return; }
            // A site met inside something else is named, not unpicked again.
            if (depth > 0 && o is PoiSite site) { sb.Append("site ").Append(site.Name).Append(' ').Append(site.Origin).Append(' ').Append(site.Rotation).Append(';'); return; }
            if (o is IEnumerable seq)
            {
                sb.Append('[');
                var items = seq.Cast<object>();
                if (o is HashSet<int> || o is HashSet<long>) items = items.OrderBy(x => Convert.ToInt64(x));
                foreach (object item in items) Print(sb, item, depth + 1, path);
                sb.Append(']');
                return;
            }
            sb.Append('{');
            foreach (FieldInfo fld in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (!fld.Name.EndsWith("Ms")) Print(sb, fld.GetValue(o), depth + 1, path);
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                // Properties that only restate fields (Stamps, Center) are skipped by name; the rest are read.
                if (p.Name.EndsWith("Ms") || p.Name is "Stamps" or "Center" or "Count") continue;
                object v;
                try { v = p.GetValue(o); } catch (Exception) { continue; }
                Print(sb, v, depth + 1, path);
            }
            sb.Append('}');
        }
        finally { if (!t.IsValueType) path.Remove(o); }
    }
}
