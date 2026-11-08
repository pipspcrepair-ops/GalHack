using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Wildlife;

/// <summary>One shared material per species: its pattern, accent and glow.</summary>
public static class CreatureMaterials
{
    static Shader _shader;
    static StandardMaterial3D _blur;
    static readonly ConditionalWeakTable<Species, ShaderMaterial> Cache = new();

    public static Shader Shader => _shader ??= DataFiles.Shader("creature.gdshader");

    public static ShaderMaterial For(Species s) => Cache.GetValue(s, sp =>
    {
        var m = new ShaderMaterial { Shader = Shader };
        m.SetShaderParameter("accent", sp.Colors.Accent);
        m.SetShaderParameter("glow_color", sp.Colors.Glow);
        m.SetShaderParameter("glow_strength", sp.Colors.GlowStrength > 0f ? 0.6f + sp.Colors.GlowStrength : 0f);
        m.SetShaderParameter("glow_tint", 1f);
        m.SetShaderParameter("pattern", (int)sp.Colors.Pattern);
        float width = sp.Rig == RigKind.Swarm ? sp.Body.Size : MathF.Max(0.2f, sp.Body.Width);
        m.SetShaderParameter("pattern_freq", sp.Colors.PatternScale * 2.2f / width);
        m.SetShaderParameter("veins_glow", sp.Corrupted ? 1f : 0f);
        m.SetShaderParameter("roughness_value", sp.Form == "boulder" ? 0.95f : 0.7f);
        // Pebbled hide at a scale that suits the animal: fine on a critter, coarse on a giant.
        m.SetShaderParameter("detail_freq", 48f / MathF.Sqrt(MathF.Max(0.3f, sp.Body.Size)));
        m.SetShaderParameter("detail_strength", sp.Rig == RigKind.Swarm ? 0f : 0.1f);
        // A Husk's worms reach about a third of their own length either way.
        if (sp.Husk) m.SetShaderParameter("worm_scale", WormSway(sp.Figure));
        return m;
    });

    /// <summary>How far a Husk's worm tips sway, m, by the size of its head.</summary>
    public static float WormSway(Figure f) => f.HeadRadius * 0.55f;

    /// <summary>The soft see-through fan of a wing beating too fast to see. Its colour and strength come from the mesh's vertices.</summary>
    public static StandardMaterial3D Blur => _blur ??= new StandardMaterial3D
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        Roughness = 0.6f,
        DisableReceiveShadows = true,
    };

    static ShaderMaterial _clothed;

    /// <summary>The one material every person shares: their colours are in their meshes.</summary>
    public static ShaderMaterial Clothed => _clothed ??= MakeClothed();

    static ShaderMaterial MakeClothed()
    {
        var m = new ShaderMaterial { Shader = Shader };
        m.SetShaderParameter("glow_strength", 0.6f);
        m.SetShaderParameter("glow_tint", 0f);
        m.SetShaderParameter("pattern", 0);
        m.SetShaderParameter("roughness_value", 0.82f);
        m.SetShaderParameter("detail_freq", 90f);
        m.SetShaderParameter("detail_strength", 0.05f);
        m.SetShaderParameter("rim_strength", 0.45f);
        m.SetShaderParameter("far_lift", 0.28f);
        m.SetShaderParameter("worm_scale", 0.065f); // only a Husk has worms to sway
        return m;
    }

    /// <summary>Drops the shared materials and shader on the main thread (at shutdown).</summary>
    public static void Release()
    {
        var materials = new List<ShaderMaterial>();
        foreach (var (_, material) in (IEnumerable<KeyValuePair<Species, ShaderMaterial>>)Cache) materials.Add(material);
        Cache.Clear();
        foreach (ShaderMaterial material in materials) material.Dispose();
        _shader?.Dispose();
        _shader = null;
        _blur?.Dispose();
        _blur = null;
        _clothed?.Dispose();
        _clothed = null;
        ProceduralAudio.ReleaseHum();
    }

    /// <summary>A plain vertex-colour material for plants and props, glowing in their own colours.</summary>
    public static ShaderMaterial Plain(float glow)
    {
        var m = new ShaderMaterial { Shader = Shader };
        m.SetShaderParameter("glow_strength", glow);
        m.SetShaderParameter("glow_tint", 0f);
        m.SetShaderParameter("pattern", 0);
        m.SetShaderParameter("roughness_value", 0.85f);
        return m;
    }
}

