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
    /// <summary>A site of abandoned vehicles, dressed by the library and set on the ground piece by piece, its lane down the road.</summary>
    sealed class WreckPiece : Piece
    {
        public WreckSite Site;

        public override void Emit(TileBuilder b)
        {
            DecoPlan plan = InfraDeco.DressSite(Site.Site, Site.Seed, Site.Tech);
            if (plan == null) return;
            // Each piece stands on the ground under it (a pile-up keeps its stacking).
            var ground = plan.Placements.Select(pl =>
            {
                var (x, z) = Site.World(pl.X, pl.Z);
                return b.Ground(x, z);
            }).ToArray();
            NV corner = b.Local(Site.X, Site.Y, Site.Z);
            float yaw = Mathf.DegToRad(Site.Yaw), siteY = Site.Y;
            b.Extra.Add(() =>
            {
                DecoMeshCache cache = InfraDeco.Meshes;
                if (cache == null) return new Node3D { Name = "Wrecks (no library)" };
                var view = DecoPlanView.Create(cache, plan, collide: true, lights: false);
                view.Name = Site.Site;
                view.Position = new Vector3(corner.X, siteY, corner.Z);
                view.Rotation = new Vector3(0, yaw, 0);
                for (int i = 0; i < view.Nodes.Count && i < plan.Placements.Count; i++)
                {
                    Vector3 at = view.Nodes[i].Position;
                    view.Nodes[i].Position = at with { Y = ground[i] - siteY + plan.Placements[i].Y };
                }
                return view;
            });
        }
    }
}
