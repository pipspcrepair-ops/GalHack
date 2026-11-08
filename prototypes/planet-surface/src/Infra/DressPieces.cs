using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Matrix4x4 = System.Numerics.Matrix4x4;
using NV = System.Numerics.Vector3;

namespace PlanetSurface.Infra;

public partial class InfraLayer
{
    /// <summary>A world's dressing (<see cref="WorldDress"/>), each piece drawn into the tile it stands in; marks on house fronts go with their house.</summary>
    public static IEnumerable<Piece> DressPieces(PlanetWorld world)
    {
        foreach (DressItem it in world.Dress.Items)
            if (it.Site != DressSite.Wall) yield return new DressPiece { Item = it, X = it.X, Z = it.Z };
    }

    sealed class DressPiece : Piece
    {
        public DressItem Item;
        public override void Emit(TileBuilder b) => b.Dress(Item);
    }
}

public sealed partial class TileBuilder
{
    /// <summary>Models that stand up straight whatever the slope (posts, masts, stands, frames); the rest lie with the ground.</summary>
    static readonly HashSet<string> Upright = new(StringComparer.Ordinal)
    {
        "lamp", "sign_post", "banner", "beacon", "bell_post", "two_flags", "scaffold", "tent", "grandstand", "stage", "stall", "ticket_booth", "shrine",
        "mannequins", "telescope", "memorial", "hitch_rail", "water_barrels", "planters", "timber_frame", "queue_rails", "scarves", "soup_kitchen",
        "washing_line", "picket", "burn_barrel", "spikes", "fence", "wire", "survey_stakes", "dust_sheets", "garden_table", "tape",
    };

    /// <summary>A piece of the world's dressing, drawn into the tile: its model by era, painted in the world's paint and worn by its age.</summary>
    public void Dress(DressItem it)
    {
        float yaw = it.Yaw;
        var (ax, az) = it.Across;
        if (it.Site == DressSite.Span || it.Model == "tape" && it.Site is DressSite.Garden or DressSite.Gate)
        {
            // Each post stands on its own ground; the middle is the model's origin.
            double half = it.Length / 2;
            float mid = Ground(it.X, it.Z), left = Ground(it.X - ax * half, it.Z - az * half), right = Ground(it.X + ax * half, it.Z + az * half);
            if (it.Site != DressSite.Span) mid = MathF.Min(mid, MathF.Min(left, right));
            DecoModel span = DecoDressing.Build(it.Model, it.Args(left - mid, right - mid));
            Place(span, it, Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(Local(it.X, mid, it.Z)));
            return;
        }
        DecoModel model = DecoDressing.Build(it.Model, it.Args());
        float radius = MathF.Max(0.3f, it.Part.Reach * 0.75f);
        Matrix4x4 seat = Seat(it.X, it.Z, radius, Upright.Contains(it.Model) ? 0f : 0.3f);
        Place(model, it, Matrix4x4.CreateRotationY(yaw) * seat);
    }

    /// <summary>The marks and fittings on a house's front, on what's left of its walls by the door (the house as spawned, at its origin in the tile).</summary>
    public void DressHouse(FlavourRuin ruin, PoiInstance inst, NV origin)
    {
        if (!World.Dress.OnHouse.TryGetValue(ruin, out var items)) return;
        Layout l = ruin.Layout;
        PoiGenerator.Gen.Int3 v = l.EntranceFacing.Vec(), t = new(-v.Z, 0, v.X), door = l.EntranceCell - v;
        bool Solid(PoiGenerator.Gen.Int3 c) => BlockInfo.Collides(inst.Blocks.Get(c).Type);
        bool Open(PoiGenerator.Gen.Int3 c) => !Solid(c);
        float yaw = MathF.Atan2(-v.X, -v.Z);
        foreach (DressItem it in items)
        {
            bool fitting = it.Model is "awning" or "shop_sign";
            int y0 = l.EntranceCell.Y;
            // Which wall cells it needs, by how wide and how high it is.
            int[] rows = fitting ? new[] { 2 } : new[] { 1 };
            int half = it.Model == "mark" && it.Style.StartsWith("mural", StringComparison.Ordinal) ? 1 : 0;
            PoiGenerator.Gen.Int3? at = null;
            foreach (int shift in new[] { 0, 1, -1, 2, -2 })
            {
                int along = (int)MathF.Round(it.Along) + (it.Along >= 0 ? shift : -shift);
                if (fitting && it.Model == "awning") along = shift;
                bool ok = true;
                for (int k = -half; k <= half && ok; k++)
                    foreach (int row in rows)
                    {
                        PoiGenerator.Gen.Int3 c = door + t * (along + k) + PoiGenerator.Gen.Int3.Up * row;
                        if (it.Model == "awning")
                        {
                            // Over the door: the lintel and the wall either side.
                            ok &= Solid(c + t) || Solid(c - t) || Solid(c);
                            ok &= Open(c + v) && Open(c + v - PoiGenerator.Gen.Int3.Up);
                        }
                        else ok &= Solid(c) && Open(c + v);
                        if (!ok) break;
                    }
                if (!ok || along == 0 && !fitting) continue;
                at = door + t * along;
                break;
            }
            if (at is not { } cell) continue;
            // The wall's outer face: half a cell out from the wall cell's middle, at the door's floor.
            NV face = origin + new NV(cell.X + 0.5f + v.X * 0.5f, y0, cell.Z + 0.5f + v.Z * 0.5f);
            DecoModel model = DecoDressing.Build(it.Model, it.Args());
            Place(model, it, Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(face));
        }
    }

