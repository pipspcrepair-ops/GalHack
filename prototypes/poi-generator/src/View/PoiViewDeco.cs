using System;
using System.IO;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Dressing from the shared decorations library (<c>shared/Deco</c>), room by room as the game dresses
/// a place (<see cref="RoomDecor"/>): each room's size, doorways and kept-clear floor go in, the library
/// arranges its pieces as the room was used and then left, and the generator's own props go from it.
/// The pieces are scenery here (no collision), so they never close a way the architect checked.
/// </summary>
public partial class PoiView
{
    static DecoPoi _decoPoi;
    static DecoMeshCache _decoMeshes;
    static bool _decoTried;

    /// <summary>Whether places are shown dressed by the library (the game's look); off shows the generator's own props.</summary>
    public static bool Dressed = true;

    static string ItemsData => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "shared", "Items", "data"));

    /// <summary>The library, loaded once (null if it can't be read).</summary>
    public static DecoPoi Decor
    {
        get
        {
            if (_decoTried) return _decoPoi;
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
            return _decoPoi;
        }
    }

    void DressFromDecoLibrary()
    {
        if (!Dressed || Decor == null) return;
        var holder = new Node3D { Name = "Deco" };
        AddChild(holder);
        foreach (RoomDecor.Dressable room in RoomDecor.Rooms(Poi))
        {
            try
            {
                DecoPlan plan = _decoPoi.Dress(room.Room, Poi.Kit?.Id);
                RoomDecor.ClearProps(Poi, room);
                var view = DecoPlanView.Create(_decoMeshes, plan, collide: false, lights: false);
                view.Position = new Vector3(room.Corner.X, room.Corner.Y + room.Floor + 0.008f, room.Corner.Z);
                holder.AddChild(view);
            }
            catch (Exception e)
            {
                GD.PushWarning($"could not dress {room.Module.Purpose}: {e.Message}");
            }
        }
    }
}
