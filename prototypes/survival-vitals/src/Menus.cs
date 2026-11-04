using System;
using System.Linq;
using Godot;
using SurvivalVitals.Model;

namespace SurvivalVitals;

/// <summary>Shared look for the two menus: a dark panel with an orange edge.</summary>
static class MenuStyle
{
    public static StyleBoxFlat Panel() => new()
    {
        BgColor = new Color(0.07f, 0.08f, 0.13f, 0.94f),
        BorderColor = Palette.Orange,
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 18, ContentMarginBottom = 18,
    };

    public static Label Heading(string text, int size = 18, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Palette.Yellow);
        return label;
    }

    public static Label Line(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 15);
        label.AddThemeColorOverride("font_color", color ?? Palette.Cream);
        return label;
    }

    public static Button Button(string text, Action pressed, bool enabled = true, bool highlighted = false)
    {
        var button = new Button { Text = text, Disabled = !enabled, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 15);
        if (highlighted) button.AddThemeColorOverride("font_color", Palette.Yellow);
        button.Pressed += pressed;
        return button;
    }
}

/// <summary>
/// I: wear a suit, fit boosters, eat, plug in a bottle, use a medkit, and pick what drops on
/// death. One screen, one click per thing: managing gear should never become a chore.
/// </summary>
public partial class InventoryPanel : PanelContainer
{
    public Main Main;
    VBoxContainer _rows;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", MenuStyle.Panel());
        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        CustomMinimumSize = new Vector2(820, 0);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 8);
        AddChild(_rows);
        Visible = false;
    }

    public void Refresh()
    {
        foreach (Node child in _rows.GetChildren()) child.QueueFree();
        SurvivalSim sim = Main.Sim;
        Inventory inv = sim.Player.Inventory;
        Loadout gear = sim.Player.Gear;
        ArmorCatalog armor = sim.Data.Armor;

        _rows.AddChild(MenuStyle.Heading("INVENTORY   ·   I or Esc to close", 22, Palette.Orange));

        _rows.AddChild(MenuStyle.Heading("Suit"));
        var suits = new HFlowContainer();
        if (gear.Suit != null)
        {
            SuitKind worn = armor.Suit(gear.Suit.Kind);
            suits.AddChild(MenuStyle.Button($"Wearing: {worn.Name} ({worn.Slots} slots, {gear.Suit.Durability:P0})", () => { }, false, true));
            suits.AddChild(MenuStyle.Button("Take off", () => Main.Act(sim.RemoveSuit())));
        }
        foreach (SuitItem suit in inv.Suits)
        {
            SuitKind kind = armor.Suit(suit.Kind);
            suits.AddChild(MenuStyle.Button($"Wear {kind.Name} ({kind.Slots} slots)", () => Main.Act(sim.EquipSuit(suit.Kind))));
        }
        _rows.AddChild(suits);

        int slots = gear.Slots(armor);
        _rows.AddChild(MenuStyle.Heading($"Boosters  ({gear.Boosters.Count} of {slots} slots)"));
        var boosters = new HFlowContainer();
        foreach (string id in gear.Boosters)
            boosters.AddChild(MenuStyle.Button($"Remove {armor.Booster(id).Name}", () => Main.Act(sim.RemoveBooster(id)), true, true));
        foreach (string id in inv.Boosters.Distinct())
            boosters.AddChild(MenuStyle.Button($"Fit {armor.Booster(id).Name}", () => Main.Act(sim.EquipBooster(id)), gear.Boosters.Count < slots));
        _rows.AddChild(boosters);

        _rows.AddChild(MenuStyle.Heading("Food"));
        var food = new HFlowContainer();
        foreach (FoodKind kind in sim.Data.Food.Foods)
        {
            int count = inv.CountFood(kind.Id);
            if (count == 0) continue;
            FoodItem oldest = inv.Food.Where(f => f.Kind == kind.Id).OrderBy(f => f.MadeAt).First();
            double gain = sim.Data.Food.NourishmentOf(oldest, sim.Time);
            food.AddChild(MenuStyle.Button($"Eat {kind.Name} x{count}  (+{gain:F0})", () => Main.Act(sim.Eat(kind.Id))));
        }
        if (food.GetChildCount() == 0) food.AddChild(MenuStyle.Line("No food carried. Feed at a replicator (F), pick fruit, or loot an escape pod.", Palette.Cream));
        _rows.AddChild(food);

        _rows.AddChild(MenuStyle.Heading("Oxygen and first aid"));
        var kit = new HFlowContainer();
        OxygenRules o = sim.Data.Vitals.Oxygen;
        kit.AddChild(MenuStyle.Button($"Small O2 bottle x{inv.SmallBottles} (+{o.SmallBottle:F0})", () => Main.Act(sim.UseBottle(false)), inv.SmallBottles > 0));
        kit.AddChild(MenuStyle.Button($"Large O2 bottle x{inv.LargeBottles} (+{o.LargeBottle:F0})", () => Main.Act(sim.UseBottle(true)), inv.LargeBottles > 0));
        kit.AddChild(MenuStyle.Button($"Medkit x{inv.Medkits} (+{sim.Data.Vitals.Healing.MedkitHealth:F0} health)", () => Main.Act(sim.UseMedkit()), inv.Medkits > 0));
        _rows.AddChild(kit);

        _rows.AddChild(MenuStyle.Heading("On death, drop  (difficulty)"));
        var drop = new HFlowContainer();
        foreach (var (rule, label) in new[] { (DropRule.Everything, "Everything"), (DropRule.Inventory, "Inventory only"), (DropRule.Nothing, "Nothing") })
            drop.AddChild(MenuStyle.Button((sim.DropRule == rule ? "> " : "") + label, () =>
            {
                sim.DropRule = rule;
                Main.Toast($"On death you now drop: {label.ToLowerInvariant()}. The survival tool always stays with you.");
                Refresh();
            }, true, sim.DropRule == rule));
        _rows.AddChild(drop);

        string kits = inv.Kits.Count == 0 ? "none (B builds one from ore)" : string.Join(", ", inv.Kits.Select(k => $"{k.Fuel:F0} fuel"));
        _rows.AddChild(MenuStyle.Line($"Organics {inv.Organics:F1} kg  ·  ore {inv.Ore}  ·  survival replicator kits: {kits}  ·  survival tool: always kept", new Color(Palette.Cream, 0.8f)));
    }
}

