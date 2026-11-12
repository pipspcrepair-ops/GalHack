using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Arms;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Items;
using Slice.Places;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using Wildlife;
using HitKind = GalHack.Arms.HitKind;
using Layers = GalHack.Physics.Layers;
using N = System.Numerics;

namespace Slice.Arms;

/// <summary>
/// The hand weapon layer in the slice: the player's guns (shared/Arms rows and their <see cref="Gun"/>
/// state), the pool of shots in flight, and the world those shots fly in. It answers the shots'
/// questions (<see cref="IShotWorld"/>) with the slice's own: the voxel ground and its colliders,
/// structures and their blocks (<see cref="VesselDamage.Shot"/> and <see cref="VesselDamage.Blast"/>),
/// creatures (wildlife's damage, noise and fright), townsfolk, and the player (self-harm is on).
/// Every shot flies at a speed you can see. Nothing here is about any one weapon: each is a row of
/// parts, and the parts are handled once, here and in shared/Arms.
/// </summary>
public partial class ArmsHost : Node3D, IShotWorld, IAfflicted
{
    public Game.Game Game;
    SurfaceZone Zone => Game.Zone;

    public Armoury Table { get; private set; }
    public readonly ShotPool Pool = new(256);
    public readonly Afflictions Effects = new(64);
    public ArmsFx Fx { get; private set; }
    public GunSounds Sounds { get; private set; }

    /// <summary>The player as a target: arcs, mines, blasts and self-harm find them by this.</summary>
    public static readonly object PlayerThing = new();

    readonly Dictionary<string, Gun> _guns = new(StringComparer.Ordinal);
    readonly List<SharedStorage> _nearby = new();
    readonly Random _rng = new();
    RayCast3D _ray;

    /// <summary>The gun in the player's hands now, or null.</summary>
    public Gun InHand { get; private set; }

    // ---- For tests: what has happened so far ----
    public int Fired, Hits, CreatureHits, BlockHits, GroundHits, PlayerHits, PersonHits, Blasts, Bounces, Sticks, Placements, Trips, ArcJumps, ConeHits, DotTicks, Releases, Refused;
    public float PlayerDamage;
    /// <summary>The last physics tick's cost of moving every shot (ms) and what it allocated on this thread (bytes).</summary>
    public double LastStepMs { get; private set; }
    public long LastStepBytes { get; private set; }
    /// <summary>Every harm dealt: what kind of thing, the thing, how much (before armour).</summary>
    public event Action<HitKind, object, float> Dealt;

    public override void _Ready()
    {
        Table = Zone.Data.Arms;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _ray = new RayCast3D { Name = "ShotRay", Enabled = false, HitFromInside = false, CollisionMask = SolidMask, TargetPosition = Vector3.Forward };
        AddChild(_ray);
        Fx = new ArmsFx { Name = "ShotLooks" };
        AddChild(Fx);
        Fx.Setup(Pool, Table.All);
        Sounds = new GunSounds { Name = "GunSounds" };
        AddChild(Sounds);
        Sounds.Make(Table.All);
        Zone.FrameShifted += by => Pool.Shift(by.ToNumerics());
        People = new SimArms(this);
    }

    /// <summary>The weapons of the wildlife sims' people and machines.</summary>
    public SimArms People { get; private set; }

    /// <summary>People carry the player's own weapon models: the bodies leave every weapon out of their mesh but the bow.</summary>
    static ArmsHost() => Weapons.GameDraws = _ => true;

    /// <summary>What stops a shot: the ground and what is fixed to it, structures' hulls, trees. (Creatures and people are their own hit spheres.)</summary>
    const uint SolidMask = Layers.World | Layers.Hull | PlanetSurface.World.Layers.Flora;

    public override void _PhysicsProcess(double delta)
    {
        if (Game?.Survivor == null || Zone?.Planet == null) return;
        float dt = (float)delta;
        SyncExceptions(); // before the step: the step itself touches nothing but the shots and the world
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        Pool.Step(dt, this);
        Effects.Tick(dt, this);
        People?.Tick(dt);
        LastStepMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        LastStepBytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
    }

    // ---- The gun in hand -------------------------------------------------------------------------

    /// <summary>The player's gun for a weapon (made the first time; its magazine stays with it).</summary>
    public Gun GunFor(WeaponDef def)
    {
        if (_guns.TryGetValue(def.Item, out Gun gun)) return gun;
        gun = new Gun(def);
        bool self = def.Ammo == def.Item; // a mine is its own ammo: each throw takes one from the stack
        ItemCatalogue items = Zone.Data.Items;
        gun.RoundsOf = id => items.TryGet(id, out ItemDef it) && it.Rounds > 0 ? it.Rounds : 1;
        if (self)
        {
            gun.Take = (id, n) => Math.Min(n, Math.Max(0, Game.Pack[id] - gun.Loaded));
            gun.Available = id => Math.Max(0, Game.Pack[id] - gun.Loaded);
        }
        else
        {
            gun.Take = (id, n) => Game.Pack.TakeAmmo(id, n, Nearby());
            gun.Available = Available;
        }
        gun.Did += (e, n) => OnGun(gun, e, n);
        _guns[def.Item] = gun;
        return gun;
    }

    /// <summary>
    /// One tick with a weapon in hand: the trigger held or not, pressed on this tick. It fires what
    /// the gun says, from the barrel tip toward what the crosshair is on.
    /// </summary>
    public void Hold(float dt, string itemId, bool held, bool pressed)
    {
        WeaponDef def = Table.OfItem(itemId);
        if (def == null) { Idle(dt); return; }
        if (def.Delivery == Delivery.Strike)
        {
            // A hand weapon: no rounds, a blow as often as its rate lets, from the eye along the look.
            Idle(dt);
            _swingIn -= dt;
            if (!pressed || _swingIn > 0f) return;
            _swingIn = def.SecondsPerShot;
            Swing(def, Game.Survivor.WorldEye(), Game.Survivor.WorldLook(), PlayerThing, 0);
            return;
        }
        Gun gun = GunFor(def);
        if (InHand != gun) InHand?.Tick(dt, false, false, out _); // the one put away lets go of its trigger
        InHand = gun;
        if (pressed && gun.Def.Placed != null && Pool.Placed(gun.Def) >= gun.Def.Placed.Limit)
        {
            Refused++;
            Game.Say($"PLACED · {gun.Def.Placed.Limit}/{gun.Def.Placed.Limit}", Tones.Caution);
            pressed = false;
        }
        int shots = gun.Tick(dt, held, pressed, out int stage);
        if (shots > 0) Fire(gun.Def, shots, stage);
    }

