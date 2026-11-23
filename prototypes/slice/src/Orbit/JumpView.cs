using System;
using Godot;
using Slice.Game;

namespace Slice.Orbit;

/// <summary>
/// What a jump looks and sounds like, all drawn and synthesised: while the drive spools, streaks gather round the view
/// and its hum climbs; in the tunnel they pour out of the point ahead (the way it travels) and rush outward and past, as
/// stars streak past a ship going forward, and nothing outside the hull shows, over a steady roar; on arrival a flash and a
/// thump, and the view clears onto where it arrived. A sphere round the camera,
/// drawn past the hull (the cabin stands in front of it), and one synthesised voice.
/// </summary>
public partial class JumpView : Node3D
{
    public Spaceflight Flight;

    MeshInstance3D _tunnel;
    ShaderMaterial _material;
    ThrusterPlayer _player;
    readonly WarpVoice _voice = new();
    Jump _shown;
    float _cover, _flash, _streaks;

    /// <summary>The tunnel's radius round the eye, m: past any cabin, inside the world.</summary>
    const float Radius = 60f;

    const string Source = """
        shader_type spatial;
        render_mode unshaded, cull_front, depth_draw_never, shadows_disabled, fog_disabled;
        uniform float amount = 0.0;
        uniform float cover = 0.0;
        uniform float flash = 0.0;
        uniform float phase = 0.0; // how far the jump has flown, growing
        varying vec3 dir;
        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
        void vertex() {
            dir = VERTEX; // the sphere's own frame: -z is the pilot's forward
        }
        void fragment() {
            vec3 d = normalize(dir);
            // Stars on a tube round the way ahead: one standing a fixed way out and z ahead is seen at cot(angle) = z. Going
            // forward, z falls at a steady rate: each star is born small at the point ahead and rushes outward, faster and
            // longer as it nears the edge of the view, and on past the camera.
            float z = -d.z / max(length(d.xy), 1e-3);
            float around = atan(d.x, d.y) / 6.2831853 + 0.5;
            float lanes = 120.0;
            float lane = floor(around * lanes);
            float across = fract(around * lanes);
            float r = hash(vec2(lane, 3.7));
            float run = z * (0.8 + 0.6 * r) + phase * (0.7 + 0.6 * r) + r * 17.0; // a star's z falls as the phase grows
            float cell = floor(run), f = fract(run);
            float there = step(0.55, hash(vec2(lane * 1.7, cell)));
            float len = mix(0.18, 0.5, hash(vec2(cell * 0.31, lane + 7.0)));
            float from = hash(vec2(lane + 3.3, cell * 1.9)) * (1.0 - len);
            float t = (f - from) / len; // 0 at its outer end (the head, leading the way out), 1 at its tail toward the point ahead
            float head = there * step(0.0, t) * step(t, 1.0) * (1.0 - t) * (1.0 - t);
            float width = smoothstep(0.0, 0.5, 1.0 - abs(across - 0.5) * 2.0);
            float shown = smoothstep(14.0, 4.0, z) * smoothstep(-6.0, -0.5, z); // born faint just off the point ahead, gone well behind
            float streak = head * width * shown;
            float ahead = clamp(0.5 - 0.5 * d.z, 0.0, 1.0);
            vec3 deep = mix(vec3(0.004, 0.006, 0.02), vec3(0.03, 0.05, 0.16), ahead * ahead);
            vec3 lit = mix(vec3(0.45, 0.65, 1.0), vec3(0.95, 0.97, 1.0), r) * 2.6;
            ALBEDO = deep * cover + lit * streak * amount + vec3(1.6, 1.7, 2.0) * flash;
            ALPHA = clamp(max(cover, max(streak * amount, flash)), 0.0, 1.0);
        }
        """;

