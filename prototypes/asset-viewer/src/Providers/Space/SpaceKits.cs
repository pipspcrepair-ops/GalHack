using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GalHack.Items.Bridge;
using GalHack.Structures;
using N = System.Numerics;

namespace AssetViewer.Providers;

/// <summary>
/// The building catalogue the game uses for vessels (building's blocks plus the physics sandbox's vessel parts and shared/Items'
/// parts and materials, exactly as the slice's GameData loads it), and the way a piece of space is turned from blueprint text
/// into a structure. The station, wreck-field and escape-pod pieces are composed as the slice's Derelicts composes them (that
/// file is slice-only and its composers are private, so the same small block recipes are written here over the same blueprint
/// format); capital hulls come straight from the POI generator's CapitalHull.
/// </summary>
public static class SpaceKits
{
    static Catalogue _catalogue;

    static string Proto(params string[] parts) => Path.Combine(ResourceMount.PrototypesRoot, Path.Combine(parts));
    static string Shared(params string[] parts) => Path.GetFullPath(Path.Combine(ResourceMount.PrototypesRoot, "..", "shared", Path.Combine(parts)));

    public static Catalogue Catalogue => _catalogue ??= BuildingBridge.LoadCatalogue(Proto("building", "data"),
        new[] { Proto("physics-sandbox", "data", "vessel-parts.json"), Shared("Items", "data", "building-parts.json") },
        new[] { Shared("Items", "data", "building-materials.json") });

    /// <summary>One structure and where it floats, in the site's frame.</summary>
    public sealed record Piece(Structure Structure, N.Vector3 Position, N.Quaternion Rotation, string Label);

    /// <summary>Blueprint text to a coreless wreck, every rule but "has a core" holding.</summary>
    public static Structure Build(string text, string name)
    {
        BlueprintData data = Blueprint.Parse(text);
        List<string> errors = Blueprint.Validate(data, Catalogue).Where(e => !e.StartsWith("no core") && !e.Contains("not attached") && !e.Contains("attached to the core")).ToList();
        if (errors.Count > 0) throw new InvalidOperationException($"'{name}': " + string.Join("; ", errors.Take(5)));
        Structure s = Blueprint.Build(data, Catalogue);
        s.DirtyChunks.Clear();
        return s;
    }

    static N.Quaternion RandomTurn(Random rng)
    {
        double u1 = rng.NextDouble(), u2 = rng.NextDouble() * Math.Tau, u3 = rng.NextDouble() * Math.Tau;
        double a = Math.Sqrt(1 - u1), b = Math.Sqrt(u1);
        return N.Quaternion.Normalize(new N.Quaternion((float)(a * Math.Sin(u2)), (float)(a * Math.Cos(u2)), (float)(b * Math.Sin(u3)), (float)(b * Math.Cos(u3))));
    }

    static string HullColour(Random rng) => new[] { "8d99ae", "a3a380", "6c757d", "b08968", "7f8c8d" }[rng.Next(0, 5)];

    // ---- the kinds ---------------------------------------------------------------------------

    /// <summary>A station: a hub with an airlock, modules out on truss tubes, one module torn off and drifting.</summary>
    public static List<Piece> Station(Random rng, string name)
    {
        var hub = new Cells(rng, "d8d4c8", "1b263b");
        hub.Box(-3, 0, -3, 3, 4, 3, "hull", hollow: true);
        hub.Box(-2, 2, -2, 2, 2, 2, "deck");
        hub.Clear(-1, 2, -1, 0, 2, 0);
        hub.HullDoor(3, 1);
        foreach (var (x, z) in new[] { (-2, -2), (2, -2), (-2, 2) }) hub.Set(x, 1, z, "container", paint: "cargo");
        hub.Tube(0, 2, -4, 0, 0, -1, 5);
        hub.Set(0, 2, -3, "door", open: true);
        hub.Box(-2, 0, -13, 2, 4, -9, "hull", hollow: true);
        hub.Set(0, 2, -9, "door", open: true);
        hub.Set(-1, 1, -11, "container", paint: "cargo");
        hub.Set(1, 1, -12, "container", paint: "cargo");
        hub.Tube(4, 2, 0, 1, 0, 0, 3);
        hub.Set(3, 2, 0, "door", open: true);
        hub.Scars(8);
        var torn = new Cells(rng, "d8d4c8", "1b263b");
        torn.Box(-2, -2, -2, 2, 2, 2, "hull", hollow: true);
        torn.Tube(-5, 0, 0, 1, 0, 0, 3);
        torn.Clear(-2, 0, 0, -2, 0, 0);
        torn.Set(1, -1, 1, "container", paint: "cargo");
        torn.Scars(4);
        return new List<Piece>
        {
            new(hub.Build(name), N.Vector3.Zero, N.Quaternion.Identity, name),
            new(torn.Build(name + " (module)"), new N.Vector3(26f, 6f, 9f), N.Quaternion.CreateFromYawPitchRoll(0.6f, 0.4f, -0.3f), name + " (module)"),
        };
    }

