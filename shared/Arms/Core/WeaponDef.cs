using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GalHack.Arms;

/// <summary>How a weapon fires (docs/weapon-ideas.md section 2, Trigger).</summary>
public enum Trigger { Semi, Auto, Burst, Pump, Charge, Hold }

/// <summary>How the effect travels (section 2, Delivery). Lob is a bolt under gravity; the parts say the rest. Strike: a hand weapon's blow, landing within <see cref="WeaponDef.Reach"/>.</summary>
public enum Delivery { Bolt, Pellets, Lob, Cone, Arc, Swarm, Placed, Strike }

/// <summary>The design's three damage classes, for armour and resistance (section 5.1).</summary>
public enum DamageClass { Kinetic, Explosive, Energy }

/// <summary>What kind of harm on top of the class (section 2, Tags). Fire, gas and radiation go past a personal shield.</summary>
[Flags]
public enum HarmTags { None = 0, Fire = 1, Bio = 2, Shock = 4, Gas = 8, Rad = 16, Force = 32 }

/// <summary>What a seeking shot looks for: what you are aiming near, or living flesh.</summary>
public enum Seek { None, Aim, Flesh }

/// <summary>The tech band a gun is built in, which sets how it looks: crude and welded, utilitarian, sleek, or grown.</summary>
public enum Band { Low, Mid, High, Living }

/// <summary>What flies: the shape a shot is drawn with.</summary>
public enum Body { Tracer, Ball, Rocket, Puck, Worm, Hornet, Slug, Spark, Flame }

// ---- The twelve shared parts. A weapon is a row that combines them; nothing is per gun. -------------

/// <summary>Hold to climb through stages; each adds speed, pierce and damage.</summary>
public sealed record ChargePart(int Stages, float Seconds, float Speed, int Pierce, float Damage);
/// <summary>Passes through this many things (creatures or blocks) before it stops.</summary>
public sealed record PiercePart(int Count);
/// <summary>Skips off ground and blocks this many times, keeping this share of its speed. A fuse (seconds) makes it go off on time rather than on impact.</summary>
public sealed record BouncePart(int Count, float Keep, float Fuse);
/// <summary>A spread of bolts per shot, degrees.</summary>
public sealed record PelletsPart(int Count, float Spread);
/// <summary>A hit jumps on to the nearest targets in reach, each once; wet targets carry it further.</summary>
public sealed record ArcPart(int Jumps, float Range, float Damage, float WetReach);
/// <summary>Attaches to what it hits for this long, riding it.</summary>
public sealed record StickPart(float Seconds);
/// <summary>Damage over time on what it hit or stuck to, per second for so many seconds; Flee scares a creature for that long.</summary>
public sealed record DotPart(float PerSecond, float Seconds, float Flee);
/// <summary>Turns toward a target: what you aim near (inside the cone, degrees) or living flesh, at so many radians per metre flown.</summary>
public sealed record HomingPart(Seek Seek, float Cone, float TurnPerMetre, float Range);
/// <summary>Each shot is so many short-lived seekers that wander as they go.</summary>
public sealed record SwarmPart(int Count, float Wander);
/// <summary>A short wedge, range in metres and half-angle in degrees, hitting so many times a second.</summary>
public sealed record ConePart(float Range, float Angle, float Ticks);
/// <summary>A thing set down: arms after so many seconds and goes off when anything comes within the radius. At most Limit at once.</summary>
public sealed record PlacedPart(float Arm, float Radius, int Limit);
/// <summary>An explosion where it lands: radius in metres, damage at the centre, and how hard it is on blocks.</summary>
public sealed record BlastPart(float Radius, float Damage, float Blocks);
/// <summary>A blast out of the back of the tube: length, half-angle in degrees, damage.</summary>
public sealed record BackblastPart(float Length, float Angle, float Damage);

/// <summary>How a shot is drawn: colour (0xRRGGBB), width and length of its streak, and its body.</summary>
public sealed record TracerSpec(uint Colour, float Width, float Length, Body Body, bool Light);

