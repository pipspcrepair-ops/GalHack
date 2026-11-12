using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// People's acts and gestures, pose by pose, for side-by-side checks against the owner's shots
/// (run windowed): --view=acts --out=DIR writes DIR/NN-name-front.png and -side.png for every
/// act, four people of different folk doing it at once, fresh each run. --pick=a,b limits it to
/// those scenes by name.
/// </summary>
public partial class Screenshot
{
    delegate void ActShape(int who, ref Gesture g, HumanRig rig, int tick);

    sealed record ActScene(string Name, ActShape Shape, string Prop = null, bool Husk = false, int Hold = 40, BodyEnd End = BodyEnd.Fall);

    static ActScene Act(string name, BodyAct act, int style = 0) => new(name, (int who, ref Gesture g, HumanRig rig, int tick) =>
    {
        g.Act = act;
        g.ActAmount = 1f;
        g.Style = style;
    });

    static readonly ActScene[] ActScenes =
    {
        Act("cower", BodyAct.Cower),
        Act("wave-off", BodyAct.WaveOff),
        Act("beckon", BodyAct.Beckon),
        Act("point", BodyAct.Point),
        Act("shrug", BodyAct.Shrug),
        Act("grieve", BodyAct.Grieve),
        Act("fidget", BodyAct.Fidget),
        Act("taunt", BodyAct.Taunt),
        Act("greet-chest", BodyAct.Greet, 2),
        Act("play", BodyAct.Play),
        Act("thanks-high", BodyAct.Thanks, 0),
        Act("thanks-palm", BodyAct.Thanks, 1),
        Act("thanks-chest", BodyAct.Thanks, 2),
        Act("greet-wave", BodyAct.Greet, 0),
        Act("greet-palm", BodyAct.Greet, 1),
        Act("hands-up", BodyAct.HandsUp),
        Act("warn", BodyAct.Warn),
        Act("celebrate", BodyAct.Celebrate),
        Act("panic", BodyAct.Panic),
        Act("present", BodyAct.Present),
        Act("startle", BodyAct.Startle),
        new("sip", (int who, ref Gesture g, HumanRig rig, int tick) => g.Sip = 1f, Prop: "bottle"),
        new("bow-draw", (int who, ref Gesture g, HumanRig rig, int tick) => g.Aim = 1f, Prop: "bow"),
        new("crouch", (int who, ref Gesture g, HumanRig rig, int tick) => g.Crouch = 1f),
        new("stalk", (int who, ref Gesture g, HumanRig rig, int tick) => { g.Stalk = 1f; g.Speed = 0.6f; g.Walked = tick / 60f * 0.6f; }, Husk: true),
        new("punch", (int who, ref Gesture g, HumanRig rig, int tick) => { g.Blow = StrikeKind.Punch; g.Strike = MathF.Min(1f, tick / 30f); }),
        new("punch-windup", (int who, ref Gesture g, HumanRig rig, int tick) => { g.Blow = StrikeKind.Punch; g.Strike = MathF.Min(0.6f, tick / 30f); }),
        new("swat", (int who, ref Gesture g, HumanRig rig, int tick) => { g.Blow = StrikeKind.Swat; g.Strike = MathF.Min(1f, tick / 30f); }),
        new("swat-windup", (int who, ref Gesture g, HumanRig rig, int tick) => { g.Blow = StrikeKind.Swat; g.Strike = MathF.Min(0.6f, tick / 30f); }),
        new("seize-windup", (int who, ref Gesture g, HumanRig rig, int tick) => { g.Blow = StrikeKind.Seize; g.Strike = MathF.Min(0.6f, tick / 30f); }, Husk: true),
        new("seize", (int who, ref Gesture g, HumanRig rig, int tick) => { g.Blow = StrikeKind.Seize; g.Strike = MathF.Min(1f, tick / 30f); }, Husk: true),
        new("spew", (int who, ref Gesture g, HumanRig rig, int tick) => { g.Blow = StrikeKind.Spew; g.Strike = MathF.Min(1f, tick / 30f); }, Husk: true),
        new("hit-chest", (int who, ref Gesture g, HumanRig rig, int tick) => { if (tick == 30) rig.Struck(rig.ChestPoint, rig.Facing * -1f + Vector3.Right * 0.3f * (who - 1.5f), 0.4f); }, Hold: 34),
        new("hit-heavy", (int who, ref Gesture g, HumanRig rig, int tick) => { if (tick == 26) rig.Struck(rig.ChestPoint + Vector3.Up * 0.1f, rig.Facing * -1f, 1f); }, Hold: 36),
        new("hit-head", (int who, ref Gesture g, HumanRig rig, int tick) => { if (tick == 30) rig.Struck(rig.HeadPoint, rig.Facing * -1f + rig.Side * 0.6f, 0.5f); }, Hold: 34),
        new("dead", (int who, ref Gesture g, HumanRig rig, int tick) => { if (tick == 2) rig.Struck(rig.ChestPoint, rig.Facing * -1f + rig.Side * (who - 1.5f) * 0.5f, 1f); g.Dead = tick >= 2; g.Down = 1f; }, Hold: 200),
        // Blown apart (people, a Husk, a machine: no blood, its own plates and works) and burned away.
        new("gib", (int who, ref Gesture g, HumanRig rig, int tick) => { }, Hold: 200, End: BodyEnd.Gib),
        new("ash", (int who, ref Gesture g, HumanRig rig, int tick) => { }, Hold: 60, End: BodyEnd.Vaporise),
    };