    /// <summary>No weapon in hand (or hands busy): a charge in progress is let go without firing.</summary>
    public void Idle(float dt)
    {
        if (InHand == null) return;
        InHand.Tick(dt, false, false, out _);
        InHand = null;
    }

    public void Reload() => InHand?.StartReload();

    public void NextMode()
    {
        if (InHand == null || InHand.Base.Modes.Count < 2) return;
        InHand.NextMode();
        Game.Say($"{InHand.Base.Name} · {InHand.Def.Mode}", Tones.Text);
    }

    /// <summary>Ammo containers in reach, nearest first: the linked structures' storage.</summary>
    IEnumerable<SharedStorage> Nearby()
    {
        _nearby.Clear();
        foreach (Vessel v in Game.CoresInReach) _nearby.Add(v.Storage);
        return _nearby;
    }

    /// <summary>Ammo items within reach: your own, then the gun's origin (a remote slot's), then what's in the containers in range.</summary>
    int Available(string id)
    {
        PlayerInventory pack = Game.Pack;
        int n = pack[id];
        if (pack.OnRemoteBar && pack.HandRemote is RemoteSlot r && pack.InRange(r.Source)) n += r.Source.Count(id);
        foreach (Vessel v in Game.CoresInReach)
            if (v.Storage != pack.Storage && pack.InRange(v.Storage) && !(pack.HandRemote?.Source == v.Storage && pack.OnRemoteBar)) n += v.Storage.Count(id);
        return n;
    }

    void OnGun(Gun gun, GunEvent e, int n)
    {
        Vector3 at = Muzzle();
        WeaponDef d = gun.Def;
        switch (e)
        {
            case GunEvent.Fired:
                if (d.Ammo == d.Item) Game.Pack.Remove(d.Item, n); // it is what was thrown
                break;
            case GunEvent.Dry: Sounds.Play(d, VoiceKind.Mechanism, at, 1.7f, -10f); break;
            case GunEvent.Pumped: Sounds.Play(d, VoiceKind.Mechanism, at); break;
            case GunEvent.ReloadStarted: Sounds.Play(d, VoiceKind.Reload, at); break;
            case GunEvent.ReloadRound: Sounds.Play(d, VoiceKind.Mechanism, at, 1.3f, -8f); break;
            case GunEvent.Stage: Sounds.Play(d, VoiceKind.Stage, at, 1f + 0.18f * n); break;
            case GunEvent.ModeChanged: Sounds.Play(d, VoiceKind.Mechanism, at, 1.2f, -6f); break;
        }
    }

    /// <summary>Where shots leave: the held gun's muzzle when it shows, else just ahead of the eye, low and right.</summary>
    public Vector3 Muzzle() => Game.Hands?.BarrelTip ?? Game.Tool.Muzzle();

    /// <summary>The point the crosshair is on (a shot from the muzzle flies to it).</summary>
    public Vector3 AimPoint(float range = 300f)
    {
        Vector3 eye = Game.Survivor.WorldEye(), look = Game.Survivor.WorldLook();
        return Sweep(eye.ToNumerics(), (eye + look * range).ToNumerics(), null, 0, 0f, out ShotHit h) ? h.Point.ToGodot() : eye + look * range;
    }

    /// <summary>Fires so many shots of a row (at a charge stage) from the muzzle, toward the crosshair.</summary>
    public void Fire(WeaponDef d, int shots, int stage = 0)
    {
        Survivor sv = Game.Survivor;
        Vector3 look = sv.WorldLook();
        Vector3 muzzle = Muzzle();
        Vector3 aim = AimPoint();
        Vector3 to = aim - muzzle;
        Vector3 dir = to.Length() > 1.5f && to.Normalized().Dot(look) > 0.6f ? to.Normalized() : look;
        bool moving = new Vector2(sv.Body.Velocity.X, sv.Body.Velocity.Z).Length() > 1.5f;
        float spread = moving ? d.SpreadMoving : d.Spread;
        Fired += shots;
        if (d.Cone != null)
        {
            for (int k = 0; k < shots; k++) ConeTick(d, muzzle, dir);
            Sounds.Hold(d, muzzle);
            if (Fired % 6 < shots) Game.Shots.Muzzle(d.Feel, muzzle, dir, default, 1f, Game.Tool.HeldMuzzle);
            Zone.Wildlife.Noise(muzzle, d.Noise);
            return;
        }
        float speed = d.Speed + stage * (d.Charge?.Speed ?? 0f);
        float damage = d.Damage + stage * (d.Charge?.Damage ?? 0f);
        int pierce = stage * (d.Charge?.Pierce ?? 0);
        int count = d.Pellets?.Count ?? d.Swarm?.Count ?? 1;
        float wide = count > 1 ? d.Pellets?.Spread ?? 0f : 0f;
        for (int k = 0; k < shots; k++)
            for (int p = 0; p < count; p++)
            {
                Vector3 v = Jitter(dir, spread + wide) * speed;
                if (d.Delivery == Delivery.Placed) v += sv.WorldUp() * 2.5f; // an underarm throw arcs up
                Pool.Spawn(d, 0, muzzle.ToNumerics(), v.ToNumerics(), damage, pierce);
            }
        if (d.Backblast != null) Backblast(d, muzzle, dir);
        Color tint = GunModel.ColourOf(d.Tracer.Colour);
        Game.Shots.Muzzle(d.Feel, muzzle, dir, tint, 1f + stage * 0.3f, Game.Tool.HeldMuzzle);
        Game.Hands?.Recoil(GunFeel.Get(d.Feel), d.Recoil * (1f + stage * 0.35f));
        Sounds.Play(d, VoiceKind.Report, muzzle, (0.96f + 0.08f * (float)_rng.NextDouble()) * (1f - stage * 0.06f));
        Zone.Wildlife.Noise(muzzle, d.Noise * (1f + stage * 0.25f));
        PoiPlaces places = Game.Places;
        if (places != null && d.Noise > 30f) (places.TownAt(muzzle) ?? places.TownAt(muzzle + dir * 30f))?.Provoked();
    }

    float _swingIn;

    /// <summary>Shots someone else fired (people, robots), and blows they struck (tests).</summary>
    public int OthersFired, OthersStruck;

    /// <summary>The owner of a shot fired by anyone but the player: it can hit the player (it isn't friendly fire).</summary>
    public const int Hostile = -1;

