using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Things that crawl on the surfaces of caves and places: creepers slung between long hooked
/// legs, smooth duct eels, soft moss grubs, armoured scrappers. The body is a chain of
/// segments following the head; each segment is pressed back onto the surface (its clearance
/// off the solid) every frame, so the body bends round a corner from floor to wall to ceiling
/// with the creature and never sinks into the rock. Legs plant their feet on the surface and
/// step in alternating sets, knees high, like a harvestman's. A telegraph lifts the front of the
/// body off the surface; hidden in a crack, the creature is not drawn at all.
/// </summary>
public sealed class CrawlRig : Rig
{
    readonly int _head, _tip;
    readonly int[] _segs;
    readonly Vector3[] _p, _n;
    readonly float _segLen, _legLen, _clearance;
    readonly List<Leg> _legs = new();
    float _gait;

    sealed class Leg
    {
        public int Upper, Lower, Seg, Side, Group;
        public float Along;      // where it joins its segment, -0.5..0.5 of the segment
        public float Splay;      // how far forward (+) or back (-) it reaches, radians
        public Vector3 Foot, From, To;
        public bool Planted;
        public float Swing = 1f;
    }

    public CrawlRig(Species s) : base(s, Build)
    {
        Proportions b = s.Body;
        int n = Segments(s);
        _p = new Vector3[n];
        _n = new Vector3[n];
        _segLen = (b.Size - b.HeadSize * 0.7f) / n;
        _legLen = MathF.Max(0.05f, b.LegLength);
        _clearance = Crawl.ClearanceFor(s);
        var pieces = Layout.Pieces;
        _segs = new int[n];
        for (int i = 0; i < n; i++) _segs[i] = pieces.FindIndex(p => p.Name == $"seg{i}");
        _head = pieces.FindIndex(p => p.Name == "head");
        _tip = pieces.FindIndex(p => p.Name == "tailtip");
        int pairs = b.Legs / 2;
        for (int k = 0; k < pairs; k++)
        for (int side = -1; side <= 1; side += 2)
        {
            float t = pairs == 1 ? 0.3f : 0.12f + 0.62f * k / (pairs - 1);
            float bodyAt = t * (n - 1);
            int seg = Math.Clamp((int)MathF.Round(bodyAt), 0, n - 1);
            _legs.Add(new Leg
            {
                Upper = pieces.FindIndex(p => p.Name == $"up{k}_{side}"),
                Lower = pieces.FindIndex(p => p.Name == $"lo{k}_{side}"),
                Seg = seg,
                Side = side,
                Group = (k + (side > 0 ? 1 : 0)) % 2,
                Along = Math.Clamp(bodyAt - seg, -0.5f, 0.5f),
                Splay = Mathf.Lerp(0.75f, -0.85f, pairs == 1 ? 0.5f : k / (float)(pairs - 1)),
            });
        }
    }

    static int Segments(Species s) => Math.Max(2, s.Body.Segments);

    /// <summary>Body segment centres, for the "never inside the rock" checks.</summary>
    public IReadOnlyList<Vector3> SegmentCenters => _p;
    /// <summary>Feet on the surface, for the contact checks.</summary>
    public override int FootCount => _legs.Count;
    public override bool FootPlanted(int i) => _legs[i].Planted;
    public override Vector3 FootDrawn(int i) => _legs[i].Foot;

    public override void Reset(WildSim w, Creature c)
    {
        ResetIntents(c);
        Vector3 n = c.Facing.Y, f = -c.Facing.Z;
        Vector3 head = c.Position + n * _clearance;
        for (int i = 0; i < _p.Length; i++)
        {
            Vector3 q = head - f * _segLen * (i + 0.5f);
            if (c.Space != null && SpaceMath.Project(c.Space, ref q, _clearance, out Vector3 nn)) _n[i] = nn;
            else _n[i] = n;
            _p[i] = q;
        }
        foreach (Leg leg in _legs)
        {
            leg.Foot = FootTarget(c, leg, f);
            leg.Planted = true;
            leg.Swing = 1f;
        }
        Update(w, c, 0.016f);
    }

