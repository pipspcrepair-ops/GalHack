using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.World;

namespace PlanetSurface.Terrain;

/// <summary>
/// What a world's cliffs are made of, for rock.gdshaderinc. Nothing is picked by the world's theme: the
/// beds take the world's own rock and what is in it, its commonest minerals (the deposit map) and its
/// soil; weathering streaks are desert varnish where the land is dry and damp stains where it is wet; what
/// grows on rock is the colour its plants grow, as much as the air is humid, and none where nothing lives.
/// How a face is shaped is its landform's by rule (<see cref="FaceOf"/>): a mesa is banded beds under a
/// caprock, a canyon wall banded and fluted, a sea cliff jointed over a wet foot, a mountain crag all
/// joints and blocks, karst fluted, badlands thin-bedded and gullied. One texel every planet/256 holds the
/// blend of the region's landforms, so a face changes as the land does.
/// </summary>
public sealed class RockLook
{
    public const int TexWidth = 256, TexHeight = 128;

    public readonly Rgb Base, Pale, Dark, Mineral, Mineral2, Soil, Varnish, Life;
    public readonly float LifeAmount;
    public readonly ImageTexture Style, Beds;

    static readonly ConditionalWeakTable<PlanetWorld, RockLook> Made = new();

    /// <summary>The world's rock look, made once.</summary>
    public static RockLook Of(PlanetWorld world) => Made.GetValue(world, w => new RockLook(w));

    /// <summary>Sets a terrain material's rock uniforms. <paramref name="cliffInfo"/>: its meshes' UV2 is the cliff (voxels, far rings).</summary>
    public static void Apply(ShaderMaterial m, PlanetWorld world, bool cliffInfo)
    {
        RockLook r = Of(world);
        static Color C(Rgb c) => new(c.R, c.G, c.B);
        m.SetShaderParameter("rock_base", C(r.Base));
        m.SetShaderParameter("rock_pale", C(r.Pale));
        m.SetShaderParameter("rock_dark", C(r.Dark));
        m.SetShaderParameter("rock_mineral", C(r.Mineral));
        m.SetShaderParameter("rock_mineral2", C(r.Mineral2));
        m.SetShaderParameter("rock_soil", C(r.Soil));
        m.SetShaderParameter("rock_varnish", C(r.Varnish));
        m.SetShaderParameter("rock_life", C(r.Life));
        m.SetShaderParameter("rock_life_amount", r.LifeAmount);
        m.SetShaderParameter("rock_style", r.Style);
        m.SetShaderParameter("rock_beds", r.Beds);
        m.SetShaderParameter("rock_style_on", 1f);
        m.SetShaderParameter("rock_uv2", cliffInfo ? 1f : 0f);
    }

    RockLook(PlanetWorld world)
    {
        WorldMap map = world.Map;
        GroundPalette pal = world.Palette;
        Base = pal.Rock;

        // The land's own soil and how humid its air is, over the land.
        Rgb soil = default;
        float humid = 0f;
        int land = 0;
        for (int j = 0; j < map.Height; j += 4)
        for (int i = 0; i < map.Width; i += 4)
        {
            int k = j * map.Width + i;
            if (map.Elevation[k] <= 0f) continue;
            soil += pal.SoilAt(map.CellX(i), map.CellY(j), out _);
            humid += map.Humidity[k];
            land++;
        }
        soil = land > 0 ? soil * (1f / land) : pal.BareSoil;
        humid = land > 0 ? humid / land : 0.5f;

        // Its two commonest minerals, by how much of each lies in the ground.
        var amount = new Dictionary<DepositKind, float>();
        if (map.Deposits != null)
            foreach (Deposit d in map.Deposits.All()) amount[d.Kind] = amount.GetValueOrDefault(d.Kind) + d.Amount;
        var kinds = new List<KeyValuePair<DepositKind, float>>(amount);
        kinds.Sort((a, b) => b.Value.CompareTo(a.Value));
        Rgb first = kinds.Count > 0 ? DepositLook.Color(kinds[0].Key) : Base;
        Rgb second = kinds.Count > 1 ? DepositLook.Color(kinds[1].Key) : Rgb.Lerp(Base, soil, 0.5f);

        // A pale bed is the rock bleached toward its own sand: lighter and greyer, never brighter in hue.
        Pale = Rgb.Lerp(Base, soil, 0.3f).Saturate(0.6f) * 1.14f + new Rgb(0.03f, 0.03f, 0.03f);
        Dark = Base * 0.68f;
        Mineral = Rgb.Lerp(Base, first, 0.28f);
        Mineral2 = Rgb.Lerp(Base, second, 0.2f);
        Soil = Rgb.Lerp(Base, soil, 0.4f);

        // Life: the colour the world's plants grow (lichen, moss), as much as its air is humid.
        GalHack.Plants.Tone? verdure = pal.Flora?.Book.Verdure;
        Life = verdure is GalHack.Plants.Tone v ? Rgb.Lerp(new Rgb(v.R, v.G, v.B), Base, 0.35f) : Base;
        LifeAmount = verdure == null ? 0f : Math.Clamp((humid - 0.25f) * 1.4f, 0f, 0.8f);
        // Weathering down a face: dry, a dark varnish of the rock's own iron and manganese; wet, damp and algae.
        Rgb dry = Rgb.Lerp(Base * 0.32f, first * 0.4f, 0.3f);
        Rgb wet = Rgb.Lerp(Base * 0.42f, Life * 0.35f, LifeAmount);
        Varnish = Rgb.Lerp(dry, wet, Math.Clamp((humid - 0.3f) * 2f, 0f, 1f));

        (Style, Beds) = Textures(world, pal.Strata);
    }