    /// <summary>
    /// Someone else's weapon goes off (a person, a robot's mount): the row's own shot, pellets,
    /// spread, speed, damage, flash and report, from their muzzle along their aim, plus their own
    /// unsteadiness (degrees). The shooter is looked past until the shot has struck something. Ammo is
    /// theirs to count. A strike row is a blow instead.
    /// </summary>
    public void FireFrom(WeaponDef d, Vector3 muzzle, Vector3 dir, float unsteady, object shooter, bool moving = false)
    {
        if (d.Delivery == Delivery.Strike) { Swing(d, muzzle, dir, shooter, Hostile); return; }
        dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector3.Forward;
        float spread = (moving ? d.SpreadMoving : d.Spread) + MathF.Max(0f, unsteady);
        int count = d.Pellets?.Count ?? d.Swarm?.Count ?? 1;
        float wide = count > 1 ? d.Pellets?.Spread ?? 0f : 0f;
        OthersFired++;
        if (d.Cone != null)
        {
            ConeTick(d, muzzle, dir, Hostile, shooter);
            Zone.Wildlife.Noise(muzzle, d.Noise);
            return;
        }
        for (int p = 0; p < count; p++)
        {
            Vector3 v = Jitter(dir, spread + wide) * d.Speed;
            int slot = Pool.Spawn(d, Hostile, muzzle.ToNumerics(), v.ToNumerics(), d.Damage);
            if (slot >= 0) Pool.Shots[slot].LastHit = shooter;
        }
        Game.Shots.Muzzle(d.Feel, muzzle, dir, GunModel.ColourOf(d.Tracer.Colour), 1f);
        Sounds.Play(d, VoiceKind.Report, muzzle, 0.94f + 0.12f * (float)_rng.NextDouble());
        Zone.Wildlife.Noise(muzzle, d.Noise);
    }

    /// <summary>
    /// A blow with a hand weapon (or bare hands): whatever is first along the line from where it
    /// starts, within the row's reach, takes the row's damage as a shot of it would. True if it landed.
    /// </summary>
    public bool Swing(WeaponDef d, Vector3 from, Vector3 dir, object attacker, int owner)
    {
        dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector3.Forward;
        float reach = MathF.Max(0.5f, d.Reach);
        if (owner != 0) OthersStruck++;
        if (!Sweep(from.ToNumerics(), (from + dir * reach).ToNumerics(), attacker, owner, 1f, out ShotHit hit))
        {
            Sounds.Play(d, VoiceKind.Mechanism, from, 1.4f, -12f); // the swish of a miss
            return false;
        }
        var blow = new Shot { Live = true, Def = d, Owner = owner, Pos = from.ToNumerics(), Vel = (dir * 6f).ToNumerics(), Damage = d.Damage, State = ShotState.Flying };
        Strike(ref blow, hit, d.Damage);
        if (owner == 0 && hit.Kind == HitKind.Player) return false;
        Zone.Wildlife.Noise(from, d.Noise);
        return true;
    }

    /// <summary>A random direction within so many degrees of another, evenly over the cone.</summary>
    Vector3 Jitter(Vector3 dir, float degrees)
    {
        if (degrees <= 0f) return dir;
        Vector3 side = MathF.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        Vector3 u = dir.Cross(side).Normalized(), w = dir.Cross(u);
        float a = Mathf.DegToRad(degrees) * MathF.Sqrt((float)_rng.NextDouble()), spin = (float)_rng.NextDouble() * MathF.Tau;
        return (dir * MathF.Cos(a) + (u * MathF.Cos(spin) + w * MathF.Sin(spin)) * MathF.Sin(a)).Normalized();
    }

    // ---- The cone: a flame's wedge -----------------------------------------------------------------

    /// <summary>
    /// One tick of a cone: everything living inside its wedge, in reach and not behind a wall, takes
    /// the tick's damage and a burn timer, and a creature runs from it. Blocks take only what the row
    /// says (a flame: nothing on stone or steel). A few short licks of flame show it, harmless themselves.
    /// </summary>
    void ConeTick(WeaponDef d, Vector3 origin, Vector3 dir, int owner = 0, object ignore = null)
    {
        ConePart cone = d.Cone;
        float cos = MathF.Cos(Mathf.DegToRad(cone.Angle));
        N.Vector3 o = origin.ToNumerics(), nd = dir.ToNumerics();
        WildlifePatch w = Zone.Wildlife;
        for (int si = -1; si < w.Sites.Count; si++)
        {
            WildSim sim = si < 0 ? w.Sim : w.Sites[si].Sim;
            if (sim == null) continue;
            Vector3 so = si < 0 ? w.Origin : w.Sites[si].GlobalPosition;
            List<Creature> list = sim.Creatures;
            for (int i = 0; i < list.Count; i++)
            {
                Creature c = list[i];
                if (c.Dead || c.Removed) continue;
                if (!BodyInWedge(c, so, o, nd, cos, cone.Range, out Vector3 centre)) continue;
                if (SolidBetween(origin, centre)) continue;
                if (c == ignore) continue;
                ConeHits++;
                Harm(c, d.Damage, centre, dir, byPlayer: owner == 0);
                if (d.Dot != null)
                {
                    Effects.Add(c, d.Dot.PerSecond, d.Dot.Seconds, d.Class, d.Tags, owner);
                    if (d.Dot.Flee > 0f) sim.Scare(c, origin - so, d.Dot.Flee);
                }
            }
        }
        PoiPlaces places = Game.Places;
        if (places != null)
            foreach (Place pl in places.Standing)
            {
                if (pl.Memory.Town is not Town t) continue;
                foreach (Town.Person p in t.People)
                {
                    if (p.Dead || p.Indoors || p == ignore) continue;
                    Vector3 at = t.World(p.Pos) + Vector3.Up;
                    if (!ShotPool.InCone(o, nd, cos, cone.Range, at.ToNumerics()) || SolidBetween(origin, at)) continue;
                    ConeHits++;
                    PersonHits++;
                    t.Struck(p, d.Damage, d.Class, byPlayer: owner == 0);
                    Dealt?.Invoke(HitKind.Person, p, d.Damage);
                }
            }
        // Someone else's flame reaches the player too.
        if (owner != 0 && !Game.Suit.Dead)
        {
            Vector3 chest = Game.Survivor.WorldFeet + Game.Survivor.WorldUp() * 1.1f;
            if (ShotPool.InCone(o, nd, cos, cone.Range, chest.ToNumerics()) && !SolidBetween(origin, chest)) HurtPlayer(new HitInfo(d.Damage, d.Class, d.Tags, owner), origin);
        }
        // The look: short licks of flame thrown along the wedge.
        for (int k = 0; k < 4; k++)
        {
            Vector3 v = Jitter(dir, cone.Angle * 0.7f) * d.Speed * (0.8f + 0.4f * (float)_rng.NextDouble());
            int slot = Pool.Spawn(d, owner, o, v.ToNumerics(), 0f);
            if (slot >= 0)
            {
                Pool.Shots[slot].Visual = true;
                Pool.Shots[slot].Life = d.Life * (0.8f + 0.4f * (float)_rng.NextDouble());
            }
        }
    }

