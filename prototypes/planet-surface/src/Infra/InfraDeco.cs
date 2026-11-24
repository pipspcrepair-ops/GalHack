using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using Godot;
using PlanetSurface.World;
using PoiGenerator.Gen;
using NV = System.Numerics.Vector3;

namespace PlanetSurface.Infra;

/// <summary>
/// The decorations library for the world round the places: loaded once, on first need, shared by
/// every tile. Planning a site is not thread-safe in the library, so tiles take turns.
/// </summary>
public static class InfraDeco
{
    static readonly object Gate = new();
    static DecoPoi _poi;
    static DecoMeshCache _meshes;
    static bool _tried;

    static string ItemsData => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..", "shared", "Items", "data"));

    /// <summary>The library's site planner, or null if it could not load.</summary>
    public static DecoPoi Poi
    {
        get
        {
            lock (Gate)
            {
                if (_tried) return _poi;
                _tried = true;
                try
                {
                    var catalogue = DecoCatalogue.LoadDirectory(ItemsData);
                    var forge = new DecoForge(catalogue);
                    _poi = new DecoPoi(forge, DecoArrangements.LoadDirectory(ItemsData, catalogue));
                }
                catch (Exception e)
                {
                    GD.PushWarning($"decorations library unavailable: {e.Message}");
                }
                return _poi;
            }
        }
    }

    /// <summary>Godot meshes for the library's looks (main thread only).</summary>
    public static DecoMeshCache Meshes => _meshes ??= Poi == null ? null : new DecoMeshCache(Poi.Forge);

    /// <summary>
    /// Lets the library go with its caches, on the main thread (a new game: the last world's looks are no use to
    /// the next); the next use loads it afresh, and anything of the last world still working keeps the old one.
    /// </summary>
    public static void Forget()
    {
        lock (Gate)
        {
            _poi = null;
            _tried = false;
        }
        _meshes = null;
    }

    /// <summary>A site of the library dressed (an evacuation jam, a derailed train...), one tile at a time.</summary>
    public static DecoPlan DressSite(string site, ulong seed, TechLevel tech, string state = "abandoned")
    {
        DecoPoi poi = Poi;
        if (poi == null) return null;
        lock (Gate)
        {
            DecoRoom room = poi.SiteRoom(site, seed, (DecoTech)(int)tech, state);
            return poi.DressSite(room);
        }
    }
}

public partial class InfraLayer
{
    /// <summary>A vehicle of a wreck site's id among <see cref="PlanetWorld.Felled"/>: one taken apart stays gone, as felled trees do.</summary>
    public static long WreckId(WreckSite site, int index) => long.MinValue | (long)(Planets.Gen.Hash.Of(site.Seed, index, 0x3EC5) >> 2);

    /// <summary>A wreck site stood up, near the player: a game makes its vehicles salvageable.</summary>
    public event Action<WreckSite, DecoPlanView> WreckStood;

    /// <summary>A site of abandoned vehicles, dressed by the library and set on the ground piece by piece, its lane down the road.</summary>
    sealed class WreckPiece : Piece
    {
        public WreckSite Site;

        public override void Emit(TileBuilder b)
        {
            DecoPlan plan = InfraDeco.DressSite(Site.Site, Site.Seed, Site.Tech);
            if (plan == null) return;
            // Each piece seats on the ground under it (a pile-up keeps its stacking; a train's cars stay on their line).
            var seats = plan.Placements.Select(pl =>
            {
                var (x, z) = Site.World(pl.X, pl.Z);
                if (pl.Assembly >= 0 || pl.Support >= 0) return (Local: b.Local(x, b.Ground(x, z), z), Tilt: System.Numerics.Matrix4x4.Identity);
                GalHack.Deco.DecoRect foot = GalHack.Deco.DecoLayout.Footprint(InfraDeco.Poi.Forge, pl);
                float reach = MathF.Max(0.6f, MathF.Min(foot.U.Length(), foot.V.Length()));
                System.Numerics.Matrix4x4 seat = b.Seat(x, z, reach, 0.3f);
                return (Local: seat.Translation, Tilt: seat with { Translation = NV.Zero });
            }).ToArray();
            NV corner = b.Local(Site.X, Site.Y, Site.Z);
            PlanetWorld world = b.World;
            float yaw = Mathf.DegToRad(Site.Yaw), siteY = Site.Y;
            // A piece a step: a jam of vehicles made in one go stopped a frame for 100 ms.
            DecoPlanView view = null;
            Basis toSite = new Basis(Vector3.Up, yaw).Inverse();
            b.Extra.Add(() =>
            {
                if (InfraDeco.Meshes == null) return new Node3D { Name = "Wrecks (no library)" };
                view = DecoPlanView.Start(plan);
                view.Name = Site.Site;
                view.Position = new Vector3(corner.X, siteY, corner.Z);
                view.Rotation = new Vector3(0, yaw, 0);
                return view;
            });
            for (int p = 0; p < plan.Placements.Count; p++)
            {
                int i = p;
                b.Extra.Add(() =>
                {
                    if (view == null) return null;
                    DecoNode node = view.Add(InfraDeco.Meshes, plan.Placements[i], collide: true, lights: false);
                    var (local, t) = seats[i];
                    node.Position = node.Position with { Y = local.Y - siteY + plan.Placements[i].Y };
                    // The slope, in the site's frame, under the piece's own turn.
                    var slope = new Basis(new Vector3(t.M11, t.M12, t.M13), new Vector3(t.M21, t.M22, t.M23), new Vector3(t.M31, t.M32, t.M33));
                    node.Basis = toSite * slope * toSite.Inverse() * node.Basis;
                    if (world.Felled.Contains(WreckId(Site, i))) node.Visible = false; // taken apart: its body goes in the next step
                    return null;
                });
            }
            b.Extra.Add(() =>
            {
                if (view == null) return null;
                view.AddWires(InfraDeco.Meshes);
                view.SetMeta("wreck", world.Infra.Wrecks.IndexOf(Site));
                return view;
            });
        }
    }
}