/// <summary>
/// What a gun is made of, for its generated model, its icon and its sound: the band, the calibre
/// (0 a needle, 1 a rocket), the barrel's length, and the parts: stock, magazine or cell, sight,
/// emitter, vents and fins. Our own shapes; nothing here copies a real or famous gun.
/// </summary>
public sealed record LookSpec(Band Band, float Calibre, float Barrel, float Body, string Stock, string Mag, string Sight, string Emitter, int Vents, int Fins, uint Accent);

/// <summary>
/// One weapon as data (shared/Arms/data/weapons.json): trigger, delivery and parts, damage and
/// handling numbers, ammo, feel and look. A weapon with fire modes or shell types has one resolved
/// row per mode (<see cref="Modes"/>): each is the base row with that mode's fields laid over it.
/// Engine-free, so the slice's player and, later, its enemies fire the same rows.
/// </summary>
public sealed class WeaponDef
{
    public string Id { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>The item that is this weapon.</summary>
    public string Item { get; init; } = "";
    /// <summary>This mode's label (SEMI, BURST, SLUG...), or empty when the weapon has one way of firing.</summary>
    public string Mode { get; init; } = "";

    public Trigger Trigger { get; init; }
    public int Burst { get; init; } = 3;
    public Delivery Delivery { get; init; }
    public DamageClass Class { get; init; }
    public HarmTags Tags { get; init; }

    public float Damage { get; init; } = 10f;
    public float Rpm { get; init; } = 300f;
    public int Magazine { get; init; } = 10;
    public float Reload { get; init; } = 1.5f;
    /// <summary>Loads one round per <see cref="Reload"/> (shells, one at a time), and firing interrupts it.</summary>
    public bool ReloadEach { get; init; }
    /// <summary>Seconds of the manual action after each shot (a pump), 0 for none.</summary>
    public float Pump { get; init; }
    public float Speed { get; init; } = 200f;
    public float Life { get; init; } = 2f;
    public float Spread { get; init; } = 0.5f;
    public float SpreadMoving { get; init; } = 1.5f;
    /// <summary>Degrees the camera kicks per shot.</summary>
    public float Recoil { get; init; } = 0.5f;
    /// <summary>How far the shot is heard, metres: creatures in earshot turn to it.</summary>
    public float Noise { get; init; } = 40f;
    /// <summary>A multiplier on hits to blocks (0: blocks are unharmed).</summary>
    public float Blocks { get; init; } = 1f;
    /// <summary>The share of the planet's gravity the shot falls under (0: none).</summary>
    public float Gravity { get; init; }
    /// <summary>A strike's reach from the shoulder, m (0 for anything that flies).</summary>
    public float Reach { get; init; }

    public string Ammo { get; init; } = "";
    /// <summary>Rounds one ammo item gives (0: the item's own round count, or 1).</summary>
    public int PerItem { get; init; }

    public ChargePart Charge { get; init; }
    public PiercePart Pierce { get; init; }
    public BouncePart Bounce { get; init; }
    public PelletsPart Pellets { get; init; }
    public ArcPart Arc { get; init; }
    public StickPart Stick { get; init; }
    public DotPart Dot { get; init; }
    public HomingPart Homing { get; init; }
    public SwarmPart Swarm { get; init; }
    public ConePart Cone { get; init; }
    public PlacedPart Placed { get; init; }
    public BlastPart Blast { get; init; }
    public BackblastPart Backblast { get; init; }

    public string Feel { get; init; } = "bullet";
    public TracerSpec Tracer { get; init; } = new(0xffe6a0, 0.012f, 1.2f, Body.Tracer, false);
    public LookSpec Look { get; init; } = new(Band.Mid, 0.4f, 0.2f, 0.2f, "none", "box", "post", "none", 0, 0, 0x555555);

    /// <summary>Every way this weapon fires; the first is the base row itself when it has no modes.</summary>
    public IReadOnlyList<WeaponDef> Modes { get; internal set; } = Array.Empty<WeaponDef>();

    /// <summary>The weapon this mode belongs to (itself for the base row).</summary>
    public WeaponDef Base { get; internal set; }

    public float SecondsPerShot => 60f / MathF.Max(1f, Rpm);

    /// <summary>The parts this row combines, by name (tests and the tech tree's text).</summary>
    public IEnumerable<string> Parts()
    {
        if (Charge != null) yield return "charge";
        if (Pierce != null) yield return "pierce";
        if (Bounce != null) yield return "bounce";
        if (Gravity > 0f) yield return "gravity";
        if (Pellets != null) yield return "pellets";
        if (Arc != null) yield return "arc";
        if (Stick != null) yield return "stick";
        if (Dot != null) yield return "dot";
        if (Homing != null) yield return "homing";
        if (Swarm != null) yield return "swarm";
        if (Cone != null) yield return "cone";
        if (Placed != null) yield return "placed";
    }

    public override string ToString() => Mode.Length > 0 ? $"{Id} ({Mode})" : Id;
}

/// <summary>The rules every weapon follows (section 11): self-harm on, friendly fire a server setting, effects on a player for seconds.</summary>
public sealed record ArmsRules(bool SelfHarm, bool FriendlyFire, float PlayerEffectSeconds, float GroundGraze);

/// <summary>The weapon table: shared/Arms/data/weapons.json.</summary>
public sealed class Armoury
{
    public const string Schema = "galhack.weapons";