    /// <summary>A rocket's backblast: whatever stands behind the tube is hurt.</summary>
    void Backblast(WeaponDef d, Vector3 muzzle, Vector3 dir)
    {
        BackblastPart b = d.Backblast;
        float cos = MathF.Cos(Mathf.DegToRad(b.Angle));
        Vector3 back = muzzle - dir * 0.6f;
        WildlifePatch w = Zone.Wildlife;
        for (int si = -1; si < w.Sites.Count; si++)
        {
            WildSim sim = si < 0 ? w.Sim : w.Sites[si].Sim;
            if (sim == null) continue;
            Vector3 so = si < 0 ? w.Origin : w.Sites[si].GlobalPosition;
            foreach (Creature c in sim.Creatures)
            {
                if (c.Dead || c.Removed) continue;
                if (BodyInWedge(c, so, back.ToNumerics(), (-dir).ToNumerics(), cos, b.Length, out Vector3 centre) && !SolidBetween(back, centre))
                    Harm(c, b.Damage, centre, -dir);
            }
        }
    }

    // ---- What a shot meets ----------------------------------------------------------------------

    public N.Vector3 Gravity(N.Vector3 at) => new(0f, -Zone.Data.Rules.Planet.Gravity, 0f);

    /// <summary>
    /// The nearest thing along a segment: the colliders of the world (ground, places, hulls, trees),
    /// the voxel ground where nothing is loaded, creatures' and people's hit spheres, and the player
    /// once a shot has left the muzzle.
    /// </summary>
    public bool Sweep(N.Vector3 from, N.Vector3 to, object ignore, int owner, float age, out ShotHit hit)
    {
        hit = default;
        Vector3 a = from.ToGodot(), b = to.ToGodot();
        Vector3 seg = b - a;
        float len = seg.Length();
        if (len < 1e-5f) return false;
        Vector3 dir = seg / len;
        float best = len;
        bool found = SolidHit(a, dir, len, owner, age, ref hit);
        if (found) best = hit.Distance;

        // A creature: the one a pierce just went through, or a corpse, is looked past.
        Creature c = null;
        float cd = 0f, offset = 0f;
        Vector3 cp = default;
        for (int k = 0; k < 4 && offset < best; k++)
        {
            c = Zone.Wildlife.Raycast(a + dir * offset, dir, best - offset, out cd, out cp);
            if (c == null) break;
            cd += offset;
            if (!c.Dead && c != ignore) break;
            offset = MathF.Max(cd + 0.05f, Exit(c, a, dir) + 0.02f); // on from where the line leaves its body
            c = null;
        }
        if (c != null && cd < best)
        {
            best = cd;
            found = true;
            hit = new ShotHit { Kind = HitKind.Creature, Thing = c, Point = cp.ToNumerics(), Normal = (-dir).ToNumerics(), Distance = cd, Through = 0.2f };
        }
        PoiPlaces places = Game.Places;
        if (places != null && places.Standing.Count > 0)
        {
            Town.Person p = places.PersonAlong(a, dir, best, out float pd);
            if (p != null && p != ignore && pd < best)
            {
                best = pd;
                found = true;
                hit = new ShotHit { Kind = HitKind.Person, Thing = p, Point = (a + dir * pd).ToNumerics(), Normal = (-dir).ToNumerics(), Distance = pd, Through = 0.3f };
            }
        }
        if ((owner != 0 || age > 0.12f) && ignore != PlayerThing && PlayerAlong(a, dir, best, out float md))
        {
            found = true;
            hit = new ShotHit { Kind = HitKind.Player, Thing = PlayerThing, Point = (a + dir * md).ToNumerics(), Normal = (-dir).ToNumerics(), Distance = md };
        }
        return found;
    }

    int _exceptions = -1;

    /// <summary>
    /// The shot ray passes the creatures' blockers (bodies that only keep the player off them, on the
    /// props layer with tree trunks): a shot meets the creature's own hit spheres instead. They are
    /// taken from <see cref="WorldRay.Ignored"/> whenever that list grows, which is seldom.
    /// </summary>
    void SyncExceptions()
    {
        var ignored = WorldRay.Ignored;
        int n = ignored.Count;
        if (n == _exceptions) return;
        if (n < _exceptions || _exceptions < 0)
        {
            _ray.ClearExceptions();
            _exceptions = 0;
        }
        // The list only grows (its bodies are pooled and reused), so only the new ones are added.
        for (int i = _exceptions; i < n; i++) _ray.AddExceptionRid(ignored[i]);
        _exceptions = n;
    }

