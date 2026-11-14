using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GalHack.Arms;
using GalHack.Items;
using Godot;
using Slice.Arms;
using Slice.Items;
using Slice.Player;
using Slice.Ui;

namespace AssetViewer.Providers.Parts;

/// <summary>Builds the viewer's picture of one item (see <see cref="ItemsProvider"/>).</summary>
public static class ItemAssets
{
    static readonly MethodInfo StandInMethod = typeof(ItemIcons).GetMethod("ModelOf", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo PieceMethod = typeof(ItemIcons).GetMethod("PieceModel", BindingFlags.Static | BindingFlags.NonPublic);

    public static AssetInstance Build(ItemCatalogue items, ItemDef item, BuildRequest r)
    {
        var asset = new ItemAsset { Name = item.Id };
        asset.Stage = new StageSpec { Ground = true };
        asset.Setup(items, item);
        string view = r.Get("View", ItemsProvider.ViewModel);

        WeaponDef weapon = item.Category == ItemCategory.Weapon ? GunModel.Rows.OfItem(item.Id) : null;
        if (weapon != null)
        {
            string mode = r.Get("Mode");
            WeaponDef chosen = weapon.Modes.FirstOrDefault(m => (m.Mode.Length > 0 ? m.Mode : m.Id) == mode) ?? weapon;
            if (view != ItemsProvider.ViewIcon && view != ItemsProvider.ViewRecipe) asset.ShowGun(weapon, chosen, view == ItemsProvider.ViewBoth);
        }
        else if (view == ItemsProvider.ViewModel || view == ItemsProvider.ViewBoth)
        {
            Node3D model = ModelOf(asset, items, item, out ModelSource? source);
            if (model != null) asset.Mount(model, plinth: source is null or ModelSource.Held, offset: view == ItemsProvider.ViewBoth ? new Vector3(-1.1f, 0, 0) : null);
        }
        if (view == ItemsProvider.ViewIcon || view == ItemsProvider.ViewBoth) asset.ShowIcon(item, view == ItemsProvider.ViewBoth ? new Vector3(1.1f, 0, 0) : Vector3.Zero);
        if (view == ItemsProvider.ViewRecipe) asset.ShowRecipe(item);
        asset.Finish(items, item, weapon);
        return asset;
    }

    /// <summary>The model the game shows for an item, or null for an item with nothing to show.</summary>
    public static Node3D ModelOf(ItemAsset host, ItemCatalogue items, ItemDef item, out ModelSource? source)
    {
        source = null;
        if (item.IsDecoPiece && PartsData.Deco?.Catalogue.TryGet(item.BlockId, out _) == true && PieceMethod != null)
            return (Node3D)PieceMethod.Invoke(null, new object[] { item.BlockId });
        if (!item.IsBlock && ItemModels.Build(item) is ItemModel own)
        {
            source = own.Source;
            foreach (Node n in Walk(own.Root).Where(n => n is Light3D).ToList()) { n.GetParent().RemoveChild(n); n.Free(); }
            return own.Root;
        }
        if (StandInMethod == null) return null;
        var args = new object[] { item, null };
        var model = (Node3D)StandInMethod.Invoke(host.Icons, args);
        source = (ModelSource?)args[1];
        return model;
    }

    static IEnumerable<Node> Walk(Node n)
    {
        yield return n;
        foreach (Node c in n.GetChildren()) foreach (Node d in Walk(c)) yield return d;
    }
}

/// <summary>An item on show: its model on a plinth, its icon on a quad, and (for guns) the flash, recoil and charge.</summary>
public partial class ItemAsset : ModelAsset
{
    public ItemIcons Icons { get; private set; }
    readonly List<(ItemDef item, MeshInstance3D quad)> _waiting = new();

    // Weapon state.
    GunModel.Built _gun;
    ShotFx _fx;
    WeaponDef _def, _base;
    float _kick, _dip, _charge;
    Vector3 _gunPos;

    public void Setup(ItemCatalogue items, ItemDef item)
    {
        Icons = new ItemIcons { Name = "Icons" };
        Icons.Setup(items, PartsData.Blocks);
        AddChild(Icons);
    }

    // ---- Icons -----------------------------------------------------------------------------

