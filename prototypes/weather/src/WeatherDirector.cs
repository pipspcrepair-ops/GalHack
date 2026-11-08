using System;
using System.Collections.Generic;
using Godot;
using Weather.Forces;
using Weather.Model;

namespace Weather;

/// <summary>
/// Runs the weather in the scene. Each physics tick, before anything moves: step the model
/// around the player, copy its wind and tornadoes into the force module, apply forces to every
/// registered body, then hand out what happened (strikes, impacts) to the player and the FX.
/// Also turns the debug keys into weather.
/// </summary>
public partial class WeatherDirector : Node
{
    public WeatherSim Sim { get; private set; }
    public ForceModule Forces { get; } = new();
    public Player Player;
    public WeatherFx Fx;
    public WeatherAudio Audio;

    /// <summary>Short visor messages: caught, released, struck, line snapped.</summary>
    public readonly List<(string text, float until, bool danger)> Messages = new();
    public float Clock { get; private set; }

    StrikePoint _playerPoint, _hoverPoint, _roverPoint;
    HoverVessel _hover;
    Rover _rover;
    WeatherKind _lastKey = WeatherKind.Clear;

    static readonly WeatherKind[][] Keys =
    {
        new[] { WeatherKind.Clear },
        new[] { WeatherKind.Clouds },
        new[] { WeatherKind.Rain, WeatherKind.Monsoon },
        new[] { WeatherKind.Fog },
        new[] { WeatherKind.Snow, WeatherKind.Blizzard },
        new[] { WeatherKind.DustStorm },
        new[] { WeatherKind.Thunderstorm },
        new[] { WeatherKind.FireStorm },
        new[] { WeatherKind.Hurricane },
        new[] { WeatherKind.Tornado },
    };

    public void Setup(WeatherData data, ulong seed, string archetype, Spire spire, HoverVessel hover, Rover rover)
    {
        Sim = new WeatherSim(data, archetype, seed, WeatherKind.Clouds) { GroundHeight = Terrain.Height };
        Forces.Wind = Sim.WindAt;
        _hover = hover;
        _rover = rover;

        int id = 1;
        Sim.StrikePoints.Add(new StrikePoint { Id = id++, Name = "the spire", Position = spire.RodTip });
        foreach (Vector3 top in Foliage.TreeTops) Sim.StrikePoints.Add(new StrikePoint { Id = id++, Name = "a tree", Position = top });
        _hoverPoint = new StrikePoint { Id = id++, Name = "the hover vessel" };
        _roverPoint = new StrikePoint { Id = id++, Name = "the rover" };
        _playerPoint = new StrikePoint { Id = id++, Name = "YOU" };
        Sim.StrikePoints.AddRange(new[] { _hoverPoint, _roverPoint, _playerPoint });

        Forces.Register(Player);
        Forces.Register(hover);
        Forces.Register(rover);
        Forces.PhaseChanged += OnPhase;
        Player.Landed += (dealt, flung) =>
            Say(flung ? $"Thrown down hard: -{dealt:F0}" : $"Hard landing: -{dealt:F0}", danger: true);
        UpdateMovingPoints();
    }

    public override void _Ready() => ProcessPhysicsPriority = -20; // before the player and the bodies move

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Clock += dt;
        UpdateMovingPoints();
        Sim.Step(dt, Player.GlobalPosition);
        Glue.Sync(Sim, Forces);
        Forces.Step(dt);

        Conditions c = Sim.Current;
        _rover.LightsOn = c.Visibility < 900f || c.Solar < 0.25f; // the driver would turn them on
        Player.MoveFactor = c.MoveSpeed;
        Player.Exposure = c.Exposure;
        Player.Solar = c.Solar;

