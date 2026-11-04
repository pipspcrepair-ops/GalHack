using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Persistence;
using GalHack.Records;
using Godot;
using Saves.Zones;

namespace Saves;

/// <summary>
/// The two places in this prototype, and how records become nodes in them.
///
/// The surface is a flat desert of mesas and spires around the player's base. Orbit has the same
/// planet below (a sphere of radius <see cref="PlanetRadius"/> whose top is at the origin) and
/// stars all round. A point on the surface maps to the point on the sphere straight above it and
/// back, so you come down where you crossed into the atmosphere.
/// </summary>
public sealed class Places : IZoneContent
{
    public const float PlanetRadius = 4000f;
    public static readonly Vector3 PlanetCenter = new(0, -PlanetRadius, 0);

    // Heights above the ground (surface) or the planet (orbit). The gaps between them are hysteresis:
    // you arrive well clear of the edge you just crossed, so you never bounce straight back.
    public const float SurfacePreloadAltitude = 420f;  // climbing past this starts loading orbit
    public const float SurfaceEdgeAltitude = 600f;     // the atmosphere's edge: the climb tunnel starts
    public const float SurfaceArrivalAltitude = 520f;  // where you come out of re-entry
    public const float OrbitArrivalAltitude = 650f;    // where you come out of the climb
    public const float OrbitPreloadAltitude = 560f;    // descending past this starts loading the surface
    public const float OrbitEdgeAltitude = 400f;       // the atmosphere's top: the re-entry tunnel starts
    public const float SurfaceHalfSize = 3500f;

    public RecordTable Table { get; set; }

    /// <summary>The local player's node, placed wherever its record says. Null in tests: players get a stand-in.</summary>
    public Player LocalPlayer { get; set; }

    readonly Dictionary<string, ProceduralSkyMaterial> _skies = new();
    readonly Dictionary<string, Godot.Environment> _environments = new();

    // ---- Mapping between surface and orbit -----------------------------------------------

    public static Vector3 OrbitPointAbove(float x, float z, float altitude)
    {
        Vector3 up = new Vector3(x / PlanetRadius, 1f, z / PlanetRadius).Normalized();
        return PlanetCenter + up * (PlanetRadius + altitude);
    }

    public static Vector2 SurfacePointBelow(Vector3 orbitPosition)
    {
        Vector3 up = (orbitPosition - PlanetCenter).Normalized();
        float y = Mathf.Max(up.Y, 0.25f); // the far side of the planet maps to the edge of the one surface we have
        return new Vector2(
            Mathf.Clamp(PlanetRadius * up.X / y, -SurfaceHalfSize, SurfaceHalfSize),
            Mathf.Clamp(PlanetRadius * up.Z / y, -SurfaceHalfSize, SurfaceHalfSize));
    }

    public static float OrbitAltitude(Vector3 orbitPosition) => (orbitPosition - PlanetCenter).Length() - PlanetRadius;

    /// <summary>"Up" at a point in orbit: away from the planet's centre.</summary>
    public static Vector3 OrbitUp(Vector3 orbitPosition) => (orbitPosition - PlanetCenter).Normalized();

    // ---- Generating, on a worker thread ---------------------------------------------------

    sealed class SurfaceData
    {
        public readonly List<(Vector3 Position, Vector3 Size, float Yaw, int Color)> Mesas = new();
        public Vector3[] Rocks;
    }

    sealed class OrbitData
    {
        public byte[] Stars;
        public int StarsWidth, StarsHeight;
        public byte[] PlanetSkin;
        public int SkinWidth, SkinHeight;
    }

    public object Generate(string zoneId) => zoneId switch
    {
        ZoneIds.Surface => GenerateSurface(),
        ZoneIds.Orbit => GenerateOrbit(),
        _ => null, // a zone this prototype has no terrain for: just its records
    };

