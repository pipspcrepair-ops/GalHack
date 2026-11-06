# shared/Items: materials and manufacturing

GalHack's whole item catalogue as data, the recipe tree from ore to every block, blocks as craftable items (as in Empyrion), and the machines that work it: constructor tiers with self-planning queues and an ammo restock lane, the deconstructor, the blueprint factory, and block placement in survival and creative. Plus seeded ore deposit shapes for the terrain. Plain C#, no engine types; `prototypes/manufacturing` tests it and shows it working.

Namespace `GalHack.Items` (and `.Production`, `.Deposits`, `.Bridge`).

## What's in it

| Folder | What | Needs |
| --- | --- | --- |
| `data/` | `items.json`, `recipes.json`, `blocks.json`, `machines.json`, `deposits.json`, plus `building-parts.json` and `building-materials.json` (blocks building doesn't have yet) | |
| `Core/` | Items, recipes and bills; the catalogue loader and its design-rule validator; the recipe graph (levels, cycles, ore cost, expansion); shared storage; spill crates; the game mode | nothing |
| `Production/` | `Constructor` (orders, auto sub-parts, ammo restock), `AmmoRestock`, `Deconstructor`, `BlueprintFactory` | `Core` |
| `Deposits/` | Ore body shapes: pockets, veins, beds, deep clusters ([ORE-NOTES.md](ORE-NOTES.md)) | `Core` |
| `Bridge/` | The adapter to building's structure model: loading its catalogue with extra blocks, naming block items, a structure's class, `BlockPlacer` (placement by block item in survival and creative), storage capacity from containers, the guns' ammo | `Core`, building's `src/Structures` |

Compile it in where it lives, never copy it:

```xml
<Compile Include="..\..\shared\Items\Core\**\*.cs" />
<Compile Include="..\..\shared\Items\Production\**\*.cs" />
<Compile Include="..\..\shared\Items\Deposits\**\*.cs" />
<!-- Only where building's structure core is compiled in too: -->
<Compile Include="..\..\shared\Items\Bridge\**\*.cs" />
```

## The catalogue

82 items on the design's five-step rarity ladder, each with mass and volume (storage units, a litre), plus 63 block items, one per block and grid:

| Rarity | Colour | Items | |
| --- | --- | --- | --- |
| Common | grey `#9aa3ad` | 38 | 8 raw, 8 refined, 8 parts, 2 components, 8 consumables, 3 ammo, 1 tool |
| Uncommon | green `#3fcf5e` | 17 | 5 raw, 5 refined, 4 parts, 1 component, 2 ammo |
| Rare | blue `#3d8bff` | 11 | 3 raw, 3 refined, 3 components, 1 consumable, 1 ammo |
| Exotic | purple `#a64dff` | 9 | 3 raw, 3 refined, 3 components |
| Unique | gold `#ffbf2e` | 7 | 4 unique finds, 3 components made from them |

- **Raw** (mined, dug or harvested): the design's ladder. Common: iron, copper, silicon and carbon ore, ice, stone (dug from any rock), biomass, burnstone. Uncommon: cobalt, magnesium, neodymium, gold, gravium. Rare: raw fold crystal, emberite, starsilver. Exotic: voidglass, stormcore, rimefire.
- **Unique finds** can't be mined or crafted: precursor alloy, echo shards, lattice cores, lattice arrays. They come only from POIs, wrecks and derelicts; whatever needs one (lattice processors for CPU tier 3, charge plating, precursor armour) is marked as needing a find.
- **Refined** materials are made from raw ore only (bars, wafers, graphite, glass, polymer, ceramic, concrete mix, gravium ingots, fold crystal...). There is no smelter: a constructor refines ore as one more step of the chain (design section 10).
- **Parts** (hull plates, wire, windings, circuit boards, servos, nozzles, barrels, magnets, machine frames, logic modules, propellant, charges) and **components** (control and power units, thrust assemblies, field emitters, warp coils, fusion cores, lenses...) take refined materials and parts, never raw ore.
- **Consumables**, **ammo** (counted in boxes: a 12 mm belt is 200 rounds, a box of 40 mm shells 25) and one **tool**, the survival constructor.
- **Block items**, on top of those: one item per block and grid, generated from `blocks.json` (see below), and one per deco piece and grid from `deco.json` when `DecoCatalogue.AddBillsTo` is called.

Names: plain words and real metals for common goods, the design's crystal and star-metal names for rare ones, lost-technology names for finds. All GalHack's own; a test checks no item borrows another game's names.

59 recipes for those, one per item, each with a time and the lowest constructor tier that knows it, and one for each block item. The deepest chain is five steps (copper ore, bar, wire, circuit board, logic module, field emitter).

## Rules the tests hold (`ItemCatalogue.Validate` and the test scenes)

- Every block in building's catalogue (and the sandbox's vessel parts, and the machines and guns here) has a bill on each grid it goes on, and every bill has a recipe path down to gathered ore, or is marked unique-only (precursor armour).
- Every block family on every grid it goes on is an item with a recipe (its bill), a stack size, a mass, a volume, a tech-tree node and the classes of structure it fits.
- No cycles in the recipe tree; loading refuses one and names it.
- An item's rarity is never lower than any of its inputs'.
- Breaking anything down never makes material; build-then-deconstruct returns exactly the stated rate.
- The constructor ladder builds itself: each machine's bill uses only recipes a lower tier knows, and the foothold base's block items (core, generator, fuel tank, small constructor, container, floor) are all made by the carried survival constructor.
- Everything a first small vessel needs comes from what the planet generator puts on every planet (iron, copper, silicon, carbon, water ice) plus stone dug from any rock.