        foreach (WeatherEvent e in Sim.Events) Handle(e);
        Messages.RemoveAll(m => m.until < Clock);
    }

    void UpdateMovingPoints()
    {
        _playerPoint.Position = Player.HeadTop;
        _hoverPoint.Position = _hover.GlobalPosition + Vector3.Up * 1.4f;
        _roverPoint.Position = _rover.GlobalTransform * new Vector3(1.2f, 5.6f, -0.6f); // antenna tip
    }

    void Handle(WeatherEvent e)
    {
        switch (e.Kind)
        {
            case WeatherEventKind.LightningStrike:
            case WeatherEventKind.DebrisImpact:
                Fx?.OnImpact(e);
                if (e.Kind == WeatherEventKind.LightningStrike) Audio?.Thunder(e.Position);
                float distance = Player.HeadTop.DistanceTo(e.Position);
                if (e.Target == _playerPoint || distance < e.DamageRadius)
                {
                    float share = e.Target == _playerPoint ? 1f : 1f - distance / e.DamageRadius * 0.6f;
                    float dealt = Player.Hurt(e.Damage * share);
                    Say(e.Kind == WeatherEventKind.LightningStrike ? $"STRUCK BY LIGHTNING: -{dealt:F0}" : $"Hit by burning debris: -{dealt:F0}", danger: true);
                }
                else if (e.Kind == WeatherEventKind.LightningStrike) Player.Shake += Mathf.Clamp(60f / Mathf.Max(distance, 10f), 0f, 1.2f);
                if (e.Target == _hoverPoint) Say("Lightning hit the hover vessel", danger: false);
                if (e.Target == _roverPoint) Say("Lightning hit the rover", danger: false);
                break;
            case WeatherEventKind.TornadoForming:
                Say($"Funnel cloud forming {WeatherSim.Bearing(Player.GlobalPosition, e.Position)}", danger: true);
                break;
            case WeatherEventKind.TornadoTouchdown:
                Say("Tornado on the ground", danger: true);
                break;
        }
    }

    void OnPhase(IForceBody body, VortexPhase phase)
    {
        if (body == Player)
        {
            if (phase == VortexPhase.Caught) Say("CAUGHT IN THE VORTEX", danger: true, seconds: 5f);
            if (phase == VortexPhase.Released) Say("RELEASED. Jetpack to soften the landing", danger: true, seconds: 5f);
        }
        else if (body == _hover && phase == VortexPhase.Caught && _hover.Moored)
        {
            _hover.Moored = false;
            Say("The hover vessel's mooring line snapped. R to re-moor", danger: false, seconds: 6f);
        }
    }

    public void Say(string text, bool danger, float seconds = 4f)
    {
        Messages.RemoveAll(m => m.text == text);
        Messages.Add((text, Clock + seconds, danger));
        if (Messages.Count > 4) Messages.RemoveAt(0);
    }

    // ---- Keys -------------------------------------------------------------------------

    public void HandleKey(Key key)
    {
        int digit = key >= Key.Key0 && key <= Key.Key9 ? (int)(key - Key.Key0) : -1;
        if (digit >= 0)
        {
            WeatherKind[] options = Keys[digit];
            int next = Array.IndexOf(options, _lastKey) + 1; // press again for the stronger variant
            WeatherKind kind = options[next % options.Length];
            _lastKey = kind;
            Sim.Force(kind);
            Say($"Forcing {Sim.Data.State(kind).Name.ToLowerInvariant()}", danger: false);
        }
        else if (key == Key.T) SpawnTornadoAhead();
        else if (key == Key.B)
        {
            List<Archetype> all = Sim.Data.Archetypes;
            Archetype next = all[(all.IndexOf(Sim.Archetype) + 1) % all.Count];
            Sim.SetArchetype(next.Id);
            Say($"Planet: {next.Name}", danger: false);
        }
    }

    /// <summary>T: a tornado forms 260 m ahead of where you're looking and comes for you.</summary>
    public Tornado SpawnTornadoAhead(float distance = 260f)
    {
        Vector3 look = Player.LookDirection;
        var flat = new Vector3(look.X, 0, look.Z);
        flat = flat.LengthSquared() < 0.01f ? Vector3.Forward : flat.Normalized();
        return Sim.SpawnTornado(Player.GlobalPosition + flat * distance, Player.GlobalPosition);
    }

    // ---- Test hooks -------------------------------------------------------------------

    public void DebugTouchdown(Tornado t) => t.SkipFormation();

    public void ClearTornadoes()
    {
        Sim.ClearTornadoes();
        Forces.Vortices.Clear();
    }
}
