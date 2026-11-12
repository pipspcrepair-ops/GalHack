using System;
using System.Collections.Generic;
using Godot;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Places;

/// <summary>
/// The town's people open the place's doors on their way (<see cref="DoorWay"/>): coming up to a shut one,
/// they open it, are held off its leaves till it stands open enough for them, and go through; it shuts itself
/// behind them a while after the doorway is clear (<see cref="DoorWays.AutoClose"/>). A locked door opens only
/// for those who would carry its card; a town that has barred itself keeps its way in shut to everyone.
/// </summary>
public sealed partial class Town
{
    List<DoorWay> _ways;

    /// <summary>The keycard a job carries (0 staff, 1 science, 2 security, 3 command), or -1 for none.</summary>
    static int Card(PersonRole job) => job switch
    {
        PersonRole.Elder => 3,
        PersonRole.Guard or PersonRole.Lawman => 2,
        PersonRole.Medic => 1,
        PersonRole.Worker or PersonRole.Mechanic or PersonRole.Trader or PersonRole.Farmer or PersonRole.Scavenger => 0,
        _ => -1,
    };

    /// <summary>Whether a person may open a door: not a lift's (the car does that), not the way in of a barred town, a locked one only with its card.</summary>
    public bool MayOpen(Person p, DoorWay d)
    {
        if (d.Lift || d.Jammed) return false;
        if (GatesShut && d.Index == -1) return false;
        if (d.Clearance == null) return true;
        return Card(p.Job) >= Array.IndexOf(Slice.Game.Game.Clearances, d.Clearance);
    }

    /// <summary>
    /// The doors on a person's way just ahead opened for them. A door they may not open drops the way: they
    /// wait where they are rather than stand at it. False then.
    /// </summary>
    bool ThroughDoors(Person p)
    {
        if (_ways == null || _ways.Count == 0 || p.Path == null) return true;
        Vector3 from = p.Pos;
        for (int k = p.Step; k < p.Path.Count && k <= p.Step + 2; k++)
        {
            Vector3 to = p.Path[k];
            float len = new Vector2(to.X - from.X, to.Z - from.Z).Length();
            int n = Math.Max(1, (int)MathF.Ceiling(len / 0.25f));
            for (int i = 0; i <= n; i++)
            {
                Vector3 q = from.Lerp(to, i / (float)n);
                if (new Vector2(q.X - p.Pos.X, q.Z - p.Pos.Z).Length() > 2f) break;
                var c = new PInt3((int)MathF.Floor(q.X), (int)MathF.Floor(q.Y + 0.05f), (int)MathF.Floor(q.Z));
                foreach (DoorWay d in _ways)
                {
                    if (!d.Holds(c) || d.Jammed || d.Target > 0.5f && !d.Shut) continue;
                    if (!MayOpen(p, d))
                    {
                        p.Path = new List<Vector3>();
                        p.Step = 0;
                        p.Wait = MathF.Max(p.Wait, 4f);
                        return false;
                    }
                    d.Target = 1f;
                    d.ByFolk = true;
                    d.ClearFor = 0f;
                    Memory.DoorsOpen[d.Index] = true;
                }
            }
            from = to;
        }
        return true;
    }

    /// <summary>With no place standing (a day kept without the game), its doors move here.</summary>
    void TickWays(float dt)
    {
        if (_ways == null || _place != null && GodotObject.IsInstanceValid(_place)) return;
        foreach (DoorWay d in _ways) DoorWays.Step(Memory, d, d.Target, dt);
    }

    /// <summary>The town's people about, as the doors see them (local feet, radius, height).</summary>
    IEnumerable<(Vector3 Feet, float Radius, float Height)> BodiesNear()
    {
        foreach (Person p in People)
            if (!p.Dead && !p.Gone && !p.Indoors && !p.Away) yield return (p.Pos, BodyRadius(p), Height(p));
    }
}