    static readonly DecoRgb Linen = DecoRgb.Hex("dcd6c6"), Canvas = DecoRgb.Hex("9e9272"), Paper = DecoRgb.Hex("d6cfb8"), Composite = DecoRgb.Hex("d2d6da"),
        Wood = DecoRgb.Hex("6b4f36"), Metal = DecoRgb.Hex("727578"), Chrome = DecoRgb.Hex("bfc3c8"), Rust = DecoRgb.Hex("6e4026"), Bronze = DecoRgb.Hex("9c7438"),
        Gold = DecoRgb.Hex("c49e40"), Dark = DecoRgb.Hex("1f2022"), Rubber = DecoRgb.Hex("121212"), Hazard = DecoRgb.Hex("d8b018"), Alert = DecoRgb.Hex("b81e1a"),
        Char = DecoRgb.Hex("1a1512"), Ash = DecoRgb.Hex("68645e"), Bone = DecoRgb.Hex("cdc2a8"), Grime = DecoRgb.Hex("4a4238"), DeadGlow = DecoRgb.Hex("2a2c2e"),
        Holo = DecoRgb.Hex("5cc8f0");

    static DecoRgb D(Rgb c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

    /// <summary>A slot's colour on this world: made things in its paint, faded and dirtied by its age (fresh when the living keep them); stone, earth and growth its own.</summary>
    DecoRgb Colour(DecoSlot s, DressItem it)
    {
        WorldPaint paint = World.Profile.Paint;
        DecoRgb main = paint != null ? D(paint.Main) : DecoRgb.Hex("8a6a4e"), accent = paint != null ? D(paint.Accent) : DecoRgb.Hex("b04a32"),
            cloth = paint != null ? D(paint.Cloth) : DecoRgb.Hex("c08a3a"), light = paint != null ? D(paint.Light) : DecoRgb.Hex("ffd08a"),
            roof = paint != null ? D(paint.Roof) : DecoRgb.Hex("5a6e7a");
        DecoRgb rock = D(World.RubbleRock), weed = World.Weed is Rgb w ? D(w) : DecoRgb.Hex("4f6a34");
        DecoRgb made = s switch
        {
            DecoSlot.Fabric => cloth,
            DecoSlot.Trim => accent,
            DecoSlot.Signal => main.Luma < 0.18f ? roof : main,
            DecoSlot.Body => main,
            DecoSlot.Linen => Linen,
            DecoSlot.Canvas => Canvas,
            DecoSlot.Paper => Paper,
            DecoSlot.Plastic => Composite.Lerp(main, 0.12f),
            DecoSlot.Wood => Wood,
            DecoSlot.Metal => Metal,
            DecoSlot.Chrome => Chrome,
            DecoSlot.Rust => Rust,
            DecoSlot.Bronze => Bronze,
            DecoSlot.Gold => Gold,
            DecoSlot.Rubber => Rubber,
            DecoSlot.Hazard => Hazard,
            DecoSlot.Alert => Alert,
            DecoSlot.Bone => Bone,
            DecoSlot.Grime => Grime,
            DecoSlot.Stone => rock,
            DecoSlot.Soil => rock.Scale(0.7f).Lerp(DecoRgb.Hex("5a4632"), 0.4f),
            DecoSlot.Plant => weed,
            DecoSlot.Char => Char,
            DecoSlot.Ash => Ash,
            DecoSlot.Lamp => it.Lit ? light.Lerp(new DecoRgb(1, 1, 1), 0.2f) : DeadGlow.Lerp(light, 0.12f),
            DecoSlot.Screen => it.Lit ? Holo.Lerp(light, 0.25f) : DeadGlow,
            _ => Dark,
        };
        // Natural things and burning lamps keep their own; made things weather as the world's paint does.
        if (s is DecoSlot.Stone or DecoSlot.Soil or DecoSlot.Plant or DecoSlot.Char or DecoSlot.Ash || it.Lit && s is DecoSlot.Lamp or DecoSlot.Screen) return made;
        if (it.Kept || paint == null) return made.Lerp(Grime, it.Wear * 0.3f);
        Rgb back = paint.Weather(new Rgb((byte)(made.R * 255), (byte)(made.G * 255), (byte)(made.B * 255)));
        // Cloth and paper go grey faster than paint; a long century more again.
        DecoRgb aged = D(back);
        if (s is DecoSlot.Fabric or DecoSlot.Linen or DecoSlot.Canvas or DecoSlot.Paper or DecoSlot.Signal or DecoSlot.Trim)
            aged = aged.Lerp(new DecoRgb(aged.Luma, aged.Luma, aged.Luma), it.Wear * 0.25f);
        return aged;
    }

    DecoSurface SurfaceOf(DecoSlot s, DressItem it) => s switch
    {
        DecoSlot.Lamp or DecoSlot.Screen => it.Lit ? DecoSurface.Glow : DecoSurface.Matte,
        DecoSlot.Glass => DecoSurface.Matte,
        _ => DecoPalette.SurfaceOf(s) == DecoSurface.Glow ? DecoSurface.Matte : DecoPalette.SurfaceOf(s),
    };

    /// <summary>A dressing model into the tile: its parts and cables meshed, worn, painted; its solid parts collide if the part is solid.</summary>
    void Place(DecoModel model, DressItem it, Matrix4x4 place)
    {
        var mesh = new DecoMeshData { Wear = it.Kept ? 1 : it.Wear > 0.5f ? 3 : 2 };
        foreach (DecoPart p in model.Parts)
            DecoEmit.Part(mesh, p, (_, _, slot) => Colour(slot, it), slot => SurfaceOf(slot, it));
        foreach (DecoCable c in model.Cables)
            foreach (DecoPart p in DecoCables.Tubes(DecoCables.Path(c), c.Radius, c.Slot))
                DecoEmit.Part(mesh, p, (_, _, slot) => Colour(slot, it), slot => SurfaceOf(slot, it));
        if (it.Part.Solid) DecoEmit.Collision(mesh, model.Parts.Where(p => !p.Is(PartFlags.NoCollide)), 10);
        Into(mesh, place, it.Part.Solid);
    }

    /// <summary>Copies a deco mesh into the tile under a transform, keeping each vertex's face for the surface detail; its collision boxes become collision.</summary>
    void Into(DecoMeshData data, Matrix4x4 place, bool collide)
    {
        for (int s = 0; s < data.Surfaces.Length; s++)
        {
            DecoSurfaceData src = data.Surfaces[s], dst = Mesh.Surfaces[s];
            if (src.Positions.Count == 0) continue;
            while (dst.Faces.Count < dst.Positions.Count) dst.Faces.Add(default);
            for (int i = 0; i < src.Positions.Count; i++)
            {
                dst.Positions.Add(NV.Transform(src.Positions[i], place));
                dst.Normals.Add(NV.Normalize(NV.TransformNormal(src.Normals[i], place)));
                dst.Colors.Add(src.Colors[i]);
                dst.Faces.Add(src.FaceAt(i));
            }
        }
        if (data.Triangles > 0)
        {
            Mesh.Min = NV.Min(Mesh.Min, NV.Transform(data.Min, place));
            Mesh.Max = NV.Max(Mesh.Max, NV.Transform(data.Max, place));
        }
        if (!collide) return;
        foreach (DecoBox box in data.Boxes)
        {
            NV c = NV.Transform(box.Centre, place);
            NV ax = NV.TransformNormal(box.X * box.Half.X, place), ay = NV.TransformNormal(box.Y * box.Half.Y, place), az = NV.TransformNormal(box.Z * box.Half.Z, place);
            CollisionBox(c, ax, ay, az);
        }
    }
}
