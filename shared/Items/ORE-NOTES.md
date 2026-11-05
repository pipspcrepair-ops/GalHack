# Ore deposit shapes: notes and proposal

The design asks for deposit shapes (veins, layered beds, pockets and deep clusters) that take ideas from the Vintage Story mod **Interesting Ore Gen**, as reference only and after checking its licence. This file records what was read, what the licence allows, and what GalHack does instead. The proposal is implemented in [`Deposits/`](Deposits/) and tested by `prototypes/manufacturing/tests/ItemsTest.tscn`; wiring it into the terrain is left to the terrain's owner (see the end of this file).

## Source

- **Interesting Ore Gen (IOG)** by Ross the Great, version 3.0.2 for Vintage Story 1.22: https://mods.vintagestory.at/interestingoregen (mod page, its 3.0.0 changelog and the author's replies in its comments, read November 2026).
- No source repository is linked from the page, so no code was seen. Nothing was decompiled or downloaded.

## Licence finding

The mod page carries a copyright notice, not an open licence: **all rights reserved**. It says the mod, "including its source code, compiled binaries, and original assets, may not be redistributed", republished, modified or used for derivative versions without the author's prior written permission. It explicitly permits independent JSON content mods, compatibility patches and integrations that work with IOG without redistributing its code or assets.