    /// <summary>A wreck field: hull fragments and loose cargo boxes, scattered and tumbling apart.</summary>
    public static List<Piece> Field(Random rng, string name)
    {
        var pieces = new List<Piece>();
        int n = 5 + rng.Next(0, 4);
        string colour = HullColour(rng);
        for (int i = 0; i < n; i++)
        {
            var f = new Cells(rng, colour);
            f.Clump(6 + rng.Next(0, 22));
            if (i % 2 == 0) f.Set(0, 1, 0, "container", paint: "cargo");
            double a = i * Math.Tau / n + rng.NextDouble() * 0.5, r = 25 + rng.NextDouble() * 70;
            pieces.Add(new Piece(f.Build($"{name}: fragment {i + 1}"), new N.Vector3((float)(Math.Cos(a) * r), (float)((rng.NextDouble() - 0.5) * 40), (float)(Math.Sin(a) * r)), RandomTurn(rng), $"Fragment {i + 1}"));
        }
        return pieces;
    }

    /// <summary>An escape pod cluster: a ring of pods, each a seat, a locker and a hatch.</summary>
    public static List<Piece> Pods(Random rng, string name)
    {
        var pods = new List<Piece>();
        int n = 4 + rng.Next(0, 3);
        for (int i = 0; i < n; i++)
        {
            var p = new Cells(rng, "e85d04", "2b2d42");
            p.Box(-1, 0, -2, 1, 2, 2, "hull", hollow: true);
            p.Set(0, 1, -2, "door");
            p.Set(0, 1, 0, "cockpit");
            p.Set(0, 1, 1, "container", paint: "cargo");
            p.Scars(1);
            double a = i * Math.Tau / n;
            pods.Add(new Piece(p.Build($"Escape pod {i + 1}"), new N.Vector3((float)(Math.Cos(a) * 22), (float)((rng.NextDouble() - 0.5) * 8), (float)(Math.Sin(a) * 22)), RandomTurn(rng), $"Escape pod {i + 1}"));
        }
        return pods;
    }

    // ---- a layout as blueprint text ------------------------------------------------------------

    sealed class Cells
    {
        readonly Random _rng;
        readonly SortedDictionary<(int y, int z, int x), string> _cells = new();
        readonly string _hull, _accent;

        public Cells(Random rng, string hull, string accent = "2b2d42")
        {
            _rng = rng;
            _hull = hull;
            _accent = accent;
        }

        public void Set(int x, int y, int z, string type, string paint = null, string rot = null, bool open = false)
        {
            paint ??= type is "steel" or "hardened" ? "hull" : null;
            var line = new StringBuilder(type);
            if (rot != null) line.Append(" rot=").Append(rot);
            if (paint != null) line.Append(" paint=").Append(paint);
            if (open) line.Append(" open");
            _cells[(y, z, x)] = line.ToString();
        }

        public void Clear(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) _cells.Remove((y, z, x));
        }

