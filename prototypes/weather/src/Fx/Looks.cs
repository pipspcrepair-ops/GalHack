using System.Collections.Generic;
using Godot;
using Weather.Model;

namespace Weather;

/// <summary>
/// How each weather is painted: sky gradient, cloud colours, fog, sun and ambient light. A
/// 1970s sci-fi paperback palette: big saturated skies, deep ultramarine zeniths, hot horizons.
/// Presentation data only; the model never sees it.
/// </summary>
public struct Look
{
    public Color Zenith, Horizon, Ground, CloudLit, CloudShade, Fog, Sun, Ambient;
    public float SunEnergy, AmbientEnergy, SunDisc, FogScatter, Saturation;

    public static Look Lerp(in Look a, in Look b, float t) => new()
    {
        Zenith = a.Zenith.Lerp(b.Zenith, t),
        Horizon = a.Horizon.Lerp(b.Horizon, t),
        Ground = a.Ground.Lerp(b.Ground, t),
        CloudLit = a.CloudLit.Lerp(b.CloudLit, t),
        CloudShade = a.CloudShade.Lerp(b.CloudShade, t),
        Fog = a.Fog.Lerp(b.Fog, t),
        Sun = a.Sun.Lerp(b.Sun, t),
        Ambient = a.Ambient.Lerp(b.Ambient, t),
        SunEnergy = Mathf.Lerp(a.SunEnergy, b.SunEnergy, t),
        AmbientEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, t),
        SunDisc = Mathf.Lerp(a.SunDisc, b.SunDisc, t),
        FogScatter = Mathf.Lerp(a.FogScatter, b.FogScatter, t),
        Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
    };

    static Look Make(string zenith, string horizon, string cloudLit, string cloudShade, string fog,
        string sun, float sunEnergy, string ambient, float ambientEnergy, float sunDisc, float scatter = 0.1f, float saturation = 1.15f, string ground = "6b5a3a")
        => new()
        {
            Zenith = new Color(zenith), Horizon = new Color(horizon), Ground = new Color(ground),
            CloudLit = new Color(cloudLit), CloudShade = new Color(cloudShade), Fog = new Color(fog),
            Sun = new Color(sun), SunEnergy = sunEnergy, Ambient = new Color(ambient), AmbientEnergy = ambientEnergy,
            SunDisc = sunDisc, FogScatter = scatter, Saturation = saturation,
        };

    public static readonly Dictionary<WeatherKind, Look> ByKind = new()
    {
        [WeatherKind.Clear] = Make("16378a", "f2c48c", "fff4e0", "8a93b8", "e6c89c", "ffdcaa", 1.55f, "8fa0c8", 0.5f, 1f),
        [WeatherKind.Clouds] = Make("27559e", "ecd2a4", "fffaf0", "6f7aa0", "d9c6a2", "ffe4bc", 1.35f, "8f9cbc", 0.55f, 0.9f),
        [WeatherKind.Rain] = Make("36464f", "8e9e9a", "a4aeac", "465054", "7a8a88", "c8d2d0", 0.45f, "7a8a92", 0.75f, 0.12f, 0.05f, 1.05f),
        [WeatherKind.Monsoon] = Make("1c2a2c", "52645c", "72807a", "29332f", "4c5e56", "a2b2aa", 0.25f, "5c6c6a", 0.7f, 0f, 0.05f, 1.0f),
        [WeatherKind.Fog] = Make("9aa89a", "c9d0be", "e2e6da", "a0a898", "b6c0ae", "f2ecd2", 0.6f, "a8b2a6", 0.75f, 0.35f, 0.3f, 1.0f),
        [WeatherKind.Snow] = Make("8aa2c4", "dde6ee", "f0f4f8", "98a6bc", "c8d6e4", "e8f0ff", 0.65f, "b4c4dc", 0.8f, 0.25f, 0.2f, 1.05f),
        [WeatherKind.Blizzard] = Make("4e6a8e", "a4b8cc", "d0dce8", "6a809c", "8ea6bf", "e6eeff", 0.45f, "8aa0bc", 0.75f, 0.1f, 0.3f, 1.15f),
        [WeatherKind.DustStorm] = Make("6a300c", "ec8c38", "f2a860", "8a4a20", "cc6e28", "ffd0a0", 0.75f, "c27a48", 0.75f, 0.9f, 0.7f, 1.25f),
        [WeatherKind.Thunderstorm] = Make("100e2a", "3e3a6a", "66648e", "1a1832", "282a48", "9a9ad0", 0.22f, "3a3a66", 0.6f, 0f, 0.05f, 1.15f),
        [WeatherKind.FireStorm] = Make("0c0302", "b8300a", "d0501c", "1e0603", "4a1408", "ff8a40", 0.55f, "6e2a14", 0.55f, 0.4f, 0.35f, 1.3f),
        [WeatherKind.Hurricane] = Make("1e2a32", "6a7a80", "86949a", "283238", "52626a", "b0c0c8", 0.3f, "58686e", 0.65f, 0f, 0.05f, 1.05f),
        [WeatherKind.Tornado] = Make("1c281a", "b8c46c", "a6b276", "263022", "76865a", "eaf2a2", 0.85f, "6a7a52", 0.6f, 0.2f, 0.15f, 1.15f),
    };
}