/// <summary>
/// Draws one creature: a mesh per rig piece, posed straight from the rig every physics tick
/// (physics interpolation smooths it on fast screens). Pieces are direct children of a node at
/// the origin, so a pose is just a local transform. Swarms are one MultiMesh.
/// </summary>
public partial class CreatureView : Node3D
{
    public Creature Creature { get; private set; }

    /// <summary>How much of it shows (0..1) as the view last faded it, and how far it has come in since it was built (WildlifeView.Fader).</summary>
    public float Shows = 1f, Arrived = 1f;
    readonly List<MeshInstance3D> _parts = new();
    MultiMeshInstance3D _swarm;
    float _hurtShown, _wakeShown;
    AudioStreamPlayer3D _hum;
    /// <summary>How near the listener has to be for a hoverer's hum to play at all, m.</summary>
    const float HumReach = 24f;

    public int NodeCount => _swarm != null ? 1 : _parts.Count;

    public void Build(Creature c)
    {
        Creature = c;
        Name = $"Creature{c.Id}";
        ShaderMaterial material = CreatureMaterials.For(c.Species);
        float range = c.Species.Plan == BodyPlan.Giant ? 600f : c.Species.Body.Size < 0.8f ? 110f : 220f;
        if (c.Species.InSpace) range = MathF.Max(400f, c.Species.Body.Size * 45f); // nothing hides a leviathan in open space

        if (c.Rig is SwarmRig swarm)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = MeshBuilder.For(c.Rig.Layout.Pieces[0]),
                InstanceCount = swarm.Members.Length,
            };
            _swarm = new MultiMeshInstance3D
            {
                Multimesh = mm,
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = range,
            };
            AddChild(_swarm);
        }
        else
        {
            foreach (Piece piece in c.Rig.Layout.Pieces)
            {
                Mesh mesh = MeshBuilder.For(piece);
                var mi = new MeshInstance3D
                {
                    Mesh = mesh,
                    MaterialOverride = piece.Blur ? CreatureMaterials.Blur : material,
                    CastShadow = piece.Shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                    // The small pieces go first: a body keeps its shape from afar and draws a fraction of the meshes.
                    VisibilityRangeEnd = MathF.Min(range, 36f + 160f * mesh.GetAabb().GetLongestAxisSize()),
                };
                AddChild(mi);
                _parts.Add(mi);
            }
            if (c.Species.Flight == FlightKind.Hover)
            {
                _hum = new AudioStreamPlayer3D
                {
                    Stream = ProceduralAudio.Hum,
                    UnitSize = 1.4f,
                    MaxDistance = HumReach,
                    VolumeDb = -9f,
                    PitchScale = Math.Clamp(0.34f / c.Species.Body.Size, 0.8f, 1.4f) * (0.96f + 0.08f * (c.Id % 5) / 4f),
                    Bus = "Master",
                };
                AddChild(_hum);
            }
        }
        Sync();
        ResetPhysicsInterpolation();
        foreach (var p in _parts) p.ResetPhysicsInterpolation();
    }

    /// <summary>A hoverer's hum runs while it is up and someone is near enough to hear; nothing plays for the rest.</summary>
    public void Listen(float distance)
    {
        if (_hum == null) return;
        Creature c = Creature;
        bool humming = !c.Landed && !c.Dead && distance < HumReach && c.Space == null; // inside a place nothing makes a constant noise
        if (humming) _hum.GlobalPosition = c.Position;
        if (humming != _hum.Playing)
        {
            if (humming) _hum.Play();
            else _hum.Stop();
        }
    }

    /// <summary>Whether one creature's built meshes fit another's rig: the same species (a person is a species of one).</summary>
    public static bool SameBody(Creature a, Creature b) => a != null && b != null && ReferenceEquals(a.Species, b.Species);

    /// <summary>The same body for a creature carried into a new sim: its meshes stay, so nothing is built or freed. False if it isn't the same body.</summary>
    public bool Rebind(Creature c)
    {
        // Only its own body: another of its kind (another person, the other sex) has other proportions,
        // and its meshes on this rig would leave gaps at every joint.
        if (_swarm != null || !SameBody(Creature, c) || c.Rig.Layout.Pieces.Count != _parts.Count) return false;
        Creature = c;
        Sync();
        ResetPhysicsInterpolation();
        foreach (var p in _parts) p.ResetPhysicsInterpolation();
        return true;
    }

    /// <summary>Copies the rig's pose onto the meshes.</summary>
    public void Sync()
    {
        Creature c = Creature;
        if (_swarm != null)
        {
            var swarm = (SwarmRig)c.Rig;
            MultiMesh mm = _swarm.Multimesh;
            mm.VisibleInstanceCount = swarm.Alive;
            for (int i = 0; i < swarm.Alive; i++) mm.SetInstanceTransform(i, new Transform3D(swarm.MemberBasis(i), swarm.Members[i]));
            return;
        }

        Transform3D[] pose = c.Rig.Pose;
        if (c.Tumble.IsEqualApprox(Quaternion.Identity))
            for (int i = 0; i < _parts.Count; i++) _parts[i].Transform = pose[i];
        else
        {
            // Thrown or downed: the whole body turned about its middle, whatever its rig does.
            Vector3 middle = c.Position + Vector3.Up * c.Species.Body.Height * 0.5f;
            var turned = new Transform3D(new Basis(c.Tumble), middle) * new Transform3D(Basis.Identity, -middle);
            for (int i = 0; i < _parts.Count; i++) _parts[i].Transform = turned * pose[i];
        }

        // Only touch the per-instance hurt flash when it changes.
        float hurt = MathF.Round(c.HurtFlash * 10f) / 10f;
        if (MathF.Abs(hurt - _hurtShown) > 0.01f)
        {
            _hurtShown = hurt;
            foreach (MeshInstance3D p in _parts) p.SetInstanceShaderParameter("hurt", hurt);
        }
        // A lurker's eyes opening in the dark.
        float wake = MathF.Round(c.Wake * 10f) / 10f;
        if (MathF.Abs(wake - _wakeShown) > 0.01f)
        {
            _wakeShown = wake;
            foreach (MeshInstance3D p in _parts) p.SetInstanceShaderParameter("wake", wake);
        }
        // A Husk's worms reaching for whatever moves near (only its head has any).
        float stir = MathF.Round(c.Stir * 10f) / 10f;
        if (MathF.Abs(stir - _stirShown) > 0.01f)
        {
            _stirShown = stir;
            foreach (MeshInstance3D p in _parts) p.SetInstanceShaderParameter("worm_stir", stir);
        }
    }

    float _stirShown;
}