    public ArmsRules Rules { get; private init; } = new(true, false, 4f, 1.5f);
    public IReadOnlyList<WeaponDef> All { get; private init; } = Array.Empty<WeaponDef>();
    readonly Dictionary<string, WeaponDef> _byItem = new(StringComparer.Ordinal);

    /// <summary>The weapon an item is, or null.</summary>
    public WeaponDef OfItem(string itemId) => itemId != null && _byItem.TryGetValue(itemId, out WeaponDef d) ? d : null;
    public WeaponDef Get(string id) => All.FirstOrDefault(w => w.Id == id) ?? throw new KeyNotFoundException($"no weapon '{id}'");

    static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static Armoury LoadFile(string path) => Parse(File.ReadAllText(path));

    public static Armoury Parse(string json)
    {
        JsonObject root = JsonNode.Parse(json, documentOptions: Options)?.AsObject() ?? throw new InvalidDataException("weapons: empty");
        if (root["schema"]?.GetValue<string>() != Schema) throw new InvalidDataException($"weapons: not a {Schema} file");
        JsonObject r = root["rules"]?.AsObject() ?? new JsonObject();
        var rules = new ArmsRules(Bool(r, "selfHarm", true), Bool(r, "friendlyFire", false), Num(r, "playerEffectSeconds", 4f), Num(r, "groundGraze", 1.5f));
        var list = new List<WeaponDef>();
        foreach (JsonNode n in root["weapons"]?.AsArray() ?? new JsonArray())
        {
            JsonObject row = n.AsObject();
            WeaponDef def = Read(row, "");
            var modes = new List<WeaponDef>();
            if (row["modes"] is JsonArray ms && ms.Count > 0)
            {
                foreach (JsonNode m in ms)
                {
                    var merged = (JsonObject)row.DeepClone();
                    merged.Remove("modes");
                    foreach (var (k, v) in m.AsObject())
                        if (k != "name") merged[k] = Merge(merged[k], v);
                    WeaponDef md = Read(merged, m["name"]?.GetValue<string>() ?? "");
                    md.Base = def;
                    modes.Add(md);
                }
            }
            else modes.Add(def);
            def.Base = def;
            def.Modes = modes;
            foreach (WeaponDef md in modes) md.Modes = modes;
            list.Add(def);
        }
        var a = new Armoury { Rules = rules, All = list };
        foreach (WeaponDef d in list)
        {
            if (!a._byItem.TryAdd(d.Item, d)) throw new InvalidDataException($"weapons: two weapons are item '{d.Item}'");
        }
        return a;
    }

