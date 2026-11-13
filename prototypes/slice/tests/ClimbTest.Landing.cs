using System;
using System.Collections.Generic;
using Godot;
using Slice.Game;
using Slice.Places;
using Slice.Player;

namespace Slice.Tests;

/// <summary>
/// Landing on city ground off the jetpack (the owner's stuck-hopping report): over the open ground at a city
/// building's door, up on the pack, across and down, Space held through the landing and on, then let go.
/// A held Space jumps once; landing with it still held is no new jump, so the castaway stands, never hops in
/// place, and is never rescued. (-- --landing: only this, on a midrise and a house.)
/// </summary>
public partial class ClimbTest
{
    static readonly string[] LandingFamilies = { "city_midrise", "city_house" };

    IEnumerable<int> Landing(Game.Game game, Driver drive, Place place)
    {
        Survivor s = game.Survivor;
        int rescues = 0;
        game.Zone.Physics.Rescued += r => { if (r.Body == s.Body) rescues++; };
        Vector3 door = s.WorldFeet;
        foreach (int x in Stand(game, drive, door + new Vector3(0f, 0f, 6f))) yield return x;
        // Up on the pack and across, Space held all the way down and on.
        game.Controls.Hold(Key.Space);
        game.Controls.Hold(Key.W);
        int hopsHeld = 0, landed = -1;
        bool was = s.Body.OnFloor;
        for (int t = 0; t < 600; t++)
        {
            if (t == 90) game.Controls.Hold(Key.W, false);
            yield return 0;
            bool on = s.Body.OnFloor;
            if (landed < 0 && t > 30 && on) landed = t;
            if (landed >= 0 && was && !on && s.Body.Velocity.Y > 1f) hopsHeld++;
            was = on;
            if (landed >= 0 && t > landed + 180) break;
        }
        game.Controls.Hold(Key.Space, false);
        int hopsLet = 0;
        for (int t = 0; t < 120; t++)
        {
            yield return 0;
            bool on = s.Body.OnFloor;
            if (was && !on && s.Body.Velocity.Y > 1f) hopsLet++;
            was = on;
        }
        Check(landed >= 0 && hopsHeld == 0 && hopsLet == 0 && rescues == 0 && s.Body.OnFloor,
            $"{place.Site.Name}: off the jetpack onto the city ground with Space still held, the castaway lands and stands: {hopsHeld} hops while held, {hopsLet} after letting go, {rescues} rescues");
    }
}
