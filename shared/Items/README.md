# shared/Items: materials and manufacturing

GalHack's whole item catalogue as data, the recipe tree from ore to every block, and the machines that work it: constructor tiers with self-planning queues and an ammo restock lane, the deconstructor, the blueprint factory, and block placement in survival and creative. Plus seeded ore deposit shapes for the terrain. Plain C#, no engine types; `prototypes/manufacturing` tests it and shows it working.

Namespace `GalHack.Items` (and `.Production`, `.Deposits`, `.Bridge`).

## What's in it

| Folder | What | Needs |
| --- | --- | --- |
| `data/` | `items.json`, `recipes.json`, `blocks.json`, `machines.json`, `deposits.json`, plus `building-parts.json` and `building-materials.json` (blocks building doesn't have yet) | |
| `Core/` | Items, recipes and bills; the catalogue loader and its design-rule validator; the recipe graph (levels, cycles, ore cost, expansion); shared storage; spill crates; the game mode | nothing |
| `Production/` | `Constructor` (orders, auto sub-parts, ammo restock), `AmmoRestock`, `Deconstructor`, `BlueprintFactory` | `Core` |
| `Deposits/` | Ore body shapes: pockets, veins, beds, deep clusters ([ORE-NOTES.md](ORE-NOTES.md)) | `Core` |
| `Bridge/` | The adapter to building's structure model: loading its catalogue with extra blocks, pricing blocks, `BlockPlacer` (survival and creative placement), storage capacity from containers, the guns' ammo | `Core`, building's `src/Structures` |

Compile it in where it lives, never copy it:

```xml
<Compile Include="..\..\shared\Items\Core\**\*.cs" />
<Compile Include="..\..\shared\Items\Production\**\*.cs" />
<Compile Include="..\..\shared\Items\Deposits\**\*.cs" />
<!-- Only where building's structure core is compiled in too: -->
<Compile Include="..\..\shared\Items\Bridge\**\*.cs" />
```

## The catalogue

81 items on the design's five-step rarity ladder, each with mass and volume (storage units, a litre):

| Rarity | Colour | Items | |
| --- | --- | --- | --- |
| Common | grey `#9aa3ad` | 37 | 8 raw, 8 refined, 8 parts, 2 components, 7 consumables, 3 ammo, 1 tool |
| Uncommon | green `#3fcf5e` | 17 | 5 raw, 5 refined, 4 parts, 1 component, 2 ammo |
| Rare | blue `#3d8bff` | 11 | 3 raw, 3 refined, 3 components, 1 consumable, 1 ammo |
| Exotic | purple `#a64dff` | 9 | 3 raw, 3 refined, 3 components |
| Unique | gold `#ffbf2e` | 7 | 4 unique finds, 3 components made from them |

- **Raw** (mined, dug or harvested): the design's ladder. Common: iron, copper, silicon and carbon ore, ice, stone (dug from any rock), biomass, burnstone. Uncommon: cobalt, magnesium, neodymium, gold, gravium. Rare: raw fold crystal, emberite, starsilver. Exotic: voidglass, stormcore, rimefire.
- **Unique finds** can't be mined or crafted: precursor alloy, echo shards, lattice cores, lattice arrays. They come only from POIs, wrecks and derelicts; whatever needs one (lattice processors for CPU tier 3, charge plating, precursor armour) is marked as needing a find.
- **Refined** materials are made from raw ore only (bars, wafers, graphite, glass, polymer, ceramic, concrete mix, gravium ingots, fold crystal...). There is no smelter: a constructor refines ore as one more step of the chain (design section 10).
- **Parts** (hull plates, wire, windings, circuit boards, servos, nozzles, barrels, magnets, machine frames, logic modules, propellant, charges) and **components** (control and power units, thrust assemblies, field emitters, warp coils, fusion cores, lenses...) take refined materials and parts, never raw ore.
- **Consumables**, **ammo** (counted in boxes: a 12 mm belt is 200 rounds, a box of 40 mm shells 25) and one **tool**, the survival constructor.

Names: plain words and real metals for common goods, the design's crystal and star-metal names for rare ones, lost-technology names for finds. All GalHack's own; a test checks no item borrows another game's names.

58 recipes, one per item, each with a time and the lowest constructor tier that knows it. The deepest chain is five steps (copper ore, bar, wire, circuit board, logic module, field emitter).

## Rules the tests hold (`ItemCatalogue.Validate` and the test scenes)

- Every block in building's catalogue (and the sandbox's vessel parts, and the machines and guns here) has a bill on both grids, and every bill has a recipe path down to gathered ore, or is marked unique-only (precursor armour).
- No cycles in the recipe tree; loading refuses one and names it.
- An item's rarity is never lower than any of its inputs'.
- Breaking anything down never makes material; build-then-deconstruct returns exactly the stated rate.
- The constructor ladder builds itself: each machine's bill uses only recipes a lower tier knows, and the foothold base (core, generator, fuel tank, small constructor, container, floor) needs only what the carried survival constructor makes.
- Everything a first small vessel needs comes from what the planet generator puts on every planet (iron, copper, silicon, carbon, water ice) plus stone dug from any rock.

