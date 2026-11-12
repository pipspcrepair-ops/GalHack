using System;
using System.Collections.Generic;
using GalHack.Items;
using Godot;
using Slice.Items;
using Slice.Places;
using Wildlife;

namespace Slice.Game;

/// <summary>
/// How bodies end. A body that falls is searched where it lies, by its own shape. One blown apart
/// comes down as pieces of itself (a machine with sparks and smoke instead of blood); one burned
/// away leaves ash on a scorch. Whatever it carried is never lost: it is left where the body was,
/// to be searched there.
/// </summary>
public partial class Game
{
    /// <summary>The pieces and the ash, in the zone.</summary>
    public Gibs Gibs { get; private set; }

    /// <summary>What a weapon's kill does to a body.</summary>
    public static BodyEnd EndOf(GalHack.Arms.KillEffect k) => k switch
    {
        GalHack.Arms.KillEffect.Gib => BodyEnd.Gib,
        GalHack.Arms.KillEffect.Vaporise => BodyEnd.Vaporise,
        _ => BodyEnd.Fall,
    };

    void SetUpBodies()
    {
        Gibs = new Gibs { Name = "Gibs", Layer = GalHack.Physics.Layers.Props, Mask = GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull | GalHack.Physics.Layers.Interior | GalHack.Physics.Layers.Props };
        Zone.AddChild(Gibs);
        Town.Fell += OnPersonFell;
    }

    /// <summary>Balls round a creature's body as it is drawn now (its rig's hit spheres, turned as its view turns them), in the world.</summary>
    IEnumerable<Vector4> BodyBalls(Creature c)
    {
        if (c.Removed || c.Rig == null) yield break;
        Zone.Wildlife.SimOf(c, out Vector3 origin, out _);
        Vector3 middle = c.Position + Vector3.Up * c.Species.Body.Height * 0.5f;
        bool turned = !c.Tumble.IsEqualApprox(Quaternion.Identity);
        var turn = new Transform3D(new Basis(c.Tumble), middle) * new Transform3D(Basis.Identity, -middle);
        for (int i = 0; i < c.Rig.HitCount; i++)
        {
            Vector4 h = c.Rig.Hits[i];
            Vector3 p = new(h.X, h.Y, h.Z);
            if (turned) p = turn * p;
            p += origin;
            yield return new Vector4(p.X, p.Y, p.Z, h.W);
        }
    }

    /// <summary>A town person's body as it lies, as balls in the world.</summary>
    static IEnumerable<Vector4> PersonBalls(Town t, Town.Person p)
    {
        if (p.Rig == null || p.Gone) yield break;
        p.Rig.UpdateHits();
        for (int i = 0; i < p.Rig.HitCount; i++)
        {
            Vector4 h = p.Rig.Hits[i];
            Vector3 w = t.World(new Vector3(h.X, h.Y, h.Z));
            yield return new Vector4(w.X, w.Y, w.Z, h.W);
        }
    }

    /// <summary>The floor under a point someone could stand on (where remains are left).</summary>
    Vector3 FloorAt(Vector3 at) => Zone.Loot.Settle != null ? Zone.Loot.Settle(at) : at;