    /// <summary>The solid part of a sweep: colliders first, then the voxel ground where colliders aren't loaded.</summary>
    bool SolidHit(Vector3 a, Vector3 dir, float len, int owner, float age, ref ShotHit hit, int ghosts = 0)
    {
        Vessel aboard = owner == 0 ? Game.Survivor.Aboard : null;
        bool solid;
        Vector3 point, normal;
        GodotObject collider;
        if (aboard != null)
        {
            // Aboard, the cabin answers for the structure you are in (WorldRay's rule).
            solid = WorldRay.Cast(Zone, aboard, a, dir, len, SolidMask, out LookHit look);
            point = look.Point;
            normal = look.Normal;
            collider = look.Collider;
        }
        else
        {
            _ray.GlobalPosition = a;
            _ray.TargetPosition = dir * len;
            _ray.ForceRaycastUpdate();
            solid = _ray.IsColliding();
            point = solid ? _ray.GetCollisionPoint() : default;
            normal = solid ? _ray.GetCollisionNormal() : default;
            collider = solid ? _ray.GetCollider() : null;
        }
        if (solid)
        {
            float d = a.DistanceTo(point);
            Vessel v = collider == null ? null : Zone.VesselOf(Zone.Host.BodyOf(collider));
            if (v != null)
            {
                Int3 cell = StructureBridge.CellAt(v.Structure, (v.Body.ToLocal(point.ToNumerics()) - GalHack.Physics.MathX.Unrotate(v.Body.Rotation, normal.ToNumerics()) * 0.02f));
                // A block just taken away keeps its collider until the structure's shapes are rebuilt: nothing is
                // there, so the ray goes on through it (a mine dropping off a shot-away block lands on what is under it).
                if (!v.Structure.Has(cell))
                {
                    if (ghosts >= 4 || d + 0.02f >= len) return false;
                    if (!SolidHit(point + dir * 0.02f, dir, len - d - 0.02f, owner, age, ref hit, ghosts + 1)) return false;
                    hit.Distance += d + 0.02f;
                    return true;
                }
                hit = new ShotHit { Kind = HitKind.Block, Thing = v, CellX = cell.X, CellY = cell.Y, CellZ = cell.Z, Point = point.ToNumerics(), Normal = normal.ToNumerics(), Distance = d, Through = v.Structure.Grid.CellSize };
            }
            else hit = new ShotHit { Kind = HitKind.Ground, Point = point.ToNumerics(), Normal = normal.ToNumerics(), Distance = d };
            return true;
        }
        // No collider on the way: the voxel ground, where colliders may not be loaded (far off). A
        // cube's corner stands proud of the drawn ground, so where the drawn ground has its collider
        // under that point, the collider has had the say and the cube is only a corner.
        Vector3 end = a + dir * len;
        if (MathF.Min(a.Y, end.Y) > MathF.Max(Zone.GroundAt(a.X, a.Z).Y, Zone.GroundAt(end.X, end.Z).Y) + 4f) return false; // well up in the air: no ground to meet
        var store = Zone.Planet.Terrain.Store;
        if (!store.Raycast(a.ToNumerics(), dir.ToNumerics(), len, out _, out TerrainDigging.Terrain.Int3 face, out float vd)) return false;
        float graze = Table.Rules.GroundGraze;
        _ray.GlobalPosition = a + dir * vd + Vector3.Up * graze;
        _ray.TargetPosition = Vector3.Down * (graze * 2f + 1f);
        _ray.ForceRaycastUpdate();
        if (_ray.IsColliding()) return false;
        var n = new Vector3(face.X, face.Y, face.Z);
        hit = new ShotHit { Kind = HitKind.Ground, Point = (a + dir * vd).ToNumerics(), Normal = (n.LengthSquared() > 0.5f ? n.Normalized() : -dir).ToNumerics(), Distance = vd };
        return true;
    }

    /// <summary>What stands between two points, said in words (tests: why a flame or an arc didn't reach).</summary>
    public string Probe(Vector3 a, Vector3 b)
    {
        Vector3 seg = b - a;
        float len = seg.Length();
        ShotHit h = default;
        if (len < 0.05f || !SolidHit(a, seg / len, len - 0.3f, 1, 1f, ref h)) return "clear";
        _ray.GlobalPosition = a;
        _ray.TargetPosition = seg;
        _ray.ForceRaycastUpdate();
        string what = _ray.IsColliding() && _ray.GetCollider() is Node n ? $"{n.Name} ({n.GetClass()}, layer {(_ray.GetCollider() as CollisionObject3D)?.CollisionLayer})" : "voxel ground";
        return $"{h.Kind} at {h.Distance:F2} of {len:F2} m: {what}";
    }

    /// <summary>Is anything solid between two points (a flame doesn't go through walls)?</summary>
    bool SolidBetween(Vector3 a, Vector3 b)
    {
        Vector3 seg = b - a;
        float len = seg.Length();
        if (len < 0.05f) return false;
        ShotHit h = default;
        return SolidHit(a, seg / len, len - 0.3f, 1, 1f, ref h);
    }

    /// <summary>How far along a line (zone frame, unit direction) it leaves a creature's body: the furthest exit from its hit spheres.</summary>
    float Exit(Creature c, Vector3 from, Vector3 dir)
    {
        if (c.Rig == null) return 0f;
        Zone.Wildlife.SimOf(c, out Vector3 so, out _);
        Vector3 o = from - so;
        float exit = 0f;
        for (int i = 0; i < c.Rig.HitCount; i++)
        {
            Vector4 s = c.Rig.Hits[i];
            Vector3 m = o - new Vector3(s.X, s.Y, s.Z);
            float b = m.Dot(dir), cc = m.Dot(m) - s.W * s.W, disc = b * b - cc;
            if (disc > 0f) exit = MathF.Max(exit, -b + MathF.Sqrt(disc));
        }
        return exit;
    }

    /// <summary>A shot's line against the player's body (a capsule from the knees to the eye).</summary>
    bool PlayerAlong(Vector3 a, Vector3 dir, float reach, out float distance)
    {
        distance = 0f;
        if (Game.Suit.Dead || Game.Survivor.Seated != null) return false;
        Vector3 p = Game.Survivor.WorldFeet + Game.Survivor.WorldUp() * 0.3f, q = Game.Survivor.WorldEye();
        if (!SegmentNear(a, a + dir * reach, p, q, out float s, out float dist) || dist > 0.36f) return false;
        distance = s * reach;
        return true;
    }