So GalHack copies **nothing**: no code, no data, no configuration values, no names. What follows uses only the general ideas the page describes in prose (which kinds of shapes exist, that richness varies inside a deposit, that the surface hints at what's below), re-expressed in GalHack's own terms and implemented from scratch. Ideas themselves aren't covered by copyright, and none of IOG's expression is reproduced here.

## What IOG does (in our words)

- **Five deposit families** instead of the base game's blobs: clusters of irregular pockets joined by narrow mineralised passages; broad warped sheets (seams) that follow rock layers and sometimes split into more sheets; branching fracture-like bodies (faults) that swell, narrow, split and rejoin; rising branching bodies (chimneys) wrapped in cracked rock; and winding interconnected tubes (hydrotubes).
- **Richness varies inside a deposit.** A deposit starts poor and builds up through hot spots to its richest grade (poor, medium, rich, bountiful), so where you dig in a deposit matters. The author's remembered odds outside special districts were roughly half poor, a quarter medium, the rest rich or bountiful.
- **Host rock decides the ore.** Which ores a deposit can hold depends on the stone around it; some ores only ever appear in one family (its coal only comes in seams).
- **The surface gives hints.** Boulders sitting over buried veins, tracer ore near them, and discoloured ground replace the base game's surface ore bits, so prospecting means reading the land.
- **Big regional features.** Large hydrothermal districts with height bands where ore runs richest.

## How GalHack differs

GalHack's terrain rules are much tighter than a voxel survival game's, by design (section 10):

- Digging is **remove-only**, in 1 m cells, and only about **30 m down to bedrock**. Deposits sit **within about 10 m of the surface**.
- Deposits within about 500 m **reveal themselves**, and a detector shows them to 2.5 km. Hints help a player choose *where to dig in* a deposit, not *whether one exists*.
- The planet generator already places deposits on a 200 m grid and **guarantees the five common kinds near any start**; its deposits carry a position, a kind and an amount. There is no host-rock model; planet archetype and star type decide what is where.
- Pacing comes first: a small vessel in one to two hours means common ore must be quick to reach with the survival tool.

So GalHack keeps IOG's best ideas (distinct shapes with a reason to explore them, richness hot spots, surface hints) and drops what doesn't fit (regional districts, host rock, deep tube networks).

## Proposal: four shapes

Each deposit grows one ore body from its seed. A body is a handful of signed-distance parts (blobs, tapered tubes, ground-following sheets) with a lumpy outline from 3D value noise, sized so it holds about the volume its amount asks for, laid under the real ground, never above it and never deeper than `maxDepth` (12 m) under it. Data: [`data/deposits.json`](data/deposits.json).

| Shape | What it looks like | Depth of its top | Volume at amount 1 | Good for | Measured (tests) |
| --- | --- | --- | --- | --- | --- |
| **Pocket** | 3 to 5 squat pockets joined by thin passages | 0 to 3 m | 42 m³ | Starter ores: dig it with the survival tool in minutes | compact (length:width 1.9) |
| **Vein** | A winding tube that dips into the ground, swelling and pinching, sometimes splaying a branch; one end usually shows at the surface | 0 to 2 m | 38 m³ | Metals: follow it down | long and thin (5.6:1), always outcrops |
| **Layered bed** | A thin sheet a set depth under the ground, following hills, gently warped; one in three splits into a second sheet below | 1 to 4 m | 55 m³ | Carbon (like coal seams), silicon, ice, magnesium, burnstone | broad and flat (footprint 4.4x its thickness) |
| **Deep cluster** | A rich core near the bottom of the dig band with satellite pockets, and a thin stringer rising towards the surface ending in cracked rock | 6 to 8 m | 34 m³ | Uncommon and rare ores: the rarest metals only grow here | mean depth 8 m, always a fracture tell |

**Richness.** A body's base grade is poor (45%) or medium (55%). Hot spots raise it: within a third of a hot spot's radius the ore is bountiful, then rich, then medium. Pockets have one in their biggest pocket, veins one or two at their thickest swells, beds one or two in the sheet, deep clusters always a bountiful core. A dug cell gives `oreUnitsPerCell` (12, `machines.json`) times its grade's yield: poor 0.6, medium 1.0, rich 1.5, bountiful 2.2. In the tests a copper pocket gives 373 ore at amount 0.6 and 901 at amount 1.6.

**Which ore grows how.** Commons favour shallow pockets, veins and beds; carbon only grows in beds; uncommon ores mix veins with deep clusters; gravium and starsilver only grow deep. Rarer means deeper and richer, so the best ore asks for digging, in line with the rarity ladder ("each step lives further out in more dangerous places").

**Surface tells** (`DepositBody.Tells`): a stain (centre and radius) over the footprint for discoloured ground; outcrop points wherever the body is within 1.5 m of the surface (good spots for a boulder or ore bits); and a fracture point over a deep cluster's stringer, so a player can follow it down.

**Determinism.** Everything comes from the deposit's seed through a stable hash, so a body is identical on every machine and after every reload; string seeds use a fixed FNV hash, never `GetHashCode`.

**Cost.** Growing a body takes about 1 ms (once per deposit, then cached); testing every cell of a 16³ chunk against a body takes about 0.4 ms, and a body's bounding box skips chunks it can't reach.

## Ideas left out, for later

- **Hydrotube networks** and **regional districts**: they need deeper ground than GalHack allows.
- **Host rock**: GalHack has no rock types yet. If the terrain grows strata, a bed could prefer one.
- **Faults that rejoin**: veins splay but don't reconnect, to keep bodies cheap to test.

## Wiring it into the terrain (for whoever owns `shared/Terrain`)

`shared/Terrain/PlanetTerrainSource.cs` gives each deposit one squat lens today (`OreBodiesNear`, `OreAt`, `OreKindAt`). To switch to these shapes:

1. Compile in `shared/Items/Core` and `shared/Items/Deposits` (both engine-free), and load `DepositRules.LoadDirectory(.../shared/Items/data)` once.
2. Per deposit, grow a body lazily and cache it by deposit (they are small): `DepositGrower.Grow(new DepositSeed(seed, d.X, d.Y, ore, d.Amount), (x, z) => SurfaceHeight(x, z), rules)`, with `seed` from the planet seed and the deposit's cell, and `ore = DepositRules.OreForKind(items, d.Kind.ToString())` (the `deposit` field in `items.json` maps the generator's kinds: Iron, Copper, Silicon, Carbon, Water, Titanium as magnesium, Cobalt, Sulfur as burnstone, Thorium as gold, Crystal as raw fold crystal, Resin as biomass).
3. In `GenerateChunk`, test `body.Contains(x + 0.5, y + 0.5, z + 0.5)` for bodies whose bounds (`MinX..MaxZ`) overlap the chunk, instead of the lens; `OreKindAt` returns `body.Ore`.
4. When a cell is dug, give `body.YieldAt(...)` ore units of `body.Ore`.
5. Optionally tint ground from `Tells` (stains), drop boulders at outcrops, and crack the rock at fractures.

Neodymium, gravium, emberite, starsilver and the exotics have no planet-generator kind yet; the generator would need kinds (or archetype rules) for them, and the design's "Crash to Wings" table wants small cobalt, magnesium and neodymium deposits on every planet.