    public override void _Ready()
    {
        ProcessPriority = 200; // after the camera has been placed this frame
        _material = TunnelMaterial();
        _tunnel = new MeshInstance3D
        {
            Name = "Tunnel", Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 }, MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Scale = Vector3.One * Radius, TopLevel = true,
        };
        AddChild(_tunnel);
        _player = new ThrusterPlayer { Name = "WarpVoice", Synth = _voice, Rate = WarpVoice.Rate, Db = -7f };
        AddChild(_player);
        // Drawn once, invisibly, while the game loads: its shader is compiled then, not on the first spool.
        _material.SetShaderParameter("amount", 0.0f);
    }

    int _drawnOnce;
    float _phase;

    /// <summary>How fast the streaks run, phase a second: gathering with the spool, then the tunnel's rush (a star's run a frame stays well under half its spacing, so none reads backwards).</summary>
    public static float PhaseRate(float spool, bool tunnel) => tunnel ? 6f : 0.5f + 2.5f * spool;

    /// <summary>The tunnel's look; its axis (-z) is the pilot's forward, and <c>phase</c> grows with the streaks' run.</summary>
    public static ShaderMaterial TunnelMaterial() => new() { Shader = new Shader { Code = Source } };

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Camera3D eye = Flight?.Game?.Camera;
        Jump j = Flight?.Jumping;
        if (_drawnOnce < 3) _drawnOnce++;
        if (eye == null || !eye.IsInsideTree())
        {
            _tunnel.Visible = _drawnOnce < 3;
            return;
        }
        if (j != _shown && j?.Running == true)
        {
            _shown = j;
            _flash = 0f;
        }
        float spool = 0f;
        bool tunnel = false;
        if (_shown != null)
        {
            switch (_shown.Now)
            {
                case Jump.Stage.Spool:
                    spool = (float)_shown.Spooled;
                    break;
                case Jump.Stage.Tunnel:
                case Jump.Stage.Arriving:
                    tunnel = true;
                    break;
                case Jump.Stage.Done:
                    _flash = 1f;
                    _voice.Thump();
                    _shown = null;
                    break;
                default:
                    _shown = null;
                    break;
            }
        }
        _voice.Spool = spool;
        _voice.InTunnel = tunnel;
        float streaksTo = tunnel ? 1f : spool * spool * 0.45f, coverTo = tunnel ? 1f : 0f;
        _streaks += (streaksTo - _streaks) * Math.Min(1f, dt * (tunnel ? 6f : 3f));
        _cover += (coverTo - _cover) * Math.Min(1f, dt * (tunnel ? 3.5f : 2.2f));
        _flash = MathF.Max(0f, _flash - dt * 1.8f);
        bool shown = _streaks > 0.003f || _cover > 0.003f || _flash > 0.003f || _drawnOnce < 3;
        _tunnel.Visible = shown;
        if (!shown) return;
        // Round the eye, its axis the pilot's forward (Vessel.PilotBasis): the ship flies on forward through the tunnel, so
        // from the seat the streaks pour out of the point ahead of the nose, wherever what it goes to lies (it arrives facing
        // that). Toward the target instead, a target behind the pilot ran them into the view ahead.
        Jump going = _shown ?? Flight.Jumping;
        Basis axis = going?.Carrier is { Node: { } hull } carrier && IsInstanceValid(hull) ? (hull.GlobalBasis * carrier.PilotBasis).Orthonormalized() : eye.GlobalBasis.Orthonormalized();
        _tunnel.GlobalTransform = new Transform3D(axis.Scaled(Vector3.One * Radius), eye.GlobalPosition);
        _material.SetShaderParameter("amount", _streaks);
        _material.SetShaderParameter("cover", _cover);
        _material.SetShaderParameter("flash", _flash);
        _phase = (_phase + dt * PhaseRate(spool, tunnel)) % 4096f;
        _material.SetShaderParameter("phase", _phase);
    }
}

/// <summary>
/// The warp drive's voice, sample by sample: the spool a hum and a hiss climbing together, the tunnel a steady roar with a
/// slow beat in it, the arrival a falling thump. Pure arithmetic; it glides between them and never clicks.
/// </summary>
public sealed class WarpVoice : ISynth
{
    public const int Rate = 22050;

    /// <summary>The spool's share, 0 to 1 (0: not spooling).</summary>
    public volatile float Spool;

    /// <summary>In the tunnel.</summary>
    public volatile bool InTunnel;

    volatile int _thumps;
    int _heard;
    uint _rng = 0x9E3779B9u;
    float _level, _pitch = 50f, _low, _band, _thump, _thumpPitch;
    double _a, _b, _c, _d, _e;

    /// <summary>An arrival: one thump.</summary>
    public void Thump() => _thumps++;

    float Noise()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng >> 8) * (2f / 16777216f) - 1f;
    }

    public void Render(float[] mono, int count, int offset = 0)
    {
        float spool = Math.Clamp(Spool, 0f, 1f);
        bool tunnel = InTunnel;
        float levelTo = tunnel ? 0.55f : spool > 0f ? 0.12f + 0.4f * spool : 0f;
        float pitchTo = tunnel ? 72f : 48f + 170f * spool * MathF.Sqrt(spool);
        float bright = tunnel ? 0.08f : 0.02f + 0.1f * spool;
        if (_thumps != _heard)
        {
            _heard = _thumps;
            _thump = 1f;
            _thumpPitch = 95f;
        }
        const float dt = 1f / Rate;
        for (int i = 0; i < count; i++)
        {
            _level += (levelTo - _level) * 0.00025f;
            _pitch += (pitchTo - _pitch) * 0.0004f;
            _e += 0.7 * dt * Math.Tau;
            float beat = tunnel ? 0.75f + 0.25f * MathF.Sin((float)_e) : 1f;
            _a += _pitch * dt * Math.Tau;
            _b += _pitch * 1.503 * dt * Math.Tau;
            _c += _pitch * 0.5 * dt * Math.Tau;
            float tone = MathF.Sin((float)_a) * 0.5f + MathF.Sin((float)_b) * 0.22f + MathF.Sin((float)_c) * 0.4f;
            float n = Noise();
            _low += (n - _low) * bright;
            _band += (_low - _band) * 0.02f;
            float hiss = (_low - _band) * 2.2f;
            float s = _level * beat * (tone * 0.6f + hiss * 0.8f);
            if (_thump > 0.0005f)
            {
                _thumpPitch = MathF.Max(32f, _thumpPitch * 0.99993f);
                _d += _thumpPitch * dt * Math.Tau;
                s += _thump * (MathF.Sin((float)_d) * 0.9f + n * 0.25f * _thump);
                _thump *= 0.99975f;
            }
            if (_a > 1e6) _a -= Math.Tau * Math.Floor(_a / Math.Tau);
            if (_b > 1e6) _b -= Math.Tau * Math.Floor(_b / Math.Tau);
            if (_c > 1e6) _c -= Math.Tau * Math.Floor(_c / Math.Tau);
            if (_d > 1e6) _d -= Math.Tau * Math.Floor(_d / Math.Tau);
            if (_e > 1e6) _e -= Math.Tau * Math.Floor(_e / Math.Tau);
            mono[offset + i] = Math.Clamp(s, -1f, 1f);
        }
    }
}
