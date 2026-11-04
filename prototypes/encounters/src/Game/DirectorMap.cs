using System.Collections.Generic;
using Encounters.Director;
using Godot;

namespace Encounters;

/// <summary>
/// The M map: the zone from above, drawn from the same zone data the director reads. Faction
/// territories, POIs, no-spawn circles and player bases; your view cone and the spawn ring
/// where events may appear; every ship, wreck and record; and leads as uncertainty circles.
/// North (-Z) is up.
/// </summary>
public partial class DirectorMap : Control
{
    public Main Main;

    /// <summary>Metres from the zone centre to the map's edge.</summary>
    const float ViewRadius = 8000f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
        AnchorLeft = 1;
        AnchorRight = 1;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
    }

    public override void _Process(double delta)
    {
        float size = Mathf.Min(GetViewportRect().Size.Y - 120f, 820f);
        OffsetLeft = -size - 24f;
        OffsetRight = -24f;
        OffsetTop = -size * 0.5f + 10f;
        OffsetBottom = size * 0.5f + 10f;
        if (Visible) QueueRedraw();
    }

    public override void _Draw()
    {
        EncounterDirector d = Main.Director;
        ZoneDef zone = Main.Zone;
        Vector2 size = Size;
        Vector2 centre = size * 0.5f;
        float scale = size.X * 0.5f / ViewRadius;
        Vector2 Map(System.Numerics.Vector3 p) => centre + new Vector2(p.X, p.Z) * scale;

        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.03f, 0.07f, 0.9f));
        for (int km = 2; km * 1000 <= ViewRadius * 1.5f; km += 2)
            DrawArc(centre, km * 1000 * scale, 0, Mathf.Tau, 96, new Color(1, 1, 1, 0.05f), 1f);
        DrawLine(new Vector2(centre.X, 0), new Vector2(centre.X, size.Y), new Color(1, 1, 1, 0.04f));
        DrawLine(new Vector2(0, centre.Y), new Vector2(size.X, centre.Y), new Color(1, 1, 1, 0.04f));

        // Zone circles.
        foreach (ZoneCircle c in zone.Circles)
        {
            Vector2 p = Map(c.Center);
            float r = c.R * scale;
            switch (c.Kind)
            {
                case CircleKind.Territory:
                    Color f = Palette.Faction(c.Faction);
                    DrawCircle(p, r, new Color(f, 0.1f));
                    DrawArc(p, r, 0, Mathf.Tau, 128, new Color(f, 0.65f), 2f);
                    Label(p + new Vector2(-r * 0.5f, -r * 0.62f), $"{c.Name} · {d.Deck.FactionName(c.Faction)}", new Color(f, 0.9f), 14);
                    break;
                case CircleKind.NoSpawn:
                    Hatch(p, r, new Color(1f, 0.35f, 0.3f, 0.35f));
                    DrawArc(p, r, 0, Mathf.Tau, 64, new Color(1f, 0.35f, 0.3f, 0.8f), 1.5f);
                    Label(p + new Vector2(-r * 0.6f, r + 14), $"no spawns: {c.Name}", new Color(1f, 0.5f, 0.45f), 12);
                    break;
                case CircleKind.Base:
                    Hatch(p, r, new Color(Palette.Lasting, 0.3f));
                    DrawArc(p, r, 0, Mathf.Tau, 48, Palette.Lasting, 2f);
                    Label(p + new Vector2(r + 6, 4), $"{c.Name} (player base)", Palette.Lasting, 12);
                    break;
                case CircleKind.Poi:
                    DrawCircle(p, Mathf.Max(r, 4f), new Color(1, 1, 1, 0.25f));
                    DrawArc(p, Mathf.Max(r, 4f), 0, Mathf.Tau, 48, Palette.Text, 2f);
                    Label(p + new Vector2(Mathf.Max(r, 4f) + 6, 4), c.Name, Palette.Text, 13);
                    break;
            }
        }

        // The local player: the view cone where nothing may spawn, and the ring where things can.
        PlayerState me = d.Player(Main.LocalPlayer);
        if (me != null)
        {
            Vector2 p = Map(me.Position);
            var heading = new Vector2(me.Forward.X, me.Forward.Z);
            if (heading.LengthSquared() < 1e-4f) heading = Vector2.Up;
            heading = heading.Normalized();
            float half = Mathf.DegToRad(d.Config.ViewConeHalfAngleDeg), reach = d.Config.MaxSpawnDistance * scale * 1.15f;
            var cone = new List<Vector2> { p };
            for (int i = 0; i <= 24; i++) cone.Add(p + heading.Rotated(-half + 2 * half * i / 24f) * reach);
            DrawColoredPolygon(cone.ToArray(), new Color(1, 1, 1, 0.07f));
            DrawArc(p, d.Config.MinSpawnDistance * scale, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.3f), 1f);
            DrawArc(p, d.Config.MaxSpawnDistance * scale, 0, Mathf.Tau, 64, new Color(1, 1, 1, 0.18f), 1f);
            Label(p + heading.Rotated(half * 0.5f) * reach * 0.75f, "view cone: no spawns", new Color(1, 1, 1, 0.45f), 11);
        }

        // Leads: where they point, and how unsure they are.
        foreach (SignalLead lead in d.LeadsFor(Main.LocalPlayer))
        {
            Color c = Palette.Lead(lead.Kind);
            Vector2 from = Map(lead.Origin), to = Map(lead.Estimate);
            DrawDashedLine(from, to, new Color(c, 0.7f), 1.5f, 8f);
            float r = lead.Distance * Mathf.Sin(Mathf.DegToRad(d.Config.Lead.AngleFuzzDeg)) * scale;
            DrawCircle(to, r, new Color(c, 0.12f));
            DrawArc(to, r, 0, Mathf.Tau, 48, c, 1.5f);
            Label(to + new Vector2(r + 4, -4), lead.Kind == "distress" ? "distress lead" : "transmission lead", c, 12);
        }

        // Ships, wrecks, records.
        ZoneInstance zi = d.Instance(zone.Id);
        if (zi != null)
            foreach (WorldEntity e in zi.Ships)
                Ship(Map(e.Position), e.Forward, Palette.Faction(e.Faction));
        foreach (WorldEntity e in d.Ledger.InZone(zone.Id))
        {
            Vector2 p = Map(e.Position);
            if (e.Wrecked && e.Touch == TouchKind.None)
            {
                Color w = new(Palette.Faction(e.Faction), 0.9f);
                DrawLine(p + new Vector2(-4, -4), p + new Vector2(4, 4), w, 2f);
                DrawLine(p + new Vector2(-4, 4), p + new Vector2(4, -4), w, 2f);
            }
            else DrawRect(new Rect2(p - new Vector2(4, 4), new Vector2(8, 8)), Palette.Lasting, false, 2f);
        }
        if (me != null) Ship(Map(me.Position), me.Forward, Colors.White, 9f);

        // Legend.
        float y = size.Y - 58;
        Label(new Vector2(12, 24), $"DIRECTOR MAP · {zone.Name} · {ViewRadius * 2 / 1000:F0} km across", Palette.Text, 15);
        Label(new Vector2(12, y), "▲ crewed ship (faction colour)   × wreck (salvage record)   □ touched or owned: lasting", new Color(Palette.Text, 0.75f), 12);
        Label(new Vector2(12, y + 18), "rings: min and max spawn distance   white wedge: your view cone   dashed: signal lead", new Color(Palette.Text, 0.75f), 12);
        DrawRect(new Rect2(Vector2.Zero, size), new Color("f4a100"), false, 2f);
    }

    void Ship(Vector2 p, System.Numerics.Vector3 forward, Color color, float size = 6f)
    {
        var dir = new Vector2(forward.X, forward.Z);
        dir = dir.LengthSquared() < 1e-4f ? Vector2.Up : dir.Normalized();
        Vector2 side = new(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { p + dir * size, p - dir * size * 0.6f + side * size * 0.6f, p - dir * size * 0.6f - side * size * 0.6f }, color);
    }

    void Hatch(Vector2 p, float r, Color color)
    {
        for (float x = -r; x <= r; x += 7f)
        {
            float h = Mathf.Sqrt(Mathf.Max(0, r * r - x * x));
            DrawLine(p + new Vector2(x - h * 0.3f, -h), p + new Vector2(x + h * 0.3f, h), color, 1f);
        }
    }

    void Label(Vector2 at, string text, Color color, int size)
    {
        DrawStringOutline(Hud.Font, at, text, HorizontalAlignment.Left, -1, size, 4, new Color(0, 0, 0, 0.85f));
        DrawString(Hud.Font, at, text, HorizontalAlignment.Left, -1, size, color);
    }
}
