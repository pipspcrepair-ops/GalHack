using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Surface;
using Slice.Vessels;
using Weather.Model;

namespace Slice.Orbit;

/// <summary>
/// The world's weather goes on while you are up there: the surface waits, but its air doesn't. The
/// weather is stepped here with the point under you as the place it happens round, as the surface
/// steps it round you, so the front you watch cross the globe is the one you land under, and the
/// storms over the place you come down on are the ones you saw from orbit.
/// </summary>
public partial class Spaceflight
{
    float _frameDelta;

    void WeatherAloft(double delta)
    {
        SurfaceZone s = Game.Surface;
        // While the surface runs it steps its own weather (its physics' force provider): never both.
        if (Orbit == null || !Game.InOrbit || s?.Weather?.Sim is not WeatherSim sim || s.ProcessMode != ProcessModeEnum.Disabled) return;
        Vessel carrier = Carrier();
        Vector3 at = carrier != null ? carrier.Body.Position.ToGodot() : Game.Survivor.WorldFeet;
        Vector3 under = SurfacePoint(at, out _, out _);
        sim.Step((float)delta, new Vector3(under.X, 0f, under.Z));
    }
}