    /// <summary>The closest approach of two segments: the share along the first, and the distance.</summary>
    static bool SegmentNear(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out float s, out float dist)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r);
        float t;
        if (a < 1e-8f) { s = 0f; t = Mathf.Clamp(f / Mathf.Max(e, 1e-8f), 0f, 1f); }
        else
        {
            float c = d1.Dot(r);
            float b = d1.Dot(d2), denom = a * e - b * b;
            s = denom > 1e-8f ? Mathf.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
            t = (b * s + f) / Mathf.Max(e, 1e-8f);
            if (t < 0f) { t = 0f; s = Mathf.Clamp(-c / a, 0f, 1f); }
            else if (t > 1f) { t = 1f; s = Mathf.Clamp((b - c) / a, 0f, 1f); }
        }
        dist = (p1 + d1 * s).DistanceTo(p2 + d2 * t);
        return true;
    }

    // ---- Seekers ----------------------------------------------------------------------------------

    public bool Seek(Seek kind, N.Vector3 from, N.Vector3 dir, float range, float coneCos, int owner, out object target)
    {
        target = null;
        Vector3 o = from.ToGodot(), d = dir.ToGodot();
        float best = kind == GalHack.Arms.Seek.Aim ? coneCos : range;
        WildlifePatch w = Zone.Wildlife;
        for (int si = -1; si < w.Sites.Count; si++)
        {
            WildSim sim = si < 0 ? w.Sim : w.Sites[si].Sim;
            if (sim == null) continue;
            Vector3 so = si < 0 ? w.Origin : w.Sites[si].GlobalPosition;
            List<Creature> list = sim.Creatures;
            for (int i = 0; i < list.Count; i++)
            {
                Creature c = list[i];
                if (c.Dead || c.Removed) continue;
                Vector3 to = Centre(c, so) - o;
                float dist = to.Length();
                if (dist > range || dist < 0.01f) continue;
                if (kind == GalHack.Arms.Seek.Aim)
                {
                    // What you aim near: the one closest to the line, inside the cone.
                    float cos = to.Dot(d) / dist;
                    if (cos > best) { best = cos; target = c; }
                }
                else if (dist < best) { best = dist; target = c; }
            }
        }
        return target != null;
    }

    public bool Where(object target, out N.Vector3 at)
    {
        at = default;
        if (target is not Creature c || c.Dead || c.Removed) return false;
        Zone.Wildlife.SimOf(c, out Vector3 so, out _);
        at = Centre(c, so).ToNumerics();
        return true;
    }

    /// <summary>
    /// How far a point is from a creature's body: the nearest of its hit spheres' surfaces (0 inside
    /// one), so a long neck or tail doesn't reach where the body isn't. Falls back to its middle.
    /// </summary>
    static float Gap(Creature c, Vector3 simOrigin, Vector3 p)
    {
        Rig rig = c.Rig;
        if (rig == null || rig.HitCount == 0) return MathF.Max(0f, p.DistanceTo(Centre(c, simOrigin)) - 0.4f);
        float best = float.MaxValue;
        for (int i = 0; i < rig.HitCount; i++)
        {
            Vector4 h = rig.Hits[i];
            best = MathF.Min(best, p.DistanceTo(new Vector3(h.X, h.Y, h.Z) + simOrigin) - h.W);
        }
        return MathF.Max(0f, best);
    }

    /// <summary>Does any of a creature's hit spheres reach into a wedge? Gives the point of it that does (where the flame lands).</summary>
    static bool BodyInWedge(Creature c, Vector3 simOrigin, N.Vector3 o, N.Vector3 dir, float cos, float range, out Vector3 at)
    {
        Rig rig = c.Rig;
        if (rig == null || rig.HitCount == 0)
        {
            at = Centre(c, simOrigin);
            return ShotPool.InWedge(o, dir, cos, range, at.ToNumerics(), 0.4f);
        }
        for (int i = 0; i < rig.HitCount; i++)
        {
            Vector4 h = rig.Hits[i];
            at = new Vector3(h.X, h.Y, h.Z) + simOrigin;
            if (ShotPool.InWedge(o, dir, cos, range, at.ToNumerics(), h.W)) return true;
        }
        at = default;
        return false;
    }

    /// <summary>A creature's middle in the zone (its hit spheres' bounds).</summary>
    static Vector3 Centre(Creature c, Vector3 simOrigin) =>
        c.Rig != null ? c.Rig.BoundsCenter + simOrigin : c.Position + simOrigin + Vector3.Up * 0.5f;

    // ---- Harm -------------------------------------------------------------------------------------

    readonly (HitKind Kind, WeaponDef Def, Vector3 At)[] _recent = new (HitKind, WeaponDef, Vector3)[8];
    int _recentNext;

    /// <summary>The last few things shots struck, oldest first, in words (tests: what a missed shot met instead).</summary>
    public string Recent()
    {
        var parts = new List<string>();
        for (int i = 0; i < _recent.Length; i++)
        {
            var r = _recent[(_recentNext + i) % _recent.Length];
            if (r.Def != null) parts.Add($"{r.Def.Id} {r.Kind} at {r.At.DistanceTo(Game.Survivor.WorldEye()):F1} m");
        }
        return string.Join("; ", parts);
    }

    public void Strike(ref Shot s, in ShotHit hit, float damage)
    {
        WeaponDef d = s.Def;
        if (s.State != ShotState.Stuck)
        {
            _recent[_recentNext] = (hit.Kind, d, hit.Point.ToGodot());
            _recentNext = (_recentNext + 1) % _recent.Length;
        }
        Vector3 at = hit.Point.ToGodot(), n = hit.Normal.ToGodot();
        Vector3 dir = s.Vel.LengthSquared() > 1e-4f ? s.Vel.ToGodot().Normalized() : -n;
        bool tick = s.State == ShotState.Stuck;
        if (tick) DotTicks++;
        else Hits++;
        switch (hit.Kind)
        {
            case HitKind.Creature:
                var c = (Creature)hit.Thing;
                Harm(c, damage * Zone.Wildlife.HitScale(c, at), at, dir, quiet: tick, byPlayer: s.Owner == 0);
                if (!tick && d.Dot != null && d.Stick == null) Effects.Add(c, d.Dot.PerSecond, d.Dot.Seconds, d.Class, d.Tags, s.Owner);
                if (!tick) Game.Visor?.Hit();
                break;
            case HitKind.Person:
                var p = (Town.Person)hit.Thing;
                p.Town.Struck(p, damage, d.Class, byPlayer: s.Owner == 0);
                PersonHits++;
                Dealt?.Invoke(HitKind.Person, p, damage);
                if (!tick) Game.Shots.Impact(at, n, ImpactKind.Creature);
                break;
            case HitKind.Player:
                HurtPlayer(new HitInfo(damage, d.Class, d.Tags, s.Owner), at);
                break;
            case HitKind.Block:
                var v = (Vessel)hit.Thing;
                float block = damage * d.Blocks;
                if (block > 0f && GodotObject.IsInstanceValid(v.Node))
                {
                    v.Hits.Shot(new Int3(hit.CellX, hit.CellY, hit.CellZ), hit.Point, hit.Normal, block, TypeOf(d.Class));
                    BlockHits++;
                    Dealt?.Invoke(HitKind.Block, v, block);
                }
                if (!tick) Game.Shots.Impact(at, n, ImpactKind.Block, v.Node);
                break;
            default:
                GroundHits++;
                if (!tick) Game.Shots.Impact(at, n, ImpactKind.Ground);
                break;
        }
        if (!tick && hit.Kind != HitKind.Creature) Sounds.Play(d, VoiceKind.Impact, at, 0.9f + 0.2f * (float)_rng.NextDouble(), -6f);
    }

    /// <summary>Harm to a creature, in whichever sim it lives: wildlife's damage (it turns on who did it), its blood and its cry.</summary>
    void Harm(Creature c, float amount, Vector3 at, Vector3 dir, bool quiet = false, bool byPlayer = true)
    {
        if (amount <= 0f) return;
        Zone.Wildlife.Damage(c, amount, byPlayer);
        CreatureHits++;
        Dealt?.Invoke(HitKind.Creature, c, amount);
        if (!quiet) Game.CreatureStruck(c, at, dir);
    }

    static DamageType TypeOf(DamageClass c) => c switch
    {
        DamageClass.Explosive => DamageType.Explosive,
        DamageClass.Energy => DamageType.Energy,
        _ => DamageType.Kinetic,
    };

    /// <summary>
    /// Harm that reaches the player. Self-harm is on (owner's decision): your own blast, bounce or arc
    /// hurts you. Friendly fire (another player's) is a server setting; the slice has no other players.
    /// This is where a personal shield will sit: it takes the hit first unless <see cref="HitInfo.PassesShield"/>
    /// (flame, gas and radiation go straight to health).
    /// </summary>
    public void HurtPlayer(in HitInfo hit, Vector3 from)
    {
        if (hit.Source == 0 && !Table.Rules.SelfHarm) return;
        if (hit.Source != 0 && hit.Source != -1 && !Table.Rules.FriendlyFire) return;
        if (Game.Suit.Dead || hit.Amount <= 0f) return;
        double max = Game.Suit.Sim.Data.Vitals.Health.Max;
        var kind = (hit.Tags & HarmTags.Fire) != 0 ? SurvivalVitals.Model.DamageKind.Heat : SurvivalVitals.Model.DamageKind.Hit;
        double lost = Game.Suit.Hit(hit.Amount / 100.0 * max, kind);
        PlayerHits++;
        PlayerDamage += (float)lost;
        Dealt?.Invoke(HitKind.Player, PlayerThing, hit.Amount);
        Game.Visor?.Hurt(from);
        Game.Hands?.Jolt(from, MathF.Min(1f, hit.Amount / 30f));
    }

    public void Blast(ref Shot s, N.Vector3 at, N.Vector3 normal, BlastPart blast)
    {
        Blasts++;
        if (s.State == ShotState.Armed) Trips++;
        Vector3 p = at.ToGodot(), n = normal.ToGodot();
        float r = blast.Radius;
        WildlifePatch w = Zone.Wildlife;
        for (int si = -1; si < w.Sites.Count; si++)
        {
            WildSim sim = si < 0 ? w.Sim : w.Sites[si].Sim;
            if (sim == null) continue;
            Vector3 so = si < 0 ? w.Origin : w.Sites[si].GlobalPosition;
            List<Creature> list = sim.Creatures;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Creature c = list[i];
                if (c.Dead || c.Removed) continue;
                Vector3 centre = Centre(c, so);
                float d = Gap(c, so, p);
                if (d < r) Harm(c, blast.Damage * (1f - d / r), centre, (centre - p).Normalized(), byPlayer: s.Owner == 0);
            }
        }
        Vector3 chest = Game.Survivor.WorldFeet + Game.Survivor.WorldUp() * 1f;
        float pd = p.DistanceTo(chest);
        if (pd < r) HurtPlayer(new HitInfo(blast.Damage * (1f - pd / r), DamageClass.Explosive, s.Def.Tags | HarmTags.Force, s.Owner), p);
        foreach (Vessel v in Zone.Vessels)
        {
            if (!GodotObject.IsInstanceValid(v.Node)) continue;
            if (v.DistanceFromHull(v.Body.ToLocal(at), r) >= r) continue;
            v.Hits.Blast(at, r, blast.Damage * blast.Blocks, DamageType.Explosive);
            BlockHits++;
            Dealt?.Invoke(HitKind.Block, v, blast.Damage * blast.Blocks);
        }
        PoiPlaces places = Game.Places;
        if (places != null)
            foreach (Place pl in places.Standing)
            {
                if (pl.Memory.Town is not Town t) continue;
                foreach (Town.Person person in t.People)
                {
                    if (person.Dead) continue;
                    float d = t.World(person.Pos).DistanceTo(p);
                    if (d < r) { t.Struck(person, blast.Damage * (1f - d / r), DamageClass.Explosive, byPlayer: s.Owner == 0); PersonHits++; }
                }
            }
        Fx.Blast(p, r * 0.55f);
        Game.Shots.Impact(p, n, ImpactKind.Ground);
        Zone.Dust?.Puff(p, Zone.Planet.Grit(p), 1.4f);
        Sounds.Play(s.Def, VoiceKind.Impact, p);
        Zone.Wildlife.Noise(p, 150f);
        Game.Hands?.Shake(Mathf.Clamp(1f - Game.Survivor.WorldEye().DistanceTo(p) / 40f, 0f, 0.8f));
    }

    // ---- Holding on: stuck worms, set mines ---------------------------------------------------------

    public bool Hold(in ShotHit hit, out Anchor a)
    {
        a = default;
        switch (hit.Kind)
        {
            case HitKind.Ground:
                a = new Anchor { Kind = HitKind.Ground, Local = hit.Point, LocalNormal = hit.Normal };
                return true;
            case HitKind.Block:
                var v = (Vessel)hit.Thing;
                a = new Anchor
                {
                    Kind = HitKind.Block, Thing = v, CellX = hit.CellX, CellY = hit.CellY, CellZ = hit.CellZ,
                    Local = v.Body.ToLocal(hit.Point), LocalNormal = GalHack.Physics.MathX.Unrotate(v.Body.Rotation, hit.Normal),
                };
                return true;
            case HitKind.Creature:
                var c = (Creature)hit.Thing;
                Zone.Wildlife.SimOf(c, out Vector3 so, out _);
                var turn = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -c.Yaw);
                a = new Anchor
                {
                    Kind = HitKind.Creature, Thing = c, Local = N.Vector3.Transform(hit.Point - (c.Position + so).ToNumerics(), turn),
                    LocalNormal = N.Vector3.Transform(hit.Normal, turn),
                };
                return true;
            default:
                return false;
        }
    }

    public bool Held(in Anchor a, bool verify, out N.Vector3 at, out N.Vector3 normal)
    {
        at = a.Local;
        normal = a.LocalNormal;
        switch (a.Kind)
        {
            case HitKind.Ground:
                return !verify || GroundUnder(a.Local.ToGodot(), a.LocalNormal.ToGodot());
            case HitKind.Block:
                var v = (Vessel)a.Thing;
                if (!GodotObject.IsInstanceValid(v.Node) || !Zone.Vessels.Contains(v) || !v.Structure.Has(new Int3(a.CellX, a.CellY, a.CellZ))) return false;
                at = v.Body.ToParent(a.Local);
                normal = GalHack.Physics.MathX.Rotate(v.Body.Rotation, a.LocalNormal);
                return true;
            case HitKind.Creature:
                var c = (Creature)a.Thing;
                if (c.Dead || c.Removed) return false;
                Zone.Wildlife.SimOf(c, out Vector3 so, out _);
                var turn = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, c.Yaw);
                at = (c.Position + so).ToNumerics() + N.Vector3.Transform(a.Local, turn);
                normal = N.Vector3.Transform(a.LocalNormal, turn);
                return true;
        }
        return false;
    }

    /// <summary>Is there still ground under a point on it: a collider just behind its face, or solid voxels where none is loaded?</summary>
    bool GroundUnder(Vector3 at, Vector3 normal)
    {
        _ray.GlobalPosition = at + normal * 0.2f;
        _ray.TargetPosition = -normal * 0.55f;
        _ray.ForceRaycastUpdate();
        if (_ray.IsColliding()) return true;
        var store = Zone.Planet.Terrain.Store;
        foreach (float depth in Depths)
        {
            Vector3 q = at - normal * depth;
            if (store.MaterialAt(new TerrainDigging.Terrain.Int3((int)MathF.Floor(q.X), (int)MathF.Floor(q.Y), (int)MathF.Floor(q.Z))) != TerrainDigging.Terrain.CellMaterial.Air) return true;
        }
        return false;
    }

    static readonly float[] Depths = { 0.3f, 0.8f };

    /// <summary>Anything that trips a mine: a living creature, a townsperson or the player within the radius, or a structure moving past (never the one it is on).</summary>
    public bool Near(N.Vector3 at, float radius, object exclude)
    {
        Vector3 p = at.ToGodot();
        WildlifePatch w = Zone.Wildlife;
        for (int si = -1; si < w.Sites.Count; si++)
        {
            WildSim sim = si < 0 ? w.Sim : w.Sites[si].Sim;
            if (sim == null) continue;
            Vector3 so = si < 0 ? w.Origin : w.Sites[si].GlobalPosition;
            List<Creature> list = sim.Creatures;
            for (int i = 0; i < list.Count; i++)
            {
                Creature c = list[i];
                if (c.Dead || c.Removed) continue;
                if (Gap(c, so, p) < radius) return true;
            }
        }
        if (!Game.Suit.Dead && SegmentNear(p, p, Game.Survivor.WorldFeet, Game.Survivor.WorldEye(), out _, out float pd) && pd < radius) return true;
        foreach (Vessel v in Zone.Vessels)
        {
            if (v == exclude || v.IsBase || v.Body.Velocity.LengthSquared() < 1f) continue;
            if (v.DistanceFromHull(v.Body.ToLocal(at), radius) < radius) return true;
        }
        return false;
    }

    /// <summary>An arc's next jump: the nearest living thing in reach not yet struck, the player included if they are nearest (self-harm). Wet ones are reached from further.</summary>
    public bool Nearest(N.Vector3 at, float range, float wetReach, object[] done, int count, out object target, out ShotHit hit)
    {
        target = null;
        hit = default;
        Vector3 p = at.ToGodot();
        float wet = Wetness();
        float reach = range * (1f + wet * wetReach);
        float best = reach;
        Vector3 bestAt = default;
        WildlifePatch w = Zone.Wildlife;
        for (int si = -1; si < w.Sites.Count; si++)
        {
            WildSim sim = si < 0 ? w.Sim : w.Sites[si].Sim;
            if (sim == null) continue;
            Vector3 so = si < 0 ? w.Origin : w.Sites[si].GlobalPosition;
            List<Creature> list = sim.Creatures;
            for (int i = 0; i < list.Count; i++)
            {
                Creature c = list[i];
                if (c.Dead || c.Removed || Array.IndexOf(done, c, 0, count) >= 0) continue;
                Vector3 centre = Centre(c, so);
                float d = p.DistanceTo(centre);
                if (d < best && !SolidBetween(p, centre)) { best = d; target = c; bestAt = centre; }
            }
        }
        if (Array.IndexOf(done, PlayerThing, 0, count) < 0 && !Game.Suit.Dead)
        {
            Vector3 chest = Game.Survivor.WorldFeet + Game.Survivor.WorldUp() * 1.1f;
            float d = p.DistanceTo(chest);
            if (d < best && !SolidBetween(p, chest)) { best = d; target = PlayerThing; bestAt = chest; }
        }
        if (target == null) return false;
        hit = new ShotHit
        {
            Kind = target == PlayerThing ? HitKind.Player : HitKind.Creature, Thing = target, Point = bestAt.ToNumerics(),
            Normal = (p - bestAt).Normalized().ToNumerics(), Distance = best,
        };
        return true;
    }

    /// <summary>How wet things are here, 0..1: rain falling and the ground's wetness (lightning reaches further).</summary>
    float Wetness() => Zone.Weather?.Sim == null ? 0f : Mathf.Clamp(Zone.Weather.Sim.Ground.Wetness, 0f, 1f);

    public void Saw(ShotNote note, ref Shot s, N.Vector3 at, N.Vector3 other)
    {
        Vector3 p = at.ToGodot();
        switch (note)
        {
            case ShotNote.Bounced:
                Bounces++;
                Sounds.Play(s.Def, VoiceKind.Impact, p, 1.5f, -12f);
                break;
            case ShotNote.Stuck:
                Sticks++;
                Sounds.Play(s.Def, VoiceKind.Impact, p, 0.8f, -6f);
                break;
            case ShotNote.Placed:
                Placements++;
                Sounds.Play(s.Def, VoiceKind.Mechanism, p, 1.4f, -6f);
                break;
            case ShotNote.Released:
                Releases++;
                break;
            case ShotNote.Arc:
                ArcJumps++;
                Fx.Arc(p, other.ToGodot(), Game.Camera?.GlobalPosition ?? p + Vector3.Up);
                break;
            case ShotNote.Tick:
                if (s.Anchor.Kind == HitKind.Creature && s.Anchor.Thing is Creature c) Zone.Blood.Pop(p, c.Species.Blood, 3);
                break;
        }
    }

    // ---- Timed effects ----------------------------------------------------------------------------

    public bool Alive(object target) => target switch
    {
        Creature c => !c.Dead && !c.Removed,
        _ when target == PlayerThing => !Game.Suit.Dead,
        _ => false,
    };

    public void Harm(object target, in HitInfo hit)
    {
        DotTicks++;
        if (target is Creature c)
        {
            Zone.Wildlife.SimOf(c, out Vector3 so, out _);
            Harm(c, hit.Amount, Centre(c, so), Vector3.Down, quiet: true);
        }
        else if (target == PlayerThing) HurtPlayer(hit, Game.Survivor.WorldEye());
    }

    /// <summary>Everything in flight, stuck or set is gone (a new world).</summary>
    public void Clear()
    {
        Pool.Clear();
        Effects.Clear();
    }
}