    /// <summary>A landform's faces (strata, flutes, joints, caprock) and the thickness of its beds, m.</summary>
    public static (Vector4 Style, float Beds) FaceOf(Landform l) => l switch
    {
        Landform.Mesas => (new Vector4(0.9f, 0.55f, 0.3f, 1f), 2.6f),
        Landform.Plateau => (new Vector4(0.75f, 0.4f, 0.35f, 0.8f), 3f),
        Landform.Canyonlands => (new Vector4(0.95f, 0.7f, 0.3f, 0.55f), 2.2f),
        Landform.Badlands => (new Vector4(1f, 0.85f, 0.15f, 0.4f), 0.7f),
        Landform.RiverValleys => (new Vector4(0.6f, 0.35f, 0.3f, 0.25f), 2f),
        Landform.CoastalCliffs => (new Vector4(0.6f, 0.35f, 0.6f, 0.15f), 1.4f),
        Landform.Mountains => (new Vector4(0.15f, 0.2f, 0.9f, 0f), 3.5f),
        Landform.Fjords => (new Vector4(0.1f, 0.35f, 0.85f, 0f), 3.5f),
        Landform.Hills => (new Vector4(0.3f, 0.25f, 0.4f, 0f), 2.5f),
        Landform.RockyDesert => (new Vector4(0.25f, 0.35f, 0.55f, 0f), 3f),
        Landform.Karst => (new Vector4(0.45f, 1f, 0.4f, 0f), 1.8f),
        Landform.VolcanicField => (new Vector4(0.45f, 0.15f, 0.5f, 0.2f), 2.5f),
        Landform.BasaltPlains => (new Vector4(0.5f, 0.1f, 0.95f, 0.3f), 3.2f),
        Landform.ShardFields => (new Vector4(0.05f, 0f, 1f, 0f), 4f),
        Landform.LakeDistrict => (new Vector4(0.2f, 0.2f, 0.65f, 0f), 3f),
        Landform.IcePlain => (new Vector4(0.2f, 0.2f, 0.5f, 0f), 3f),
        Landform.DomeFields => (new Vector4(0.2f, 0.3f, 0.2f, 0f), 2f),
        Landform.Ringlands => (new Vector4(0.5f, 0.2f, 0.4f, 0f), 2f),
        Landform.DuneSea => (new Vector4(0.4f, 0.2f, 0.2f, 0f), 2f),
        Landform.Archipelago => (new Vector4(0.35f, 0.25f, 0.45f, 0f), 2f),
        _ => (new Vector4(0.3f, 0.2f, 0.3f, 0f), 2.5f),
    };

    /// <summary>
    /// The style map (RGBA: strata, flutes, joints, caprock) and the beds map (R: a slow height offset of
    /// the beds across the land, a dip of a few metres a kilometre; G: their thickness), one texel every
    /// planet/256, from the blend of landforms there. The world's rock bands more or less by its kind
    /// (<paramref name="strata"/>, the palette's): a desert's sandstone more than a temperate world's.
    /// </summary>
    static (ImageTexture style, ImageTexture beds) Textures(PlanetWorld world, float strata)
    {
        WorldMap map = world.Map;
        var style = new byte[TexWidth * TexHeight * 4];
        var beds = new byte[TexWidth * TexHeight * 2];
        double sx = map.WidthMeters / TexWidth, sz = map.HeightMeters / TexHeight;
        var dip = new Fractal(Hash.Of(map.Spec.Seed, 0x524F434B), 3200, 2, 0.5f);
        float banding = 0.55f + 0.6f * strata;
        Parallel.For(0, TexHeight, j =>
        {
            for (int i = 0; i < TexWidth; i++)
            {
                double x = (i + 0.5) * sx, z = (j + 0.5) * sz;
                LandformBlend blend = map.LandformsAt(x, z);
                Vector4 s = Vector4.Zero;
                float thick = 0f, total = 0f;
                for (int b = 0; b < blend.Count; b++)
                {
                    var (look, t) = FaceOf(blend.LandformAt(b));
                    float w = blend.WeightAt(b);
                    s += look * w;
                    thick += t * w;
                    total += w;
                }
                if (total <= 0f) (s, thick, total) = (FaceOf(Landform.Plains).Style, 2.5f, 1f);
                s /= total;
                thick /= total;
                s.X = Math.Clamp(s.X * banding, 0f, 1f);
                int p = (j * TexWidth + i) * 4;
                style[p] = Rgb.Byte(s.X);
                style[p + 1] = Rgb.Byte(s.Y);
                style[p + 2] = Rgb.Byte(s.Z);
                style[p + 3] = Rgb.Byte(s.W);
                int q = (j * TexWidth + i) * 2;
                beds[q] = Rgb.Byte(0.5f + 0.5f * Math.Clamp(dip.Fbm(map.Model.Torus.At(x, z), sx), -1f, 1f));
                beds[q + 1] = Rgb.Byte((thick - 0.5f) / 3.5f);
            }
        });
        return (ImageTexture.CreateFromImage(Image.CreateFromData(TexWidth, TexHeight, false, Image.Format.Rgba8, style)),
            ImageTexture.CreateFromImage(Image.CreateFromData(TexWidth, TexHeight, false, Image.Format.Rg8, beds)));
    }
}
