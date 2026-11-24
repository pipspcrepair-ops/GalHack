using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;

namespace Wildlife;

/// <summary>Counts the engine errors that name a skeleton or the creature view (a bone added twice, a piece pointing at a bone that is not there).</summary>
public partial class SkinWatch : Logger
{
    static int _errors;
    public static int Errors => Volatile.Read(ref _errors);

    public static void Install() => OS.AddLogger(new SkinWatch());

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        string all = $"{file} {code} {rationale} {string.Join(' ', scriptBacktraces.Select(b => b.Format()))}";
        if (all.Contains("Skeleton3D") || all.Contains("skeleton_3d") || all.Contains("CreatureView")) Interlocked.Increment(ref _errors);
    }
}

public partial class BestiaryTest
{
    /// <summary>
    /// Every kind of body drawn as a skinned creature: every form in the book at many seeds (many-headed,
    /// many-legged, winged, giant), each domestic kind (with tack), people, Husks and machines. The engine logs
    /// no skeleton error, each body has a bone a piece, and a creature is one to three meshes.
    /// </summary>
    void SkinnedBodiesAreSound()
    {
        SkinWatch.Install();
        var gen = new FaunaGenerator(_data.Fauna, _data.Activity);
        var species = new List<Species>();
        foreach (FormDef f in _data.Fauna.Forms)
            for (int n = 0; n < 8; n++)
                try { species.Add(gen.RollOne(Seed(), f.Id, Enum.GetValues<Behaviour>()[_fresh.Int(0, Enum.GetValues<Behaviour>().Length - 1)], _fresh.Range(0f, 1f))); } catch (Exception) { }
        for (int w = 0; w < 6; w++)
        {
            WorldConditions world = World(_fresh.Range(-30f, 45f), alien: _fresh.Range(0f, 1f), mutagen: w % 2 * 0.8f);
            world.Coreward = world.Mutagen > 0f ? 1f : 0f;
            WorldLife life = Bestiary.Roll(_data, world);
            species.AddRange(life.Moving);
            foreach (DomesticKind kind in new[] { DomesticKind.Stock, DomesticKind.Working, DomesticKind.Pet })
                try { species.Add(life.Domestic(kind, w)); } catch (Exception) { }
            PeopleGenerator g = life.People;
            for (int n = 0; n < 6; n++)
            {
                Species p = g.Person(Seed(), Enum.GetValues<PersonRole>()[_fresh.Int(0, Enum.GetValues<PersonRole>().Length - 1)], founders: g.FoundersOf(Seed()));
                species.Add(n == 5 && Husks.CanTurn(p) ? Husks.Turn(p, Seed(), _data.Husks) : p);
            }
            for (int n = 0; n < 4; n++) species.Add(Robots.Roll(world, _fresh.Int(0, 30)));
        }
        species.AddRange(species.Where(s => s.Hosts != null).SelectMany(s => s.Hosts).ToList());

        var sim = new WildSim(_data, new Terrain((x, z) => 0f, 200f), new NavGrid(200f, 3f));
        int built = 0, bones = 0, dupes = 0, meshesMax = 0, bad = 0;
        foreach (Species s in species)
        {
            Creature c = sim.Spawn(s, new Vector3(_fresh.Range(-30f, 30f), 0f, _fresh.Range(-30f, 30f)));
            if (c.Rig is SwarmRig) continue;
            var view = new CreatureView();
            AddChild(view);
            view.Build(c);
            for (int k = 0; k < 3; k++) { sim.Step(1f / 30f); view.Sync(); }
            Skeleton3D sk = view.GetChildren().OfType<Skeleton3D>().FirstOrDefault();
            if (sk == null) { bad++; continue; }
            built++;
            var pieces = c.Rig.Layout.Pieces;
            if (sk.GetBoneCount() != pieces.Count)
            {
                bones++;
                GD.Print($"    short: {s.Id} {s.Form} {s.Plan} {s.Rig} {s.Domestic}: repeated {string.Join(",", pieces.GroupBy(q => q.Name).Where(q => q.Count() > 1).Select(q => $"{q.Key}x{q.Count()}"))}");
            }
            var names = new HashSet<string>();
            for (int i = 0; i < sk.GetBoneCount(); i++) if (!names.Add(sk.GetBoneName(i))) dupes++;
            int meshes = sk.GetChildren().OfType<MeshInstance3D>().Count();
            meshesMax = Math.Max(meshesMax, meshes);
            if (meshes is < 1 or > 3) bad++;
            view.QueueFree();
        }
        Check(built > 100, $"{built} skinned bodies built (every form, domestic kind, person, Husk and machine of fresh worlds)");
        Check(bones == 0 && dupes == 0, $"every body has a bone for every piece, each its own ({bones} short, {dupes} repeated names)");
        Check(bad == 0 && meshesMax <= 3, $"a creature is one to three meshes ({meshesMax} at most, {bad} outside)");
        Check(SkinWatch.Errors == 0, $"the engine logged no skeleton or creature view error ({SkinWatch.Errors})");
    }
}
