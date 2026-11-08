using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using N = System.Numerics;

namespace GalHack.Stars;

/// <summary>
/// The one source of truth for a game's sky: its galaxy, its home star, and the sky from where the
/// player is. The galaxy and the first sky are made on a worker thread as soon as the world exists
/// (nothing here may hold up the main thread); until then the sky simply has no stars. Every sky
/// material bound to it reads the same textures, and <see cref="MoveTo"/> (a jump) bakes the sky
/// from another star and swaps it in.
///
/// The galaxy's seed is the game's seed: same seed, same galaxy, whatever the planet type. The
/// home star is then found in it to fit the world (see <see cref="Galaxy.Home"/>).
/// </summary>
public sealed class GalaxySky
{
    public Galaxy Galaxy { get; private set; }

    /// <summary>The player's start system's star: the id the system model plugs in at.</summary>
    public int Home { get; private set; } = -1;

    /// <summary>The star whose sky this is now.</summary>
    public int Here { get; private set; } = -1;

    public SkyView View { get; private set; }

    /// <summary>Everything is on the graphics card: stars are showing.</summary>
    public bool Ready { get; private set; }

    public double MakeMs { get; private set; }
    public double BakeMs { get; private set; }

    /// <summary>The longest one frame spent putting the sky on the graphics card, ms.</summary>
    public double UploadMs { get; private set; }

    /// <summary>How the system's frame stands against the galaxy.</summary>
    public N.Quaternion Tilt { get; private set; } = N.Quaternion.Identity;

    /// <summary>That tilt, for a shader: the star field's directions into the catalogue's.</summary>
    public Basis Field => Basis(Tilt);

    readonly List<ShaderMaterial> _bound = new();
    Task<(Galaxy galaxy, int home)> _making;
    Task<(SkyView view, Image cells, Image stars, Godot.Collections.Array<Image> glow)> _baking;
    (SkyView view, Image cells, Image stars, Godot.Collections.Array<Image> glow)? _uploading;
    int _uploadStep;
    ImageTexture _cells, _stars;
    Cubemap _glow;

    /// <summary>The sky of a world whose star is already found (see PlanetWorld): only the sky itself is left to bake.</summary>
    public GalaxySky(Galaxy galaxy, int home, ulong seed)
    {
        Tilt = SkyFrame.Tilt(seed);
        Galaxy = galaxy;
        Home = home;
        MoveTo(home);
    }