    /// <summary>A mode's field over the base's: objects merge key by key (so a mode can change one number of a part), anything else replaces.</summary>
    static JsonNode Merge(JsonNode under, JsonNode over)
    {
        if (over is JsonObject o && under is JsonObject u)
        {
            var m = (JsonObject)u.DeepClone();
            foreach (var (k, v) in o) m[k] = v?.DeepClone();
            return m;
        }
        return over?.DeepClone();
    }

    static WeaponDef Read(JsonObject e, string mode)
    {
        string id = Str(e, "id", null) ?? throw new InvalidDataException("weapons: a row has no id");
        string where = $"weapon '{id}'";
        return new WeaponDef
        {
            Id = id,
            Code = Str(e, "code", ""),
            Name = Str(e, "name", id),
            Item = Str(e, "item", id),
            Mode = mode,
            Trigger = Enum<Trigger>(e, "trigger", Trigger.Semi, where),
            Burst = (int)Num(e, "burst", 3),
            Delivery = Enum<Delivery>(e, "delivery", Delivery.Bolt, where),
            Class = Str(e, "class", "kin") switch
            {
                "kin" => DamageClass.Kinetic,
                "exp" => DamageClass.Explosive,
                "en" => DamageClass.Energy,
                string s => throw new InvalidDataException($"{where}: class is kin, exp or en, not '{s}'"),
            },
            Tags = ReadTags(e, where),
            Damage = Num(e, "damage", 10f),
            Rpm = Num(e, "rpm", 300f),
            Magazine = Math.Max(1, (int)Num(e, "magazine", 10)),
            Reload = Num(e, "reload", 1.5f),
            ReloadEach = Bool(e, "reloadEach", false),
            Pump = Num(e, "pump", 0f),
            Speed = Num(e, "speed", 200f),
            Life = Num(e, "life", 2f),
            Spread = Num(e, "spread", 0.5f),
            SpreadMoving = Num(e, "spreadMoving", 1.5f),
            Recoil = Num(e, "recoil", 0.5f),
            Noise = Num(e, "noise", 40f),
            Blocks = Num(e, "blocks", 1f),
            Gravity = Num(e, "gravity", 0f),
            Reach = Num(e, "reach", 0f),
            Ammo = e["ammo"] is JsonObject am ? Str(am, "item", "") : "",
            PerItem = e["ammo"] is JsonObject ap ? (int)Num(ap, "perItem", 0) : 0,
            Charge = e["charge"] is JsonObject c ? new ChargePart(Math.Max(1, (int)Num(c, "stages", 4)), Num(c, "seconds", 0.4f), Num(c, "speed", 0f), (int)Num(c, "pierce", 0), Num(c, "damage", 0f)) : null,
            Pierce = e["pierce"] is JsonObject p ? new PiercePart((int)Num(p, "count", 1)) : null,
            Bounce = e["bounce"] is JsonObject b ? new BouncePart((int)Num(b, "count", 1), Num(b, "keep", 0.6f), Num(b, "fuse", 0f)) : null,
            Pellets = e["pellets"] is JsonObject pe ? new PelletsPart(Math.Max(1, (int)Num(pe, "count", 8)), Num(pe, "spread", 4f)) : null,
            Arc = e["arc"] is JsonObject ar ? new ArcPart((int)Num(ar, "jumps", 3), Num(ar, "range", 6f), Num(ar, "damage", 10f), Num(ar, "wetReach", 0.5f)) : null,
            Stick = e["stick"] is JsonObject st ? new StickPart(Num(st, "seconds", 5f)) : null,
            Dot = e["dot"] is JsonObject d ? new DotPart(Num(d, "perSecond", 5f), Num(d, "seconds", 3f), Num(d, "flee", 0f)) : null,
            Homing = e["homing"] is JsonObject h ? new HomingPart(Enum<Seek>(h, "seek", Seek.Aim, where), Num(h, "cone", 15f), Num(h, "turnPerMetre", 0.1f), Num(h, "range", 60f)) : null,
            Swarm = e["swarm"] is JsonObject sw ? new SwarmPart(Math.Max(1, (int)Num(sw, "count", 1)), Num(sw, "wander", 2f)) : null,
            Cone = e["cone"] is JsonObject co ? new ConePart(Num(co, "range", 6f), Num(co, "angle", 15f), Num(co, "ticks", 10f)) : null,
            Placed = e["placed"] is JsonObject pl ? new PlacedPart(Num(pl, "arm", 3f), Num(pl, "radius", 2f), Math.Max(1, (int)Num(pl, "limit", 20))) : null,
            Blast = e["blast"] is JsonObject bl ? new BlastPart(Num(bl, "radius", 3f), Num(bl, "damage", 100f), Num(bl, "blocks", 1f)) : null,
            Backblast = e["backblast"] is JsonObject bb ? new BackblastPart(Num(bb, "length", 4f), Num(bb, "angle", 25f), Num(bb, "damage", 20f)) : null,
            Feel = Str(e, "feel", "bullet"),
            Tracer = e["tracer"] is JsonObject t
                ? new TracerSpec(Hex(t, "colour", 0xffe6a0), Num(t, "width", 0.012f), Num(t, "length", 1.2f), Enum<Body>(t, "body", Body.Tracer, where), Bool(t, "light", false))
                : new TracerSpec(0xffe6a0, 0.012f, 1.2f, Body.Tracer, false),
            Look = e["look"] is JsonObject l
                ? new LookSpec(Enum<Band>(l, "band", Band.Mid, where), Num(l, "calibre", 0.4f), Num(l, "barrel", 0.2f), Num(l, "body", 0.2f), Str(l, "stock", "none"),
                    Str(l, "mag", "box"), Str(l, "sight", "post"), Str(l, "emitter", "none"), (int)Num(l, "vents", 0), (int)Num(l, "fins", 0), Hex(l, "accent", 0x555555))
                : new LookSpec(Band.Mid, 0.4f, 0.2f, 0.2f, "none", "box", "post", "none", 0, 0, 0x555555),
        };
    }