/// <summary>
/// Keeps a CreatureView for every creature in the simulation: creates views for newcomers,
/// frees views of the removed, and syncs poses. Distant creatures re-pose less often (their
/// rigs update every few frames) and very distant ones are hidden, which is most of what keeps
/// a planet full of animals cheap on a low-end PC.
/// </summary>
public partial class WildlifeView : Node3D
{
    WildSim _sim;
    readonly Dictionary<Creature, CreatureView> _views = new();
    readonly List<Creature> _gone = new();

    public float NearDistance = 60f, MidDistance = 120f, FarDistance = 220f;
    /// <summary>Where heavy footfalls are heard; left out, they aren't.</summary>
    public ProceduralAudio Audio;
    /// <summary>The lightest thing heard walking from any way off, kg, and how far.</summary>
    const float HeavyStep = 180f, StepHeard = 45f;

    public int NodeCount
    {
        get
        {
            int n = 0;
            foreach (var v in _views.Values) n += v.NodeCount;
            return n;
        }
    }

    public int ViewCount => _views.Count;

    /// <summary>The most new bodies built in a frame (a whole herd coming into view is spread over a few).</summary>
    public int MostNew = int.MaxValue;

    /// <summary>A creature's body came into view or went (built in view, or crossing the far distance), how far off, and how much of it showed or went in that frame (measurements).</summary>
    public System.Action<Creature, float, bool, float> Shown;