    /// <summary>A sky with no world of its own: the catalogue is made here, on a worker thread.</summary>
    public GalaxySky(ulong seed, StarKind kind, bool inNebula)
    {
        Tilt = SkyFrame.Tilt(seed);
        ulong key = Galaxy.HomeKey(seed, kind, inNebula);
        _making = Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            Galaxy g = Galaxy.Make(seed);
            int home = g.Home(kind, key, inNebula);
            MakeMs = watch.Elapsed.TotalMilliseconds;
            return (g, home);
        });
    }

    /// <summary>Starts reading this sky (from now, or as soon as it is ready).</summary>
    public void Bind(ShaderMaterial material)
    {
        _bound.Add(material);
        if (Ready) Set(material);
    }

    /// <summary>Call once a frame: picks up whatever a worker thread has finished.</summary>
    public void Poll()
    {
        if (_making != null)
        {
            if (!_making.IsCompleted) return;
            (Galaxy, Home) = _making.Result;
            _making = null;
            MoveTo(Home);
        }
        if (_baking is { IsCompleted: true })
        {
            _uploading = _baking.Result;
            _uploadStep = 0;
            _baking = null;
        }
        if (_uploading != null) UploadStep();
    }

    /// <summary>Waits for everything (tests and screenshots, which have nothing else to do).</summary>
    public void Finish()
    {
        while (_making != null || _baking != null || _uploading != null)
        {
            _making?.Wait();
            _baking?.Wait();
            Poll();
        }
    }

    /// <summary>Bakes the sky from another star: a jump. The old sky stays up until the new one is ready.</summary>
    public void MoveTo(int star)
    {
        Galaxy g = Galaxy;
        Here = star;
        _baking = Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            SkyView v = SkyView.From(g, star);
            Image cells = Image.CreateFromData(6 * SkyView.Grid, SkyView.Grid, false, Image.Format.Rf, Bytes(v.CellTexels));
            Image stars = Image.CreateFromData(SkyView.StarsWide, v.StarRows, false, Image.Format.Rgbaf, Bytes(v.StarTexels));
            var faces = new Godot.Collections.Array<Image>();
            foreach (float[] face in v.Glow)
            {
                Image image = Image.CreateFromData(SkyView.GlowSize, SkyView.GlowSize, false, Image.Format.Rgbf, Bytes(face));
                image.Convert(Image.Format.Rgbah);
                faces.Add(image);
            }
            BakeMs = watch.Elapsed.TotalMilliseconds;
            return (v, cells, stars, faces);
        });
    }

    /// <summary>One texture a frame: the images were made on the worker, so only the copies to the card are left.</summary>
    void UploadStep()
    {
        var watch = Stopwatch.StartNew();
        var (view, cells, stars, glow) = _uploading.Value;
        switch (_uploadStep++)
        {
            case 0: _newCells = ImageTexture.CreateFromImage(cells); break;
            case 1: _newStars = ImageTexture.CreateFromImage(stars); break;
            case 2:
                _newGlow = new Cubemap();
                _newGlow.CreateFromImages(glow);
                break;
            default:
                // All of it swaps in one frame: a pixel never reads one sky's list from another's stars.
                (_cells, _stars, _glow) = (_newCells, _newStars, _newGlow);
                View = view;
                Ready = true;
                _uploading = null;
                foreach (ShaderMaterial m in _bound) Set(m);
                break;
        }
        UploadMs = Math.Max(UploadMs, watch.Elapsed.TotalMilliseconds);
    }

    ImageTexture _newCells, _newStars;
    Cubemap _newGlow;

    void Set(ShaderMaterial m)
    {
        m.SetShaderParameter("galaxy_cells", _cells);
        m.SetShaderParameter("galaxy_stars", _stars);
        m.SetShaderParameter("galaxy_glow", _glow);
        // The central black hole from this sky's place: its shadow and disc are drawn where it really is, as big as it really looks.
        Heart hole = Galaxy.Hole;
        m.SetShaderParameter("galaxy_hole", V(hole.Position - View.Eye));
        m.SetShaderParameter("galaxy_hole_axis", V(hole.Axis));
        m.SetShaderParameter("galaxy_hole_zero", V(hole.Zero));
        m.SetShaderParameter("galaxy_hole_size", new Vector4(hole.Shadow, hole.Inner, hole.Outer, View.HoleFront));
        m.SetShaderParameter("galaxy_on", true);
    }

    static Vector3 V(N.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Toward the central black hole from this sky's place, the catalogue's frame (the map, the visor and the tests look it up here).</summary>
    public N.Vector3 ToHole => View == null ? N.Vector3.UnitX : N.Vector3.Normalize(Galaxy.Hole.Position - View.Eye);

    static byte[] Bytes(float[] data)
    {
        var bytes = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>What the graphics card holds for this sky, bytes.</summary>
    public long TextureBytes => View == null ? 0 : View.CellTexels.Length * 4L + View.StarTexels.Length * 4L + 6L * SkyView.GlowSize * SkyView.GlowSize * 8L;

    // ---- Directions -----------------------------------------------------------------------------

    public static Basis Basis(N.Quaternion q) => new(new Quaternion(q.X, q.Y, q.Z, q.W));

    /// <summary>Tells a sky how wide a pixel is, so a star stays a point at any window size or field of view.</summary>
    public static void SetPixel(ShaderMaterial sky, Viewport viewport)
    {
        Camera3D camera = viewport?.GetCamera3D();
        if (camera == null) return;
        float height = viewport.GetVisibleRect().Size.Y;
        if (height > 0f) sky.SetShaderParameter("galaxy_pixel", 2f * MathF.Tan(Mathf.DegToRad(camera.Fov) * 0.5f) / height);
    }
}
