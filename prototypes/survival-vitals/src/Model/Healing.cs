namespace SurvivalVitals.Model;

/// <summary>
/// Healing has two tools. Medkits patch players up in the field; a healing booth takes care of
/// every health need in one place. Neither works on an empty stomach.
/// </summary>
public static class Healing
{
    public static Outcome UseMedkit(Vitals vitals, Inventory inventory, VitalsRules rules)
    {
        if (inventory.Medkits <= 0) return Outcome.No("No medkits left.");
        if (vitals.Starving) return Outcome.No("Too hungry to heal: eat something first.");
        if (vitals.Health >= rules.Health.Max) return Outcome.No("Already at full health.");
        inventory.Medkits--;
        double gained = vitals.Heal(rules.Healing.MedkitHealth);
        return Outcome.Done($"Medkit: +{gained:F0} health.");
    }

    /// <summary>Full health, normal body temperature, radiation cleared.</summary>
    public static Outcome UseBooth(Vitals vitals, VitalsRules rules)
    {
        if (vitals.Starving) return Outcome.No("Too hungry to heal: feed at the replicator first (F).");
        vitals.Heal(rules.Health.Max);
        vitals.Normalise();
        return Outcome.Done("Healing booth: health full, body temperature normal, radiation cleared.");
    }
}
