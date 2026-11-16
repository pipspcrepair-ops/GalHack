using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using Godot;
using GalHack.Physics.Engine;
using PoiGenerator.Gen;
using Slice.Places;
using Slice.Vessels;
using N = System.Numerics;
using Int3 = GalHack.Structures.Int3;

namespace Slice.Tests;

/// <summary>
/// The interiors' gate (docs/ship-forms.md phase B): a large hull of each faction and a Husk-hijacked one, built as found
/// (its years' damage, dressed), set adrift in turn ahead of the Skiff in orbit, claimed with a core so its lights come on:
/// the plan of its busiest deck, then a room and a corridor from eye height. Frames into --dir as
/// interior-NAME-plan/room/corridor.png.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> InteriorView()
    {
        string dir = _args.GetValueOrDefault("dir", ".");
        System.IO.Directory.CreateDirectory(dir);
        foreach (int x in ToOrbit()) yield return x;
        for (int i = 0; i < 1200 && _game.Orbit is { Ready: false }; i++) yield return 0;
        _game.Visor.Visible = false;
        var lib = Slice.Game.WorldChoice.Library;
        var cat = _game.Zone.Data.Catalogue;
        SiteDef site = lib.Sites.Values.First(x => x.Parts.Count > 0 && x.Parts[0].Arg("what") == "capital");
        string[] roles = { "freighter", "warship", "liner", "miner", "carrier" };
        string[] styled = { "freeholders", "meridian", "exchange", "reavers", "kessari", "vharn" };
        var shots = styled.Select(f => (Faction: f, Name: f, Husk: false)).ToList();
        shots.Add((styled[Random.Shared.Next(styled.Length)], "husk", true));
        {
            Vector3 from = Skiff.Body.Position.ToGodot(), world0 = _game.Orbit.ToWorld(N.Vector3.Zero).ToGodot();
            Hurry(3);
            foreach (int x in FlyRound(Skiff, from + (from - world0).Normalized() * 1500f, 60f, 150f)) yield return x;
            Hurry(1);
        }
        foreach (var (faction, name, husk) in shots)
        {
            ulong seed = (ulong)Random.Shared.NextInt64();
            var spec = new CapitalHull.Spec(roles[Random.Shared.Next(roles.Length)], "large", "long", faction, husk);
            CapitalHull.HullPlan plan = CapitalHull.Plan(lib.Kits[site.Style], WorldProfile.Parse(site.Parts[0].Arg("tech", "mid")), spec, seed);
            PoiState state = husk ? PoiState.Infested : PoiState.Abandoned;
            HullVessel.Result r = HullVessel.Build(plan, plan.Pieces[0], cat, state, seed, Hulks.Deco());
            DeckPlan(plan, $"{dir}/interior-{name}-plan.png", $"{faction} {plan.Form.Plan} {spec.Role} {plan.L} m");
            N.Vector3 skiff = Skiff.Body.Position, world = _game.Orbit.ToWorld(N.Vector3.Zero), upN = N.Vector3.Normalize(skiff - world);
            N.Vector3 ahead = N.Vector3.Normalize(N.Vector3.Cross(upN, N.Vector3.UnitX) is var c && c.LengthSquared() > 0.01f ? c : N.Vector3.Cross(upN, N.Vector3.UnitZ));
            N.Vector3 along = N.Vector3.Normalize(N.Vector3.Cross(ahead, upN));
            var (lo, hi) = r.Structure.Bounds();
            N.Quaternion turn = N.Quaternion.Normalize(N.Quaternion.CreateFromRotationMatrix(N.Matrix4x4.CreateWorld(N.Vector3.Zero, along, upN)));
            N.Vector3 at = skiff + ahead * ((hi.Z - lo.Z) * 0.9f + 120f) - N.Vector3.Transform(new N.Vector3((lo.X + hi.X) * 0.5f, (lo.Y + hi.Y) * 0.5f, (lo.Z + hi.Z) * 0.5f), turn);
            Vessel v = Vessel.SpawnDerelict(_game.Orbit, r.Structure, at, turn);
            v.Derelict = new Slice.Orbit.DerelictPlan { Id = "interior-" + name, Name = r.Structure.Name, Structure = r.Structure, Position = at, Rotation = turn, Door = r.Door, Hull = r, State = state };
            foreach (int x in v.DressSteps(r.Decor)) yield return x;
            string why = r.CoreSlot is Int3 slot ? v.ClaimWith(slot) : "no seat";
            v.LightsOn = true;
            foreach (int x in _drive.Wait(60)) yield return x;
            GD.Print($"interior: {name} ({plan.Form.Plan}, {spec.Role}, {state}): {r.Structure.Count} blocks, {r.Spaces.Count} spaces, {r.Decor.Count} pieces of dressing, claimed {why ?? "ok"}; {plan.Inside.Style}; {plan.Inside.Log}");
            foreach (int x in Inside(v, dir, $"interior-{name}")) yield return x;
            v.Body.Position += upN * 60000f;
            v.Binding.Teleport();
            foreach (int x in _drive.Wait(10)) yield return x;
        }
    }

    /// <summary>The plan of a hull's busiest deck, each space in its purpose's colour, walls dark, doors green, ways out red, flights of stairs as bars, the prow at the top.</summary>
    static void DeckPlan(CapitalHull.HullPlan plan, string file, string title)
    {
        ShipDecks I = plan.Inside;
        int deck = Enumerable.Range(0, I.Levels).OrderByDescending(d => I.Rooms.Where(r => r.Deck == d || r.Hall && r.Deck == d - 1).Sum(r => r.Area)).First();
        const int px = 6;
        int w = I.NU * px + 20, h = I.NV * px + 20;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgb8);
        img.Fill(new Color(0.13f, 0.14f, 0.17f));
        void Cell(int u, int v, Color c) => img.FillRect(new Rect2I(10 + (u - I.U0) * px, 10 + v * px, px - 1, px - 1), c);
        Color Of(ShipDecks.Room r) => r.Purpose switch
        {
            "corridor" => new Color(0.86f, 0.8f, 0.5f), "stair" => new Color(0.95f, 0.55f, 0.2f), "bridge" => new Color(0.3f, 0.55f, 0.9f),
            "engine-room" => new Color(0.75f, 0.3f, 0.22f), "reactor" => new Color(0.9f, 0.45f, 0.25f), "core-room" => new Color(0.95f, 0.2f, 0.75f),
            "hold" or "ore-hold" => new Color(0.48f, 0.66f, 0.38f), "hangar" => new Color(0.38f, 0.62f, 0.56f), "medbay" => new Color(0.92f, 0.92f, 0.92f),
            "galley" => new Color(0.9f, 0.75f, 0.38f), "crew-quarters" or "cabin" or "barracks" or "officers" => new Color(0.78f, 0.66f, 0.55f),
            _ => new Color(0.58f, 0.58f, 0.66f),
        };
        for (int v = 0; v < I.NV; v++)
            for (int u = I.U0; u < I.U0 + I.NU; u++)
            {
                int z = I.Zone(deck, u, v);
                if (z >= 0) Cell(u, v, I.Upper(deck, z) ? Of(I.Rooms[z]).Darkened(0.45f) : Of(I.Rooms[z]));
                else if (z == ShipDecks.Fill) Cell(u, v, new Color(0.3f, 0.31f, 0.35f));
                else if (I.Mask(u, I.Walk(deck) + 1, v) != 0) Cell(u, v, new Color(0.22f, 0.23f, 0.26f));
            }
        foreach (ShipDecks.DoorPlan p in I.Doors.Where(p => p.Deck == deck))
            for (int k = 0; k < p.Width; k++) Cell(p.U + (p.DV != 0 ? k : 0), p.V + (p.DV != 0 ? 0 : k), new Color(0.15f, 0.9f, 0.4f));
        foreach (ShipDecks.Opening o in I.Openings.Where(o => o.Deck == deck))
            for (int k = 0; k < o.Width; k++) Cell(o.U + o.DU + (o.DV != 0 ? k : 0), o.V + o.DV + (o.DV != 0 ? 0 : k), new Color(1f, 0.15f, 0.15f));
        foreach (ShipDecks.Flight f in I.Flights.Where(f => f.Deck == deck || f.Deck == deck - 1))
            for (int j = 1; j <= 5; j++)
                for (int lane = 0; lane < f.Width; lane++)
                    Cell(f.U + f.DU * j + (f.DV != 0 ? lane : 0), f.V + f.DV * j + (f.DV != 0 ? 0 : lane), j % 2 == 0 ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.95f, 0.55f, 0.2f));
        img.SavePng(file);
        GD.Print($"interior: {title}, deck {deck} of {I.Levels}: {file}");
    }
}
