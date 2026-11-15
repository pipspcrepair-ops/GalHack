using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// A strip of one strike, four frames side by side (the tell, the strike, contact, just after),
/// in daylight on the world's own ground, seen side-on with a stand-in sleeper:
/// --view=strike-giant (a megafauna bite), strike-bite (a small hunter), strike-stomp,
/// strike-swipe, strike-charge. --out=FILE.png.
/// </summary>
public partial class Screenshot
{
    readonly List<Image> _strip = new();
    bool _stripBusy;

    bool StrikeSetup()
    {
        if (!_view.StartsWith("strike-")) return false;
        Main m = _main;
        Player p = m.Player;
        World(WorldKind.Temperate, 0.42f);
        string kind = _view["strike-".Length..];
        var walkers = m.Fauna.Species.Where(x => x.Rig == RigKind.Legged && !x.Flies && !x.Mechanical).ToList();
        Species s = CreaturesTest.Hunter(kind == "bite" ? walkers.OrderBy(x => x.Body.Size).First() : walkers.OrderByDescending(x => x.Body.Size).First(), kind == "bite" ? 1.1f : 7f);
        s.Parts["head"] = "jaws";
        Proportions b = s.Body;
        if (kind != "bite")
        {
            b.LegStyle = LegStyle.Upright;
            b.LegThickness = MathF.Max(b.Width * 0.25f, b.LegLength * 0.15f); // pillars
        }
        if (kind == "swipe") { s.Parts["tail"] = "club"; b.TailLength = b.Size * 0.8f; }
        if (kind == "charge") s.Parts["horns"] = "pair";
        StrikeRules r = m.Data.Combat.Strikes;

        Vector3 stand = Ground(new Vector3(-90f, 0f, 40f));
        p.Teleport(stand);
        p.Health.Max = p.Health.Current = 1e9f;
        Figure(stand);
        foreach (Creature other in m.Sim.Creatures.ToList()) if (WildSim.Flat(other.Position - stand).Length() < 80f) m.Sim.Remove(other);
        Vector3 toward = Turn(SunFlat(), -0.5f), side = Turn(toward, 1.57f);
        float off = kind switch
        {
            "stomp" => b.Size * 0.5f + b.LegLength * 0.4f,
            "swipe" => b.Size * 0.5f + b.TailLength * 0.5f,
            "charge" => b.Size * 0.5f + r.ChargeFrom + 3f,
            _ => Attacks.BodyRadius + b.Size * 0.5f + b.NeckLength * 0.6f + 1.5f,
        };
        Vector3 at = kind == "swipe" ? stand + toward * off + side * b.Size * 0.3f : stand + toward * off;
        Creature c = m.Sim.Spawn(s, Ground(at));
        // Facing the sleeper, or away for a swipe (they're behind it).
        c.Yaw = kind == "swipe" ? MathF.Atan2(-toward.X, -toward.Z) : MathF.Atan2(toward.X, toward.Z);
        int startAt = _tick + 40, tellAt = -1, landedAt = -1, struckAt = -1, asked = 0;
        m.Sim.Attacked += e => { if (e.Creature == c && tellAt >= 0 && landedAt < 0) landedAt = _tick + 1; }; // (the sim steps before this node's tick)
        float span = MathF.Max(2.2f, off + b.Size * 1.6f);
        _aim = () =>
        {
            Vector3 mid = (stand + c.Position) * 0.5f;
            return (mid + side * (span * 0.85f) + Vector3.Up * (1.6f + b.Size * 0.25f), mid + Vector3.Up * MathF.Min(2.5f, 0.5f + b.Size * 0.15f));
        };
        _each = () =>
        {
            if (_tick >= startAt && tellAt < 0 && Attacks.Start(m.Sim, c, p.Target))
            {
                tellAt = _tick + (int)(c.WindupLength * 0.6f * 60f);
                GD.Print($"strike: {c.Move} by a {b.Size:F1} m {s.CommonName}, wind-up {c.WindupLength:F2} s");
            }
            if (_tick == startAt + 120 && tellAt < 0) GD.Print($"strike: not started (shown {c.Shown}, phase {c.Phase}, state {c.State})");
            if (tellAt >= 0 && landedAt < 0 && c.Phase == AttackPhase.Lunge && struckAt < 0) struckAt = _tick;
            if (_tick == tellAt || _tick == struckAt || _tick == landedAt || landedAt >= 0 && _tick == landedAt + 15) StripFrame(++asked == 4);
        };
        _captureTick = startAt + 60 * 12; // a fallback single shot if the strike never comes
        return true;
    }

    async void StripFrame(bool last)
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        image.Resize(image.GetWidth() / 2, image.GetHeight() / 2);
        _strip.Add(image);
        GD.Print($"strike frame {_strip.Count} at tick {_tick}");
        if (!last || _stripBusy) return;
        _stripBusy = true;
        int w = _strip[0].GetWidth(), h = _strip[0].GetHeight();
        Image strip = Image.CreateEmpty(w * _strip.Count, h, false, _strip[0].GetFormat());
        for (int i = 0; i < _strip.Count; i++) strip.BlitRect(_strip[i], new Rect2I(0, 0, w, h), new Vector2I(i * w, 0));
        Error result = strip.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out} ({_strip.Count} frames)" : $"could not save {_out}: {result}");
        Shutdown.Quit(this, result == Error.Ok ? 0 : 1);
    }
}
