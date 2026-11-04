using System.Collections.Generic;
using System.Linq;

namespace DiegeticUi.Logic;

public sealed class Wallet
{
    public int Credits { get; private set; }
    public int Version { get; private set; }

    public Wallet(int credits) => Credits = credits;

    public void Add(int amount)
    {
        Credits += amount;
        Version++;
    }

    public bool Spend(int amount)
    {
        if (amount > Credits) return false;
        Credits -= amount;
        Version++;
        return true;
    }
}

/// <summary>Something the trader buys. Wanted items pay a premium and are highlighted everywhere.</summary>
public sealed record BuyOffer(ItemDef Item, int Price, bool Wanted);

public sealed class SellOffer
{
    public ItemDef Item { get; init; }
    public int Price { get; init; }
    public int Stock { get; internal set; }
}

/// <summary>
/// A trader's terminal: what they buy (some of it wanted), what they sell, and the player's
/// storage beside it. Sell-all sells exactly the wanted items from one storage in one press,
/// the quick path the design asks for.
/// </summary>
public sealed class Trader
{
    readonly Dictionary<string, BuyOffer> _buys;

    public string Name { get; }
    public IReadOnlyList<BuyOffer> Buys { get; }
    public IReadOnlyList<SellOffer> Sells { get; }
    public int Version { get; private set; }

    public Trader(string name, IEnumerable<BuyOffer> buys, IEnumerable<SellOffer> sells)
    {
        Name = name;
        Buys = buys.ToList();
        Sells = sells.ToList();
        _buys = Buys.ToDictionary(b => b.Item.Id);
    }

    public bool IsWanted(ItemDef item) => _buys.TryGetValue(item.Id, out var offer) && offer.Wanted;

    /// <summary>What the trader pays for one, or 0 if they don't buy it.</summary>
    public int PriceFor(ItemDef item) => _buys.TryGetValue(item.Id, out var offer) ? offer.Price : 0;

    /// <summary>What sell-all on this storage would pay right now, shown on the button before you press it.</summary>
    public int WantedValue(Inventory from) =>
        from.Stacks.Where(s => IsWanted(s.Item)).Sum(s => s.Count * PriceFor(s.Item));

    /// <summary>Sells every wanted item in the storage, and nothing else. Returns the credits earned.</summary>
    public int SellAllWanted(Inventory from, Wallet wallet)
    {
        int earned = 0;
        foreach (Stack stack in from.Stacks.Where(s => IsWanted(s.Item)).ToList())
            earned += from.Remove(stack.Item.Id, stack.Count) * PriceFor(stack.Item);
        if (earned > 0) wallet.Add(earned);
        Version++;
        return earned;
    }

    public bool SellOne(Inventory from, ItemDef item, Wallet wallet)
    {
        int price = PriceFor(item);
        if (price == 0 || from.Remove(item.Id, 1) == 0) return false;
        wallet.Add(price);
        Version++;
        return true;
    }

    public bool BuyOne(SellOffer offer, Inventory to, Wallet wallet)
    {
        if (offer.Stock <= 0 || to.RoomFor(offer.Item) < 1 || !wallet.Spend(offer.Price)) return false;
        to.Add(offer.Item, 1);
        offer.Stock--;
        Version++;
        return true;
    }
}
