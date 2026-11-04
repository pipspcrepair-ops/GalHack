namespace DiegeticUi.Logic;

/// <summary>
/// Every view-model the bridge shows, seeded with a small believable starting kit. The scene
/// and the tests both build one of these; nothing in it knows about the engine.
/// </summary>
public sealed class GameState
{
    public Vitals Vitals { get; } = new();
    public SuitLoadout Loadout { get; } = new();
    public Inventory Suit { get; } = new("SUIT", volumeCapL: 50, softMassKg: 60);
    public Inventory Hold { get; } = new("SHIP HOLD", volumeCapL: 4000);
    public Wallet Wallet { get; } = new(1240);
    public ShipSystems Ship { get; }
    public StationRoster Roster { get; } = new(new[] { "ORLA", "BRIX", "KESTREL" });
    public WarningTimeline Warnings { get; } = new();
    public ConstructorQueue Constructor { get; }
    public Trader Trader { get; }
    public StarMap Map { get; } = StarMap.Kessler();

    public GameState()
    {
        Ship = new ShipSystems(Hold);
        Constructor = new ConstructorQueue(Hold) { Speed = 1.5 };

        Suit.Add(Catalog.IronOre, 8);
        Suit.Add(Catalog.CopperOre, 4);
        Suit.Add(Catalog.BioGel, 3);
        Suit.Add(Catalog.Ice, 5);
        Suit.Add(Catalog.Ration, 6);
        Suit.Add(Catalog.O2Canister, 2);
        Suit.Add(Catalog.MedKit, 1);
        Suit.Add(Catalog.Polymer, 2);
        Suit.Add(Catalog.Multitool, 1);

        // No cold cover: the wrist shows the gap, and the trader sells the fix.
        Loadout.Equip(Catalog.HazmatShell);
        Loadout.Equip(Catalog.LeadLiner, 0);

        Hold.Add(Catalog.IronOre, 340);
        Hold.Add(Catalog.CopperOre, 120);
        Hold.Add(Catalog.Ice, 200);
        Hold.Add(Catalog.Polymer, 40);
        Hold.Add(Catalog.IronPlate, 3);
        Hold.Add(Catalog.HullPlate, 6);

        Trader = new Trader("KESSLER DEPOT",
            new[]
            {
                new BuyOffer(Catalog.IronOre, 17, Wanted: true),
                new BuyOffer(Catalog.BioGel, 56, Wanted: true),
                new BuyOffer(Catalog.HullPlate, 196, Wanted: true),
                new BuyOffer(Catalog.CopperOre, 18, Wanted: false),
                new BuyOffer(Catalog.Polymer, 22, Wanted: false),
                new BuyOffer(Catalog.Ice, 4, Wanted: false),
            },
            new[]
            {
                new SellOffer { Item = Catalog.ThermalPlate, Price = 480, Stock = 2 },
                new SellOffer { Item = Catalog.O2Canister, Price = 40, Stock = 12 },
                new SellOffer { Item = Catalog.MedKit, Price = 75, Stock = 5 },
                new SellOffer { Item = Catalog.Motor, Price = 150, Stock = 4 },
            });

        Roster.Assign(Station.Gunnery, "BRIX");
        Roster.Assign(Station.Engineering, "KESTREL");
    }
}