## Blocks are items

Blocks are craftable items, as in Empyrion. `blocks.json` lists every block; for each grid it goes on, the catalogue makes a **block family item** (`steel@large`, `thruster@small`; the item id is the block's tech-tree node id):

| | |
| --- | --- |
| Recipe | One item a run, from the block's bill, in the constructors that can make everything in it (a survival-tier steel block, a large-constructor hardened one). `blocks.json` can give a `tier` of its own |
| Stack, seconds | Per family and grid (`items` at the top of `blocks.json`): armour 500 small, 250 large, 1 and 2 s; devices 25 and 10, 3 and 6 s; deco 20 and 10, 2 and 4 s. An entry can override them |
| Mass, volume | Those of its bill: a block is what it is made of, so crafting one never changes what a bag holds |
| Rarity | Its rarest input's |
| Unlock | The block's own node on that grid. Unlocks gate crafting, as in Empyrion: a block you carry (looted, say) places whether or not its node is open |
| Shapes | An armour family item (`family: armour`) places as any of its shapes, so a slope and a cube cost the same (design section 11). Devices have one shape |
| Classes | Each item says which of the four classes of structure it goes on (`places`, Empyrion's `AllowPlacingAt`). Large grid: base and capital; small grid: small and hover. Narrower where a block says so: concrete and the large deconstructor are base-only, large thrusters, gyros, legs and the large command chair capital-only, small legs small-vessel, hover pads hover-only |

The small and large grids are separate families, as in Empyrion. Cores are the core block's items (`base-core`, `vessel-core`); a core item founds a structure rather than attaching to one, so every structure starts with its core and nothing else can stand alone. Devices (constructors, containers, lights, engines, guns) are block items like any other. There is no cockpit: **every seat is a command chair**, the only block that gives control. It gives no air. Device function depends only on which blocks are on a structure, never on their facing or place, except a thruster, which pushes along its facing; a test checks each device in all 24 orientations.

**Deco** (`DecoCatalogue.AddBillsTo`): every intact piece becomes a block family item on the grids it goes on, its unlock mapped to the shared `Unlock` on the `deco` tab, its theme set as the item's `Group`. Deco bills may use raw harvested goods. Damaged and knocked-over states stay Creative and POI blocks: no bill, no item.

**Placing** (`BlockPlacer`). In **survival**, placing a block uses one of its family item from the source the caller names, and taking it down gives that one item back, whole, damaged or not; whatever doesn't fit goes into a **spill crate** that never despawns: it stays where it dropped until a player empties it, and only an empty crate is removed. Taking a container away shrinks storage, and what no longer fits spills the same way (`Sync`). Upgrading armour in place uses one item of the better block and gives the old block's item back, as Empyrion's upgrade does. A block whose family doesn't fit the structure's class is refused (`WrongClass`, with "base blocks only" and so on).

In **creative**, placing is free and unlimited, nothing is consumed or given back, and every block can be placed; the class rules still hold. The mode is one per-world setting (`WorldRules`) that placement, the constructors, the deconstructor and the blueprint factory all read; switching it mid-session replans the queues and loses nothing. In creative the machines still run for testing, with no material checks, and the blueprint factory spawns a blueprint at once and free.

**Two kinds of return.** Taking a block down with the tool's deconstruct mode gives the block item back whole. The deconstructor is separate: it breaks items and wreck blocks one step down the tree and returns 50%. Salvage stays a choice between a whole block now and parts for something else.

## How Empyrion does it

Read for shape only; names, numbers and recipes are GalHack's own. The owner's three points hold over all of it: one shared storage per core, separate from the player's own; usable only inside that structure's area; and the player selects which source they take from.

| Question | Empyrion | GalHack |
| --- | --- | --- |
| Is a block an item? | Yes. `BlocksConfig.ecf`: a family block (say, small hull) has `StackSize`, `Mass`, `Volume`, a recipe in `Templates.ecf` (via `TemplateRoot`) and `ChildBlocks`, each listing `ChildShapes`; any child places from the one family item | The same shape, from `blocks.json`. One family per block and grid; building's shapes are the family's |
| Who makes which block? | `Templates.ecf`: `Target` lists constructors per recipe: basic hull everywhere from the survival constructor up, hardened only from large and advanced; the suit constructor makes no blocks | Tiers, derived from the bill. The suit fabricator makes the bare minimum and no blocks beyond the survival constructor |
| Where does a block go? | `AllowPlacingAt`: `Base,MS`, `SS,GV`, and narrower (concrete and the deconstructor `Base`, landing gear `SS`, hover engines `GV`) | `places` per grid in `blocks.json` |
| Where does a structure start? | A core is placed first; everything else attaches | The same: a core item founds a base, a small, hover or capital vessel |
| Taking a block down | The multi-tool's pick-up returns the block. Salvage mode breaks things into parts. No source found says damaged blocks are refused, and player posts describe picking them up, so GalHack gives them back whole | The tool's deconstruct mode. The block item comes back whole, damaged or not |
| What is a structure's storage? | Containers on a structure, reached through a container controller and the logistics screen, with a dropdown choosing which structure and container | **One shared storage per core** across all its containers: GalHack's deliberate difference (the design doc). The Core tab picks the structure |
| From how far? | Standing on the structure, or within 100 m of a wireless block | The structure's **area**: its bounding box plus a margin per class and a share of its size (the slice's `build.area`). GalHack has no wireless block; the margin stands in for it |
| Where do constructors work from? | An assigned input and output container on the structure, chosen by dropdown; the suit constructor works from the player's own inventory | The core's one storage, in and out; the suit fabricator from the player's bag. They never reach into each other |
| Building from a structure's stock | The virtual toolbar: a second toolbar of items in connected containers, switched with a key or by pressing a number twice | The structure toolbar, the same way: **the player selects the source**. Your toolbar draws on your stacks, the structure toolbar on that structure's storage, and neither falls back on the other |

Where Empyrion has no answer: blocks taken down go to the bag (or the structure's storage when building from its toolbar), then the structure's storage, then a spill crate, because the design says nothing is ever lost. The first three rows and the fourth's `AllowPlacingAt` come from the local configuration. The rest rests on player reports dated 2017 to 2023, which disagree in places; the wiki refused automated requests, the first link below was read in full, and the others only as search summaries:

- [Logistics, virtual toolbar, modular containers](https://empyriononline.com/goto/post?id=286137) (Empyrion forums, Alpha 9): the virtual toolbar, its key and double number press, standing on a structure or within 100 m of a wireless block.
- [Access constructor from remote control panel?](https://empyriononline.com/goto/post?id=457595) (Empyrion forums): the logistics screen, picking a vessel.
- [Logistics, containers and station services](https://guildorder.com/games/empyrion/wiki/logistics-containers-and-station-services) (Guild Order wiki): a constructor's input and output containers.
- [Alpha experimental 9.0 notes](https://devtrackers.gg/empyrion/p/5a1a9ed2-alpha-experimental-9-0): the container dropdown; the suit constructor drawing on the player's inventory.
- [Tips for beginners](https://empyriononline.com/posts/440289/) (Empyrion forums) and [recovered blocks](https://empyriononline.com/threads/recovered-blocks-are-useless-7518.95513/): pick-up against salvage.
- Local configuration, read only: `BlocksConfig.ecf`, `Templates.ecf`.

## Machines

All of them take inputs from, and deliver to, the structure's one shared storage (crafted blocks too); nothing here reaches into a player's own inventory. A product with no room waits in the machine's output tray until there is.

| Machine | Tier | Speed | Built with | Notes |
| --- | --- | --- | --- | --- |
| Survival constructor | 0 | 0.5x | (carried) | Basics: refining, hull plates, wire, windings, oxygen, medkits, energy cells, fuel packs, pistol rounds |
| Portable constructor | 1 | 0.75x | survival | The small set, mounted on a vessel |
| Small constructor | 1 | 1x | survival | Circuit boards, servos, nozzles, gun barrels: everything for a first small vessel |
| Large constructor | 2 | 2x | small | Gravium, thrust assemblies, field emitters, artillery shells |
| Advanced constructor | 3 | 3x | large | Exotics, warp coils, fusion cores, lattice parts |
| Deconstructor | | 1.5x | small | 50% return |
| Blueprint factory | | 4x | small | Pays in refined materials |

**Queues.** The player's orders sit in one queue; the constructor plans everything an order lacks in one pass over the tree (consumers before what they consume, so every part's need is summed before it's rounded up to whole runs) and adds those sub-parts to a second queue, one line per part, inputs before what they make, each saying which order it feeds. Stock already there is used first. Raw ore nobody here can make, parts a higher tier makes and parts the tech tree still locks (`Constructor.Lock`) are reported as missing with exact counts, and `Blockers` says of each what it needs ("2 Servo: needs a small constructor"); the queue resumes by itself when they arrive. Cancelling an order cancels the sub-parts planned for it. A block item is ordered like any other item, so all of this works through blocks: a small thruster plans its plates, nozzle and winding, and their bars, down to ore. A ledger of what queued jobs will take and make means two orders never count on the same unit. Cancelling stops a run in progress and puts its inputs back; if something the plan counted on leaves storage (placed as a block, say), it replans. Over the CPU budget, constructors slow down (`Efficiency`).

**Ammo restock** (on any structure with a constructor and guns). One toggle and a threshold per ammo type, counted in boxes: the unit storage and the constructor already count, so the setting, the stock and the screen always agree. When an ammo the structure's guns fire falls below its threshold, the constructor refills it to the threshold, exactly: one run makes one box, so it never overshoots, and there is no separate batch size or upper target. Restock jobs sit in a **third, lowest-priority lane**: they only run when no job the player queued can, and they never move or delay one. They only use materials no player job has reserved. If free materials run short it makes as many whole boxes as it can and pauses with what it needs for the rest ("Ammo restock paused: need 12 iron ore"), resuming when materials arrive. The guns on a structure decide which ammo is in use (`blocks.json` `ammo`: turret 40 mm shells, gatling 12 mm belts, rocket pod rockets).

**Deconstructor.** Breaks items, and blocks salvaged from wrecks, one step back down the tree: an item into what it was made of, a wreck block into its bill's parts (whole components, not ore). **The return rate is 50%**, chosen from the design's salvage range: the survival tool and multi-tool T1 salvage about 33%, the multi-tool T2 about 65% and vessel salvagers up to 80%; the deconstructor is a fixed station you bring things to, so it sits between the field tools. Fractions carry over inside the machine (breaking one winding gives two wires now and half an iron bar towards the next), so over time exactly 50% comes back and nothing is rounded away or made from nothing. Raw ore and unique finds can't be broken down.

**Blueprint factory.** Builds a whole saved blueprint (design section 11): it shows the bill of materials in refined materials (never ore), what storage lacks and how long it takes; refuses with the missing list; otherwise takes the whole bill at once, makes the parts itself, and hands rounding leftovers (two spare wires) back to storage. Its time is the parts' recipe seconds plus fitting each block (0.4 s small, 1.6 s large), at 4x.

## The starter small vessel

`prototypes/manufacturing/data/blueprints/gadfly.gbp`: a one-seat shuttle of 249 small blocks (4.4 t, thrust to weight 3.5 g) with a glazed cabin round a command chair, generator, fuel tank, gyro, container, 11 thrusters and three legs.

| | Ore | Crafting |
| --- | --- | --- |
| Gadfly | 714 (338 iron, 250 carbon, 66 silicon, 44 copper, 12 stone, 4 ice) | 527 runs of parts, 19 min on a small constructor; its 249 block items add about 6 min |
| Foothold base (large core, generator, fuel tank, small constructor, container, 12 floor plates) | 392 | 21 min of parts on the survival constructor; its block items add about 2 min |
| Fuel (20 energy cells) | 40 | |
| **Total from the crash** | **1,146 ore** | gather 25 min at 45 ore/min, craft 49 min, place 5 min |

That is **54 to 79 minutes** for an experienced player (the first if the constructor works while they gather, the second if nothing overlaps), leaving room for the rest of survival inside the design's one to two hours. The pacing reference (45 ore a minute with the survival tool, 12 ore per dug cell, 1.2 s to place a block) is data in `machines.json`. For comparison, the sandbox's walk-in Corsair 9 (556 blocks) is 1,375 ore and 36 minutes of parts; the blueprint factory, which works from materials and never touches block items, builds the Gadfly in 2:09 from 169 iron bars, 121 graphite, 31 glass panes, 22 copper bars, 11 ceramic tiles, 8 polymer and 6 wafers.

## Reference: Empyrion

The owner allowed Empyrion: Galactic Survival as guidance on manufacturing. Ideas only; no recipe tables, names, text or assets were copied, and GalHack's rules win where they differ.

- From the local install's configuration (read, never decompiled): constructors in Empyrion know nested recipe sets (survival, then small and vessel-mounted, then large, then advanced), which is the shape of GalHack's tiers; craft times there run from a second to many minutes, with a median around 12 s. GalHack's are shorter (2 to 30 s at speed 1, and 1 to 12 s for a block item), which keeps a first vessel inside one to two hours. Empyrion's deconstructor gives everything back; GalHack's design asks for salvage between 33% and 80%, so it returns 50%.
- The wiki's crafting page (https://empyrion.fandom.com/wiki/Crafting; only its search summary was reachable, the page itself refused the request) confirms survival and small constructors make only basic items and the advanced one makes everything.

## Adopting it

**Building** (`prototypes/building`): keep its files; compile in `Core` and `Bridge`, load the catalogue with `BuildingBridge.LoadCatalogue(data, extra components, extra materials)` to get the machines, guns and precursor armour, call `BuildingBridge.NameBlockItems` so block items carry building's names, and route the multi-tool through `BlockPlacer.Place`/`Remove`/`Upgrade` with the source storage and a `WorldRules` instead of `Building.Place(..., inventory)`. Its `cost`/`smallCost` fields can then go: `blocks.json` is the list. Call `BlockPlacer.Sync` after anything that changes containers, so capacity and cargo mass follow.

**The vertical slice:** one `WorldRules` per world, one `SharedStorage` per structure (capacity from `BuildingBridge.StorageCapacity`), and a `Constructor`/`Deconstructor`/`BlueprintFactory` per machine block found with `BuildingBridge.MachinesOn`, ticked at a fixed step. Set `Constructor.Restock.InUse` from `BuildingBridge.AmmoInUse` whenever the structure changes; the guns take their ammo from the same storage. Save each storage's `Contents`, each crate, the queues' orders (sub-parts and restock replan themselves) and the deconstructor's carry. The screens in `prototypes/manufacturing/src/Screens` show how each machine's page reads the model.

**Planet generator and terrain:** see [ORE-NOTES.md](ORE-NOTES.md#wiring-it-into-the-terrain-for-whoever-owns-sharedterrain). The `deposit` field in `items.json` maps every kind the generator places today to an ore; neodymium, gravium and the rare and exotic ores still need kinds or archetype rules there.

## Known limits

- The numbers (recipe times and counts, bills, the 45 ore a minute) are first passes, tuned only against the starter vessel's one-to-two-hour target.
- The tech tree lives in the slice (`Progress`): here, a constructor knows every recipe of its tier unless its `Lock` says otherwise, and the factory doesn't check unlocks.
- Warp drives, warp cell tanks, hyperdrives and fold crystal tanks are data only: they craft and place, and do nothing yet. Warp cells (about 1 per AU) and fold crystal (about 1 per light year) are their fuels; "warp cell" is a name in data, and the owner may change it.
- Repairs cost nothing (building's `Repair` is still free); the design wants them to draw from storage.
- Storage is one number per structure; docked craft, Home auto-transmit and trading are not here.
- Wreck salvage at the deconstructor is per block at 50%; the multi-tool's own field salvage rates (33%, 65%, 80%) belong to whoever builds salvage.
- Deposit shapes are generated and tested but not yet wired into `shared/Terrain`.