    List<ActScene> _actList;
    int _actIndex = -1, _actTick, _actShot;
    readonly List<(Creature c, HumanRig rig)> _actors = new();
    List<Species> _actPeople, _actHusks, _actMixed;
    Gibs _gibs;
    Vector3 _actOrigin, _actFwd, _actRight;
    bool _actSide, _actBusy;

    bool ActsSetup()
    {
        if (_view != "acts") return false;
        Main m = _main;
        Stage(0.42f);
        m.Sim.Frozen = true;
        var rng = new Rng((ulong)DateTime.UtcNow.Ticks);
        _actList = ActScenes.Where(s => _pick.Length == 0 || _pick.Split(',').Contains(s.Name)).ToList();
        // Four people of different folk and builds, a big-chested one among them: fresh worlds until found.
        _actPeople = new List<Species>();
        var folks = new HashSet<string>();
        for (int i = 0; i < 300 && _actPeople.Count < 4; i++)
        {
            var w = new WorldConditions { Seed = rng.NextU64(), Alienness = rng.Range(0f, 0.6f), TempC = rng.Range(-15f, 30f) };
            PeopleGenerator g = Bestiary.Roll(m.Data, w).People;
            PersonRole role = _actPeople.Count switch { 0 => PersonRole.Guard, 1 => PersonRole.Child, 2 => PersonRole.Elder, _ => PersonRole.Worker };
            Species s = g.Person(rng.NextU64(), role, founders: g.FoundersOf(rng.NextU64()));
            if (s.Mechanical || s.Figure.Husk || !folks.Add(s.Figure.Folk?.Id ?? "human") && i < 200) continue;
            if (_actPeople.Count == 0 && s.Figure.Padding < 1.15f && i < 200) { folks.Remove(s.Figure.Folk?.Id ?? "human"); continue; }
            _actPeople.Add(s);
        }
        _actHusks = HuskHosts().Take(4).ToList();
        _actMixed = new List<Species> { _actPeople[0], _actHusks[0], _actPeople[2] };
        for (int n = 0; n < 40 && _actMixed.Count < 4; n++) if (Robots.Roll(new WorldConditions { Seed = rng.NextU64() }, n) is { Rig: RigKind.Humanoid } r) _actMixed.Add(r);
        AddChild(_gibs = new Gibs());
        _actFwd = Turn(SunFlat(), 0.35f);
        _actRight = _actFwd.Cross(Vector3.Up).Normalized();
        _actOrigin = Ground(new Vector3(-20f, 0f, 30f));
        _out = _out.EndsWith(".png") ? _out[..^4] : _out;
        Directory.CreateDirectory(_out);
        _each = ActsEach;
        _aim = () =>
        {
            // From the front and a little to the side, as the owner looked; or square from the side.
            Vector3 eye = _actSide ? Ground(_actOrigin + _actRight * 7.5f + _actFwd * 1.5f) + Vector3.Up * 1.3f : Ground(_actOrigin + _actFwd * 6.5f - _actRight * 2.6f) + Vector3.Up * 1.5f;
            return (eye, _actOrigin + Vector3.Up * 0.9f);
        };
        m.Camera.Fov = 55f;
        _captureTick = int.MaxValue / 2;
        return true;
    }