    static SurfaceData GenerateSurface()
    {
        var data = new SurfaceData();
        var rng = new Random(1977);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        for (int i = 0; i < 180; i++)
        {
            var spot = new Vector3(R(-2600, 2600), 0, R(-2600, 2600));
            if (spot.Length() < 90f) continue; // keep the landing ground and base clear
            float width = R(10f, 70f), height = R(12f, 140f);
            bool spire = rng.NextDouble() < 0.35;
            Vector3 size = spire
                ? new Vector3(width * 0.3f, height * 1.6f, width * 0.3f)
                : new Vector3(width, height * 0.5f, width * R(0.6f, 1.4f));
            data.Mesas.Add((spot + Vector3.Up * size.Y * 0.5f, size, R(0, 90), i % 4));
        }
        data.Rocks = new Vector3[1500];
        for (int i = 0; i < data.Rocks.Length; i++)
        {
            var spot = new Vector3(R(-600, 600), 0, R(-600, 600));
            if (spot.Length() < 40f) spot *= 3f;
            data.Rocks[i] = spot;
        }
        return data;
    }

    static OrbitData GenerateOrbit()
    {
        var rng = new Random(2001);
        var data = new OrbitData { StarsWidth = 2048, StarsHeight = 1024, SkinWidth = 1024, SkinHeight = 512 };

        // Stars on deep blue-black, with a band of pink and teal nebula across the sky.
        data.Stars = new byte[data.StarsWidth * data.StarsHeight * 4];
        for (int y = 0; y < data.StarsHeight; y++)
        {
            float band = Mathf.Exp(-Mathf.Pow((y - data.StarsHeight * 0.42f) / (data.StarsHeight * 0.09f), 2));
            for (int x = 0; x < data.StarsWidth; x++)
            {
                float wobble = 0.5f + 0.5f * Mathf.Sin(x * 0.011f + Mathf.Sin(x * 0.003f) * 4f);
                float n = band * wobble;
                int i = (y * data.StarsWidth + x) * 4;
                data.Stars[i] = (byte)(6 + 70 * n);
                data.Stars[i + 1] = (byte)(8 + 22 * n);
                data.Stars[i + 2] = (byte)(20 + 60 * n);
                data.Stars[i + 3] = 255;
            }
        }
        for (int s = 0; s < 9000; s++)
        {
            int x = rng.Next(data.StarsWidth), y = rng.Next(data.StarsHeight);
            int i = (y * data.StarsWidth + x) * 4;
            byte b = (byte)rng.Next(120, 256);
            bool warm = rng.NextDouble() < 0.2;
            data.Stars[i] = b;
            data.Stars[i + 1] = (byte)(warm ? b * 0.8f : b);
            data.Stars[i + 2] = (byte)(warm ? b * 0.6f : b);
        }

        // The planet seen from above: bold bands of ochre, cream and rust, broken up by dunes.
        Color[] bands = { new("d08850"), new("e9c48a"), new("a0522d"), new("d2915a"), new("8b3a1e"), new("e8b07a") };
        data.PlanetSkin = new byte[data.SkinWidth * data.SkinHeight * 3];
        for (int y = 0; y < data.SkinHeight; y++)
        {
            for (int x = 0; x < data.SkinWidth; x++)
            {
                float v = y / (float)data.SkinHeight * 46f + 1.2f * Mathf.Sin(x * 0.018f) + 0.5f * Mathf.Sin(x * 0.071f + y * 0.09f);
                Color c = bands[Mathf.PosMod((int)v, bands.Length)].Lerp(bands[Mathf.PosMod((int)v + 1, bands.Length)], v - Mathf.Floor(v));
                float grain = 0.92f + 0.08f * (float)rng.NextDouble();
                int i = (y * data.SkinWidth + x) * 3;
                data.PlanetSkin[i] = (byte)(255 * c.R * grain);
                data.PlanetSkin[i + 1] = (byte)(255 * c.G * grain);
                data.PlanetSkin[i + 2] = (byte)(255 * c.B * grain);
            }
        }
        return data;
    }

    // ---- Building, on the main thread a few steps per frame -------------------------------

    public IEnumerable<Action> Build(Zone zone, object generated) => generated switch
    {
        SurfaceData surface => BuildSurface(zone, surface),
        OrbitData orbit => BuildOrbit(zone, orbit),
        _ => new Action[] { () => BuildEmpty(zone) },
    };

