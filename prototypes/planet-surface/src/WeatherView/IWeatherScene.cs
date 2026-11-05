using System.Collections.Generic;
using Godot;
using Weather.Model;

namespace PlanetSurface.WeatherView;

/// <summary>
/// What the lightning and tornado effects read from the weather presentation that owns them:
/// the camera, the sun, the cloud base, the hazards and two shared textures. <see cref="WeatherRig"/>
/// is one; a game that runs the weather its own way (the slice) can be another.
/// </summary>
public interface IWeatherScene
{
    Camera3D Camera { get; }
    Vector3 ToSun { get; }
    float CloudBaseY { get; }
    IReadOnlyList<Hazard> Hazards { get; }
    NoiseTexture2D CloudNoise { get; }
    Texture2D SoftDot { get; }
}
