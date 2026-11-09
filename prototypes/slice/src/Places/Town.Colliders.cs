using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Vessels;
using Wildlife;
using Layers = GalHack.Physics.Layers;

namespace Slice.Places;

/// <summary>
/// The town's people are solid near you: each one out and about within <see cref="SolidRange"/>
/// has a real collision capsule (their height, their folk's width) on the props layer, following
/// where the town has them, so your capsule, thrown things and vessels meet a body, not air. The
/// capsules are pooled: taken as someone comes into range, given back as they leave it, go
/// indoors or fall, never made twice. Rays pass through them to the town's own hit test.
/// Walking into someone presses them aside by the push rule (<see cref="Town.Space"/>): a child
/// at once, a braced guard slowly, so a braced guard stops you dead until they give way, and
/// someone pinned against a wall stops you for good. A vessel that runs into someone hits them
/// by the one impact rule creatures use.
/// </summary>
public sealed partial class Town
{
    public const float SolidRange = 14f;

    sealed class Solid
    {
        public StaticBody3D Body;
        public CapsuleShape3D Shape;
    }

    readonly Stack<Solid> _solidFree = new();
    readonly Dictionary<Person, Solid> _solids = new();
    readonly List<Person> _solidGone = new();
    Vector3 _lastPlayer;
    bool _hadPlayer;

    /// <summary>Capsules on people now, and how many were ever made (tests: none left behind, none made twice).</summary>
    public int SolidsInUse => _solids.Count;
    public int SolidsMade { get; private set; }
    /// <summary>Vessel hits on the town's people (tests).</summary>
    public int VesselHits { get; private set; }