    /// <summary>
    /// A body comes apart or burns away, from its pieces where they are (world). Blood for the living,
    /// sparks and smoke for a machine; ash and a scorch for one burned away. Returns where its remains lie.
    /// </summary>
    Vector3 EndBody(Species s, RigLayout layout, IReadOnlyList<Transform3D> pose, BodyEnd end, Vector3 centre, Vector3 hitDir, ulong seed)
    {
        Vector3 floor = FloorAt(centre);
        if (end == BodyEnd.Gib)
        {
            Vector3 from = centre - (hitDir.LengthSquared() > 1e-4f ? hitDir.Normalized() : Vector3.Down) * 1.2f + Vector3.Down * 0.4f;
            Gibs.Burst(s, layout, pose, from, 7f);
            if (s.Mechanical)
            {
                for (int k = 0; k < 4; k++) Zone.Dust?.Sparks(centre + new Vector3((k - 1.5f) * 0.15f, 0.1f * k, 0f), Vector3.Up, new Color(1f, 0.7f, 0.3f));
                Zone.Dust?.Puff(centre, new Color(0.18f, 0.18f, 0.19f, 0.9f), 2.2f);
                Zone.Dust?.Puff(centre + Vector3.Up * 0.5f, new Color(0.25f, 0.24f, 0.24f, 0.7f), 1.6f);
            }
            else
            {
                Zone.Blood.Pop(centre, s.Blood, 60);
                Zone.Blood.Hit(centre, hitDir, s.Blood, s.Figure != null ? 1.2f : MathF.Max(0.6f, s.Body.Size));
            }
        }
        else if (end == BodyEnd.Vaporise)
        {
            float width = s.Figure != null ? 0.55f : Math.Clamp(s.Body.Size * 0.6f, 0.3f, 3f);
            Gibs.Ash(floor, Vector3.Up, width, seed);
            Zone.Dust?.Puff(centre, new Color(0.3f, 0.29f, 0.28f, 0.85f), 1.8f);
            Zone.Dust?.Puff(floor + Vector3.Up * 0.3f, new Color(0.12f, 0.11f, 0.1f, 0.8f), 1.2f);
        }
        return floor;
    }

    /// <summary>Someone of a town died: what they carried stays with them, searched on the body as it lies, or where it was if nothing is left of it.</summary>
    void OnPersonFell(Town t, Town.Person p)
    {
        if (p.Looks == null || Zone?.Loot == null) return;
        Bill drops = BodyDrops(p.Looks, Wildlife.Rng.For(p.Looks.Seed, "drop", t.People.IndexOf(p)));
        int[] size = Zone.Data.Rules.Containers.Corpse;
        Vector3 at = t.World(p.Pos);
        string name = $"{Cap(p.Looks.CommonName)} body";
        if (p.End != BodyEnd.Fall && p.Rig != null)
        {
            var pose = new Transform3D[p.Rig.Pose.Length];
            Vector3 shift = t.World(Vector3.Zero);
            for (int i = 0; i < pose.Length; i++) pose[i] = new Transform3D(p.Rig.Pose[i].Basis, p.Rig.Pose[i].Origin + shift);
            at = EndBody(p.Looks, p.Rig.Layout, pose, p.End, t.World(p.Rig.ChestPoint), p.Rig.HitDir, p.Looks.Seed);
            t.Unbody(p);
            Lootable left = Zone.Loot.Create(LootKind.Corpse, $"Remains of {p.Looks.CommonName}", at, size[0], size[1], drops);
            left.Radius = 0.5f;
            return;
        }
        Lootable body = Zone.Loot.Create(LootKind.Corpse, name, at, size[0], size[1], drops);
        body.Radius = 0.6f;
        body.Shape = () => PersonBalls(t, p);
    }

    /// <summary>A creature blown apart or burned away: its pieces or its ash, and its drops left where it was.</summary>
    bool EndCreature(Creature c, out Vector3 at)
    {
        at = Zone.Wildlife.WorldOf(c);
        if (c.End == BodyEnd.Fall || c.Rig == null || c.Species.Rig == RigKind.Swarm) return false;
        Zone.Wildlife.SimOf(c, out Vector3 origin, out _);
        var pose = new Transform3D[c.Rig.Pose.Length];
        for (int i = 0; i < pose.Length; i++) pose[i] = new Transform3D(c.Rig.Pose[i].Basis, c.Rig.Pose[i].Origin + origin);
        Vector3 centre = c.Rig.BoundsCenter + origin;
        at = EndBody(c.Species, c.Rig.Layout, pose, c.End, centre, c.HitDir, c.Species.Seed ^ (ulong)c.Id);
        c.Removed = true;
        return true;
    }
}
