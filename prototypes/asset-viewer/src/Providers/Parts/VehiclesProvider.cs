using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GalHack.Deco;
using GalHack.Items;
using GalHack.Structures;
using Godot;
using Slice.Places;
using Slice.Vehicles;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// Vessels and vehicles. The hover bike's generated model; every vessel design and base blueprint the game
/// ships (slice, capital-base, building, physics-sandbox and manufacturing's .gbp files, read from where they
/// live) in the states the game's damage and paint rules make (intact, worn, rusted, burnt, damaged, wrecked);
/// the capital hulls the POI generator composes (role, size, crash, tech and kit); and the deco library's
/// vehicles, trains and airliners with their states, plus the outdoor sites they stand in.
/// </summary>
public sealed partial class VehiclesProvider : IAssetProvider
{
    public string Category => "Vehicles";

    public static readonly string[] States = { "Intact", "Worn", "Rusted", "Burnt", "Damaged", "Wrecked" };

    public IEnumerable<AssetEntry> Entries()
    {
        yield return new AssetEntry { Id = "hover-bike", Group = "Ground and hover", Name = "Hover bike", Build = _ => BuildBike() };

        string protos = PartsData.Proto();
        foreach (string dir in Directory.EnumerateDirectories(protos).OrderBy(d => d, StringComparer.Ordinal))
        {
            string proto = System.IO.Path.GetFileName(dir);
            string bp = System.IO.Path.Combine(dir, "data", "blueprints");
            if (!Directory.Exists(bp)) continue;
            foreach (string file in Directory.EnumerateFiles(bp, "*.gbp").OrderBy(f => f, StringComparer.Ordinal))
            {
                string path = file;
                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                yield return new AssetEntry
                {
                    Id = $"vessel.{proto}.{name}", Group = (proto == "slice" ? "Vessels and bases (the game's)/" : "Vessels and bases (other prototypes)/") + PartsData.Nice(proto), Name = PartsData.Nice(name),
                    Randomisable = true, Options = new[] { new AssetOption("State", States) { Rollable = true } },
                    Build = r => BuildVessel(path, proto, name, r),
                };
            }
        }

        foreach (AssetEntry e in CapitalHulls.Entries()) yield return e;

        Decor decor = PartsData.Deco;
        if (decor == null) yield break;
        foreach (DecoPiece p in decor.Catalogue.Pieces)
        {
            DecoSet set = decor.Catalogue.Set(p.Set);
            if (!set.Outdoor || p.Salvage == null) continue;
            AssetEntry e = DecoProvider.PieceEntry(decor, p);
            e.Id = "deco." + p.Id;
            e.Group = "Deco vehicles/" + set.Name;
            yield return e;
        }
        foreach (string site in decor.Poi.Arrangements.Sites.Keys)
        {
            AssetEntry e = Rooms.SiteEntry(decor, site);
            e.Group = "Vehicle sites";
            yield return e;
        }
    }

    // ---- The hover bike ------------------------------------------------------------------------

    static readonly MethodInfo BikeBuild = typeof(HoverBikes).Assembly.GetType("Slice.Vehicles.BikeModel")?.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);

    static AssetInstance BuildBike()
    {
        var asset = new ModelAsset { Name = "HoverBike" };
        Node3D model = (Node3D)BikeBuild.Invoke(null, null);
        Node3D holder = asset.Mount(model);
        asset.Stage = new StageSpec { Ground = true };
        float t = 0f;
        // At rest on its skids; hovering, it rides HoverBike.Hover above the surface with its pads lit.
        asset.AddAnimation("Parked", () => holder.Position = new Vector3(holder.Position.X, holder.Position.Y, holder.Position.Z), null);
        Vector3 rest = holder.Position;
        asset.AddAnimation("Hover", () => t = 0f, dt =>
        {
            t += dt;
            float lift = Mathf.Min(1f, t * 1.5f);
            holder.Position = rest + Vector3.Up * (HoverBike.Hover * lift + Mathf.Sin(t * 2.6f) * 0.03f * lift);
        }, () => holder.Position = rest);
        asset.FactsFn = () => new[]
        {
            ("Ride height", $"{HoverBike.Hover:0.##} m"), ("Top speed", $"{HoverBike.MaxSpeed:0} m/s, {HoverBike.BoostSpeed:0} boosting"),
            ("Weight", $"{HoverBike.Weight:0} kg"), ("Turn rate", $"{HoverBike.TurnRate:0.##} rad/s"),
            ("Class", "Ground transport, from the item 'hover-bike'"),
        };
        asset.Grow(asset);
        return asset;
    }

    // ---- A blueprint's vessel --------------------------------------------------------------------

    static AssetInstance BuildVessel(string path, string proto, string name, BuildRequest r)
    {
        Catalogue cat = PartsData.Blocks;
        Structure s = Blueprint.Load(File.ReadAllText(path), cat, out List<string> errors);
        if (s == null) throw new InvalidDataException($"{proto}/{name}.gbp: {string.Join("; ", errors.Take(3))}");
        s.DirtyChunks.Clear();
        string state = r.Get("State", "Intact");
        VesselStates.Apply(s, state, r.Seed);
        var asset = new StructureAsset(s);
        asset.FactsFn = () => VesselFacts(s, state);
        Working.Add(asset);
        return asset;
    }

    public static IEnumerable<(string, string)> VesselFacts(Structure s, string state)
    {
        StructureStats st = StructureStats.Compute(s);
        float c = s.Grid.CellSize;
        var (min, max) = s.Count > 0 ? s.Bounds() : (Int3.Zero, Int3.Zero);
        yield return ("Class", StructureClasses.Name(GalHack.Items.Bridge.BuildingBridge.ClassOf(s)));
        yield return ("Grid", s.Grid.Size == GridSize.Small ? "Small, 0.5 m" : "Large, 2 m");
        yield return ("Size", $"{(max.X - min.X + 1) * c:0.#} x {(max.Y - min.Y + 1) * c:0.#} x {(max.Z - min.Z + 1) * c:0.#} m");
        yield return ("Blocks", $"{st.Blocks} ({st.Components} devices)");
        yield return ("Mass", $"{st.Mass:0.#} t");
        yield return ("CPU", $"{st.CpuUsed:0} of {st.CpuLimit:0} (tier {st.Tier})");
        yield return ("Power", $"{st.PowerOut:0.##} MW out, {st.PowerUse:0.##} MW use");
        yield return ("Thrusters", st.Thrusters.ToString());
        if (st.Thrusters > 0) yield return ("Thrust to weight", $"{st.ThrustToWeightForward:0.00} g forward, {st.ThrustToWeightMax:0.00} g best axis");
        yield return ("Storage", $"{st.StorageCapacity:0} SU in {st.Containers} containers");
        yield return ("State", state);
    }
}