    public void ShowIcon(ItemDef item, Vector3 at, float size = 1.6f)
    {
        var quad = new MeshInstance3D { Name = "Icon", Mesh = new QuadMesh { Size = new Vector2(size, size) }, Position = at + new Vector3(0, size * 0.5f, 0) };
        quad.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY, AlbedoColor = new Color(1, 1, 1, 0.0f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(quad);
        _waiting.Add((item, quad));
        Grow(this);
    }

    public void ShowRecipe(ItemDef item)
    {
        Recipe recipe = item.Recipe;
        if (recipe == null) return;
        var parts = recipe.Inputs.Select(i => i.Item).Concat(new[] { item }).ToList();
        float step = 1.3f, x0 = -(parts.Count - 1) * step * 0.5f;
        for (int i = 0; i < parts.Count; i++)
        {
            ShowIcon(parts[i], new Vector3(x0 + i * step, 0, 0), i == parts.Count - 1 ? 1.2f : 1f);
            if (i == parts.Count - 2)
            {
                var arrow = new MeshInstance3D { Name = "Arrow", Mesh = new PrismMesh { Size = new Vector3(0.3f, 0.3f, 0.05f) }, Position = new Vector3(x0 + (i + 0.5f) * step, 0.5f, 0), RotationDegrees = new Vector3(0, 0, -90) };
                arrow.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("c9a24b"), Roughness = 0.5f };
                AddChild(arrow);
            }
        }
        SetSpins(false);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        for (int i = _waiting.Count - 1; i >= 0; i--)
        {
            var (item, quad) = _waiting[i];
            if (!IsInstanceValid(quad)) { _waiting.RemoveAt(i); continue; }
            Texture2D tex = Icons.Get(item);
            if (tex == null) continue;
            var mat = (StandardMaterial3D)quad.MaterialOverride;
            mat.AlbedoTexture = tex;
            mat.AlbedoColor = Colors.White;
            _waiting.RemoveAt(i);
        }
        if (_gun != null) StepGun((float)delta * Speed);
    }

    // ---- Guns --------------------------------------------------------------------------------

    public void ShowGun(WeaponDef weapon, WeaponDef mode, bool both)
    {
        _base = weapon.Base ?? weapon;
        _def = mode;
        _gun = GunModel.Build(_base);
        Node3D holder = Mount(_gun.Root, plinth: true, offset: both ? new Vector3(-1.1f, 0, 0) : null);
        _gunPos = _gun.Root.Position;
        _fx = new ShotFx { Name = "ShotFx" };
        AddChild(_fx);
        SetSpins(false);
        string feel = mode.Feel;
        bool bolt = mode.Delivery is Delivery.Bolt or Delivery.Pellets or Delivery.Lob;

        void Shoot()
        {
            Vector3 at = _gun.Muzzle.GlobalPosition;
            Vector3 dir = (_gun.Muzzle.GlobalTransform.Basis * Vector3.Forward).Normalized();
            GunFeel f = GunFeel.Get(feel);
            _kick = 1f;
            if (bolt) _fx.Fire(at, at + dir * 14f, GunModel.ColourOf(mode.Tracer.Colour), null, feel, () => _gun.Muzzle.GlobalPosition);
            else _fx.Muzzle(feel, at, dir, GunModel.ColourOf(mode.Tracer.Colour), 1f, () => _gun.Muzzle.GlobalPosition);
            _ = f;
        }

        float clock = 0f;
        int shots = 0;
        AddAnimation("Still", () => { _kick = 0f; _dip = 0f; GunModel.LightRings(_gun, 0); }, null);
        AddAnimation("Fire", () => { clock = 0f; shots = 0; }, dt =>
        {
            int burst = mode.Trigger == Trigger.Auto ? 6 : mode.Trigger == Trigger.Burst ? Math.Max(2, mode.Burst) : 1;
            float gap = Mathf.Max(0.09f, mode.SecondsPerShot);
            clock -= dt;
            if (clock > 0f) return;
            Shoot();
            shots++;
            clock = shots % burst == 0 ? 1.4f + mode.Pump : gap;
        });
        if (mode.Charge != null || _gun.Rings.Count > 0)
        {
            float seconds = mode.Charge?.Seconds ?? 1.5f;
            int stages = Math.Max(1, mode.Charge?.Stages ?? _gun.Rings.Count);
            AddAnimation("Charge and release", () => { _charge = 0f; }, dt =>
            {
                _charge += dt;
                float cycle = seconds * stages + 1.2f;
                float t = _charge % cycle;
                int stage = Math.Min(stages, (int)(t / seconds));
                GunModel.LightRings(_gun, t > seconds * stages ? 0 : stage);
                if (t < seconds * stages) _kick = 0f;
                if (_charge - dt < Math.Floor(_charge / cycle) * cycle + seconds * stages && _charge >= Math.Floor(_charge / cycle) * cycle + seconds * stages) Shoot();
            }, () => GunModel.LightRings(_gun, 0));
        }
        AddAnimation("Reload", () => { clock = 0f; }, dt =>
        {
            clock += dt;
            float cycle = Mathf.Clamp(mode.Reload, 0.8f, 2.5f) + 0.8f;
            float t = clock % cycle;
            _dip = t < cycle - 0.8f ? 1f : 0f;
        }, () => _dip = 0f);
        _ = holder;
    }

