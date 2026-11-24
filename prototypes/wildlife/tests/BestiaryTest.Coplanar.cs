using System.Collections.Generic;
using System.Linq;
using GalHack.Geometry;
using Godot;
using NV3 = System.Numerics.Vector3;
using NV4 = System.Numerics.Vector4;

namespace Wildlife;

public partial class BestiaryTest
{
    /// <summary>
    /// No body z-fights: on fresh worlds, people of every role and the animals of the world, built as they are drawn
    /// (one skinned mesh, each piece on its own bone), have no two parts of one piece with faces of different looks
    /// lying in one plane over each other (<see cref="Coplanar"/>). A part laid on another stands off it.
    /// </summary>
    void NoBodyFightsItself()
    {
        var bodies = new List<(string Name, IReadOnlyList<Piece> Pieces)>();
        foreach (float t in new[] { -25f, 10f, 20f, 45f })
        {
            var life = Bestiary.Roll(_data, World(t, alien: t > 15f ? 1f : 0.25f));
            for (int i = 0; i < 12; i++)
            {
                Species p = life.People.Person(Seed(), (PersonRole)(i % 10));
                bodies.Add(($"{p.Figure.Folk?.Covering ?? "human"} {(PersonRole)(i % 10)}", HumanBody.Build(p).Pieces));
            }
            foreach (Species s in life.Fauna.Species.Take(10))
                bodies.Add(($"{s.CommonName} ({s.Rig})", Rig.Create(s, null).Layout.Pieces));
        }
        int total = 0;
        var worst = new Dictionary<string, int>();
        foreach (var (name, pieces) in bodies)
        {
            int n = Fights(pieces);
            total += n;
            if (n > 0) worst[name] = n;
        }
        foreach (var kv in worst.OrderByDescending(k => k.Value).Take(8)) GD.Print($"    {kv.Value,5} pairs in a {kv.Key}");
        // Zero is the aim; a few curved parts still fold onto their neighbours. A ceiling, to be lowered, never raised.
        Check(total <= 200, $"bodies hardly draw two faces of different looks in one plane: {total} pairs in {worst.Count} of {bodies.Count} bodies, at most 200");
    }

    /// <summary>Fighting pairs within one piece of a body (faces of two pieces sit on different bones).</summary>
    static int Fights(IReadOnlyList<Piece> pieces)
    {
        MeshBuilder.Prepared m = MeshBuilder.Skinning(pieces);
        var faces = new List<Coplanar.Face>();
        for (int t = 0; t + 2 < m.I.Length; t += 3)
        {
            int a = m.I[t], b = m.I[t + 1], c = m.I[t + 2];
            NV3 P(int i) => new(m.V[i].X, m.V[i].Y, m.V[i].Z);
            Vector3 n = m.N[a] + m.N[b] + m.N[c];
            Color k = m.C[a];
            // Wound for the engine (clockwise from outside): the winding, not the smoothed normals, says which side is drawn.
            faces.Add(new Coplanar.Face(P(a), P(c), P(b), NV3.Zero, Coplanar.Look((int)(m.Uv[a].Y * 16f), new NV4(k.R, k.G, k.B, k.A)), m.Bones[a * 4]));
        }
        // A wing beat's blur is see-through (it blends, it can't fight).
        return Coplanar.Find(faces).Count(p => faces[p.P].Source == faces[p.Q].Source && !pieces[faces[p.P].Source].Blur);
    }
}
