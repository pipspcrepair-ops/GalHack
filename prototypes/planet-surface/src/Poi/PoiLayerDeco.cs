using System;
using System.IO;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using Godot;
using PoiGenerator.Gen;

namespace PlanetSurface.Poi;

/// <summary>
/// Alien places on the planet are dressed from the decorations library's alien set, room by room,
/// the same way the POI viewer does it: each room's size and doorways go in, the library arranges
/// its pieces. The pieces are scenery here (no collision), so they never close a way the architect
/// checked. The library loads once, on first need.
/// </summary>
public partial class PoiLayer
{
    static DecoPoi _decoPoi;
    static DecoMeshCache _decoMeshes;
    static bool _decoTried;

    static string ItemsData => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "shared", "Items", "data"));

    /// <summary>Rooms dressed on the planet so far (tests read it).</summary>
    public int DecoRooms { get; private set; }

    static bool Alien(PoiInstance inst) => (inst.Family.Style ?? "").StartsWith("alien_", StringComparison.Ordinal);

    void DressFromDecoLibrary(Node3D node, PoiInstance inst)
    {
        if (!Alien(inst)) return;
        if (!_decoTried)
        {
            _decoTried = true;
            try
            {
                var catalogue = DecoCatalogue.LoadDirectory(ItemsData);
                var forge = new DecoForge(catalogue);
                _decoPoi = new DecoPoi(forge, DecoArrangements.LoadDirectory(ItemsData, catalogue));
                _decoMeshes = new DecoMeshCache(forge);
            }
            catch (Exception e)
            {
                GD.PushWarning($"decorations library unavailable: {e.Message}");
            }
        }
        if (_decoPoi == null) return;
        var layout = inst.Layout;
        var holder = new Node3D { Name = "Deco" };
        node.AddChild(holder);
        string state = inst.State.ToString().ToLowerInvariant();
        foreach (var m in layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault))
        {
            var floor = m.Cells.Where(c => c.Y == m.Min.Y).ToList();
            if (floor.Count < 9) continue;
            int x0 = floor.Min(c => c.X), x1 = floor.Max(c => c.X), z0 = floor.Min(c => c.Z), z1 = floor.Max(c => c.Z);
            var room = new DecoRoom
            {
                Width = x1 - x0 + 1, Depth = z1 - z0 + 1, Height = 3f, Set = "alien", RoomId = m.Purpose, State = state,
                Seed = PoiGenerator.Gen.Hash.Combine(inst.SpawnSeed, (ulong)m.Index + 1),
            };
            foreach (var d in layout.Doorways.Where(d => d.ModuleA == m.Index || d.ModuleB == m.Index))
            {
                var cells = d.Cells.Where(c => c.Y == m.Min.Y).ToList();
                if (cells.Count == 0) continue;
                PoiGenerator.Gen.Int3 c = cells.Aggregate(PoiGenerator.Gen.Int3.Min);
                float width = cells.Count;
                if (c.Z < z0) room.Doors.Add(new DecoDoor(DecoWall.North, c.X - x0 + width / 2, width));
                else if (c.Z > z1) room.Doors.Add(new DecoDoor(DecoWall.South, c.X - x0 + width / 2, width));
                else if (c.X < x0) room.Doors.Add(new DecoDoor(DecoWall.West, c.Z - z0 + width / 2, width));
                else if (c.X > x1) room.Doors.Add(new DecoDoor(DecoWall.East, c.Z - z0 + width / 2, width));
            }
            try
            {
                DecoPlan plan = _decoPoi.Dress(room);
                var view = DecoPlanView.Create(_decoMeshes, plan, collide: false, lights: false);
                view.Position = new Vector3(x0, m.Min.Y, z0);
                holder.AddChild(view);
                DecoRooms++;
            }
            catch (Exception e)
            {
                GD.PushWarning($"could not dress {m.Purpose}: {e.Message}");
            }
        }
    }
}
