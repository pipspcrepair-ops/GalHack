using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Wildlife;

/// <summary>One shared material per species: its pattern, accent and glow.</summary>
public static class CreatureMaterials
{
    static Shader _shader;
    static readonly ConditionalWeakTable<Species, ShaderMaterial> Cache = new();

    public static Shader Shader => _shader ??= GD.Load<Shader>("res://shaders/creature.gdshader");

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
        return m;
    });

    /// <summary>Drops the shared materials and shader on the main thread (at shutdown).</summary>
    public static void Release()
    {
        var materials = new List<ShaderMaterial>();
        foreach (var (_, material) in (IEnumerable<KeyValuePair<Species, ShaderMaterial>>)Cache) materials.Add(material);
        Cache.Clear();
        foreach (ShaderMaterial material in materials) material.Dispose();
        _shader?.Dispose();
        _shader = null;
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
    readonly List<MeshInstance3D> _parts = new();
    MultiMeshInstance3D _swarm;
    float _hurtShown, _wakeShown;

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
                var mi = new MeshInstance3D
                {
                    Mesh = MeshBuilder.For(piece),
                    MaterialOverride = material,
                    CastShadow = piece.Shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = range,
                };
                AddChild(mi);
                _parts.Add(mi);
            }
        }
        Sync();
        ResetPhysicsInterpolation();
        foreach (var p in _parts) p.ResetPhysicsInterpolation();
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
        for (int i = 0; i < _parts.Count; i++) _parts[i].Transform = pose[i];

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
    }
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

    public void Bind(WildSim sim, Node unused)
    {
        Clear();
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
        foreach (Creature c in _sim.Creatures)
        {
            if (c.Removed) continue;
            if (!_views.TryGetValue(c, out CreatureView view))
            {
                view = new CreatureView();
                AddChild(view);
                view.Build(c);
                _views[c] = view;
            }

            float d = WildSim.Flat(c.Position - camera).Length();
            float far = c.Species.Plan == BodyPlan.Giant ? FarDistance * 2.5f : FarDistance;
            c.RigInterval = d < NearDistance ? 1 : d < MidDistance ? 2 : d < far ? 4 : 12;
            bool visible = d < far;
            if (view.Visible != visible) view.Visible = visible;
            if (visible && (c.RigInterval == 1 || c.RigPending == 0f)) view.Sync();
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