    public override void Update(WildSim w, Creature c, float dt)
    {
        if (dt <= 0f) return;
        SmoothIntents(c, dt);
        Proportions b = Species.Body;
        ISolidSpace space = c.Space;
        Vector3 up = c.Facing.Y, fwd = -c.Facing.Z;

        if (c.InWalls)
        {
            // In a crack, a vent or behind a door: nothing shows.
            for (int i = 0; i < Pose.Length; i++) Pose[i] = Collapsed(c.Position);
            HitCount = 0;
            SetBounds(c.Position, 0.01f);
            BodyClearance = 0f;
            return;
        }

        float speed = c.Velocity.Length();
        float walk = MathF.Max(0.2f, Species.Stats.WalkSpeed);
        bool airborne = c.Falling || c.Phase == AttackPhase.Lunge;
        _gait += speed * dt / MathF.Max(0.08f, _legLen * 0.8f);

        // The head leads: lifted off the surface by a rear (the telegraph) or a crouch's opposite.
        float flat = 1f - 0.45f * Crouch;
        Vector3 headCenter = c.Position + up * (_clearance * flat + Rearing * b.Size * 0.22f);
        _p[0] = headCenter - fwd * _segLen * 0.5f;
        _n[0] = up;
        if (space != null && space.Density(_p[0]) > -0.02f) SpaceMath.Project(space, ref _p[0], _clearance * 0.6f, out _);
        for (int i = 1; i < _p.Length; i++)
        {
            Vector3 d = _p[i] - _p[i - 1];
            d = d.LengthSquared() > 1e-8f ? d.Normalized() : -fwd;
            Vector3 q = _p[i - 1] + d * _segLen;
            float lift = Rearing * MathF.Max(0f, 1f - i / 2.5f) * b.Size * 0.18f;
            // Pressed back onto the surface it is crawling on, round corners and all.
            if (!airborne && space != null && SpaceMath.Project(space, ref q, _clearance * flat, out Vector3 nn))
            {
                _n[i] = _n[i].LengthSquared() > 0.5f ? SpaceMath.TurnToward(_n[i], nn, MathF.Min(1f, dt * 14f)) : nn;
                q += _n[i] * lift;
            }
            else
            {
                _n[i] = up;
                // In the air (a drop, a leap, a fall) the body trails behind its head; it still
                // never trails through rock: anything that would end up inside is pushed out.
                if (space != null && space.Density(q) > -0.02f) SpaceMath.Project(space, ref q, _clearance * 0.6f, out _);
            }
            _p[i] = q;
        }

        // Segments: facing along the body, backs to the open.
        Basis headBasis = Basis.Identity;
        for (int i = 0; i < _p.Length; i++)
        {
            Vector3 along = i == 0 ? (headCenter - _p[0]) : (_p[i - 1] - _p[i]);
            if (along.LengthSquared() < 1e-8f) along = fwd;
            Basis basis = SpaceMath.Frame(along, _n[i]);
            if (Dead > 0f) basis = basis * new Basis(Vector3.Back, Dead * 2.6f); // the dead roll over, legs up
            if (_segs[i] >= 0) Pose[_segs[i]] = new Transform3D(basis, _p[i]);
            if (i == 0) headBasis = basis;
        }
        // The head: turning to look, dipping into the moss to graze, pitched by a rear or lunge.
        Basis hb = headBasis * new Basis(Vector3.Up, LookYaw * 0.5f) * new Basis(Vector3.Right, -Rearing * 0.45f + Graze * 0.35f + Lunging * 0.15f);
        var headT = new Transform3D(hb, headCenter + fwd * Lunging * b.Size * 0.08f);
        if (_head >= 0) Pose[_head] = headT;

        // Legs: feet planted on the surface, stepping in two alternating sets.
        float stride = _legLen * 0.55f;
        foreach (Leg leg in _legs)
        {
            Basis sb = Pose[_segs[leg.Seg]].Basis;
            Vector3 sUp = sb.Y, sFwd = -sb.Z, sSide = sb.X;
            Vector3 hip = _p[leg.Seg] + sFwd * leg.Along * _segLen + sSide * leg.Side * b.Width * 0.42f;
            Vector3 target = FootTargetFrom(c, hip, sUp, sFwd, sSide, leg);
            if (airborne || Dead > 0.5f || space == null)
            {
                // Off the surface (a drop, a leap, death): legs splay out, or curl up dead.
                Vector3 spread = hip + (sSide * leg.Side * 0.8f + sFwd * MathF.Sin(leg.Splay) * 0.6f - sUp * 0.25f).Normalized() * _legLen * (Dead > 0.5f ? 0.4f : 0.85f);
                if (Dead > 0.5f) spread += sUp * _legLen * 0.5f;
                leg.Foot = leg.Foot.Lerp(spread, MathF.Min(1f, dt * 10f));
                leg.Planted = false;
                leg.Swing = 1f;
            }
            else if (leg.Swing < 1f)
            {
                leg.Swing = MathF.Min(1f, leg.Swing + dt / MathF.Max(0.08f, 0.16f * MathF.Sqrt(_legLen / 0.5f)));
                leg.To = target;
                float lift = MathF.Sin(leg.Swing * MathF.PI) * _legLen * 0.35f;
                leg.Foot = leg.From.Lerp(leg.To, leg.Swing) + sUp * lift;
                if (leg.Swing >= 1f) leg.Planted = true;
            }
            else
            {
                leg.Planted = true;
                bool groupTurn = ((int)MathF.Floor(_gait * 2f) & 1) == leg.Group || speed < walk * 0.15f;
                if ((leg.Foot - target).Length() > stride && groupTurn)
                {
                    leg.From = leg.Foot;
                    leg.To = target;
                    leg.Swing = 0f;
                    leg.Planted = false;
                }
            }
            // Knee high over the line from hip to foot.
            Vector3 mid = (hip + leg.Foot) * 0.5f;
            float span = (leg.Foot - hip).Length();
            float rise = MathF.Sqrt(MathF.Max(0f, _legLen * _legLen * 0.36f - span * span * 0.25f)) + _legLen * 0.12f;
            Vector3 knee = mid + sUp * rise + sSide * leg.Side * _legLen * 0.05f;
            if (leg.Upper >= 0) Pose[leg.Upper] = Geo.Segment(hip, knee, sFwd, _legLen * 0.55f);
            if (leg.Lower >= 0) Pose[leg.Lower] = Geo.Segment(knee, leg.Foot, sFwd, _legLen * 0.62f);
        }

        int last = _p.Length - 1;
        if (_tip >= 0)
        {
            Vector3 back = _p[last] - (last > 0 ? _p[last - 1] : headCenter);
            if (back.LengthSquared() < 1e-8f) back = -fwd;
            Pose[_tip] = new Transform3D(Geo.AlongY(back.Normalized(), Pose[_segs[last]].Basis.X), _p[last]);
        }

        int h = 0;
        if (_head >= 0) SetHit(h++, headCenter + fwd * b.HeadSize * 0.4f, b.HeadSize * 0.6f);
        for (int i = 0; i < _p.Length && h < Hits.Length; i++) SetHit(h++, _p[i], MathF.Max(0.08f, b.Width * 0.55f));
        HitCount = h;
        SetBounds(_p[_p.Length / 2], b.Size * 0.6f + _legLen);
        BodyClearance = space != null ? -space.Density(_p[_p.Length / 2]) : 0f;
    }

