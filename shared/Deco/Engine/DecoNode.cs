using System;
using System.Collections.Generic;
using GalHack.Items;
using Godot;

namespace GalHack.Deco.Engine;

/// <summary>
/// One placed decoration: its shared mesh, a static body made of a few boxes, the lights it gives
/// off while powered, and its animation (a flickering screen, a blinking status light, a slow
/// alien pulse, sparks from a broken panel).
/// </summary>
public partial class DecoNode : Node3D
{
    public DecoLook Look { get; private set; }
    /// <summary>The placement it was made for, when a plan view made it.</summary>
    public DecoPlacement Placement;
    public MeshInstance3D MeshInstance { get; private set; }
    public StaticBody3D Body { get; private set; }
    readonly List<OmniLight3D> _lights = new();
    readonly List<float> _lightEnergy = new();
    string _anim;
    float _phase, _t, _next, _drop = 1f, _dropUntil;
    CpuParticles3D _sparks;
    OmniLight3D _flash;
    ulong _seed;

    /// <summary>Collision layer the bodies go on (the shared physics module's static world layer by default).</summary>
    public static uint CollisionLayer = 1;

    public static DecoNode Create(DecoMeshCache cache, DecoLook look, Transform3D at, bool collide = true, bool lights = true)
    {
        DecoPiece piece = cache.Forge.Catalogue.Piece(look.Piece);
        DecoMeshData data = cache.Forge.Mesh(look);
        var node = new DecoNode { Look = look, Transform = at, Name = piece.BlockId(look.State) };
        node._seed = DecoHash.Fnv(look.Piece) ^ (ulong)at.Origin.X.GetHashCode() ^ ((ulong)at.Origin.Z.GetHashCode() << 1);
        // Drawn out to where it is a pixel or two (its longest side over 600: a cup to 40 m, a chair to 600): a street of
        // dressed rooms and a roadside wreck's hundred pieces cost the renderer nothing from far off.
        Mesh mesh = cache.Mesh(look);
        node.MeshInstance = new MeshInstance3D { Mesh = mesh, Name = "Mesh", VisibilityRangeEnd = MathF.Max(40f, mesh.GetAabb().GetLongestAxisSize() * 600f) };
        node.AddChild(node.MeshInstance);
        if (collide)
        {
            node.Body = new StaticBody3D { Name = "Body", CollisionLayer = CollisionLayer, CollisionMask = 0 };
            foreach (var (shape, xf) in cache.Shapes(look)) node.Body.AddChild(new CollisionShape3D { Shape = shape, Transform = xf });
            node.AddChild(node.Body);
        }
        if (lights && piece.Light != null)
            foreach (var (pos, col) in data.Lights)
            {
                var l = new OmniLight3D
                {
                    Position = DecoGodot.V(pos), LightColor = DecoGodot.C(col), OmniRange = piece.Light.Range,
                    LightEnergy = piece.Light.Energy * (data.Lights.Count > 1 ? 0.7f : 1f), ShadowEnabled = false, OmniAttenuation = 1.4f,
                };
                node._lights.Add(l);
                node._lightEnergy.Add(l.LightEnergy);
                node.AddChild(l);
            }
        bool lit = data.Lights.Count > 0 || data.SelfLit;
        node._anim = piece.Anim switch
        {
            "spark" when data.Spark != null => "spark",
            "spark" => null,
            _ when !lit => null,
            _ => piece.Anim,
        };
        if (node._anim == "spark") node.AddSparks(DecoGodot.V(data.Spark.Value));
        node._phase = (node._seed % 1000) / 1000f * Mathf.Tau;
        return node;
    }

    // What moves is moved by the DecoAnimator: one call a frame for all of them, and only near the eye. A node
    // of its own would be a call into this code every frame for every piece in every place standing.
    internal int AnimIndex = -1;
    internal byte AnimBand;
    internal float AnimOwed;

    public override void _EnterTree()
    {
        if (_anim != null) DecoAnimator.Join(this);
    }

    public override void _ExitTree()
    {
        if (_anim != null) DecoAnimator.Leave(this);
    }

    void AddSparks(Vector3 at)
    {
        _sparks = new CpuParticles3D
        {
            Position = at, Emitting = false, OneShot = true, Amount = 14, Lifetime = 0.55f, Explosiveness = 0.92f,
            Direction = new Vector3(0, 0.4f, -1), Spread = 55, InitialVelocityMin = 1.2f, InitialVelocityMax = 2.6f,
            Gravity = new Vector3(0, -9.8f, 0), ScaleAmountMin = 0.6f, ScaleAmountMax = 1.0f,
            Mesh = new QuadMesh { Size = new Vector2(0.025f, 0.025f), Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.82f, 0.35f) * 2.5f,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            } },
        };
        AddChild(_sparks);
        _flash = new OmniLight3D { Position = at, LightColor = new Color(1f, 0.8f, 0.45f), OmniRange = 2.5f, LightEnergy = 0, ShadowEnabled = false };
        AddChild(_flash);
        _next = 0.5f + (_seed % 7) * 0.3f;
    }

    float Rand()
    {
        _seed = DecoHash.Mix(_seed + 0x9E3779B97F4A7C15UL);
        return (_seed >> 40) / (float)(1UL << 24);
    }

    /// <summary>One step of the animation, <paramref name="delta"/> seconds on from the last (the animator calls it; a farther piece is stepped seldom, and by a longer time).</summary>
    internal void Animate(float delta)
    {
        delta = Mathf.Min(delta, 1f);
        _t += delta;
        float e = 1f;
        switch (_anim)
        {
            case "flicker":
                if (_t > _dropUntil && Rand() < delta * 1.6f) { _drop = Rand() < 0.25f ? 0.05f : 0.3f + 0.4f * Rand(); _dropUntil = _t + 0.04f + 0.18f * Rand(); }
                e = _t < _dropUntil ? _drop : 0.92f + 0.08f * Mathf.Sin(_t * 50f + _phase);
                break;
            case "blink":
                e = Mathf.PosMod(_t + _phase, 1.3f) < 0.65f ? 1f : 0.15f;
                break;
            case "pulse":
                e = 0.62f + 0.38f * Mathf.Sin(_t * 1.4f + _phase);
                break;
            case "spark":
                if (_t > _next)
                {
                    _sparks.Restart();
                    _sparks.Emitting = true;
                    _flash.LightEnergy = 1.6f;
                    _next = _t + 1.2f + 3.5f * Rand();
                }
                _flash.LightEnergy = Mathf.MoveToward(_flash.LightEnergy, 0, delta * 12f);
                return;
        }
        MeshInstance.SetInstanceShaderParameter("energy", e);
        for (int i = 0; i < _lights.Count; i++) _lights[i].LightEnergy = _lightEnergy[i] * e;
    }
}