    static HarmTags ReadTags(JsonObject e, string where)
    {
        HarmTags t = HarmTags.None;
        if (e["tags"] is not JsonArray a) return t;
        foreach (JsonNode n in a)
        {
            string s = n.GetValue<string>();
            t |= s switch
            {
                "fire" => HarmTags.Fire,
                "bio" => HarmTags.Bio,
                "shock" => HarmTags.Shock,
                "gas" => HarmTags.Gas,
                "rad" => HarmTags.Rad,
                "force" => HarmTags.Force,
                _ => throw new InvalidDataException($"{where}: unknown tag '{s}'"),
            };
        }
        return t;
    }

    static string Str(JsonObject e, string k, string fallback) => e[k] is JsonValue v ? v.GetValue<string>() : fallback;
    static float Num(JsonObject e, string k, float fallback) => e[k] is JsonValue v ? (float)v.GetValue<double>() : fallback;
    static bool Bool(JsonObject e, string k, bool fallback) => e[k] is JsonValue v ? v.GetValue<bool>() : fallback;

    static uint Hex(JsonObject e, string k, uint fallback) =>
        e[k] is JsonValue v ? uint.Parse(v.GetValue<string>().TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : fallback;

    static T Enum<T>(JsonObject e, string k, T fallback, string where) where T : struct, Enum
    {
        if (e[k] is not JsonValue v) return fallback;
        string s = v.GetValue<string>();
        return System.Enum.TryParse(s, ignoreCase: true, out T t) ? t : throw new InvalidDataException($"{where}: '{s}' is not a {typeof(T).Name.ToLowerInvariant()}");
    }
}