    /// <summary>
    /// Shows part of a body (0..1) through a dissolve, and 1 all of it again: the game's fade (left
    /// out, bodies show or go whole). With it, a body dissolves in over <see cref="ArriveSeconds"/>
    /// once built and out over the last <see cref="FadeBand"/> metres before the far distance.
    /// </summary>
    public System.Action<Node, float> Fader;
    const float ArriveSeconds = 0.6f, FadeBand = 30f, MostFadeStep = 0.08f;
    double _lastSync = -1;

    /// <summary>Takes up a new sim. Creatures carried into it from the last (old to new) keep their bodies.</summary>
    public void Bind(WildSim sim, Node unused, IReadOnlyDictionary<Creature, Creature> carried = null)
    {
        if (carried != null)
        {
            var kept = new List<(Creature, CreatureView)>();
            foreach (var (c, v) in _views)
            {
                if (carried.TryGetValue(c, out Creature next) && v.Rebind(next)) kept.Add((next, v));
                else v.QueueFree();
            }
            _views.Clear();
            foreach (var (c, v) in kept) _views[c] = v;
        }
        else Clear();
        _sim = sim;
    }

    public void Clear()
    {
        foreach (var v in _views.Values) v.QueueFree();
        _views.Clear();
    }

    public CreatureView ViewOf(Creature c) => _views.GetValueOrDefault(c);

    public void Sync(Vector3 camera)
    {
        if (_sim == null) return;
        int built = 0;
        double now = Time.GetTicksMsec() / 1000.0;
        float dt = _lastSync < 0 ? 0f : (float)Math.Min(now - _lastSync, 0.1);
        _lastSync = now;
        foreach (Creature c in _sim.Creatures)
        {
            if (c.Removed) continue;
            bool fresh = false;
            if (!_views.TryGetValue(c, out CreatureView view))
            {
                if (built >= MostNew) continue;
                built++;
                fresh = true;
                view = new CreatureView();
                AddChild(view);
                view.Build(c);
                _views[c] = view;
            }

            float d = WildSim.Flat(c.Position - camera).Length();
            float far = c.Species.Plan == BodyPlan.Giant ? FarDistance * 2.5f : FarDistance;
            c.RigInterval = d < NearDistance ? 1 : d < MidDistance ? 2 : d < far ? 4 : 12;
            bool visible = d < far;
            float shows = visible ? 1f : 0f, was = view.Shows;
            if (Fader != null)
            {
                // Built in sight: it dissolves in. Near the far distance: it dissolves out by distance.
                if (fresh) view.Arrived = visible ? 0f : 1f;
                else if (view.Arrived < 1f) view.Arrived = Math.Min(1f, view.Arrived + dt / ArriveSeconds);
                shows = Math.Min(Math.Clamp((far - d) / FadeBand, 0f, 1f), view.Arrived);
                // Never more than a step of the dissolve a frame, however long the frame or fast the eye
                // (a flyer crosses the fade band in a few frames): it always takes a dozen frames or so.
                if (!fresh) shows = Math.Clamp(shows, was - MostFadeStep, was + MostFadeStep);
                visible = shows > 0f;
                if (visible && (shows != view.Shows || fresh)) Fader(view, shows);
                view.Shows = shows;
            }
            if (fresh ? visible : view.Visible != visible) Shown?.Invoke(c, d, visible, visible ? shows : Fader != null ? was : 1f);
            if (view.Visible != visible) view.Visible = visible;
            if (visible && (c.RigInterval == 1 || c.RigPending == 0f)) view.Sync();
            view.Listen(d);
            if (Audio != null && d < StepHeard && c.Rig.Steps != c.StepsHeard)
            {
                c.StepsHeard = c.Rig.Steps;
                if (Heft.Mass(c.Species) >= HeavyStep) Audio.Footfall(c);
            }
        }

        _gone.Clear();
        foreach (var (c, v) in _views)
            if (c.Removed)
            {
                v.QueueFree();
                _gone.Add(c);
            }
        foreach (Creature c in _gone) _views.Remove(c);
    }
}