    IEnumerable<Action> BuildSurface(Zone zone, SurfaceData data)
    {
        Node3D root = zone.Root;
        yield return () =>
        {
            var sky = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color("1d3557"),
                SkyHorizonColor = new Color("f4a261"),
                GroundHorizonColor = new Color("e76f51"),
                GroundBottomColor = new Color("6d2e1f"),
                SunAngleMax = 30f,
            };
            var environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.7f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                FogEnabled = true,
                FogLightColor = new Color("f4a261"),
                FogDensity = 0.00018f,
            };
            _skies[zone.ZoneId] = sky;
            _environments[zone.ZoneId] = environment;
            root.AddChild(new WorldEnvironment { Environment = environment });
            var sun = new DirectionalLight3D { LightColor = new Color("ffd6a0"), LightEnergy = 1.3f, ShadowEnabled = true, DirectionalShadowMaxDistance = 250f };
            root.AddChild(sun);
            sun.RotationDegrees = new Vector3(-35f, 40f, 0f);

            // A moon hanging in the sky, as in walkable-ship.
            root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 420f, Height = 840f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("7fb7be"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableFog = true },
                Position = new Vector3(2900f, 1500f, -3900f),
            });
        };
        yield return () =>
        {
            var ground = new StaticBody3D { Name = "Ground", CollisionLayer = Layers.World, CollisionMask = 0 };
            ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8000f, 1f, 8000f) }, Position = new Vector3(0, -0.5f, 0) });
            ground.AddChild(new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(8000f, 8000f) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color("d08850"),
                    AlbedoTexture = new NoiseTexture2D { Noise = new FastNoiseLite { Frequency = 0.02f }, Seamless = true, Width = 512, Height = 512 },
                    Uv1Triplanar = true,
                    Uv1Scale = Vector3.One * 0.06f,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
                    Roughness = 1f,
                },
            });
            root.AddChild(ground);
        };

        Color[] rock = { new("a0522d"), new("b5651d"), new("8b3a1e"), new("d2915a") };
        var rockMaterials = rock.Select(c => new StandardMaterial3D { AlbedoColor = c, Roughness = 1f }).ToArray();
        for (int start = 0; start < data.Mesas.Count; start += 12)
        {
            int from = start;
            yield return () =>
            {
                foreach (var (position, size, yaw, color) in data.Mesas.Skip(from).Take(12))
                {
                    var mesa = new StaticBody3D { CollisionLayer = Layers.World, CollisionMask = 0, Position = position, RotationDegrees = new Vector3(0, yaw, 0) };
                    mesa.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
                    mesa.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = rockMaterials[color] });
                    root.AddChild(mesa);
                }
            };
        }
        yield return () =>
        {
            var multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.5f, 0.6f) }, InstanceCount = data.Rocks.Length };
            for (int i = 0; i < data.Rocks.Length; i++)
            {
                float s = 0.4f + (i % 7) * 0.25f;
                multimesh.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, i * 0.7f).Scaled(Vector3.One * s), data.Rocks[i] + Vector3.Up * 0.12f * s));
            }
            root.AddChild(new MultiMeshInstance3D { Multimesh = multimesh, MaterialOverride = rockMaterials[2] });
        };
    }

    IEnumerable<Action> BuildOrbit(Zone zone, OrbitData data)
    {
        Node3D root = zone.Root;
        yield return () =>
        {
            Image stars = Image.CreateFromData(data.StarsWidth, data.StarsHeight, false, Image.Format.Rgba8, data.Stars);
            var environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = ImageTexture.CreateFromImage(stars) } },
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("3a4a6b"),
                AmbientLightEnergy = 0.5f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            };
            _environments[zone.ZoneId] = environment;
            root.AddChild(new WorldEnvironment { Environment = environment });
            var sun = new DirectionalLight3D { LightColor = new Color("fff0d8"), LightEnergy = 1.6f, ShadowEnabled = true, DirectionalShadowMaxDistance = 300f };
            root.AddChild(sun);
            sun.RotationDegrees = new Vector3(-28f, 130f, 0f);
        };
        yield return () =>
        {
            Image skin = Image.CreateFromData(data.SkinWidth, data.SkinHeight, false, Image.Format.Rgb8, data.PlanetSkin);
            skin.GenerateMipmaps();
            root.AddChild(new MeshInstance3D
            {
                Name = "Planet",
                Mesh = new SphereMesh { Radius = PlanetRadius, Height = PlanetRadius * 2f, RadialSegments = 160, Rings = 80 },
                MaterialOverride = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(skin), Roughness = 1f },
                Position = PlanetCenter,
                RotationDegrees = new Vector3(0, 0, 90), // bands run across the view from orbit
            });
            root.AddChild(new MeshInstance3D
            {
                Name = "Atmosphere",
                Mesh = new SphereMesh { Radius = PlanetRadius * 1.025f, Height = PlanetRadius * 2.05f, RadialSegments = 96, Rings = 48 },
                MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = AtmosphereShader } },
                Position = PlanetCenter,
            });
        };
        yield return () =>
        {
            // A ringed giant and a teal moon far off: the paperback-cover sky.
            var giant = new Node3D { Name = "Giant", Position = new Vector3(-9000f, 2600f, -14000f), RotationDegrees = new Vector3(18f, 0f, -24f) };
            giant.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 2600f, Height = 5200f, RadialSegments = 64, Rings = 32 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("c1121f"), Roughness = 1f },
            });
            giant.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 5200f, BottomRadius = 5200f, Height = 4f, RadialSegments = 96 },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.75f, 0.45f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
            });
            root.AddChild(giant);
            root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 700f, Height = 1400f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("7fb7be"), Roughness = 1f },
                Position = new Vector3(7000f, 1800f, -9000f),
            });
        };
    }

    void BuildEmpty(Zone zone)
    {
        zone.Root.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("0b0d17") } });
        zone.Root.AddChild(new DirectionalLight3D());
    }

    const string AtmosphereShader = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back;