    Vector3 FootTarget(Creature c, Leg leg, Vector3 fwd)
    {
        Vector3 up = c.Facing.Y, side = c.Facing.X;
        Vector3 hip = _p[leg.Seg] + side * leg.Side * Species.Body.Width * 0.42f;
        return FootTargetFrom(c, hip, up, fwd, side, leg);
    }

    /// <summary>Where a foot belongs: out to the side and a little ahead or behind, pressed onto the surface.</summary>
    Vector3 FootTargetFrom(Creature c, Vector3 hip, Vector3 up, Vector3 fwd, Vector3 side, Leg leg)
    {
        Vector3 reach = (side * leg.Side * MathF.Cos(leg.Splay) + fwd * MathF.Sin(leg.Splay)) * _legLen * 0.78f;
        Vector3 target = hip + reach - up * _clearance + WildSim.Flat(c.Velocity) * 0.08f;
        if (c.Space != null && SpaceMath.Project(c.Space, ref target, 0.02f, out _)) return target;
        return hip + reach - up * _clearance;
    }

    // ---- Layout -----------------------------------------------------------------------

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        var rng = Rng.For(s.Seed, "layout");
        Proportions b = s.Body;
        int n = Segments(s);
        float segLen = (b.Size - b.HeadSize * 0.7f) / n, hs = b.HeadSize;
        float flatness = Math.Clamp(b.Height / MathF.Max(0.05f, b.Width), 0.35f, 1f);
        string back = s.Part("back");
        bool armoured = back is "plates" or "shell" || s.Form == "scrapper";
        bool soft = s.Form == "mossgrub";