    void ActsStart(ActScene scene)
    {
        Main m = _main;
        m.Sim.Clear();
        _actors.Clear();
        List<Species> who = scene.Husk ? _actHusks : scene.End != BodyEnd.Fall ? _actMixed : _actPeople;
        for (int i = 0; i < who.Count; i++)
        {
            Species s = who[i];
            // A body of its own for a scene with a prop: the prop changes the pieces.
            Species copy = s;
            if (scene.Prop != null)
            {
                copy = s.Copy();
                copy.Figure = s.Figure.Clone();
                copy.Figure.Prop = scene.Prop;
            }
            Creature c = m.Sim.Spawn(copy, _actOrigin + _actRight * (i - 1.5f) * 1.7f);
            c.Yaw = MathF.Atan2(_actFwd.X, _actFwd.Z);
            c.Rig.Reset(m.Sim, c);
            _actors.Add((c, (HumanRig)c.Rig));
        }
        m.View.Bind(m.Sim, null);
    }

    void ActsEach()
    {
        if (_actBusy) return;
        Main m = _main;
        if (_actIndex < 0 || _actTick >= _actList[_actIndex].Hold + 6)
        {
            _actIndex++;
            if (_actIndex >= _actList.Count)
            {
                _actBusy = true;
                GD.Print($"saved {_actShot} act shots to {_out}");
                Shutdown.Quit(this, 0);
                return;
            }
            _actTick = 0;
            _actSide = false;
            ActsStart(_actList[_actIndex]);
        }
        ActScene scene = _actList[_actIndex];
        float clock = 2f + _actTick / 60f;
        for (int i = 0; i < _actors.Count; i++)
        {
            var (c, rig) = _actors[i];
            var g = new Gesture { Feet = c.Position, Yaw = c.Yaw, Clock = clock + i * 0.37f, ActAt = c.Position + c.Forward * 2.5f + Vector3.Up * 1.2f };
            scene.Shape(i, ref g, rig, _actTick);
            if (scene.End != BodyEnd.Fall && _actTick == 8 && !c.Removed)
            {
                if (scene.End == BodyEnd.Gib) _gibs.Burst(c.Species, rig.Layout, rig.Pose, c.Position + new Vector3(0.6f, -0.3f, 0.8f), 7f);
                else _gibs.Ash(c.Position, Vector3.Up, 0.55f, c.Species.Seed);
                c.Removed = true;
            }
            if (g.Speed > 0f) g.Feet = c.Position + c.Forward * g.Walked;
            rig.Drive(g);
        }
        m.View.Sync(m.Camera.GlobalPosition);
        if (_actTick == scene.Hold) ActsCapture($"{_actIndex + 1:00}-{scene.Name}-front.png", false);
        if (_actTick == scene.Hold + 3) ActsCapture($"{_actIndex + 1:00}-{scene.Name}-side.png", true);
        _actTick++;
    }

    async void ActsCapture(string name, bool sideNext)
    {
        _actBusy = true;
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(_out, name));
        _actShot++;
        _actSide = !sideNext ? true : false;
        _actBusy = false;
    }
}