    Solid TakeSolid()
    {
        if (_solidFree.TryPop(out Solid s))
        {
            s.Body.CollisionLayer = Layers.Props;
            return s;
        }
        var shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.7f };
        var body = new StaticBody3D { Name = "Person", CollisionLayer = Layers.Props, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = shape });
        _view.AddChild(body);
        WorldRay.Ignored.Add(body.GetRid()); // shots meet the person by the town's own test
        SolidsMade++;
        return new Solid { Body = body, Shape = shape };
    }

    void GiveSolid(Solid s)
    {
        s.Body.CollisionLayer = 0;
        s.Body.Position = new Vector3(0f, -1000f, 0f);
        _solidFree.Push(s);
    }

    /// <summary>Capsules onto those in range, back from those gone out of it; your push; vessels.</summary>
    void Solids(Vector3 player, float dt)
    {
        if (_view == null) return;
        bool afoot = _places.Zone.Wildlife.Player is { Attackable: true };
        Vector3 moved = _hadPlayer ? player - _lastPlayer : Vector3.Zero;
        _lastPlayer = player;
        _hadPlayer = true;
        foreach (Person p in People)
        {
            bool near = !p.Dead && !p.Gone && !p.Indoors && !p.Away && p.Looks != null && !_held.Contains(p)
                && (p.Pos - player).Length() < SolidRange;
            _solids.TryGetValue(p, out Solid s);
            if (!near)
            {
                if (s != null) { GiveSolid(s); _solids.Remove(p); }
                continue;
            }
            if (s == null) _solids[p] = s = TakeSolid();
            float r = BodyRadius(p), h = MathF.Max(p.Figure.Height * (p.Sitting ? 0.7f : 1f), r * 2f + 0.05f);
            if (MathF.Abs(s.Shape.Radius - r) > 0.01f) s.Shape.Radius = r;
            if (MathF.Abs(s.Shape.Height - h) > 0.02f) s.Shape.Height = h;
            // Come down on you (you stepped into them as they turned, or they were pushed into you): out of you first, if there's room.
            if (afoot)
            {
                Vector3 o = new(p.Pos.X - player.X, 0f, p.Pos.Z - player.Z);
                float od = o.Length(), touch = r + Slice.Player.Survivor.Radius + 0.02f;
                if (od < touch && MathF.Abs(p.Pos.Y - player.Y) < 1.6f)
                {
                    Vector3 dir = od > 1e-3f ? o / od : new Vector3(MathF.Sin(p.Yaw), 0f, MathF.Cos(p.Yaw));
                    // At their give, as a push is: a braced guard leaning on you is moved off you slowest.
                    float out1 = (touch - od) * MathF.Min(1f, Give(p));
                    Vector3 step = dir * out1;
                    if (Open(p.Pos + step)) p.Pos += step;
                    else
                    {
                        Vector3 side = new Vector3(-dir.Z, 0f, dir.X) * out1;
                        if (Open(p.Pos + side + step * 0.5f)) p.Pos += side + step * 0.5f;
                    }
                }
            }
            s.Body.GlobalPosition = World(p.Pos + Vector3.Up * h * 0.5f);
            // Pressed by you: walking into them moves them aside at their own give; pinned, they stay, and stop you.
            if (afoot)
            {
                Vector3 d = new(p.Pos.X - player.X, 0f, p.Pos.Z - player.Z);
                float dist = d.Length(), contact = r + Slice.Player.Survivor.Radius + 0.12f;
                if (dist < contact && dist > 1e-3f && MathF.Abs(p.Pos.Y - player.Y) < 1.6f)
                {
                    Vector3 away = d / dist;
                    // Pressing: walking into them, or leaning on them face on (your capsule stopped by theirs) a moment.
                    float pressing = MathF.Max(moved.Dot(away) / MathF.Max(dt, 1e-3f), 0f);
                    Vector3 facing = _places.Zone.Wildlife.Player.Forward;
                    facing.Y = 0f;
                    bool leaning = facing.LengthSquared() > 1e-4f && facing.Normalized().Dot(away) > 0.5f;
                    p.PressFor = pressing > 0.2f || leaning ? p.PressFor + dt : 0f;
                    if (pressing > 0.2f || p.PressFor > 0.25f)
                    {
                        // Aside, not just on ahead of you: off the line you walk, toward the side they already stand to
                        // (pushed straight on, someone in a narrow way is only driven along it in front of you).
                        Vector3 aside = away;
                        Vector3 going = moved with { Y = 0f };
                        if (going.LengthSquared() < 1e-8f) going = facing;
                        if (going.LengthSquared() > 1e-6f)
                        {
                            going = going.Normalized();
                            Vector3 lateral = away - going * away.Dot(going);
                            if (lateral.LengthSquared() < 0.01f) lateral = new Vector3(-going.Z, 0f, going.X) * ((p.GetHashCode() & 1) == 0 ? 1f : -1f);
                            aside = (away * 0.5f + lateral.Normalized()).Normalized();
                        }
                        Vector3 step = aside * MathF.Min(1.5f * Give(p), 1.6f) * dt;
                        if (!Open(p.Pos + step)) step = away * step.Length();
                        if (Open(p.Pos + step)) p.Pos += step;
                        else
                        {
                            // Against a wall: slide off along it if there's room, else stay put (and stop you).
                            Vector3 side = new Vector3(-away.Z, 0f, away.X) * step.Length();
                            if (Open(p.Pos + side)) p.Pos += side;
                        }
                        if (p.ShovedFor <= 0f) Shoved(p, player);
                    }
                }
                // Never round you on arrival: a capsule that has come down on you lets go until you part.
                bool round = dist < r + Slice.Player.Survivor.Radius - 0.04f && MathF.Abs(p.Pos.Y - player.Y) < 1.6f;
                s.Body.CollisionLayer = round ? 0u : Layers.Props;
            }
        }
        _solidGone.Clear();
        foreach (var (p, s) in _solids)
            if (!People.Contains(p)) { GiveSolid(s); _solidGone.Add(p); }
        foreach (Person p in _solidGone) _solids.Remove(p);
        foreach (Vessel v in _places.Zone.Vessels)
            if (!v.IsBase && !v.IsWreck && v.Body.Velocity.LengthSquared() > 4f) Strike(v);
    }

    /// <summary>All capsules back (the place stands down).</summary>
    void DropSolids()
    {
        foreach (var (_, s) in _solids) GiveSolid(s);
        _solids.Clear();
    }

    /// <summary>A moving vessel against the town's people: the one impact rule, with the reduced mass. They're thrown aside and hurt; the vessel loses what it gave.</summary>
    void Strike(Vessel v)
    {
        var (min, max) = v.Structure.Bounds();
        float reach = (max - min).ToVector3().Length() * v.Structure.Grid.CellSize * 0.6f + 1f;
        Vector3 centre = v.Body.WorldPosition.ToGodot();
        foreach (Person p in People)
        {
            if (p.Dead || p.Gone || p.Indoors || p.Away || p.Looks == null || _clock - p.StruckAt < 1f) continue;
            Vector3 at = World(p.Pos + Vector3.Up * p.Figure.Height * 0.5f);
            if (at.DistanceTo(centre) > reach + 1f) continue;
            float r = BodyRadius(p);
            if (v.DistanceFromHull(v.Body.ToLocal(at.ToNumerics()), r + 0.2f) > r) continue;
            PhysicsRules.ContactData rules = _places.Zone.Physics.Rules.Contacts;
            Vector3 velocity = v.Body.Velocity.ToGodot();
            float closing = velocity.Length();
            if (closing < 1f) continue;
            Vector3 dir = velocity / closing;
            float mass = MathF.Max(20f, p.Figure.Mass), mu = Impacts.ReducedMass(v.Body.Mass, mass);
            float damage = Impacts.Damage(mu, closing, rules);
            _places.Zone.Physics.Hurt(v.Body, damage);
            float integrity = MathF.Max(0.01f, Impacts.Integrity(BodyKind.Creature, mass, _places.Zone.Physics.Rules.Integrity));
            Vector3 aside = (dir + new Vector3(dir.Z, 0f, -dir.X) * 0.5f).Normalized() * MathF.Min(mu * closing / mass * 0.15f + 1f, 4f);
            if (Open(p.Pos + new Vector3(aside.X, 0f, aside.Z))) p.Pos += new Vector3(aside.X, 0f, aside.Z);
            _places.Zone.Physics.AddImpulse(v.Body, -(dir * mu * closing * 0.5f).ToNumerics(), ForceKind.Contact);
            p.StruckAt = _clock;
            VesselHits++;
            p.Health.Current -= MathF.Max(damage / integrity * p.Health.Max, closing * 0.5f);
            p.Hurt = 0.4f;
            if (p.Dead) Fall(p);
        }
    }
}