        var head = new Piece("head");
        HeadShape shape = Parts.Head(head, s.Part("head"), hs, k, rng);
        Parts.Horns(head, s.Part("horns"), hs, k, rng, shape);
        layout.Add(head);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Math.Max(1, n - 1);
            // Creepers and scrappers: a broad thorax tapering to the abdomen; eels and grubs even.
            float girth = s.Form switch
            {
                "creeper" => 0.75f + 0.35f * MathF.Sin(MathF.PI * MathF.Min(1f, t * 1.4f + 0.2f)),
                "scrapper" => 1f - 0.25f * t,
                "mossgrub" => 0.8f + 0.25f * MathF.Sin(MathF.PI * t),
                _ => 1f - 0.45f * MathF.Pow(t, 1.5f),
            };
            float r = b.Width * 0.5f * girth;
            var piece = new Piece($"seg{i}") { Shadow = r > 0.1f };
            var profile = LoftProfile.Smooth(-segLen * 0.7f, segLen * 0.7f, new[]
            {
                (0f, r * 0.9f, r * flatness * 0.85f, 0f), (0.45f, r, r * flatness, r * flatness * 0.05f), (1f, r * 0.88f, r * flatness * 0.8f, 0f),
            }, 8, 0.3f);
            Color c0 = i % 2 == 0 ? k.Base : k.Base.Lerp(k.Accent, 0.12f);
            piece.Loft(profile, c0, k.Belly, soft ? 0.6f : 1f, 0f, soft ? Mat.Membrane : armoured ? Mat.Carapace : Mat.Skin, null, null, 12);
            if (armoured)
                piece.Add(Shape.Sphere, new Vector3(r * 2.1f, r * flatness * 0.7f, segLen * 1.3f), new Vector3(0, r * flatness * 0.55f, segLen * 0.1f),
                    k.Base.Lerp(k.Accent, back == "shell" ? 0.3f : 0.45f).Darkened(0.05f), 0.2f, 0f, Parts.Pitch(-8f), 12, Mat.Carapace);
            if (soft && k.GlowStrength > 0.2f && i % 2 == 1)
                for (int side = -1; side <= 1; side += 2)
                    piece.Add(Shape.Sphere, Vector3.One * r * 0.28f, new Vector3(side * r * 0.62f, r * flatness * 0.5f, 0f), k.Glow, 0f, k.GlowStrength * 0.8f);
            switch (back)
            {
                case "spines" when i > 0:
                    piece.Taper(new Vector3(0, r * flatness * 0.7f, 0), new Vector3(0, r * flatness * 2.2f, segLen * 0.4f), r * 0.16f, r * 0.02f, k.Bone, 0f, 0f, Mat.Horn, 6);
                    break;
                case "ridge" when i % 2 == 0:
                    piece.Taper(new Vector3(0, r * flatness * 0.8f, 0), new Vector3(0, r * flatness * 1.5f, segLen * 0.3f), r * 0.18f, r * 0.04f, k.Accent.Lerp(k.Bone, 0.3f), 0f, 0f, Mat.Horn, 6);
                    break;
                case "crystals" when i % 2 == 1:
                    piece.Add(Shape.Cone, new Vector3(r * 0.6f, r * 1.8f, 0), new Vector3(0, r * flatness + r * 0.6f, 0), (k.GlowStrength > 0 ? k.Glow : k.Accent).Lightened(0.2f), 0f, 0.9f,
                        new Basis(Vector3.Back, rng.Range(-0.4f, 0.4f)), 6, Mat.Horn);
                    break;
                case "bubbles" when i > 0:
                    for (int j = 0; j < 3; j++)
                        piece.Add(Shape.Sphere, Vector3.One * r * rng.Range(0.3f, 0.5f), new Vector3(rng.Range(-0.5f, 0.5f) * r, r * flatness * 0.8f, rng.Range(-0.4f, 0.4f) * segLen), k.Glow.Lerp(k.Base, 0.4f), 0f, k.GlowStrength * 0.7f, null, 0, Mat.Membrane);
                    break;
                case "vents" when i % 2 == 1:
                    piece.Add(Shape.Cylinder, new Vector3(r * 0.5f, r * 0.9f, r * 0.35f), new Vector3(0, r * flatness + r * 0.3f, 0), k.Dark, 0f, 0f, null, 8, Mat.Carapace);
                    break;
            }
            if (s.Corrupted && i % 2 == 1)
                piece.Add(Shape.Sphere, Vector3.One * r * 0.5f, new Vector3(r * 0.45f * (i % 4 == 1 ? 1 : -1), r * flatness * 0.6f, 0), k.Veins, 0f, 1f, null, 0, Mat.Membrane);
            layout.Add(piece);
        }

        // Legs: two jointed pieces each, thin and dark, hooked at the tip.
        int pairs = b.Legs / 2;
        float legLen = MathF.Max(0.05f, b.LegLength);
        float rr = MathF.Max(0.014f, b.Width * (s.Form == "creeper" ? 0.065f : soft ? 0.09f : 0.075f));
        for (int pk = 0; pk < pairs; pk++)
        for (int side = -1; side <= 1; side += 2)
        {
            var upper = new Piece($"up{pk}_{side}") { Shadow = false };
            float ul = legLen * 0.55f, ll = legLen * 0.62f;
            upper.Taper(Vector3.Zero, new Vector3(0, ul, 0), rr, rr * 0.8f, k.Dark.Lerp(k.Base, 0.3f), 0.2f, 0f, Mat.Carapace, 6);
            layout.Add(upper);
            var lower = new Piece($"lo{pk}_{side}") { Shadow = false };
            lower.Taper(Vector3.Zero, new Vector3(0, ll * 0.85f, 0), rr * 0.8f, rr * 0.4f, k.Dark, 0f, 0f, Mat.Carapace, 6);
            lower.Horn(k.Horn, rr * 0.45f, Mat.Horn, new Vector3(0, ll * 0.85f, 0), new Vector3(0, ll * 0.97f, rr * 0.6f), new Vector3(0, ll, rr * 1.4f));
            layout.Add(lower);
        }

        string tail = s.Part("tail");
        if (tail != "none")
        {
            var tip = new Piece("tailtip");
            Parts.TailTip(tip, tail == "whip" ? "stinger" : tail, segLen * 0.6f, b.Width * 0.6f, k);
            layout.Add(tip);
        }
        return layout;
    }
}
