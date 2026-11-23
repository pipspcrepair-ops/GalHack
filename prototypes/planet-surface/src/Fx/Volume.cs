using Godot;

namespace PlanetSurface.Fx;

/// <summary>
/// One body of something the light passes through, drawn on any renderer by stepping through moving
/// 3D noise inside a box round it (shaders/volume.gdshader): a dust storm now, fog banks and close
/// clouds later. Whoever owns it sets its shape, its look and its light each frame; nothing in here
/// knows what it is a body of.
/// </summary>
public partial class Volume : MeshInstance3D
{
    /// <summary>Drawn from outside its box (its near faces, depth-tested, so no halo round what stands in front) and from inside it.</summary>
    static readonly Shader Outside = GD.Load<Shader>("res://shaders/volume.gdshader"), Within = GD.Load<Shader>("res://shaders/volume_inside.gdshader");
    readonly ShaderMaterial _draw = new() { Shader = Outside };

    /// <summary>The heaps every volume steps through: round smooth lobes (inverted cell distance), made once, tiling.</summary>
    static NoiseTexture3D _heaps;
    static NoiseTexture3D Heaps => _heaps ??= new NoiseTexture3D
    {
        Width = 64, Height = 64, Depth = 64, Seamless = true, Invert = true,
        Noise = new FastNoiseLite
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 1f / 16f, CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance,
            FractalType = FastNoiseLite.FractalTypeEnum.None, Seed = 7,
        },
    };

    public Volume()
    {
        Mesh = new BoxMesh { Size = Vector3.One };
        MaterialOverride = _draw;
        CastShadow = ShadowCastingSetting.Off;
        GIMode = GIModeEnum.Disabled;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _draw.SetShaderParameter(Names.Of("heaps"), Heaps);
    }

    /// <summary>
    /// Where it stands and its shape: the middle of its footprint, the way its leading side faces (x, z),
    /// half its footprint along that way and across it, its floor and its tallest top, and how much lower
    /// its trailing side stands (0 level, 1 down to half). Its heaps push the outline out by up to <paramref name="billow"/>.
    /// </summary>
    public void Shape(Vector2 centre, Vector2 axis, Vector2 radii, float floor, float height, float frontRise, float billow)
    {
        var across = new Vector3(-axis.Y, 0f, axis.X);
        var along = new Vector3(axis.X, 0f, axis.Y);
        Vector3 size = new(2f * (radii.X + billow), height + billow, 2f * (radii.Y + billow));
        GlobalTransform = new Transform3D(new Basis(along * size.X, Vector3.Up * size.Y, across * size.Z), new Vector3(centre.X, floor + size.Y * 0.5f, centre.Y));
        Uniform("centre", centre);
        Uniform("axis", axis);
        Uniform("radii", radii);
        Uniform("base_y", floor);
        Uniform("height", height);
        Uniform("front_rise", frontRise);
        Uniform("billow", billow);
    }

    /// <summary>
    /// What it is like: its colour; its extinction per metre deep inside and the share left at its top;
    /// how far in from its outline it thickens; the size of its heaps, how they drift and climb, m/s;
    /// and how much of it there is, 0 to 1.
    /// </summary>
    public void Look(Color albedo, float density, float topFade, float edge, float grain, Vector3 flow, float rise, float strength)
    {
        Uniform("albedo", albedo);
        Uniform("density", density);
        Uniform("top_fade", topFade);
        Uniform("edge", edge);
        Uniform("grain", grain);
        Uniform("flow", flow);
        Uniform("rise", rise);
        Uniform("strength", strength);
    }

    /// <summary>
    /// The way to the light that shades it (the engine lights it by its own lights and sky, as it lights the land), and the
    /// environment fog at the eye, which is already drawn over everything; and where the eye is (in its box or out of it).
    /// </summary>
    public void Light(Vector3 toLight, Color fog, float fogDensity, int steps, Vector3 eye)
    {
        // Inside its box (with the near plane's breadth to spare) its near faces are behind the eye: drawn on its far ones.
        Vector3 local = GlobalTransform.AffineInverse() * eye;
        Shader shader = Mathf.Max(Mathf.Abs(local.X), Mathf.Max(Mathf.Abs(local.Y), Mathf.Abs(local.Z))) < 0.5f + 1e-3f ? Within : Outside;
        if (_draw.Shader != shader) _draw.Shader = shader;
        Uniform("to_sun", toLight);
        Uniform("fog_color", fog);
        Uniform("fog_floor", fogDensity);
        Uniform("steps", steps);
    }

    void Uniform(string name, Variant value) => _draw.SetShaderParameter(Names.Of(name), value);
}
