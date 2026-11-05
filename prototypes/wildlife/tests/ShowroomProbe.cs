using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// The real scenes, through their input path: on the planet T flips between noon and midnight
/// (with the herd bedded down at night); the underground showroom builds every station (the
/// generator cave, the dug tunnel, the outpost, the derelict hold, the mutation row, a
/// settlement's animals), each has its creatures, and nothing is ever inside the rock or a block.
///   godot --headless --path prototypes/wildlife res://tests/ShowroomProbe.tscn
/// </summary>
public partial class ShowroomProbe : Node
{
    int _failures, _checks;

    public override void _Ready() => Run();

    async void Run()
    {
        try
        {
            var main = new Main { Name = "Main" };
            AddChild(main);
            await Frames(10);
            main.Sim.Clock.Time01 = 0.4f;
            Tap(Key.T);
            await Frames(10);
            bool night = main.Sim.Clock.IsNight;
            Tap(Key.T);
            await Frames(10);
            bool noon = !main.Sim.Clock.IsNight && System.MathF.Abs(main.Sim.Clock.Time01 - 0.5f) < 0.01f;
            Check(night && noon && main.Sim.ActivityCycles && main.Sim.RestSites.Count > 0,
                $"on the planet T flips between midnight and noon, and the fauna keeps its hours ({main.Sim.RestSites.Count} rest sites; night {night}, noon {noon}, time {main.Sim.Clock.Time01:F3})");
            main.QueueFree();
            await Frames(3);

            var room = new Showroom { Name = "Showroom", FreeCamera = true };
            AddChild(room);
            await Frames(5);
            for (int i = 0; i < room.Stations.Count; i++)
            {
                room.GoTo(i);
                await Frames(120);
                Showroom.Area a = room.Current;
                int alive = a.Sim.Creatures.Count(c => !c.Removed && c.Alive);
                float worst = float.MinValue;
                foreach (Creature c in a.Sim.Creatures)
                {
                    if (c.Removed || c.InWalls || c.Space == null) continue;
                    Vector3 center = Crawl.Center(c);
                    float inside = a.Place != null ? (a.Place.Solid((int)Mathf.Floor(center.X), (int)Mathf.Floor(center.Y), (int)Mathf.Floor(center.Z)) ? 1f : -1f) : a.Space.Density(center);
                    worst = System.MathF.Max(worst, inside);
                }
                Check(alive > 0 && worst <= 0f && a.Sim.Rescues == 0 && a.View.ViewCount > 0,
                    $"showroom station {i + 1}, {room.Stations[i].label}: {alive} creatures, {a.View.ViewCount} drawn, none inside the rock or a block, {a.Sim.Rescues} put back");
            }
            Showroom.Area town = room.Town;
            Settlement s = town.Sim.Settlements[0];
            bool kept = room.Domestics.All(c => s.Contains(c.Position)) && room.Domestics.Any(c => c.Species.Domestic == DomesticKind.Stock)
                        && room.Domestics.Any(c => c.Species.Domestic == DomesticKind.Working) && room.Domestics.Any(c => c.Species.Domestic == DomesticKind.Pet);
            Check(kept, "the settlement's stock, working beast and pets are all inside the town");
            Check(room.Row.Select(c => c.Species.Mutation).SequenceEqual(Showroom.RowStrengths), "the mutation row shows one species at 0, 0.5 and 1");
        }
        catch (System.Exception e)
        {
            Fail($"crashed: {e}");
        }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_checks} checks)" : $"{_failures} OF {_checks} CHECK(S) FAILED");
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    static void Tap(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = false });
    }

    void Check(bool ok, string what)
    {
        _checks++;
        if (ok) GD.Print($"PASS  {what}");
        else
        {
            _failures++;
            GD.PrintErr($"FAIL  {what}");
            GD.Print($"FAIL  {what}");
        }
    }

    void Fail(string what)
    {
        _failures++;
        GD.Print($"FAIL  {what}");
    }
}
