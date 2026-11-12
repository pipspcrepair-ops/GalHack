using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GalHack.Arms;
using Godot;
using Slice.Surface;
using Wildlife;
using Layers = GalHack.Physics.Layers;

namespace Slice.Arms;

/// <summary>
/// The weapons of the wildlife sims' people and machines (a raider band, a squatter in a ruin, a
/// walking machine): each carries a real weapon item (<see cref="Weapons.ItemOf"/>, a machine its
/// <see cref="Species.Mount"/>), drawn as the player's own model in the hand or on the mount, fired
/// through <see cref="ArmsHost.FireFrom"/> from its muzzle with the row's own numbers, using up
/// rounds it carries, and dropped with what's left where it falls. It answers the sim's
/// <see cref="Arming"/> hooks: whether a gun reaches (rounds, range, a clear line), the shot, and a
/// blow's damage (a hand weapon's, or bare hands').
/// </summary>
public sealed class SimArms
{
    readonly ArmsHost _host;
    readonly ConditionalWeakTable<Creature, Kit> _kits = new();
    readonly List<(Creature C, Kit K)> _firing = new();
    readonly List<Creature> _scan = new();
    float _drawIn;

    /// <summary>What one of them carries, and where its fire stands.</summary>
    public sealed class Kit
    {
        public Clip Clip;
        /// <summary>Rounds still to go in the burst under way, the time to the next, at whom.</summary>
        public int Pending;
        public float Next;
        public ITarget Target;
        /// <summary>The last look along the line to a target: when, and whether it was clear.</summary>
        public float LookedAt = -9f;
        public bool Clear;
        /// <summary>The model drawn in its hand or on its mount, and the view it hangs on.</summary>
        public GunModel.Built Model;
        public Node3D On;
        public bool Mounted;
        public Rng Rng;
    }

    /// <summary>Shots fired and blows struck by the sims' people and machines (tests).</summary>
    public int Shots, Drawn;

    public SimArms(ArmsHost host)
    {
        _host = host;
        Arming.Reach = Reach;
        Arming.Fire = Fire;
        Arming.BlowDamage = Blow;
    }

    SurfaceZone Zone => _host.Game.Zone;
    WildlifePatch Patch => Zone?.Wildlife;
    float Now => (float)Time.GetTicksMsec() / 1000f;

    /// <summary>Its kit (made the first time it's asked): null for anything that fights with its body alone.</summary>
    public Kit KitOf(Creature c)
    {
        if (c == null) return null;
        if (_kits.TryGetValue(c, out Kit k)) return k;
        Species s = c.Species;
        string item = s.Mechanical ? s.Mount : s.Figure != null && !s.Husk ? Weapons.ItemOf(s.Figure.Prop) : null;
        if (string.IsNullOrEmpty(item)) return null;
        WeaponDef def = _host.Table.OfItem(item);
        if (def == null) return null;
        var rng = Rng.For(s.Seed, "arms", c.Id);
        k = new Kit { Clip = Clip.Of(def, Zone.Data.Items, def.Delivery == Delivery.Strike ? 0 : rng.Int(1, 3)), Mounted = s.Mechanical, Rng = rng };
        _kits.Add(c, k);
        return k;
    }

    /// <summary>A creature's middle in the zone, and the sim's origin.</summary>
    Vector3 WorldOf(Creature c, out Vector3 origin)
    {
        Patch.SimOf(c, out origin, out _);
        return c.Position + origin;
    }

    float Reach(Creature c, ITarget t)
    {
        if (c.Dead || Patch == null) return 0f;
        Kit k = KitOf(c);
        if (k == null || k.Clip.Strike || k.Clip.Spent) return 0f;
        Vector3 at = WorldOf(c, out Vector3 origin);
        Vector3 aim = t.Position + origin + Vector3.Up * 1.1f;
        float range = k.Clip.Range;
        if ((aim - at).Length() > range) return 0f;
        // A clear line from where its gun is (looked at a few times a second, not every think).
        if (Now - k.LookedAt > 0.35f)
        {
            k.LookedAt = Now;
            Vector3 from = Muzzle(c, k, origin);
            var q = PhysicsRayQueryParameters3D.Create(from, aim, Layers.World | Layers.Hull);
            k.Clear = _host.GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
        }
        return k.Clear ? range : 0f;
    }

    float Fire(WildSim w, Creature c, ITarget t)
    {
        Kit k = KitOf(c);
        if (k == null || k.Clip.Strike) return 0.5f;
        if (k.Clip.Loaded <= 0)
        {
            // Out: it reloads where it stands (the sound of it), or has nothing left.
            if (!k.Clip.Reload()) return 0.4f;
            _host.Sounds.Play(k.Clip.Def, VoiceKind.Reload, WorldOf(c, out _));
            return k.Clip.Def.Reload;
        }
        k.Pending = Math.Min(k.Clip.Burst(k.Rng), k.Clip.Loaded);
        k.Next = 0f;
        k.Target = t;
        if (!_firing.Exists(f => f.C == c)) _firing.Add((c, k));
        // Its own cadence: the burst at the weapon's rate, then a breath before the next aim.
        return k.Pending * k.Clip.Def.SecondsPerShot + k.Rng.Range(0.35f, 0.9f) + (k.Clip.Def.Pump > 0f ? k.Clip.Def.Pump : 0f);
    }

