using System;
using System.Collections.Generic;
using Godot;

namespace AssetViewer.Providers;

/// <summary>
/// The viewer's own reference pieces: a person for scale, a metre cube, and two test subjects that
/// exercise animations and the randomiser. They are also the template for writing a provider:
/// list cheap <see cref="AssetEntry"/> records, and build the real thing only inside Build.
/// </summary>
public sealed partial class ReferenceProvider : IAssetProvider
{
    public string Category => "Reference";

    public IEnumerable<AssetEntry> Entries()
    {
        yield return new AssetEntry { Id = "person-scale", Group = "Scale", Name = "Person, 1.8 m", Build = _ => new ScalePerson() };
        yield return new AssetEntry { Id = "metre-cube", Group = "Scale", Name = "Metre cube", Build = _ => new Cube(1f, new Color("c9a24b")) };
        yield return new AssetEntry { Id = "animated-cube", Group = "Tests", Name = "Animated cube", Build = _ => new Cube(1.2f, new Color("4b8bc9"), true) };
        yield return new AssetEntry
        {
            Id = "crystal-cluster", Group = "Tests", Name = "Crystal cluster", Randomisable = true,
            Options = new[] { new AssetOption("Tint", "Blue", "Green", "Violet", "Amber") { Rollable = true }, new AssetOption("Size", "Small", "Large") },
            Build = r => new Crystals(r),
        };
    }

    sealed partial class ScalePerson : AssetInstance
    {
        public ScalePerson()
        {
            var body = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.22f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) };
            body.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("8da3b8"), Roughness = 0.8f };
            AddChild(body);
        }

        public override IEnumerable<(string Label, string Value)> Facts() { yield return ("Height", "1.8 m"); }
    }

    sealed partial class Cube : AssetInstance
    {
        readonly MeshInstance3D _mesh;
        readonly float _size;
        string _animation = "";
        float _time, _speed = 1f;
        readonly bool _animated;

        public Cube(float size, Color colour, bool animated = false)
        {
            _animated = animated;
            _size = size;
            _mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * size }, Position = new Vector3(0, size * 0.5f, 0) };
            _mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.5f, Metallic = 0.2f };
            AddChild(_mesh);
        }

        public override IReadOnlyList<string> Animations => _animated ? new[] { "Bob", "Pulse", "Tumble" } : Array.Empty<string>();
        public override void PlayAnimation(string name) { base.PlayAnimation(name); _animation = name; _time = 0f; }
        public override void SetSpeed(float scale) => _speed = scale;

        public override void _Process(double delta)
        {
            if (!_animated) return;
            _time += (float)delta * _speed;
            Vector3 p = new Vector3(0, _size * 0.5f, 0);
            _mesh.Scale = Vector3.One;
            _mesh.Rotation = Vector3.Zero;
            switch (_animation)
            {
                case "Bob": p.Y += 0.4f + Mathf.Sin(_time * 3f) * 0.35f; break;
                case "Pulse": _mesh.Scale = Vector3.One * (1f + 0.25f * Mathf.Sin(_time * 4f)); break;
                case "Tumble": _mesh.Rotation = new Vector3(_time * 1.3f, _time * 0.9f, 0f); p.Y += 0.5f; break;
            }
            _mesh.Position = p;
        }
    }

    sealed partial class Crystals : AssetInstance
    {
        readonly string _tint;
        readonly int _count;

        public Crystals(BuildRequest r)
        {
            var rng = new RandomNumberGenerator { Seed = r.Seed };
            _tint = r.Get("Tint", "Blue");
            Color c = _tint switch { "Green" => new Color("48c97a"), "Violet" => new Color("9a62d6"), "Amber" => new Color("e0a63a"), _ => new Color("4aa8e0") };
            float scale = r.Get("Size", "Small") == "Large" ? 2.4f : 1f;
            _count = rng.RandiRange(5, 12);
            for (int i = 0; i < _count; i++)
            {
                float h = rng.RandfRange(0.4f, 1.6f) * scale, w = rng.RandfRange(0.08f, 0.22f) * scale;
                var m = new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(w, h, w) } };
                m.MaterialOverride = new StandardMaterial3D { AlbedoColor = c.Lerp(Colors.White, rng.Randf() * 0.4f), Roughness = 0.15f, Metallic = 0.3f, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.3f };
                float a = rng.Randf() * Mathf.Tau, d = rng.RandfRange(0f, 0.5f) * scale;
                m.Position = new Vector3(Mathf.Cos(a) * d, h * 0.5f, Mathf.Sin(a) * d);
                m.Rotation = new Vector3(rng.RandfRange(-0.35f, 0.35f), rng.Randf() * Mathf.Tau, rng.RandfRange(-0.35f, 0.35f));
                AddChild(m);
            }
        }

        public override IEnumerable<(string Label, string Value)> Facts()
        {
            yield return ("Crystals", _count.ToString());
            yield return ("Tint", _tint);
        }
    }
}