        public void Box(int x0, int y0, int z0, int x1, int y1, int z1, string kind, bool hollow = false)
        {
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int edges = (x == x0 || x == x1 ? 1 : 0) + (y == y0 || y == y1 ? 1 : 0) + (z == z0 || z == z1 ? 1 : 0);
                        if (hollow && edges == 0) continue;
                        if (kind == "hull") Set(x, y, z, edges >= 2 ? "hardened" : "steel", edges >= 2 ? "trim" : "hull");
                        else Set(x, y, z, "steel", kind == "deck" ? "deck" : "wall");
                    }
        }

        public void HullDoor(int wallX, int z) => Set(wallX, 1, z, "door", paint: "hazard");

        public void Tube(int x, int y, int z, int dx, int dy, int dz, int length)
        {
            (int, int, int) p = dx != 0 ? (0, 1, 0) : (1, 0, 0), q = dz != 0 ? (0, 1, 0) : (0, 0, 1);
            if (dy != 0) (p, q) = ((1, 0, 0), (0, 0, 1));
            for (int i = 0; i < length; i++)
            {
                int cx = x + dx * i, cy = y + dy * i, cz = z + dz * i;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                    {
                        int px = cx + p.Item1 * a + q.Item1 * b, py = cy + p.Item2 * a + q.Item2 * b, pz = cz + p.Item3 * a + q.Item3 * b;
                        if (a == 0 && b == 0) _cells.Remove((py, pz, px));
                        else Set(px, py, pz, "steel", "trim");
                    }
            }
        }

        public void Scars(int count)
        {
            var shell = _cells.Where(c => c.Value.StartsWith("steel") && c.Value.Contains("paint=hull")).Select(c => c.Key).ToList();
            for (int i = 0; i < count && shell.Count > 0; i++)
            {
                int k = _rng.Next(0, shell.Count);
                _cells.Remove(shell[k]);
                shell.RemoveAt(k);
            }
        }

        public void Clump(int size)
        {
            var at = new List<(int x, int y, int z)> { (0, 0, 0) };
            Set(0, 0, 0, "steel", "hull");
            while (_cells.Count < size)
            {
                var (x, y, z) = at[_rng.Next(0, at.Count)];
                int d = _rng.Next(0, 6);
                (x, y, z) = d switch { 0 => (x + 1, y, z), 1 => (x - 1, y, z), 2 => (x, y + 1, z), 3 => (x, y - 1, z), 4 => (x, y, z + 1), _ => (x, y, z - 1) };
                if (Math.Abs(y) > 1 || _cells.ContainsKey((y, z, x))) continue;
                Set(x, y, z, _rng.Next(0, 4) == 0 ? "hardened" : "steel", _rng.Next(0, 3) == 0 ? "trim" : "hull");
                at.Add((x, y, z));
            }
        }

        public Structure Build(string name)
        {
            var text = new StringBuilder();
            text.AppendLine("schema galhack.blueprint");
            text.AppendLine("version 1");
            text.AppendLine($"name \"{name}\"");
            text.AppendLine("grid large");
            text.AppendLine("mode capital");
            text.AppendLine("gravity off");
            text.AppendLine($"paint hull base={_hull} grime=3 texture=plates");
            text.AppendLine($"paint trim base={_accent} grime=3");
            text.AppendLine("paint wall base=4a4e69 grime=3");
            text.AppendLine("paint deck base=22223b grime=3 texture=ribbed");
            text.AppendLine("paint cargo base=e85d04 accent=2b2d42 pattern=stripes grime=3");
            text.AppendLine("paint hazard base=f4a100 accent=2b2d42 pattern=hazard grime=2");
            foreach (var ((y, z, x), what) in _cells) text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"block {x} {y} {z} {what}"));
            return SpaceKits.Build(text.ToString(), name);
        }
    }
}

/// <summary>Frozen structures: in the viewer nothing moves a wreck, and gravity must not pull a wreck-without-core out of the picture.</summary>
public static class FrozenViews
{
    public static void Freeze(GalHack.Structures.Presentation.StructureView v)
    {
        v.GravityScale = 0f;
        v.CollisionLayer = 0;
        v.CollisionMask = 0;
        v.Freeze = true;
        v.FreezeMode = Godot.RigidBody3D.FreezeModeEnum.Static;
    }
}