    float _dipShown;

    void StepGun(float dt)
    {
        _kick = Mathf.MoveToward(_kick, 0f, dt * 7f);
        _dipShown = Mathf.MoveToward(_dipShown, _dip, dt * 5f);
        float recoil = GunFeel.Get(_def.Feel).Recoil;
        _gun.Root.Position = _gunPos + new Vector3(0f, -0.08f * _dipShown, 0.06f * recoil * _kick);
        _gun.Root.RotationDegrees = new Vector3(-28f * _dipShown + 7f * recoil * _kick, 0f, 12f * _dipShown);
    }

    // ---- Facts -------------------------------------------------------------------------------

    public void Finish(ItemCatalogue items, ItemDef item, WeaponDef weapon) => FactsFn = () => Facts(items, item, weapon);

    static IEnumerable<(string, string)> Facts(ItemCatalogue items, ItemDef i, WeaponDef w)
    {
        yield return ("Category", i.Category + (i.IsBlock ? $" ({i.Family}, {(i.SmallGrid ? "small" : "large")} grid)" : ""));
        yield return ("Rarity", Rarities.Name(i.Rarity));
        yield return ("Mass", i.MassKg >= 1 ? $"{i.MassKg:0.##} kg" : $"{i.MassKg * 1000:0.#} g");
        yield return ("Volume", $"{i.VolumeSu:0.###} SU");
        yield return ("Stack", i.StackSize.ToString());
        if (i.Source != ItemSource.Crafted) yield return ("Source", i.Source.ToString().ToLowerInvariant());
        if (i.Recipe != null)
        {
            yield return ("Recipe", string.Join(", ", i.Recipe.Inputs.Select(x => $"{x.Count} {x.Item.Name}")) + (i.Recipe.Count > 1 ? $" for {i.Recipe.Count}" : ""));
            yield return ("Made in", $"{i.Recipe.Seconds:0.#} s, constructor tier {i.Recipe.Tier} ({items.Machines.TierName(i.Recipe.Tier)})");
        }
        if (i.Unlock != null) yield return ("Tech tier", $"level {i.Unlock.Level}, {i.Unlock.Cost} points ({i.Unlock.Tab})");
        if (i.Rounds > 0) yield return ("Rounds", i.Rounds.ToString());
        if (i.FuelUnits > 0) yield return ("Fuel units", i.FuelUnits.ToString());
        if (PartsData.Consumables.TryGetValue(i.Id, out ConsumableEffect e))
            yield return ("Effect", $"food {e.Food:0.##}, oxygen {e.Oxygen:0.##}, health {e.Health:0.#}");
        if (w != null)
        {
            yield return ("Trigger", w.Trigger.ToString());
            yield return ("Damage", $"{w.Damage:0.#} {w.Class}, {w.Rpm:0} rpm");
            yield return ("Magazine", $"{w.Magazine}, reload {w.Reload:0.#} s");
            yield return ("Ammo", items.TryGet(w.Ammo, out ItemDef a) ? a.Name : w.Ammo);
            string parts = string.Join(", ", w.Parts());
            if (parts.Length > 0) yield return ("Parts", parts);
            if (w.Modes.Count > 1) yield return ("Modes", string.Join(", ", w.Modes.Select(m => m.Mode)));
        }
    }
}
