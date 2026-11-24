namespace PlanetSurface.Skies;

/// <summary>
/// How the scene's light comes down to the screen's, and nothing more: no scene-wide grade pushes colour either way
/// (owner, 23 Nov). A natural world is natural because of its own elements, air and dust, a vivid one because of its own
/// (<see cref="Planets.Look.WorldPalette"/>), and the tonemap keeps each as it is.
/// </summary>
public static class Grade
{
    /// <summary>The engine's tonemappers, compared by shots.</summary>
    public enum Curve { Filmic, Agx, Aces, Linear }

    /// <summary>
    /// The game's curve, picked by shots against AgX, ACES, plain linear and a hue-keeping shoulder of my own (23 Nov): Filmic
    /// at white 6. Its middle tones match plain linear's and the shoulder's; AgX came out darker and duller; ACES a little
    /// punchier; and the hue-keeping shoulder turned lava's glow pale peach, where Filmic's channels, rolling off one by one,
    /// take a bright orange toward yellow as fire looks.
    /// </summary>
    public const Curve Game = Curve.Filmic;

    /// <summary>Tests and shots: the curve to use instead of the game's (null: the game's).</summary>
    public static Curve? Force;

    /// <summary>The game's tonemapper on an environment.</summary>
    public static void Apply(Godot.Environment env)
    {
        env.TonemapMode = (Force ?? Game) switch
        {
            Curve.Filmic => Godot.Environment.ToneMapper.Filmic, Curve.Agx => Godot.Environment.ToneMapper.Agx,
            Curve.Aces => Godot.Environment.ToneMapper.Aces, _ => Godot.Environment.ToneMapper.Linear,
        };
        env.TonemapWhite = 6f;
        env.TonemapExposure = 1f;
    }
}