void fragment() {
    float rim = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    ALBEDO = mix(vec3(0.95, 0.55, 0.25), vec3(0.35, 0.75, 0.85), rim);
    ALPHA = pow(rim, 2.5) * 0.9;
}";

    /// <summary>The surface sky darkens towards space as you climb.</summary>
    public void SetAltitude(string zoneId, float altitude)
    {
        if (!_skies.TryGetValue(zoneId, out ProceduralSkyMaterial sky)) return;
        float t = Mathf.Clamp(altitude / SurfaceEdgeAltitude, 0f, 1f);
        sky.SkyTopColor = new Color("1d3557").Lerp(new Color("05070f"), t);
        sky.SkyHorizonColor = new Color("f4a261").Lerp(new Color("3a2a4a"), t * 0.8f);
        _environments[zoneId].FogDensity = 0.00018f * (1f - t);
    }

    // ---- Records into nodes -----------------------------------------------------------------

    public void Spawn(Zone zone, SaveRecord record)
    {
        if (record.Type == RecordTypes.Ship)
        {
            Ship ship = SpawnShip(zone, record);
            foreach (SaveRecord aboard in Table.InZone(ZoneIds.Cabin(record.Id)).OrderBy(r => r.Id, StringComparer.Ordinal).ToList())
                SpawnIn(zone, aboard, Frame.InCabin(ship));
            return;
        }
        SpawnIn(zone, record, Frame.InZone(zone.Root));
    }

    /// <summary>Spawns a record into a frame (the zone, or a ship's cabin) and registers it with the zone.</summary>
    public void SpawnIn(Zone zone, SaveRecord record, Frame frame)
    {
        if (record.Type == RecordTypes.Player && LocalPlayer != null && record.Id == LocalPlayer.RecordId)
        {
            var data = GameRecords.Read<PlayerData>(record);
            var at = new Vector3((float)data.Position[0], (float)data.Position[1], (float)data.Position[2]);
            LocalPlayer.MainWorld = zone.Root;
            if (frame.Ship != null) LocalPlayer.SpawnAboard(frame.Ship, at, (float)data.Yaw);
            else LocalPlayer.SpawnOutside(at, (float)data.Yaw);
            zone.Register(record.Id, LocalPlayer);
            return;
        }
        zone.Register(record.Id, RecordViews.Spawn(record, frame));
    }

    Ship SpawnShip(Zone zone, SaveRecord record)
    {
        var data = GameRecords.Read<ShipData>(record);
        var ship = new Ship { RecordId = record.Id, Name = record.Id, InSpace = zone.ZoneId != ZoneIds.Surface };
        zone.Root.AddChild(ship);
        ship.Transform = new Transform3D(
            new Basis(new Quaternion((float)data.Rotation[0], (float)data.Rotation[1], (float)data.Rotation[2], (float)data.Rotation[3]).Normalized()),
            new Vector3((float)data.Position[0], (float)data.Position[1], (float)data.Position[2]));
        ship.ResetPhysicsInterpolation();
        zone.Register(record.Id, ship);
        return ship;
    }

    /// <summary>Re-makes a record's view after it changed: views are thrown away, never patched.</summary>
    public void Respawn(Zone zone, string recordId)
    {
        if (zone.Spawned.TryGetValue(recordId, out Node node) && node is RecordView old)
        {
            old.Despawn();
            zone.Unregister(recordId);
        }
        if (!Table.TryGet(recordId, out SaveRecord record)) return;
        if (record.ShipId != null)
        {
            if (zone.Spawned.GetValueOrDefault(record.ShipId) is Ship ship) SpawnIn(zone, record, Frame.InCabin(ship));
        }
        else if (record.ZoneId == zone.ZoneId) SpawnIn(zone, record, Frame.InZone(zone.Root));
    }

    /// <summary>Removes a record's view (it was picked up, or carried off).</summary>
    public static void Despawn(Zone zone, string recordId)
    {
        if (zone.Spawned.TryGetValue(recordId, out Node node) && node is RecordView view) view.Despawn();
        zone.Unregister(recordId);
    }

    /// <summary>Writes where ships and the local player have moved back into their records, rounded to the millimetre.</summary>
    public void Capture(Zone zone)
    {
        foreach (var (id, node) in zone.Spawned.ToList())
        {
            if (!Table.TryGet(id, out SaveRecord record) || !GodotObject.IsInstanceValid(node)) continue;
            if (node is Ship ship && record.Type == RecordTypes.Ship)
            {
                double[] position = Round(ship.Position);
                Quaternion q = ship.Basis.GetRotationQuaternion();
                double[] rotation = { Math.Round(q.X, 4), Math.Round(q.Y, 4), Math.Round(q.Z, 4), Math.Round(q.W, 4) };
                var data = GameRecords.Read<ShipData>(record);
                if (!data.Position.SequenceEqual(position) || !data.Rotation.SequenceEqual(rotation))
                    WorldOps.Place(Table, id, record.ZoneId, null, position, rotation: rotation);
            }
            else if (node is Player player && record.Type == RecordTypes.Player)
            {
                bool aboard = player.CurrentMode != Player.Mode.Outside;
                string zoneId = aboard ? ZoneIds.Cabin(player.Ship.RecordId) : zone.ZoneId;
                string shipId = aboard ? player.Ship.RecordId : null;
                Vector3 at = player.CurrentMode == Player.Mode.Piloting ? ShipLayout.SeatExitLocal : player.GlobalPosition;
                double[] position = Round(at);
                double yaw = Math.Round(player.Yaw, 3);
                var data = GameRecords.Read<PlayerData>(record);
                if (record.ZoneId != zoneId || !data.Position.SequenceEqual(position) || data.Yaw != yaw)
                    WorldOps.Place(Table, id, zoneId, shipId, position, yaw: yaw);
            }
        }
    }

    public static double[] Round(Vector3 v) => new[] { Math.Round(v.X, 3), Math.Round(v.Y, 3), Math.Round(v.Z, 3) };
}