## Blocks are paid at placement

Blocks are never items. In **survival**, placing a block takes its bill (`blocks.json`, by building's block id) from the structure's single shared storage, and taking it down with the multi-tool gives the whole bill back, nothing lost. If storage is full, what doesn't fit goes into a **spill crate** that never despawns: it stays where it dropped until a player empties it, and only an empty crate is removed. Taking a container away shrinks storage, and what no longer fits spills the same way. Every shape of a material costs the same as its cube (design section 11). Upgrading armour in place refunds the old bill and takes the new one as one step.

In **creative**, placing is free and unlimited, nothing is consumed or refunded, and every block can be placed, priced or not. The mode is one per-world setting (`WorldRules`) that placement, the constructors, the deconstructor and the blueprint factory all read; switching it mid-session replans the queues and loses nothing. In creative the machines still run for testing, with no material checks, and the blueprint factory spawns a blueprint at once and free.

## Machines

All of them take inputs from, and deliver to, the structure's one shared storage. A product with no room waits in the machine's output tray until there is.

| Machine | Tier | Speed | Built with | Notes |
| --- | --- | --- | --- | --- |
| Survival constructor | 0 | 0.5x | (carried) | Basics: refining, hull plates, wire, windings, oxygen, medkits, energy cells, fuel packs, pistol rounds |
| Portable constructor | 1 | 0.75x | survival | The small set, mounted on a vessel |
| Small constructor | 1 | 1x | survival | Circuit boards, servos, nozzles, gun barrels: everything for a first small vessel |
| Large constructor | 2 | 2x | small | Gravium, thrust assemblies, field emitters, artillery shells |
| Advanced constructor | 3 | 3x | large | Exotics, warp coils, fusion cores, lattice parts |
| Deconstructor | | 1.5x | small | 50% return |
| Blueprint factory | | 4x | small | Pays in refined materials |

**Queues.** The player's orders sit in one queue; the constructor plans everything an order lacks in one pass over the tree (consumers before what they consume, so every part's need is summed before it's rounded up to whole runs) and adds those sub-parts to a second queue, one line per part, inputs before what they make, each saying which order it feeds. Raw ore nobody here can make, or parts a higher tier makes, are reported as missing with exact counts; the queue resumes by itself when they arrive. A ledger of what queued jobs will take and make means two orders never count on the same unit. Cancelling stops a run in progress and puts its inputs back; if something the plan counted on leaves storage (placed as a block, say), it replans. Over the CPU budget, constructors slow down (`Efficiency`).

**Ammo restock** (on any structure with a constructor and guns). One toggle and a threshold per ammo type, counted in boxes: the unit storage and the constructor already count, so the setting, the stock and the screen always agree. When an ammo the structure's guns fire falls below its threshold, the constructor refills it to the threshold, exactly: one run makes one box, so it never overshoots, and there is no separate batch size or upper target. Restock jobs sit in a **third, lowest-priority lane**: they only run when no job the player queued can, and they never move or delay one. They only use materials no player job has reserved. If free materials run short it makes as many whole boxes as it can and pauses with what it needs for the rest ("Ammo restock paused: need 12 iron ore"), resuming when materials arrive. The guns on a structure decide which ammo is in use (`blocks.json` `ammo`: turret 40 mm shells, gatling 12 mm belts, rocket pod rockets).

**Deconstructor.** Breaks items, and blocks salvaged from wrecks, one step back down the tree: an item into what it was made of, a wreck block into its bill's parts (whole components, not ore). **The return rate is 50%**, chosen from the design's salvage range: the survival tool and multi-tool T1 salvage about 33%, the multi-tool T2 about 65% and vessel salvagers up to 80%; the deconstructor is a fixed station you bring things to, so it sits between the field tools. Fractions carry over inside the machine (breaking one winding gives two wires now and half an iron bar towards the next), so over time exactly 50% comes back and nothing is rounded away or made from nothing. Raw ore and unique finds can't be broken down.

**Blueprint factory.** Builds a whole saved blueprint (design section 11): it shows the bill of materials in refined materials (never ore), what storage lacks and how long it takes; refuses with the missing list; otherwise takes the whole bill at once, makes the parts itself, and hands rounding leftovers (two spare wires) back to storage. Its time is the parts' recipe seconds plus fitting each block (0.4 s small, 1.6 s large), at 4x.

## The starter small vessel

`prototypes/manufacturing/data/blueprints/gadfly.gbp`: a one-seat shuttle of 249 small blocks (4.4 t, thrust to weight 3.5 g) with a glazed cabin, generator, fuel tank, gyro, container, 11 thrusters and three legs.

| | Ore | Crafting |
| --- | --- | --- |
| Gadfly | 717 (338 iron, 250 carbon, 69 silicon, 44 copper, 12 stone, 4 ice) | 527 runs, 19 min on a small constructor |
| Foothold base (large core, generator, fuel tank, small constructor, container, 12 floor plates) | 392 | 21 min on the survival constructor |
| Fuel (20 energy cells) | 40 | |
| **Total from the crash** | **1,149 ore** | gather 26 min at 45 ore/min, craft 41 min, place 5 min |

That is **46 to 72 minutes** for an experienced player (the first if the constructor works while they gather, the second if nothing overlaps), leaving room for the rest of survival inside the design's one to two hours. The pacing reference (45 ore a minute with the survival tool, 12 ore per dug cell, 1.2 s to place a block) is data in `machines.json`. For comparison, the sandbox's walk-in Corsair 9 (556 blocks) is 1,378 ore and 36 minutes of crafting; the blueprint factory builds the Gadfly in 2:09 from 169 iron bars, 121 graphite, 33 glass panes, 22 copper bars, 11 ceramic tiles, 8 polymer and 6 wafers.

## Reference: Empyrion

The owner allowed Empyrion: Galactic Survival as guidance on manufacturing. Ideas only; no recipe tables, names, text or assets were copied, and GalHack's rules win where they differ.

- From the local install's configuration (read, never decompiled): constructors in Empyrion know nested recipe sets (survival, then small and vessel-mounted, then large, then advanced), which is the shape of GalHack's tiers; craft times there run from a second to many minutes, with a median around 12 s. GalHack's are shorter (2 to 30 s at speed 1) because blocks are paid for at placement rather than crafted one by one, which keeps a first vessel inside one to two hours. Empyrion's deconstructor gives everything back; GalHack's design asks for salvage between 33% and 80%, so it returns 50%.
- The wiki's crafting page (https://empyrion.fandom.com/wiki/Crafting; only its search summary was reachable, the page itself refused the request) confirms survival and small constructors make only basic items and the advanced one makes everything.

## Adopting it

**Building** (`prototypes/building`): keep its files; compile in `Core` and `Bridge`, load the catalogue with `BuildingBridge.LoadCatalogue(data, extra components, extra materials)` to get the machines, guns and precursor armour, and route the multi-tool through `BlockPlacer.Place`/`Remove`/`Upgrade` with the structure's `SharedStorage` and a `WorldRules` instead of `Building.Place(..., inventory)`. Its `cost`/`smallCost` fields can then go: `blocks.json` is the price list. Call `BlockPlacer.Sync` after anything that changes containers, so capacity and cargo mass follow.

**The vertical slice:** one `WorldRules` per world, one `SharedStorage` per structure (capacity from `BuildingBridge.StorageCapacity`), and a `Constructor`/`Deconstructor`/`BlueprintFactory` per machine block found with `BuildingBridge.MachinesOn`, ticked at a fixed step. Set `Constructor.Restock.InUse` from `BuildingBridge.AmmoInUse` whenever the structure changes; the guns take their ammo from the same storage. Save each storage's `Contents`, each crate, the queues' orders (sub-parts and restock replan themselves) and the deconstructor's carry. The screens in `prototypes/manufacturing/src/Screens` show how each machine's page reads the model.

**Planet generator and terrain:** see [ORE-NOTES.md](ORE-NOTES.md#wiring-it-into-the-terrain-for-whoever-owns-sharedterrain). The `deposit` field in `items.json` maps every kind the generator places today to an ore; neodymium, gravium and the rare and exotic ores still need kinds or archetype rules there.

## Known limits

- The numbers (recipe times and counts, bills, the 45 ore a minute) are first passes, tuned only against the starter vessel's one-to-two-hour target.
- There is no tech tree yet: any constructor knows every recipe of its tier, and the factory doesn't check unlocks.
- Repairs cost nothing (building's `Repair` is still free); the design wants them to draw from storage.
- Storage is one number per structure; docked craft, Home auto-transmit and trading are not here.
- Wreck salvage at the deconstructor is per block at 50%; the multi-tool's own field salvage rates (33%, 65%, 80%) belong to whoever builds salvage.
- Deposit shapes are generated and tested but not yet wired into `shared/Terrain`.