/// <summary>The death screen: what happened, where your backpack is, and four ways back.</summary>
public partial class DeathScreen : Control
{
    public Main Main;
    VBoxContainer _rows;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new Color(0.22f, 0.04f, 0.03f, 0.72f), AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(720, 0) };
        panel.AddThemeStyleboxOverride("panel", MenuStyle.Panel());
        center.AddChild(panel);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 10);
        panel.AddChild(_rows);
        Visible = false;
    }

    public void Open()
    {
        foreach (Node child in _rows.GetChildren()) child.QueueFree();
        SurvivalSim sim = Main.Sim;
        _rows.AddChild(MenuStyle.Heading("SUIT TELEMETRY LOST", 34, Palette.Orange));
        _rows.AddChild(MenuStyle.Line($"Cause: {sim.Player.DeathCause}."));
        _rows.AddChild(MenuStyle.Line(sim.LastBackpack != null
            ? $"Your backpack ({sim.LastBackpack.Contents.ItemCount} items) stays where you fell, under a beam and on your visor, until you pick it up. It never despawns."
            : "Nothing was dropped (drop setting: nothing)."));
        if (sim.Player.Gear.Suit is SuitItem suit)
            _rows.AddChild(MenuStyle.Line($"Your {sim.Data.Armor.Suit(suit.Kind).Name.ToLowerInvariant()} took a knock: {suit.Durability:P0} durability."));
        _rows.AddChild(MenuStyle.Line("You come back at full health, with at least half a stomach of food. Respawn at:", Palette.Yellow));

        int key = 1;
        foreach (RespawnOption option in sim.RespawnOptions())
        {
            RespawnChoice choice = option.Choice;
            _rows.AddChild(MenuStyle.Button($"{key++}   {option.Label}  ·  {option.Detail}", () => Main.RespawnAt(choice), option.Available));
        }
        Visible = true;
    }
}
