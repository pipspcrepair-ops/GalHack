using System;

namespace SurvivalVitals.Model;

/// <summary>The result of an action the player asked for, with a message worth showing them.</summary>
public readonly record struct Outcome(bool Ok, string Message)
{
    public static Outcome Done(string message) => new(true, message);
    public static Outcome No(string message) => new(false, message);
}

/// <summary>Where a replicator's energy comes from.</summary>
public interface IEnergySource
{
    double Available { get; }
    void Draw(double amount);
}

/// <summary>A survival replicator's own fuel tank.</summary>
public sealed class FuelTank : IEnergySource
{
    public double Fuel;
    public double Available => Fuel;
    public void Draw(double amount) => Fuel = Math.Max(0, Fuel - amount);
}

/// <summary>A base's or vessel's power, shared by everything plugged into it.</summary>
public sealed class PowerGrid : IEnergySource
{
    public double Stored;
    public double Available => Stored;
    public void Draw(double amount) => Stored = Math.Max(0, Stored - amount);
}

/// <summary>Organics and energy one feed or craft will take.</summary>
public readonly record struct FeedQuote(double Organics, double Energy);

/// <summary>
/// Turns organics plus energy into food. Every tier can feed the player straight to full;
/// the tier sets how much nourishment each kilogram of organics gives, and which food items
/// it can make for carrying.
/// </summary>
public sealed class Replicator
{
    public int Id;
    public ReplicatorTier Tier;
    public Pos Position;
    public IEnergySource Energy;
    /// <summary>Base storage it draws from before the player's own organics; none for the survival replicator.</summary>
    public Inventory Storage;

    /// <summary>The cost of this much nourishment: in proportion to it, cheaper at higher tiers.</summary>
    public FeedQuote Quote(double nourishment) =>
        new(nourishment / Tier.NourishmentPerOrganic, nourishment * Tier.EnergyPerNourishment);

    public double OrganicsAvailable(Inventory player) => player.Organics + (Storage?.Organics ?? 0);

    /// <summary>
    /// Feeds to full on demand. If organics or energy run short it feeds as far as they go,
    /// rather than refusing: half a meal now beats a trip back.
    /// </summary>
    public Outcome FeedToFull(Vitals vitals, Inventory player, double foodMax)
    {
        double hunger = foodMax - vitals.Food;
        if (hunger < 0.5) return Outcome.No("Already full.");
        FeedQuote cost = Quote(hunger);
        double share = Math.Min(1, Math.Min(OrganicsAvailable(player) / cost.Organics, Energy.Available / cost.Energy));
        if (share <= 0)
            return Outcome.No(OrganicsAvailable(player) <= 0
                ? $"{Tier.Name}: no organics. Pick fruit, or carry organics from storage."
                : $"{Tier.Name}: out of energy.");

        TakeOrganics(cost.Organics * share, player);
        Energy.Draw(cost.Energy * share);
        vitals.Food = Math.Min(foodMax, vitals.Food + hunger * share);
        string used = $"{cost.Organics * share:F1} kg organics, {cost.Energy * share:F0} energy";
        return share >= 1
            ? Outcome.Done($"Fed to full ({used}).")
            : Outcome.Done($"Fed to {vitals.Food / foodMax:P0}: ran short ({used}).");
    }

    public bool CanMake(FoodKind food) => food.Tier >= 1 && food.Tier <= Tier.Tier;

    /// <summary>Makes one food item to carry. It ages from now.</summary>
    public Outcome Craft(FoodKind food, Inventory player, double now)
    {
        if (!CanMake(food))
            return Outcome.No($"{food.Name} needs a tier {food.Tier} replicator; this {Tier.Name.ToLowerInvariant()} is tier {Tier.Tier}.");
        FeedQuote cost = Quote(food.Nourishment);
        if (OrganicsAvailable(player) < cost.Organics)
            return Outcome.No($"{food.Name} needs {cost.Organics:F1} kg organics; {OrganicsAvailable(player):F1} kg to hand.");
        if (Energy.Available < cost.Energy)
            return Outcome.No($"{Tier.Name}: not enough energy for {food.Name.ToLowerInvariant()}.");
        TakeOrganics(cost.Organics, player);
        Energy.Draw(cost.Energy);
        player.Food.Add(new FoodItem { Kind = food.Id, MadeAt = now });
        return Outcome.Done($"Made {food.Name.ToLowerInvariant()} ({cost.Organics:F1} kg organics, {cost.Energy:F0} energy).");
    }

    void TakeOrganics(double amount, Inventory player)
    {
        if (Storage != null)
        {
            double fromStorage = Math.Min(amount, Storage.Organics);
            Storage.Organics -= fromStorage;
            amount -= fromStorage;
        }
        player.Organics = Math.Max(0, player.Organics - amount);
    }
}

/// <summary>Where a replicator may be set down.</summary>
public static class Deployment
{
    public static Outcome Check(ReplicatorTier tier, Pos at, Planet planet)
    {
        if (!tier.Portable) return Outcome.No($"A {tier.Name.ToLowerInvariant()} is built into a base or vessel.");
        if (planet.TerritoryAt(at) is Territory land)
            return Outcome.No($"Can't set down a {tier.Name.ToLowerInvariant()} inside {land.Name}. Step outside the red ring.");
        return Outcome.Done($"{tier.Name} set down.");
    }
}