    float Blow(Creature c)
    {
        Kit k = KitOf(c);
        if (k == null) return -1f;
        if (k.Clip.Strike) return k.Clip.Def.Damage;
        // A gun swung or butted when they're on you: a heavy blow, a machine's own strike.
        return k.Mounted ? -1f : _host.Table.Get(Weapons.Fists).Damage * 1.5f;
    }

    /// <summary>Where its shots leave: the muzzle of the model in its hand or on its mount (undrawn, far off: about where it would be).</summary>
    public Vector3 Muzzle(Creature c, Kit k, Vector3 origin)
    {
        if (k.Model != null && GodotObject.IsInstanceValid(k.Model.Muzzle) && k.Model.Muzzle.IsInsideTree()) return k.Model.Muzzle.GlobalPosition;
        float h = c.Species.Figure?.Height ?? c.Species.BodyHeight;
        return c.Position + origin + Vector3.Up * h * 0.75f + c.Forward * 0.5f;
    }

    /// <summary>Every physics tick: bursts under way go round by round, and new bodies in view get their weapon drawn.</summary>
    public void Tick(float dt)
    {
        if (Patch == null) return;
        for (int i = _firing.Count - 1; i >= 0; i--)
        {
            var (c, k) = _firing[i];
            if (c.Dead || c.Removed || k.Pending <= 0 || k.Target == null || !k.Target.Attackable) { k.Pending = 0; _firing.RemoveAt(i); continue; }
            if ((k.Next -= dt) > 0f) continue;
            k.Next = k.Clip.Def.SecondsPerShot;
            if (!k.Clip.Shot()) { k.Pending = 0; continue; }
            k.Pending--;
            Vector3 origin;
            Vector3 body = WorldOf(c, out origin);
            Vector3 muzzle = Muzzle(c, k, origin);
            Vector3 aim = k.Target.Position + origin + Vector3.Up * 1.15f;
            // A person's hands aren't a mount: a little less steady, and less again on the move.
            float unsteady = (k.Mounted ? 0.8f : 1.6f) + (c.Velocity.Length() > 0.8f ? 1.5f : 0f);
            _host.FireFrom(k.Clip.Def, muzzle, aim - muzzle, unsteady, c);
            Shots++;
        }
        if ((_drawIn -= dt) > 0f) return;
        _drawIn = 0.5f;
        DrawAll(Patch.Sim, Patch.View == null ? null : Patch.View.ViewOf);
        foreach (DeepSite site in Patch.Sites) DrawAll(site.Sim, site.ViewOf);
    }

    /// <summary>Hangs each armed body's weapon model on its hand (or mount), once its view is built.</summary>
    void DrawAll(WildSim sim, Func<Creature, CreatureView> viewOf)
    {
        if (sim == null || viewOf == null) return;
        foreach (Creature c in sim.Creatures)
        {
            if (c.Dead || c.Removed || !Arming.Fights(c.Species)) continue;
            Kit k = KitOf(c);
            if (k == null || k.Clip.Def.Id == Weapons.Fists) continue;
            if (!k.Mounted && !Weapons.DrawnByGame(c.Species.Figure.Prop)) continue;
            CreatureView v = viewOf(c);
            if (v == null) continue;
            Node3D on = k.Mounted ? v.Part(c.Species.Figure != null ? "arm1l" : "head") : v.Part("prop");
            if (on == null || on == k.On && k.Model != null && GodotObject.IsInstanceValid(k.Model.Root)) continue;
            if (on.GetNodeOrNull<Node3D>("Weapon") is Node3D old) { k.Model = GunModel.Of(old) ?? k.Model; k.On = on; continue; } // a view rebound to another of the same kind keeps its weapon
            float size = c.Species.Figure != null ? HumanBody.ArmScale(c.Species.Figure) : 1.2f;
            // In the hand or at the end of the forearm, the barrel along the limb; on a walker, under its head facing ahead.
            Transform3D at = k.Mounted && c.Species.Figure == null
                ? new Transform3D(Basis.Identity.Scaled(Vector3.One * size), new Vector3(0f, -c.Species.Body.HeadSize * 0.3f, -c.Species.Body.HeadSize * 0.4f))
                : new Transform3D(Clip.GunInProp.Scaled(Vector3.One * size), k.Mounted ? Vector3.Up * c.Species.Figure.Forearm : Vector3.Zero);
            k.Model = GunModel.Hang(on, k.Clip.Def, at);
            k.On = on;
            Drawn++;
        }
    }

    /// <summary>What a fallen one leaves of its weapon: the weapon item (a person's, not a machine's mount) and the ammo it had left.</summary>
    public void Drops(Creature c, GalHack.Items.Bill drops)
    {
        Kit k = KitOf(c);
        if (k == null) return;
        WeaponDef d = k.Clip.Def;
        var items = Zone.Data.Items;
        if (!k.Mounted && items.Has(d.Item)) drops.Add(d.Item, 1);
        if (k.Clip.ItemsLeft > 0 && items.Has(d.Ammo)) drops.Add(d.Ammo, k.Clip.ItemsLeft);
    }
}
