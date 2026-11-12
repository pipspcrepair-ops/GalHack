# Grid figures: reference numbers for the 1 m grid data pass

*Status: reference only, on branch `proto/figures`, 12 Nov 2026. It answers the **(check)** items of [one-metre-grid.md](one-metre-grid.md) (sections 3, 4 and 10). Nothing here is built. Every figure follows Empyrion's configs as balance reference, recorded in GalHack's own names with the Empyrion block cited for each; no file, config text, recipe list or table is copied. The earlier research it leans on lives in `G:\GalHack\docs\floorplan-refs\`, outside the repo.*

**Conventions used throughout**

- **Counting rule** (plan section 10): Empyrion's block counts are 1 m cells. A device's footprint is its `SizeInBlocks` as cells, whole numbers only. Small-ship parts come out twice their Empyrion size, capital parts half.
- **Stat conversion**: volume, so x8 for a small-grid part and x1/8 for a capital or base part (details and exceptions in 1.1). Efficiency ratios inside one Empyrion grid are preserved.
- **Raw and set**: a figure like `3,120 -> **20,300**` is Empyrion converted -> what the plan's efficiency rule needs; with no arrow, the Empyrion number already satisfies the rule.
- **Estimate** marks anything not taken from a config. Every estimate is named as such in the text.
- **Sources**: `BlocksConfig.ecf` (`SizeInBlocks`, `Mass`, `HitPoints`, `EnergyIn`, `EnergyInIdle`, `ThrusterForce`, `EnergyOut`, `FuelCapacity`, `VolumeCapacity`, `HVEngineForce`, `Torque`, `ShieldCapacity` and others), `Templates.ecf` (recipes and crafting-time factors), `ItemsConfig.ecf` (fuels, weapons), `MaterialConfig.ecf`, and the earlier research `09-empyrion-stats.csv`, `09-empyrion-ship-plans.md`, `10-7dtd-prefabs.md`.

## Contents

1. Tiered devices (ladders for the devices of plan section 10)
2. Fixed devices (chairs and cockpits, doors, the rest)
3. Plates and the base block, with shape fractions
4. Fuels: generator fuels, tanks, burn rates, warp and hyperspace fuel
5. Speed and turn caps by mass band
6. Interiors per craft class
7. 7 Days to Die scale check
8. Where the figures disagree with the plan
9. Calibration against today's GalHack numbers

## 1. Tiered devices

### 1.1 How the ladders are built

- **Footprint**: Empyrion's `SizeInBlocks` counted as 1 m cells, width x height x depth as listed in the config. A device with no size is 1x1x1.
- **Conversion of stats** (plan section 10: capital parts half and small-ship parts twice their Empyrion size in each dimension, so volume x1/8 and x8): mass, thrust, lift, torque, generation, storage, fuel capacity and power draw are multiplied by 8 for a small-grid source (`SS`, which includes hover vessels) and by 1/8 for a capital or base source (`MS`). Ratios of output to mass and output to power inside one grid are untouched. Items that exist on both grids use x1 (`X`). Quantities that do not grow with volume (crafting speed, warp range, scan range, damage per hit) are not scaled.
- **Hit points** follow the steel cell instead: a device's Empyrion hit points are divided by the hit points of that grid's plain steel cube (50 small, 500 large) and multiplied by the proposed steel cell of section 3 (158 hp). That keeps a device's toughness in step with the plating around it.
- **Raw and set values**: `a -> **b**` means Empyrion converted gives `a` and the efficiency rule needs `b`. The rule, with a margin of 10 percent per tier: output per cell, output per tonne and output per unit of power must each be at least 1.10 times the tier below. Where a rung fails, output is raised first (per cell), then mass is lowered (per tonne), then power draw is lowered (per power). Treat every `->` as a starting suggestion: the margin is one number to change.
- **Power** is Empyrion `EnergyIn` (the config header says kW) and idle is `EnergyInIdle`. Where Empyrion gives no idle figure the device draws only while working (shown as `-`).
- **Unlock level** is the level of the Empyrion block or of its block group (the parent holding `UnlockLevel`). **Recipe shape** is the share of ingredient items by kind: plates (structural metal), machinery (motors, mechanical parts, coils), electronics (boards, fibre, computers, oscillators), advanced (nanotubes, matrices, capacitor parts), glass, plus the total item count. Only proportions are recorded, never the ingredient lists.
- **Fuel use**: only generators burn fuel (section 4.3). Every other device's fuel use is its power draw, and warp and hyperdrives burn crystals (4.4). Idle draw is given only where Empyrion has an `EnergyInIdle` (shield generators, fabricators).
- **Names**: `thruster-t1` and so on are GalHack names. The Empyrion block in the second column is the citation: stats from `BlocksConfig.ecf`, recipes from `Templates.ecf`, items from `ItemsConfig.ecf`.

### 1.2 Ladders

#### Thrusters

| Tier | Empyrion source (grid) | Cells WxHxD | Mass kg | HP | Power working | idle | Thrust | per cell | per tonne | per power | Unlock lvl | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thruster-t1 | `ThrusterSVDirectional` (SS) | 1x1x1 | 1,470 | 474 | 160 | - | 600 | 600 | 408 | 3.75 | 5 | 21 items: machinery 33%, electronics 43%, advanced 24% |
| thruster-t2 | `ThrusterSVRoundNormal` (SS) | 1x2x1 | 2,260 | 948 | 256 | - | 1,360 | 680 | 603 | 5.31 | 7 | 24 items: machinery 42%, electronics 38%, advanced 21% |
| thruster-t3 | `ThrusterMSRoundNormal` (MS) | 1x2x1 | 551 | 295 | 99.6 | - | 2,090 | 1,050 | 3,800 | 21 | 10 | 52 items: plates 38%, machinery 13%, electronics 48% |
| thruster-t4 | `ThrusterMSRoundNormal2x2` (MS) | 2x4x2 | 2,820 | 1,790 | 558 | - | 26,200 | 1,640 | 9,310 | 47 | 15 | 84 items: plates 48%, machinery 5%, electronics 40%, advanced 7% |
| thruster-t5 | `ThrusterMSRoundNormal3x3` (MS) | 3x6x3 | 7,860 | 5,290 | 1,560 | - | 115,000 | 2,130 | 14,600 | 74 | 20 | 151 items: plates 60%, machinery 4%, electronics 31%, advanced 5% |

Empyrion never makes a higher tier worse on output per cell, per tonne or per power here. 

Empyrion's jet thrusters (`ThrusterJetRound*`, `ThrusterJetMSRound*`, 1x3x1 up to 3x13x3) are an atmosphere-only second family with a booster factor of 1.25 to 1.5. They are not carried over; the round family above is the ladder.

t2 and t3 share a 1x2x1 footprint, as in Empyrion where the small-ship round thruster and the capital round thruster are the same size in blocks. t3 is about 4 times lighter and 4 times more efficient per power than t2, so the rule holds. The plan's placeholder for T3 (2x2x2, 1200 kN) does not match Empyrion's proportions: Empyrion's 2x2 capital thruster is 2x4x2 (16 cells) and its 3x3 is 3x6x3 (54 cells). The capital directional thruster (`ThrusterMSDirectional`, level 7, 1x1x1) converts to 900 thrust, 295 kg and is a plain upgrade of t1 at the same footprint if a same-size tier is wanted.

Fuel use: thrusters burn no fuel of their own in Empyrion. They draw `EnergyIn` from the shared pool and the generators burn fuel to supply it (section 4).

Dropped rungs: `ThrusterSVRoundNormalT2` (230 thrust raw, 1x2x1, level 10 group), `ThrusterSVRoundLarge` (275, 1x3x1) and `ThrusterSVRoundLargeT2` (380, 1x3x1) are in-between small-ship rungs, worth keeping as alternates if the ladder wants a 1x3x1 part.

#### Generators

| Tier | Empyrion source (grid) | Cells WxHxD | Mass kg | HP | Power working | idle | Output kW | per cell | per tonne | per power | Unlock lvl | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| generator-t1 | `GeneratorSVSmall` (SS) | 1x1x1 | 1,180 | 395 | 0 | - | 5,600 | 5,600 | 4,730 | - | 1 | 3 items: machinery 33%, advanced 67% |
| generator-t2 | `GeneratorSV` (SS) | 1x2x1 | 2,100 | 632 | 0 | - | 12,000 -> **12,300** | 6,160 | 5,880 | - | 3 | 6 items: machinery 33%, advanced 67% |
| generator-t3 | `GeneratorMS` (MS) | 3x1x1 | 1,670 | 79 | 0 | - | 3,120 -> **20,300** | 6,780 | 12,200 | - | 10 | 75 items: plates 29%, machinery 36%, electronics 32%, advanced 3% |
| generator-t4 | `GeneratorMST2` (MS) | 6x2x2 | 4,670 | 158 | 0 | - | 22,500 -> **179,000** | 7,450 | 38,300 | - | 15 | 127 items: plates 35%, machinery 34%, electronics 28%, advanced 3% |

Where Empyrion breaks the efficiency rule (a higher tier is worse): generator-t3 below generator-t2 on output per cell (1,040 vs 6,000); generator-t3 below generator-t2 on output per tonne (1,870 vs 5,730); generator-t4 below generator-t3 on output per cell (938 vs 1,040). It is level with, or within 10 percent of, the tier below at: generator-t2 on output per cell (6,000 vs 5,600).

Rungs renumbered t1 to t4 (the base generator `GeneratorBA` is dropped, see below). Output is kW, fed to the capacitor bank. The surprise in Empyrion's numbers: small-ship generators are far denser than base and capital ones (700 kW from one half-metre block, against 2500 kW from a base generator that is 2x1x1 large blocks). The conversion keeps that, so t3 and t4 must be raised a lot to satisfy the rule. If that raise is judged too generous, the alternative is to lower t1 and t2 and drop the plan's bottom-rung rule (small part x8) for generators only.

Fuel: Empyrion generators burn the fuels of section 4 at a rate equal to output divided by the fuel's energy (burn rates by tier in 4.3).

Dropped rungs: `GeneratorBA` (2x1x1, 2500 kW raw, level 3): dominated, about 1/40 of an SV generator's output per cell once converted, at the same level. Fold it into t2 or leave it out.

#### Fuel tanks

| Tier | Empyrion source (grid) | Cells WxHxD | Mass kg | HP | Power working | idle | Capacity (kWh of fuel) | per cell | per tonne | per power | Unlock lvl | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| fuel-tank-t1 | `FuelTankSVSmall` (SS) | 1x1x1 | 336 | - | 8 | - | 2,400 | 2,400 | 7,140 | 300 | 1 | 4 items: plates 25%, electronics 25%, advanced 50% |
| fuel-tank-t2 | `FuelTankSV` (SS) | 1x1x2 | 704 -> **672** | - | 8 | - | 4,800 -> **5,280** | 2,640 | 7,860 | 660 | 3 | 8 items: plates 25%, electronics 25%, advanced 50% |
| fuel-tank-t3 | `FuelTankMSLarge` (MS) | 1x2x1 | 314 | 25.3 | 0.12 | - | 338 -> **5,810** | 2,900 | 18,500 | 46,500 | 10 | 78 items: plates 77%, electronics 23% |
| fuel-tank-t4 | `FuelTankMSLargeT2` (MS) | 2x2x1 | 446 | 25.3 | 0.12 | - | 750 -> **12,800** | 3,190 | 28,600 | 102,000 | 15 | 106 items: plates 66%, electronics 34% |

Where Empyrion breaks the efficiency rule (a higher tier is worse): fuel-tank-t2 below fuel-tank-t1 on output per tonne (6,820 vs 7,140); fuel-tank-t3 below fuel-tank-t2 on output per cell (169 vs 2,400); fuel-tank-t3 below fuel-tank-t2 on output per tonne (1,070 vs 6,820). It is level with, or within 10 percent of, the tier below at: fuel-tank-t2 on output per cell (2,400 vs 2,400).

Rungs renumbered t1 to t4. The capital tanks hold about 1/16 of a small-ship tank per cell once converted, so they are raised heavily. Tanks draw a nominal 1 kW.

Dropped rungs: `FuelTankMSSmall` (1x1x1, 1200 raw, level 3): same footprint as t1 but about 16 times less dense.

#### Storage containers

| Tier | Empyrion source (grid) | Cells WxHxD | Mass kg | HP | Power working | idle | Capacity SU | per cell | per tonne | per power | Unlock lvl | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| container-t1 | `ContainerControllerSmall` (SS) | 1x1x1 | 440 | 316 | 24 | - | 1,000 | 1,000 | 2,270 | 41.7 | 3 | 3 items: plates 67%, electronics 33% |
| container-t2 | `CargoContainerSV` (SS) | 1x1x1 | 360 | 126 | 8 | - | 1,000 -> **1,100** | 1,100 | 3,060 | 138 | 1 | 2 items: plates 100% |
| container-t3 | `CargoContainerSmall` (MS) | 1x1x1 | 75 | 31.6 | 0.12 | - | 1,000 -> **1,210** | 1,210 | 16,100 | 9,680 | 1 | 12 items: plates 100% |
| container-t4 | `CargoContainerMedium` (MS) | 2x1x1 | 125 | 158 | 0.12 | - | 2,000 -> **2,660** | 1,330 | 21,300 | 21,300 | 1 | 12 items: plates 100% |

Empyrion never makes a higher tier worse on output per cell, per tonne or per power here. It is level with, or within 10 percent of, the tier below at: container-t2 on output per cell (1,000 vs 1,000); container-t3 on output per cell (1,000 vs 1,000); container-t4 on output per cell (1,000 vs 1,000).

Empyrion has no storage tiers: one controller holds 125 SU (small) or 8000 SU (large), extensions add the same, and the caps are 32,000 (small) and 320,000 (large). Converted, both give 1000 SU per cell, which matches the plan's `1000 SU at 1x1x1, 8000 SU at 2x2x2` and today's 320,000 cap. t1 is the crafted small container (the controller block, 125 SU, level 3); t2 to t4 are the single-block cargo containers (`CargoContainerSV` 125, `CargoContainerSmall` 8000, `CargoContainerMedium` 16000 in 2x1x1), because the plan drops the controller and extension pair. Power draw is nominal (1 kW).

Unlock levels shown are those of the crafting group (1). The controllers are level 3 (small) and 5 (large) and the extensions 5 and 7.

#### Hover plates

| Tier | Empyrion source (grid) | Cells WxHxD | Mass kg | HP | Power working | idle | Lift index | per cell | per tonne | per power | Unlock lvl | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| hover-t1 | `HoverEngineSmall` (SS) | 1x1x1 | 1,120 | 316 | 80 | - | 1,600 | 1,600 | 1,430 | 20 | 3 | 23 items: plates 43%, machinery 9%, electronics 26%, advanced 22% |
| hover-t2 | `HoverEngineLarge` (SS) | 2x1x2 | 2,720 | 1,110 | 320 | - | 6,400 -> **7,040** | 1,760 | 2,590 | 22 | 8 | 45 items: plates 56%, machinery 9%, electronics 24%, advanced 11% |

Empyrion never makes a higher tier worse on output per cell, per tonne or per power here. It is level with, or within 10 percent of, the tier below at: hover-t2 on output per cell (1,600 vs 1,600); hover-t2 on output per power (20 vs 20).

`HoverEngineSmall` is 1x1x1 (level 3), `HoverEngineLarge` is 2x1x2 (level 8). The large plate has exactly 4 times the lift and 4 times the power of the small one, so lift per power does not improve in Empyrion, only mass per cell does. Ride height is not in the configs (only damping coefficients `HVEngineDampCoef` 30 and 100). The plan's 1.5 m and 3 m ride heights stand as an estimate: scale with the footprint edge, 1.5 m at one cell and 3 m at two.

The starter `HoverEngineThruster` (level 1, at most four per vessel) is not a plate rung.

#### Gyros (RCS blocks)

| Tier | Empyrion source (grid) | Cells WxHxD | Mass kg | HP | Power working | idle | Torque index | per cell | per tonne | per power | Unlock lvl | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| gyro-t1 | `RCSBlockSV` (SS) | 1x1x1 | 2,050 | - | 80 | - | 800 | 800 | 391 | 10 | 5 | 35 items: plates 57%, machinery 29%, electronics 14% |
| gyro-t2 | `RCSBlockMS` (MS) | 1x1x1 | 2,000 | - | 3.12 | - | 1,250 | 1,250 | 627 | 400 | 7 | 44 items: plates 45%, electronics 50%, advanced 5% |
| gyro-t3 | `RCSBlockMS_T2` (MS) | 1x1x1 | 2,400 | - | 6.25 | - | 18,800 | 18,800 | 7,830 | 3,000 | 10 | 57 items: plates 35%, electronics 49%, advanced 16% |

Empyrion never makes a higher tier worse on output per cell, per tonne or per power here. 

All three are 1x1x1 in Empyrion; torque rises 12 times between the capital block and its T2 for the same footprint. The plan's idea of a 2x2x2 large gyro has no Empyrion counterpart. If footprints should grow with tier, treat t3 as 2x2x2 and read its per-cell figures as divided by 8.

#### Shield generators

Output here is shield capacity (hit points of the bubble), converted the way device hit points are (relative to the steel cube of its grid, times 158). Raw Empyrion capacities are 4,000 (small ship), 18,000 (capital) and 36,000 (capital T2); the base versions are 25,000 and 50,000 (`ShieldGeneratorBA`, `ShieldGeneratorBAT2`, also 3x3x3). Cooldown after the shield is broken is 15 s on every one (`ShieldCooldown`); each crystal item gives 1000 capacity (`ShieldPerCrystal`).

| Tier | Empyrion source (grid) | Cells WxHxD | Mass kg | HP | Power working | idle | Shield capacity | per cell | per tonne | per power | Unlock lvl | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| shield-t1 | `ShieldGeneratorSV` (SS) | 2x1x3 | 13,000 | 790 | 28,800 | 2,880 | 12,600 | 2,110 | 969 | 0.44 | 7 | 22 items: machinery 32%, electronics 36%, advanced 32% |
| shield-t2 | `ShieldGeneratorCV` (MS) | 3x3x3 | 1,620 | 158 | 2,250 | 100 | 5,690 -> **62,600** | 2,320 | 38,700 | 27.8 | 10 | 53 items: machinery 57%, electronics 15%, advanced 28% |
| shield-t3 | `ShieldGeneratorCVT2` (MS) | 3x3x3 | 1,830 -> **1,620** | 158 | 4,500 -> **2,250** | 100 | 11,400 -> **68,800** | 2,550 | 42,600 | 30.6 | 15 | 74 items: machinery 47%, electronics 22%, advanced 31% |

Where Empyrion breaks the efficiency rule (a higher tier is worse): shield-t2 below shield-t1 on output per cell (211 vs 2,110). It is level with, or within 10 percent of, the tier below at: shield-t3 on output per power (2.53 vs 2.53). Idle draw is a tenth of the working draw (`EnergyInIdle` 360 of 3600, 800 of 18000, 1600 of 36000), which gives the 'working while rebuilding, idle while up' split the plan describes. Raw recharge per second is 500 (small ship), 120 and 120 (capital); converted by the same hit-point rule: shield-t1 1,580, shield-t2 37.9, shield-t3 37.9 per second. The generator's own hit points (the part that fries at 0) are 250 small, 500 capital and 1000 base before conversion.

#### Fabricators

| Tier | Empyrion source (grid) | Cells WxHxD | Mass kg | HP | Power working | idle | Crafting speed (1 / time factor) | per cell | per tonne | per power | Unlock lvl | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| portable-fabricator | `ConstructorSurvival` (X) | 1x1x1 | 40 | 150 | 10 | - | 0.62 | 0.62 | 15.6 | 0.06 | 1 | 5 items: polymer 100% |
| fabricator-t1 | `ConstructorT0` (MS) | 1x1x1 | 208 | 79 | 31.2 -> **12.1** | 0.24 | 0.83 | 0.83 | 4.02 | 0.07 | 3 | 13 items: plates 23%, machinery 54%, electronics 23% |
| fabricator-t2 | `ConstructorT1V2` (MS) | 2x2x2 | 514 | 79 | 62.5 -> **13.2** | 0.26 | 1 | 0.12 | 1.94 | 0.08 | 5 | 23 items: plates 22%, machinery 52%, electronics 26% |
| fabricator-t3 | `ConstructorT2` (MS) | 2x2x2 | 1,720 | 79 | 188 -> **24** | 0.08 | 2 | 0.25 | 1.16 | 0.08 | 10 | 95 items: plates 63%, machinery 23%, electronics 8%, advanced 5% |

Where Empyrion breaks the efficiency rule (a higher tier is worse): fabricator-t1 below portable-fabricator on output per power (0.03 vs 0.06); fabricator-t2 below fabricator-t1 on output per power (0.02 vs 0.03); fabricator-t3 below fabricator-t2 on output per power (0.01 vs 0.02). 

Speed is the inverse of Empyrion's crafting-time factor from the header of `Templates.ecf`: portable constructor 1.6, base constructor T0 1.2, large constructor 1.0, advanced constructor 0.5 (lower is faster). The small-ship and hover constructors (`ConstructorSV` 1x1x3, 507 kg, 420 kW, level 7; `ConstructorHV`, level 5) sit at 1.4 and are not rungs. The deconstructor (`Deconstructor`, 2x2x2, 825 kW, idle 10, level 12, factor 0.5) is a fixed 2x2x2 companion to t3. The plan's three tiers are t1 to t3, with the portable one apart.

Empyrion breaks the rule hard: the advanced constructor is only twice as fast as the large one but draws three times the power, and the large one is 1.2 times faster than the T0 base constructor for double the power. The set column cuts power so speed per kW rises 10 percent per tier. Each tier also gates recipes (the target lists in `Templates.ecf`), which the data pass owns.

#### Drill modules

Output is terrain damage per second (750 or 700 per 0.5 s shot, from `ItemsConfig.ecf`: `DrillAttachmentWeapon`, `TurretGVDrillWeapon`, `TurretMSDrillWeapon`). Radius per shot is 2, 4 and 5 blocks; range 8 and 150. The plan's t2 and t3 are the small and large drill turrets; Empyrion's are both 3x1x3 (the retractable 3x2x3 version adds 40 percent hit points).

| Tier | Empyrion source (grid) | Cells WxHxD | Mass kg | HP | Power working | idle | Terrain damage per second | per cell | per tonne | per power | Unlock lvl | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| drill-t1 | `DrillAttachment` (SS) | 1x6x1 | 5,200 | 1,580 | 2,000 | 8 | 1,500 | 250 | 288 | 0.75 | 7 | 26 items: plates 8%, machinery 92% |
| drill-t2 | `TurretGVDrill` (SS) | 3x1x3 | 6,320 -> **4,730** | 2,690 | 5,500 -> **1,820** | 37 | 1,500 | 167 | 317 | 0.83 | 15 | 19 items: plates 53%, machinery 11%, electronics 32%, advanced 5% |
| drill-t3 | `TurretMSDrill` (MS) | 3x1x3 | 1,130 | 711 | 394 | 4.12 | 1,400 | 156 | 1,240 | 3.56 | 20 | 66 items: plates 76%, machinery 3%, electronics 20%, advanced 2% |

Where Empyrion breaks the efficiency rule (a higher tier is worse): drill-t2 below drill-t1 on output per tonne (237 vs 288); drill-t2 below drill-t1 on output per power (0.27 vs 0.75).  Levels: 7 for the 1x6x1 mechanical drill (`DrillAttachment`), 15 for the hover turret group (`TurretGVToolBlocks`), 20 for the capital group (`TurretMSToolBlocks`). The capital 1x6x1 `DrillAttachmentCV` (level 25, 3650 kg, 2220 kW) and the hover T2 attachment (`DrillAttachmentT2`, level 20, 1000 per hit) are not carried: the plan has no capital-only drill.

#### Warp drive and hyperdrive

Empyrion has one `WarpDrive` class for jumps inside a system (AU), inside a sector (km) and between stars (light years). The small-ship drive has no light-year range, so it maps to the plan's warp drive; the capital drive T1 and T2 map to the plan's hyperdrive T1 and T2. All three are 3x2x5 (30 cells). Mass and power are converted x8 for the small-ship drive and x1/8 for the capital drives.

| Tier | Source | Cells | Mass kg | HP | Power kW | Range LY / AU / km | Crystals per LY / AU / km | Unlock | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|
| warp-drive-t1 | `WarpDriveSV` (SS) | 3x2x5 | 11,900 | 790 | 200 | 0 / 15 / 15000 | 0 / 1 / 1 | 10 | 100 items: plates 70%, advanced 30% |
| hyperdrive-t1 | `WarpDrive` (MS) | 3x2x5 | 10,300 | 363 | 3.12 | 30 / 24 / 20000 | 1 / 2 / 2 | 10 | 148 items: plates 68%, machinery 14%, electronics 11%, advanced 8% |
| hyperdrive-t2 | `WarpDriveT2` (MS) | 3x2x5 | 10,300 | 363 | 3.12 | 60 / 48 / 40000 | 1 / 3 / 3 | 20 | 173 items: plates 58%, machinery 17%, electronics 14%, advanced 11% |

Cooldown between micro-warps is 25 s on all (`MicroWarpCooldown`). The T2 drive doubles every range for the same mass and power, at 3 crystals per AU instead of 2, so range per mass and per power doubles and the rule holds between the two hyperdrives. Between the small-ship drive and hyperdrive-t1 the conversion makes a jump that is not in Empyrion's design: mass is about equal (11,900 against 10,300 kg) but power falls from 200 kW to 3 kW, because the raw 25 kW is the same on both grids and the conversion multiplies one by 8 and divides the other by 8. Set the warp drive at 25 kW raw (3 kW after the capital conversion, 200 kW after the small-ship one) and let the data pass choose: 200 and 3 as converted, or one common figure. The plan's "hyperdrives might be longer, for example 2x2x4" has no Empyrion basis: Empyrion's capital drive is 3x2x5 and stays.
Fuel is the crystal of section 4.4.

#### Warp tank (small ship) and fold tank (capital)

| Tier | Source | Cells | Mass kg | HP | Capacity litres (crystal is 2.4 L) | Unlock | Recipe shape |
|---|---|---|---|---|---|---|---|
| warp-tank | `PentaxidTankSV` (SS) | 1x1x2 | 2,620 | 316 | 1,600 | 7 | 74 items: plates 81%, advanced 19% |
| fold-tank | `PentaxidTank` (MS) | 1x2x1 | 308 | 31.6 | 75 | 10 | 43 items: plates 23%, machinery 5%, advanced 72% |

Raw tank capacity is 200 L (small ship) and 600 L (capital); converted 1,600 and 75 L, so the capital tank is the smaller per cell, a rule violation. Set the fold tank at 1.1 times the warp tank per cell (800 L per cell): about 1,760 L at 1x2x1. A full tank must cover several maximum jumps: the capital T1 drive's longest jump (30 LY) costs 30 crystals and 24 AU costs 48, and one tank holds 83 (small ship) or 250 (capital) crystals raw.

#### Landing gear and docking pads

Footprints are Empyrion's, counted in 1 m cells. The plan's gear is an attachment point, not a load-bearing part, so only mass and hit points matter. Length versions for nesting exist in Empyrion (small ship, level 5): 1x1x2 (171 kg raw, 50 hp), 1x2x2 (150, 120), 1x3x1 (150 to 200, 120), 1x2x4 (240, 175), 1x3x3 (171, 50). Capital gear: 1x1x2 and 1x2x2 (1700 kg, 350 hp), 1x2x4 (2800, 650), 1x2x1 (1830, 644), 1x2x2 retracting (2980, 945).

| Tier | Source | Cells | Mass kg | HP | Unlock | Recipe shape |
|---|---|---|---|---|---|---|
| pad-t1 | `LandinggearSV` (SS) | 1x1x2 | 1,370 | 158 | 5 | 6 items: machinery 67%, electronics 33% |
| pad-t2 | `LandinggearHeavySV` (SS) | 2x1x2 | 1,860 | 253 | 10 | 13 items: machinery 62%, electronics 38% |
| pad-t3 | `LandinggearMSLight` (MS) | 1x1x2 | 212 | 111 | 7 | 14 items: machinery 86%, electronics 14% |
| pad-t4 | `LandinggearMSHeavy` (MS) | 2x1x2 | 229 | 205 | 7 | 14 items: machinery 86%, electronics 14% |

Empyrion's gear is among the weakest parts of its ships (50 hp on a small ship, 350 to 945 on a capital, against a steel cube at 50 and 500), which agrees with the plan's weak-point rule: converted, a pad cell has 51 to 79 hp against a plated steel cell's 158, so gear breaks first. Mass per cell of pad falls with every tier (684, 464, 106 and 57 kg for t1 to t4), so the efficiency rule holds without a fix. Pad tiers go by area: t1 and t3 are 1x1x2, t2 and t4 are 2x1x2.

#### Solar panels

Empyrion has flat panels on the base grid only: `SolarPanelSmallBlocks` 3x1x2 (level 1, efficiency 1.0, 350 kg, 100 hp) and `SolarPanelBlocks` 5x1x3 (level 3, efficiency 1.8, 730 kg, 150 hp; its children are slopes of 5x2x3). Absolute output is not in the configs, only the efficiency index; the only absolute solar figure is `SolarGenerator` (1x2x1, 5000 kW, 2050 kg, 150 hp, level 1), a different device.

| Tier | Source | Cells | Mass kg (/8) | HP | Efficiency index | per cell | Estimated output kW | Unlock | Recipe shape |
|---|---|---|---|---|---|---|---|---|---|
| solar-t1 | `SolarPanelSmallBlocks` (MS) | 3x1x2 | 43.8 | 31.6 | 1.0 | 0.17 | 250 (estimate, 250 kW per unit of index) | 1 | 5 items: plates 40%, electronics 20%, glass 40% |
| solar-t2 | `SolarPanelBlocks` (MS) | 5x1x3 | 91.2 | 47.4 | 1.8 | 0.12 | 450 (estimate, 250 kW per unit of index) | 3 | 7 items: plates 43%, electronics 14%, glass 43% |

Where Empyrion breaks the rule: the 5x1x3 panel (efficiency 1.8 over 15 cells) gives 0.12 per cell against 0.167 for the 3x1x2 (1.0 over 6 cells). Set t2 efficiency at 2.75 (15 cells x 0.167 x 1.10) and keep mass at 91 kg. Estimate: 250 kW per unit of efficiency is a placeholder, about 4 percent of a t1 generator's 5,600 kW per cell, until the generator ladder fixes the power scale. Panels scale by area in the plan, so each tier is a flat part of height 1 and a third rung (7x1x4) would follow the same step.

#### Forcefield emitters

Empyrion's `ForcefieldEmitterBlocks` (level 7, `TechTreeNames Misc`) is one device with a fixed field size per child: 1x1, 1x2, 1x3, 3x5, 3x9, 5x11, 7x12 (and a legacy 7x14), all with the same stats: 40 kg, 80 hp, 1 kW, airtight, placeable on both grids. Empyrion's field is a single block that fills a whole rectangle; the plan builds the field between emitters on opposite edges. So the tier here is the reach between emitters, set from Empyrion's field sizes, with the emitter block staying small. The recipe is one shape for every size: about 7 items, electronics 50 percent, advanced 25 percent, the rest a pentaxid element and capacitor part.

| Tier | Source sizes | Emitter cells | Mass kg | HP | Reach between emitters | Power while powered |
|---|---|---|---|---|---|---|
| emitter-t1 | `ForcefieldEmitter1x1` to `3x5` | 1x1x1 | 40 | 80 | up to 5 cells | 1 kW per 15 cells of field (Empyrion's flat 1 kW; area scaling is an estimate, 0.07 kW per cell of field) |
| emitter-t2 | `ForcefieldEmitter3x9`, `5x11` | 1x1x1 | 40 | 80 -> 120 | up to 11 cells | 0.05 kW per cell of field (estimate) |
| emitter-t3 | `ForcefieldEmitter7x12` | 1x1x1 | 40 | 80 -> 160 | up to 14 cells | 0.035 kW per cell of field (estimate) |

Estimate: Empyrion's power does not scale with area at all (1 kW whatever the size), so any area-scaled draw is invented here. The figures anchor one 15-cell field at the 1 kW Empyrion charges for the smallest, then lower the draw per area by about 30 percent per tier so a higher tier is more efficient, as the rule asks. Emitters are plated parts in the plan, so the hit points rise by plating, not by Empyrion.

#### Capacitors

Estimate: Empyrion has no capacitor device (`CapacitorMS` and `CapacitorSS`, 2x1x1, 500 kg and 200 kg, are decorations with no stored energy, and `CapacitorComponent` is a crafting ingredient). The numbers below are modelled on the fuel tank ladder, since an Empyrion tank also holds energy, with leak and volatility from the plan.

| Tier | Modelled on | Cells | Mass kg | HP | Storage kWh | Leak per hour | Blast radius on destruction |
|---|---|---|---|---|---|---|---|
| capacitor-t1 | `FuelTankSVSmall` | 1x1x1 | 340 | 395 | 120 | 2 percent | 2 cells |
| capacitor-t2 | `FuelTankMSLarge` | 1x2x1 | 320 | 160 | 640 | 2 percent | 3 cells |
| capacitor-t3 | `FuelTankMSLargeT2` | 2x2x1 | 450 | 160 | 1,550 | 2 percent | 3 cells |

Storage is 5 percent of the same-size fuel tank's set capacity, so one cell bank carries a t1 generator for about 77 s at full draw (120 kWh against 5,600 kW); tune in play. Leak and blast radius (Empyrion device `BlastRadius` values are 2 to 5) are invented proportions. Higher tiers keep the 10 percent per-cell growth, but because a bank is linear in cells stacking never wins, which is the plan's intent.

#### Comms array

Estimate: Empyrion has no comms array. The nearest analogues are the detector (`DetectorSVT1` 1x1x1, 5 kW, range 3,500 in atmosphere and 14,000 in space; `DetectorCV` 1x1x1, 320 kW, 6,000 and 24,000), the remote connection (`RemoteConnection`, level 3, range 100, at most 10, 10 kW) and the decorative antenna masts (1x3x1 up to 1x8x1, 6,290 kg, 3 kW). The array takes footprints from the masts and ranges from the plan.

| Tier | Cells | Mass kg (mast 6,290 / 8) | HP | Power | Range |
|---|---|---|---|---|---|
| comms-t1 | 1x3x1 | 786 | 158 | 3 kW (`Antenna`) | 5 km, about `DetectorCV` 6 km atmosphere range |
| comms-t2 | 1x5x1 | 1,100 | 250 | 6 kW | the planet and its orbit |
| comms-t3 | 1x8x1 | 1,500 | 400 | 12 kW | the star system |

### 1.3 Set values at a glance

The set column of every ladder above in one table (Empyrion converted, adjusted by the 10 percent rule; where a rung needed no adjustment the figure is the converted Empyrion one). Hit points are the converted device hit points (steel cell 158). Output units are as in each ladder. Estimated devices (capacitors, comms array, forcefield emitters, solar panels) are in their own tables.

| Tier | Cells | Mass kg | HP | Power working kW | idle kW | Output | Unlock |
|---|---|---|---|---|---|---|---|
| thruster-t1 | 1x1x1 | 1,470 | 474 | 160 | - | 600 | 5 |
| thruster-t2 | 1x2x1 | 2,260 | 948 | 256 | - | 1,360 | 7 |
| thruster-t3 | 1x2x1 | 551 | 295 | 99.6 | - | 2,090 | 10 |
| thruster-t4 | 2x4x2 | 2,820 | 1,790 | 558 | - | 26,200 | 15 |
| thruster-t5 | 3x6x3 | 7,860 | 5,290 | 1,560 | - | 115,000 | 20 |
| generator-t1 | 1x1x1 | 1,180 | 395 | 0 | - | 5,600 | 1 |
| generator-t2 | 1x2x1 | 2,100 | 632 | 0 | - | 12,300 | 3 |
| generator-t3 | 3x1x1 | 1,670 | 79 | 0 | - | 20,300 | 10 |
| generator-t4 | 6x2x2 | 4,670 | 158 | 0 | - | 179,000 | 15 |
| fuel-tank-t1 | 1x1x1 | 336 | - | 8 | - | 2,400 | 1 |
| fuel-tank-t2 | 1x1x2 | 672 | - | 8 | - | 5,280 | 3 |
| fuel-tank-t3 | 1x2x1 | 314 | 25.3 | 0.12 | - | 5,810 | 10 |
| fuel-tank-t4 | 2x2x1 | 446 | 25.3 | 0.12 | - | 12,800 | 15 |
| container-t1 | 1x1x1 | 440 | 316 | 24 | - | 1,000 | 3 |
| container-t2 | 1x1x1 | 360 | 126 | 8 | - | 1,100 | 1 |
| container-t3 | 1x1x1 | 75 | 31.6 | 0.12 | - | 1,210 | 1 |
| container-t4 | 2x1x1 | 125 | 158 | 0.12 | - | 2,660 | 1 |
| hover-t1 | 1x1x1 | 1,120 | 316 | 80 | - | 1,600 | 3 |
| hover-t2 | 2x1x2 | 2,720 | 1,110 | 320 | - | 7,040 | 8 |
| gyro-t1 | 1x1x1 | 2,050 | - | 80 | - | 800 | 5 |
| gyro-t2 | 1x1x1 | 2,000 | - | 3.12 | - | 1,250 | 7 |
| gyro-t3 | 1x1x1 | 2,400 | - | 6.25 | - | 18,800 | 10 |
| shield-t1 | 2x1x3 | 13,000 | 790 | 28,800 | 2,880 | 12,600 | 7 |
| shield-t2 | 3x3x3 | 1,620 | 158 | 2,250 | 100 | 62,600 | 10 |
| shield-t3 | 3x3x3 | 1,620 | 158 | 2,250 | 100 | 68,800 | 15 |
| portable-fabricator | 1x1x1 | 40 | 150 | 10 | - | 0.62 | 1 |
| fabricator-t1 | 1x1x1 | 208 | 79 | 12.1 | 0.24 | 0.83 | 3 |
| fabricator-t2 | 2x2x2 | 514 | 79 | 13.2 | 0.26 | 1 | 5 |
| fabricator-t3 | 2x2x2 | 1,720 | 79 | 24 | 0.08 | 2 | 10 |
| drill-t1 | 1x6x1 | 5,200 | 1,580 | 2,000 | 8 | 1,500 | 7 |
| drill-t2 | 3x1x3 | 4,730 | 2,690 | 1,820 | 37 | 1,500 | 15 |
| drill-t3 | 3x1x3 | 1,130 | 711 | 394 | 4.12 | 1,400 | 20 |

Unlock levels and recipe shapes for the devices Empyrion does not have (all estimates): forcefield emitter t1 at level 7 (the Empyrion emitter's level), t2 at 12 and t3 at 17; capacitor t1 to t3 at 5, 10 and 15; comms array t1 at level 7 (the detector's level), t2 at 15 and t3 at 20. Steps of 5 levels follow Empyrion's own thruster ladder (5, 7, 10, 15, 20). Recipe shapes: capacitor like a fuel tank (plates 60 percent, electronics 25 percent, advanced 15 percent); comms array like the antenna mast (plates 30 percent, electronics 70 percent); forcefield emitters as in their own table.


## 2. Fixed devices

### 2.1 Chairs and cockpits (sized for a 1.8 m person)

Seated envelope used for the check (estimate): 0.6 m wide, 1.35 m high, 1.1 m deep with the knees, plus a console 0.5 m in front. A chair therefore needs 1 cell of width, 2 of height and 2 to 3 of depth. Empyrion counts below are the config `SizeInBlocks` used as 1 m cells.

| GalHack part | Empyrion source (grid) | Cells WxHxD | Seated person visibly fits | Mass kg | HP | Power kW | Unlock | Recipe shape |
|---|---|---|---|---|---|---|---|---|
| command-chair | `CockpitOpen2SV` (SS) | 1x2x3 | yes, 1 wide, 2 high, 3 deep | 1,920 | 695 | 80 | 1 | 16 items: machinery 38%, electronics 38%, glass 25% |
| passenger-seat | `PassengerSeat2SV` (SS) | 1x3x2 | yes, 1x3x2 | 576 | 474 | 0 | 3 | 10 items: plates 70%, electronics 30% |
| cockpit-t1 | `CockpitSV05` (SS) | 3x2x4 | yes, 3x2x4 enclosed, head height 2 m | 5,760 | 2,210 | 80 | 1 | 16 items: machinery 38%, electronics 38%, glass 25% |
| cockpit-t2 | `CockpitSV01` (SS) | 3x3x5 | yes, 3x3x5 | 6,760 | 2,840 | 80 | 1 | 16 items: machinery 38%, electronics 38%, glass 25% |
| cockpit-t3 | `CockpitSV01T2` (SS) | 3x3x5 | yes, 3x3x5, doubled hp | 11,100 | 5,690 | 80 | 10 | 45 items: plates 44%, machinery 16%, electronics 22%, glass 18% |
| (capital seat, too small) | `CockpitMS01` (MS) | 1x1x1 | no, 1x1x1 is a 1 m cube against 1.8 m | 12.5 | 47.4 | 2.5 | 7 | 64 items: machinery 78%, electronics 14%, glass 8% |
| (capital console, too small) | `CockpitMS02` (MS) | 2x1x1 | no, 2x1x1 is 1 m high | 35.5 | 94.8 | 2.5 | 7 | 64 items: machinery 78%, electronics 14%, glass 8% |

Findings. Empyrion's small-ship cockpits (3x3x3 to 3x3x5 blocks of 0.5 m) are only 1.5 m wide in the game, cramped for a person; counted as 1 m cells they become 3 m wide and 3 m tall, which seats a 1.8 m person with room to see out. Empyrion's capital seat, `CockpitMS01`, is one large block (2 m) and shrinks to a 1 m cube under the count rule, too small: the plan's concern is right. Use the small-ship counts for every vessel. The chair rung (`CockpitOpen2SV`, 1x2x3) is the minimum that seats the person; `PassengerSeatSV` is the wider 2x3x2. The cockpit ladder is bigger parts at later unlocks, with t3 doubling hit points (1800 against 900 before conversion) for 1.6 times the mass. Other small-ship variants (`CockpitSV` 3x3x3, `CockpitSV_fighter` 4x3x5, `CockpitSV07` 2x2x9) are shapes of the same part. Estimate: the camera sits about 1.2 m above the seat floor, so every chair needs 2 m of open height over the seat.

### 2.2 Doors

Standard sizes are 1x2x1 and 2x2x1, plus larger. Empyrion's shutter-door family (capital grid, so mass is divided by 8 and hit points are taken relative to the 500 hp steel cube) gives the per-cell proportions:

| Size (cells) | Source | Mass kg raw | Mass kg /8 | HP raw | HP set | HP per cell (set) | Power |
|---|---|---|---|---|---|---|---|
| 1x2x1 | `ShutterDoor1x2` | 360 | 45 | 240 | 75.8 | 37.9 | 1 kW while moving |
| 2x2x1 | `ShutterDoor2x2` | 340 | 42.5 | 480 | 152 | 37.9 | 1 kW while moving |
| 1x3x1 | `ShutterDoor1x3` | 510 | 63.8 | 360 | 114 | 37.9 | 1 kW while moving |
| 3x3x1 | `ShutterDoor3x3` | 450 | 56.2 | 1,080 | 341 | 37.9 | 1 kW while moving |
| 1x4x1 | `ShutterDoor1x4` | 660 | 82.5 | 480 | 152 | 37.9 | 1 kW while moving |
| 4x4x1 | `ShutterDoor4x4` | 560 | 70 | 1,910 | 604 | 37.7 | 1 kW while moving |
| 5x5x1 | `ShutterDoor5x5` | 700 | 87.5 | 2,980 | 942 | 37.7 | 1 kW while moving |
| 5x3x1 | `HangarDoor5x3` | 8,730 | 1,090 | 2,580 | 815 | 54.4 | 1 kW while moving |
| 7x5x1 | `HangarDoor7x5` | 10,200 | 1,280 | 6,010 | 1,900 | 54.3 | 1 kW while moving |
| 10x7x1 | `HangarDoor10x7` | 11,400 | 1,430 | 12,000 | 3,800 | 54.3 | 1 kW while moving |
| 14x7x1 | `HangarDoor14x7` | 12,000 | 1,500 | 16,800 | 5,320 | 54.2 | 1 kW while moving |

Findings. Hit points grow with area (120 hp per cell raw for every shutter door from 2x2 to 5x5, so 38 hp per cell set, a quarter of a plated steel cell), while mass is almost flat for hangar doors (9,000 to 12,000 kg raw whatever the size). The plan's rule, cost and hit points scale with size, matches the hit points; set mass per cell at about 20 kg for doors up to 25 cells and 12 kg above (estimate), since raw capital mass does not follow area. The recipe shape is one for every size (machinery 55 percent, electronics 25 percent, plates 20 percent, about 29 items; hangar doors add an energy matrix), so cost per cell should follow the area rule. For comparison, Empyrion's small-ship doors are 3x3x1 (`DoorSS01`, 183 kg, 180 hp) and 2x3x1 slim (`DoorSS01Slim`, `DoorInterior01SlimSV`, 123 to 148 kg, 70 to 120 hp), and every capital door is one 1x1x1 large block (`DoorInterior01`, 200 kg, 120 hp). Neither family has a 1x2x1 or a 2x2x1; those are the plan's own and the `ShutterDoor1x2` and `ShutterDoor2x2` lines are the closest proportions. Power: 1 kW only while moving, no idle draw.

### 2.3 Everything else

Converted the same way as the ladders. A device with no `SizeInBlocks` is 1x1x1.

| GalHack part | Empyrion source (grid) | Cells | Mass kg | HP | Power working / idle kW | Other stats | Unlock | Recipe shape |
|---|---|---|---|---|---|---|---|---|
| core | `Core` (X) | 1x1x1 | 140 | 250 | 5 | one core, no tiers; Empyrion hit points 250 | 1 | 8 items: electronics 100% |
| cloning-bay | `CloneChamber` (MS) | 1x2x1 | 266 | - | 2.5 | footprint raised from Empyrion's 1x1x1: that block is a 2 m cube, and a 1 m cube would not hold a 1.8 m person; the hover version is 2x3x2 (`CloneChamberHV`, 890 kg raw, level 5) | 1 | 13 items: plates 77%, electronics 8%, advanced 15% |
| medical-bay | `MedicStationHV` (SS) | 2x2x1 | 1,040 | 158 | 80 | 2x2x1; the base version `MedicinelabMS` is 1x1x1, 13,500 kg raw, 15 kW | 7 | 8 items: machinery 50%, electronics 38%, advanced 12% |
| gravity-generator | `GravityGeneratorMS` (MS) | 2x2x2 | 2,850 | 126 | 100 | 2x2x2, field radius 75 (raw). Smaller variants: 2x1x2 radius 50 (11,200 kg raw), 1x1x1 radius 25 (3,200 kg), 1x1x1 radius 12 (960 kg) | 7 | 17 items: machinery 76%, advanced 24% |
| life-support | `OxygenStation` (MS) | 1x1x1 | 74.4 | - | 0.25 | refills the suit; level 3 in Empyrion; small-ship `OxygenStationSV` is 1x2x1, 70 kg raw | 3 | 16 items: machinery 50%, electronics 19%, advanced 31% |
| refrigerator | `FridgeMS` (MS) | 1x1x1 | 18.8 | - | 0.62 | storage 1,750 SU raw; by volume one 1 m cell holds about 219 SU, rounded to 250 (estimate); small-ship `FridgeSV` 250 SU, 62 kg raw, level 3 | 1 | 36 items: plates 56%, electronics 36%, advanced 8% |
| scanner | `DetectorCV` (MS) | 1x1x1 | 440 | 94.8 | 40 | range 6,000 m atmosphere, 24,000 m space; small-ship `DetectorSVT1` 3,500 and 14,000, 55 kg raw, 5 kW, level 5; same converted mass and power as this one | 7 | 81 items: plates 48%, electronics 47%, advanced 5% |
| teleporter | `TeleporterBA` (MS) | 1x1x1 | 400 | 111 | 1,500 | 12,000 kW raw at the moment of sending; `TeleporterBAT1` 1,200 kW and 320 kg is the cheap form; capital `TeleporterCV` 6,000 kW, 2,500 kg. Idle draw none | 15 | 152 items: machinery 13%, electronics 11%, advanced 76% |
| spotlight | `SpotlightBlocks` (X) | 1x1x1 | 85 | - | 5 | both grids; level 1 | 1 | 8 items: plates 25%, electronics 62%, glass 12% |
| ladder | `LadderBlocks` (X) | 1x1x1 | 30 | 100 | 0 | raw 30 kg per 2 m block, so 15 kg per 1 m piece (estimate) | 1 | 4 items: plates 100% |
| grav-lift | `ElevatorMS` (MS) | 2x2x1 | 40 | - | 0.5 | estimate: Empyrion `ElevatorMS` is 1x1x1 (80 kg raw, 1 kW, level 5); a 2x2x1 piece is half the volume of that 2 m block, so 40 kg and 0.5 kW | 5 | 7 items: plates 29%, electronics 71% |
| light | `LightMS01` (X) | 1x1x1 | 0.4 | - | 1 | one recipe for all lamps | 1 | 8 items: plates 25%, electronics 62%, glass 12% |

Notes. The harvester module has no Empyrion counterpart; the nearest is the saw attachment (`SawAttachment`, 5x2x1, 220 kg raw, 150 hp, 70 kW, level 3). The auto miner (`AutoMiningDeviceT1`, 3x6x3 on terrain, 1,835 kg raw, 200 hp, 80 kW, 100 items per hour, storage 1,750; T2 and T3 double each) is not on the plan's lists. Empyrion's `RepairBay`, water and O2 generators are not carried, as the plan says.

## 3. Plates and the base block

Source: `BlocksConfig.ecf` full cubes of each armour family (`HullFullLarge`, `HullArmoredFullLarge`, `HullCombatFullLarge`, `ConcreteFull`, `WoodFull`, `PlasticFullLarge`, the matching small-grid cubes, and the window blocks `WindowLargeBlocks`, `WindowArmoredLargeBlocks`), recipes from `Templates.ecf`, damage categories from `MaterialConfig.ecf` and `ItemsConfig.ecf`.

**What Empyrion has.** Hit points and mass per cube, per grid, and nothing else. There is no per-material armour value and no damage-type resistance in any config: protection is hit points, and the damage multipliers in `ItemsConfig.ecf` are by material category (`hull`, `hullarmored`, `hullcombat`, `concrete`, `woodblock`, `shield`, `rock`...) and are mostly 0.01 to 1 against terrain, creatures and shields, not tuned per plate. Armour and resistances below are therefore GalHack's own (today's values in `prototypes/building/data/materials.json`), kept, and marked as such.

**Scale.** Empyrion's large cube (2 m) and small cube (0.5 m) are counted as one 1 m cell. The two grids do not agree on hit points or mass per cube: the large steel cube is 500 hp and 1000 kg, the small one 50 hp and 8 kg. At 1 m the volume rule gives 62 hp and 125 kg from the large and 400 hp and 64 kg from the small. The proposal is the geometric mean of the two, so a 1 m steel cell is **158 hp and 89 kg** (sqrt(500 x 50) = 158 and sqrt(1000 x 8) = 89) and every other material follows the large grid's proportions to steel. That 158 is the reference used for device hit points in section 1. Per metre of thickness the large wall gives 250 hp and the small 100 hp, the 158 sits between the two.

| Plate (GalHack name) | Empyrion block | Large cube hp / kg | Small cube hp / kg | vs steel (hp, mass) | **Per 1 m cube hp / kg** | Armour (flat, GalHack today) | Resist kinetic / explosive / energy (GalHack today) | Cost per cube, raw ingredient shape |
|---|---|---|---|---|---|---|---|---|
| base-block | `PlasticFullLarge / PlasticFullSmall` | 200 / 100 | 20 / 3 | 0.40, 0.10 | **63 / 9** | 2 (estimate) | 0.05 / 0.05 / 0.05 (estimate) | 2 polymer units per large cube, 1 per small; polymer is 1 crushed stone each |
| wood-plate | `WoodFull` | 100 / 360 | - | 0.20, 0.36 | **32 / 32** | 1 (estimate) | 0 / 0 / 0 (estimate) | 1 plank per cube (50 planks per log); base-grid only in Empyrion |
| concrete-plate | `ConcreteFull` | 800 / 1100 | - | 1.60, 1.10 | **253 / 98** | 8 | 0.15 / 0.20 / 0.20 | 1 cement per cube (cement is 2 rock dust); the group parent lists 600 hp, the full cube 800; base-grid only |
| iron-plate | `HullFullLarge / HullFullSmall` | 500 / 1000 | 50 / 8 | 1.00, 1.00 | **158 / 89** | 10 | 0.10 / 0.10 / 0.10 | 10 steel plates (large) or 2 (small); plates are 2 iron ingots per 10 |
| hardened-plate | `HullArmoredFullLarge / ...SmallBlocks` | 1000 / 2000 | 200 / 32 | 2.00, 2.00 | **316 / 178** | 25 | 0.45 / 0.20 / 0.15 | 10 steel plates plus 10 titan plates (5 titan rods per 10) |
| combat-plate | `HullCombatFullLarge / ...SmallBlocks` | 2000 / 4000 | 400 / 64 | 4.00, 4.00 | **632 / 356** | 50 | 0.30 / 0.55 / 0.25 | 10 steel plates plus 10 armoured plates (5 sathium ingots per 10) |
| glass-plate | `WindowLargeBlocks` | 250 / 330 | - | 0.50, 0.33 | **79 / 29** | 2 | 0.05 / 0.05 / 0.30 | 5 steel plates plus 5 glass plates (1 silicon ingot each); armoured window `WindowArmoredLargeBlocks` is 1000 hp and 600 kg |

Notes.
- Empyrion's small grid is steeper: its armoured and combat cubes are 4x and 8x steel in both hit points and mass (200 and 400 hp), against 2x and 4x on the large grid. The proposal uses the large grid's ratios because it has the full set.
- The alien cube (`AlienFullLarge`, 2750 to 3500 hp, 2600 to 3910 kg) is a salvage material, not carried over.
- A glass plate is 0.5 of steel's hit points at one third of the mass, so a window plate is about 79 hp and 29 kg per cell.
- GalHack's current `materials.json` already uses Empyrion's large-cube numbers for steel, hardened and combat (500 hp and 1.0 t, 1000 and 2.0, 2000 and 4.0) and a glass of 250 hp and 0.6 t (Empyrion's window: 250 hp and 0.33 t). Scaling them by 0.316 in hit points and 0.089 in mass gives the 1 m column. Concrete is the one in disagreement (GalHack 400 hp and 2.4 t, Empyrion 800 hp and 1.1 t): take Empyrion's.
- Armour and resistances: Empyrion has none, so there is no source. The flat armour of 10, 25 and 50 for steel, hardened and combat is about 1/50 of the large cube hit points (2 percent), and that proportion is what to keep if the data pass re-derives armour; it does not depend on the cube size because it is a per-hit reduction.
- Estimates: base-block and wood armour and resistances (Empyrion has no figures). The base block (polymer from rock, plan section 3) is Empyrion's plastic cube: **63 hp and 9 kg per 1 m cell**, 0.4 of steel's hit points at 0.1 of its mass, for 2 polymer units, which is the cheapest and lightest cube Empyrion has.
- Craftable from the start: every plate cube is level 1 in the config. What actually gates them is the ingredient chain (titan rods, sathium ingots), which is where the plan's per-material unlock sits.

### 3.1 Cost and hit points by shape

Empyrion has one recipe per block group (`TemplateRoot`), so a half block, a wedge and a cube all cost the same in the config; hit points and mass of a shape follow the volume of its voxels (`BlockSizeScale`, `BlocksConfig.ecf` header: 'Mass and HitPoints will be scaled according to its voxels'). The shape meshes are not in the configs, so the fractions below are estimates from the shape names (geometry of the family), in whole eighths. The share of each shape in Empyrion's biggest hulls (from the earlier `.epb` study) says which fractions matter.

| Shape family (Empyrion names) | Fraction of a full cube (estimate) | Share of blocks in the 60 largest hulls | Plates and hit points |
|---|---|---|---|
| Cube | 1 | 48% | full |
| CubeHalf, Plate, thick Wall | 1/2 | 3.5% | half |
| CubeQuarter, thin Wall, WallLow | 1/4 | n/a | quarter |
| CubeEighth, Beam, Pole | 1/8 | n/a | eighth, minimum 1 plate |
| RampA, RampC, RampD, RampADouble, wedge and stair-like ramps | 1/2 | 5.4, 3.5, 1.9, 2.6% | half |
| RampCMedium, RampBMedium, RampCHalf | 1/4 to 3/8 | 4% | quarter |
| RampCLow | 1/8 | n/a | eighth |
| CornerA/B/C and CutCorner family (SlicedCorner, CornerSmall) | 1/6 to 3/4 | 1.4% and below each | 3/4 for CutCorner, 1/2 for CornerA, 1/4 to 1/6 for small corners |
| EdgeRound, RoundCorner, Cylinder, Cone, SphereHalf | 1/2 to 3/4 | 1% or less | by volume |
| Corridor pieces, Stairs | per piece | small | by volume (a stairs block with two 0.5 m steps is about 3/4) |

Rule: plates = round(fraction x the cube's plate count), minimum 1; hit points and mass = fraction x the cube's; so a half block is 79 hp and 45 kg for iron. Half blocks are 0.5 m tall (plan section 3), matching Empyrion's `CubeHalf`. The glass and plastic ones follow the same fractions.

## 4. Fuels

Source: `ItemsConfig.ecf` (`FuelValue`, `Mass`, `Volume`, recipes in `Templates.ecf`). Empyrion states the unit as Wh but its own comment in `BlocksConfig.ecf` ('oxygengenerator should run with 1 energycell (FuelValue 10kWh) => 60min') and the header ('EnergyIn ... value is given in kW') show it is kWh against kW, so one unit of fuel energy lasts one hour at 1 kW. That makes burn rate = draw in kW divided by fuel energy in kWh, in units of fuel per hour. Whether the hour is a game hour or a real one is not in the configs; treat the burn rates as estimates until a time scale is set.

### 4.1 Generator fuels

Every Empyrion generator and fuel tank accepts all of these (`FuelAccept`).

| Fuel (GalHack name) | Empyrion item | Energy per unit (kWh) | Mass per unit kg | Energy per kg | Source | Recipe proportions | Craft time s | Unlock |
|---|---|---|---|---|---|---|---|---|
| `bio-fuel` | `BioFuel` | 10 | 20 | 0.50 | plant fibre | 10 fibre -> 1 unit | 28 | 1 |
| `basic-cell` | `EnergyCellSmall` | 15 | 7 | 2.1 | steel plates and electronics, no ore | 5 plates + 2 electronics -> 2 units | 2 | 1 |
| `promethium-pack-s` | `EnergyCell` | 30 | 14.5 | 2.1 | promethium pellets (10 pellets per ore) | 10 pellets -> 2 units, so 5 pellets each | 5 | 1 |
| `promethium-pack-l` | `EnergyCellLarge` | 150 | 30 | 5.0 | promethium pellets | 50 pellets -> 2 units, so 25 pellets each | 15 | 10 |
| `hydrogen-cell` | `EnergyCellHydrogen` | 15 | 20 | 0.75 | hydrogen bottles; in Empyrion made from water jugs (5 jugs -> 10 bottles) | recipe is switched off in the config ('temporarily deactivated'); one bottle per cell is the implied proportion (estimate) | 5 | none |
| `fusion-cell` | `FusionCell` | 300 | 75 | 4.0 | promethium pellets and hydrogen bottles | 50 pellets + 10 hydrogen bottles -> 2 units | 15 | 20 |

Energy per unit of raw resource: bio fuel 1 kWh per fibre; both promethium packs 6 kWh per pellet (60 per ore, the same, only the packaging differs); fusion 12 kWh per pellet before counting the bottles. Hydrogen bottle: 17.25 kg, 10 L; promethium ore 23.7 kg, pellet 2.2 kg. Item stack size is 4000 for cells and 2000 for bio fuel; a fuel tank accepts all. Pellets and the fusion route need the large and advanced constructors (large and advanced targets in `Templates.ecf`), which is how Empyrion gates the dense fuels.

Rule for the ladder: each denser fuel is a better deal per kg (bio fuel 0.5, basic and small promethium packs 2.1, large pack 5.0 kWh per kg). Fusion is 4.0 per kg because its bottles are heavy; its edge is 12 kWh per pellet against 6. The generator tier does not change the fuel, only the burn rate. Hydrogen is the odd one: lowest energy density in the list and not craftable; the plan keeps ice 'as an ingredient in some fuels', which suits it.

### 4.2 Tanks

Capacity is the same unit as `FuelValue`: the 2,400 kWh `fuel-tank-t1` (section 1.2) holds 240 units of bio fuel, 80 of `promethium-pack-s`, 16 of `promethium-pack-l` or 8 of `fusion-cell`. Empyrion's tank accepts a mix.

### 4.3 Burn rates by tier

Estimate: burn at full output, units per hour = output kW / energy per unit. Real burn follows the load. Using the set outputs of section 1.2 (the raw conversion in brackets).

| Generator tier | Output kW (set) | bio-fuel / h | basic-cell / h | promethium-pack-s / h | promethium-pack-l / h | fusion-cell / h |
|---|---|---|---|---|---|---|
| generator-t1 | 5,600 (5,600) | 560 | 373 | 187 | 37.3 | 18.7 |
| generator-t2 | 12,300 (12,000) | 1,230 | 821 | 411 | 82.1 | 41.1 |
| generator-t3 | 20,300 (3,120) | 2,030 | 1,360 | 678 | 136 | 67.8 |
| generator-t4 | 179,000 (22,500) | 17,900 | 11,900 | 5,960 | 1,190 | 596 |

Empyrion's rule is simple: burn is proportional to output, so a bigger generator burns more per hour in direct proportion, which is the plan's 'bigger tiers burn more'. The efficiency rule (more efficient per mass and per power) is then met by output per tonne, not by burn per kWh: all fuel-to-electricity conversion is 1 to 1 in Empyrion, and the figures in 1.2 show output per tonne rising with tier. If the data pass wants higher tiers to be kinder to fuel too, a 5 percent drop in burn per kWh per tier is one choice (estimate; not in Empyrion).
Thruster draw in fuel terms (converted full draw of thruster-t1 to t5: 160, 256, 100, 558 and 1,560 kW): 16, 26, 10, 56 and 156 `bio-fuel` per hour, or 1.1, 1.7, 0.7, 3.7 and 10.4 `promethium-pack-l` per hour (estimate).

### 4.4 Warp fuel and hyperspace fuel

Empyrion has one jump fuel, the pentaxid crystal (`PentaxidCrystal`, 12.8 kg, 2.4 L, stack 50), for every jump of the warp drive: `ItemPerAU` is the crystal on every `WarpDrive`. A jump costs `CostPerAU` crystals per AU and `CostPerLY` per light year. Recipe: 1 pentaxid ore -> 2 crystals (5 s); a pentaxid element (2.2 kg) is a separate product, 1 ore -> 5 elements; pentaxid ore itself (88.9 kg per unit, level none) is made in the advanced constructor from five other ores (105 ore per craft from 5 each of promethium, erestrum, zascosium, sathium and crushed stone), or found in deposits.

| Drive (Empyrion) | Range AU / LY | Crystals per AU | per LY | Crystals for the longest jump | Tank that carries it (litres of crystals) |
|---|---|---|---|---|---|
| `WarpDriveSV` (warp-drive-t1) | 15 / 0 | 1 | 0 | 15 | `PentaxidTankSV` 200 L = 83 crystals |
| `WarpDrive` (hyperdrive-t1) | 24 / 30 | 2 | 1 | 48 in system, 30 between stars | `PentaxidTank` 600 L = 250 crystals |
| `WarpDriveT2` (hyperdrive-t2) | 48 / 60 | 3 | 1 | 144 in system, 60 between stars | the same tank |

GalHack names: `warp-fuel` (in-system jumps, per AU) and `hyperspace-fuel` (between stars, per LY). Units per jump follow Empyrion's costs one for one: **warp-fuel per AU is 1 (warp-drive-t1), 2 (hyperdrive-t1) and 3 (hyperdrive-t2)**, **hyperspace-fuel per LY is 1**. Estimate: both fuels use Empyrion's one chain (1 ore -> 2 units, 12.8 kg per unit). If hyperspace should be the dearer fuel, a factor of 3 on its recipe is a choice, not Empyrion's. A drive's micro-warp cooldown is 25 s.

## 5. Speed and turn caps by mass band

Empyrion's configs carry no vessel speed or turn caps (the only speed fields are creature and entity speeds in `EClassConfig.ecf`); they are hard-coded in the game, so nothing here is copied. What the data can give is where the mass bands fall. Mass here is estimated: each Empyrion block count read as 1 m cells (plan section 10) times the proposed 89 kg plated cell of section 3, devices not added, so it understates.

| Empyrion class | Blueprints | Cells p10 / median / p90 | Estimated tonnes p10 / median / p90 | Share above 100 t | Thrusters per 100 cells (median) |
|---|---|---|---|---|---|
| hover vessel (`HV`) | 20 | 40 / 138 / 968 | 4 / 12 / 86 | 10% | 4.0 |
| small vessel (`SV`) | 58 | 122 / 356 / 1958 | 11 / 32 / 174 | 22% | 2.6 |
| capital vessel (`CV`) | 461 | 289 / 3238 / 11208 | 26 / 288 / 998 | 72% | 1.1 |
| base (`Base`) | 1785 | 109 / 1969 / 18160 | 10 / 175 / 1616 | 60% | n/a |

Findings. The plan's 100 t line separates the classes well: 78 percent of Empyrion small vessels and 90 percent of hover vessels fall under it, 72 percent of capital vessels fall over it. The medians (32 t small, 12 t hover, 288 t capital) sit a factor of 3 to 10 away from the line on both sides. Thrust-to-weight is about equal between classes once converted: a median small vessel has 8 thrusters of `thruster-t2` (1360 each) on about 50 t, a median capital vessel 32 of `thruster-t3` (2090 each) on about 306 t, both near 0.22 thrust units per kg. So the caps below are about handling feel, not about raw acceleration.

| Mass band | Empyrion class that fills it | Top speed air / space (m/s) | Basis |
|---|---|---|---|
| under 20 t | hover and the lightest small vessels | 70 / 130, hover mode 50 / 50 | plan today (`handling.json`: small 70 / 130, hover 50 / 50) |
| 20 to 100 t | most small vessels | 70 / 130 | plan today |
| 100 to 1,000 t | capital vessels (median 288 t) | 60 / 100 | plan today |
| over 1,000 t | the top tenth of capital vessels and bases (p90 about 1,000 t) | 50 / 80 | estimate; no Empyrion basis, a gentle step down |

Turn caps (the plan's `SmallVesselMaxTurn` and the gyro-torque-over-mass rule of `handling.json`). The converted gyro ladder of section 1.2 gives torque per tonne of 391 (t1), 627 (t2) and 7,830 (t3) at their own mass; a vessel's turn rate is its total torque over its mass, so the band caps are an upper limit, not a formula. Estimate by band, deg/s: under 20 t 90, 20 to 100 t 60, 100 to 1,000 t 30, over 1,000 t 15, never below the plan's floor of 12 (`handling.json` `turning.floor`). These follow the cube root of mass loosely (each step is 10 times the mass and roughly half the cap); Empyrion gives nothing to check them against.

## 6. Interiors per craft class

Source: the `.epb` blueprints in `Z:\...\Empyrion - Galactic Survival\Content\Prefabs` read with `G:\GalHack\docs\floorplan-refs\tools-09\epb.py` (grid data only: occupied cells and block ids). Method (`tools-09\interior2.py`): per column, an air run with a block directly below and above is an interior cell; its height in blocks is the run length. A walkable cell has a run long enough for a 1.8 m person (3 blocks on the small grid, 1 on the large grid, where one block is 2 m). Runs are grouped into rooms and corridors per deck by flood fill (components of at least 8 blocks), width is the largest square that fits. Samples: 40 small vessels (`SV`, every one of the 58 that has a door or a seat and 150 to 40,000 blocks, minus the 18 with none), 9 hover vessels (`HV`, all that fit the same filter), 70 capital vessels (`CV`) and 70 bases (`Base`), picked at random from the blueprints that have a door or a seat and 150 to 40,000 blocks. Heights are weighted by area (cells in each component).

Empyrion's block edge is 0.5 m on `SV` and `HV` and 2 m on `CV` and `Base`. The plan's count rule (blocks as 1 m cells) turns a block count into the same number of metres.

### 6.1 Floor to ceiling

| Class | 1 | 2 | 3 | 4 | 5 | 6 | 7 or more (blocks, share of walkable area) | Mode (blocks) | Mode in metres in Empyrion | Mode as 1 m cells (count rule) |
|---|---|---|---|---|---|---|---|---|---|---|
| small vessel | - | - | 48% | 35% | 7% | 6% | 4% | 3 | 1.5 m | 3 |
| hover vessel | - | - | 65% | - | 35% | - | - | 3 | 1.5 m | 3 (5 for the larger decks) |
| capital vessel | 27% | 30% | 17% | 12% | 6% | 5% | 4% | 2 | 4 m | 2 |
| base | 23% | 26% | 18% | 9% | 9% | 1% | 14% | 2 | 4 m | 2 |

Against the owner's standard (3 to 4 m ceilings, 4 m preferred; at least two large blocks floor to ceiling on capital ships):

- **Capital vessels and bases**: 73 percent of capital walkable area and 77 percent of base area has two or more large blocks (4 m or more) of headroom, so Empyrion mostly meets the two-block standard physically; 27 percent (23 percent on bases) is a single 2 m block, which is below 3 m and which the owner calls too low. The mode is exactly 2 blocks, 4 m, the preferred height. **But the count rule breaks it**: counted as 1 m cells, two large blocks are 2 cells. The new blueprints have to be authored in metres (4 cells for a capital deck), not by block count, as plan section 6 already says (design from scratch).
- **Small vessels**: the modal 3 blocks is only 1.5 m in Empyrion, below the 1.8 m of a person; 35 percent have 4 blocks (2 m), 17 percent 5 or more. Counted as 1 m cells the mode is 3 cells (3 m, the bottom of the owner's range) and 4 cells for the third of cases with 2 m of headroom: that meets the standard. Empyrion's small vessels are cramped as built and the count rule fixes them.
- **Hover vessels**: 3 blocks (1.5 m) in two thirds of walkable area, 5 (2.5 m) in the rest, same fix.

### 6.2 Rooms and corridors

| Class | Walkable components | Corridor-like share | Corridor width median (blocks, metres in Empyrion, cells under count rule) | Room width median (blocks, metres, cells) | Component area p10 / median / p90 (blocks) |
|---|---|---|---|---|---|
| small vessel | 55 | 96% (width up to 3 blocks) | 2 blocks, 1 m, 2 cells | 4.5 blocks, 2.3 m, 4 to 5 cells | 8 / 13 / 28 |
| hover vessel | 5 | 100% | 2 blocks, 1 m, 2 cells | none | 12 / 17 / 35 |
| capital vessel | 1,141 | 65% (width up to 2 blocks) | 2 blocks, 4 m, 2 cells | 3 blocks, 6 m, 3 cells | 8 / 14 / 50 |
| base | 1,039 | 77% | 2 blocks, 4 m, 2 cells | 3 blocks, 6 m, 3 cells | 8 / 14 / 44 |

Findings. Capital corridors are 2 large blocks wide (4 m) and rooms 3 blocks (6 m), spacious; the count rule turns them into 2 and 3 cells, which is too narrow for a 4 m ceiling. Small-vessel corridors are 1 m physically (2 blocks) and under the count rule 2 cells. Takeaway: Empyrion's capital corridors are as wide as they are tall (2 blocks by 2 blocks, 4 m by 4 m). A 4-cell ceiling in 1 m cells needs corridors 3 to 4 cells wide to keep that proportion and rooms 6 cells wide or more. The median component is only 14 blocks (56 m2 capital, 3 m2 small), most spaces are small rooms and cabins.

### 6.3 Deck-slice plans

34 plans rendered to `G:\GalHack\docs\floorplan-refs\images\09-empyrion\` (not in git, out of the repo), 10 each for small vessel, capital vessel and base and 4 for hover vessels (all the walkable ones in the sample). One deck per blueprint: the floor level with the most walkable cells, cell colour by block id, red seats and cockpits, green doors, yellow containers, dim blue-grey for interior air, a bar for 10 m. Named `<blueprint>_deck_y<level>.png`. A summary is appended to `09-empyrion-ship-plans.md`.

| Class | Blueprint and deck |
|---|---|
| small vessel | `SVJ_SavoirWreck_deck_y19` (46x33x82); `SVJ_M-Calibur_deck_y2` (33x13x41); `SVJ_RevolutionFrontWreck_deck_y19` (50x28x73); `UCH Diplomat_deck_y3` (19x11x32); `SVJ_RevolutionBackWreck_deck_y7` (42x19x35); `SV_DestroyedTransport_deck_y3` (19x9x32); `SVJ_KrielWraith_deck_y2` (36x10x39); `SVJ_WreckedScythe_deck_y10` (60x31x47); `BA_TOPWreckageSV_deck_y2` (19x10x28); `SV_DestroyedSV-C_deck_y1` (45x27x36) |
| hover vessel | `Santas Express_deck_y3` (10x10x44); `HV_ContainerTest_deck_y3` (13x14x20); `UCH Raider Technical_deck_y1` (9x6x18); `HVsm_NomadTransport_deck_y2` (11x7x17) |
| capital vessel | `OPV_RAVSuperfreighter_deck_y11` (67x32x115); `CV_Talos-Cruiser-MKII_deck_y8` (33x27x97); `OPV_SM12YadenVessel_deck_y10` (137x25x86); `OPV_RAVLupusProelium_deck_y2` (69x18x98); `OPV_Ragnarok_deck_y8` (41x22x83); `OPV_ARCKuiXing_deck_y2` (57x14x57); `CVJ_Soul-Crusher_deck_y4` (45x22x64); `OPV_Zeus_deck_y4` (42x17x54); `OPV_UCHLightCarrier_deck_y5` (31x20x65); `OPV_RAVSkollDestroyer_deck_y4` (61x18x75) |
| base | `BAO_DerpacsStation_deck_y13` (67x43x61); `BAO_UCHFactory_deck_y44` (45x60x87); `BA_AbyssalDronebase_deck_y1` (32x35x35); `BAO_UCHGasRefinery_deck_y50` (69x66x77); `BA_TribalBurialChamberV3_deck_y7` (35x36x37); `BA_PolarisMiningStation_deck_y1` (58x29x86); `BAO_WarlordEagleClaw_deck_y8` (63x45x117); `BA_TransportationStationPlayer_deck_y2` (51x17x61); `BAO_GhystOrbitalPrison_deck_y23` (56x35x63); `Pyramid-Instance_deck_y2` (33x29x47) |

## 7. 7 Days to Die scale check

Result: **1 block = 1 m is confirmed**, from three things in the game data. Details are recorded in `G:\GalHack\docs\floorplan-refs\10-7dtd-prefabs.md`.

- **Standard door**: in `Data/Config/blocks.xml` the house, commercial and old-wood doors (`oldWoodDoor`, `exteriorHouseDoor`, `commercialGlassDoor` models) are `MultiBlockDim 1,2,1`: one block wide, two high. A double door is 2,2,1; garage doors are 3,3,1 to 5,3,1 and 3,2,2. A door two blocks high only fits a 1.8 m person if a block is about 1 m.
- **Ceiling height**: across 80 sampled houses, shops, apartments, offices and schools (27,560 interior columns, from the `.tts` prefab data) the open height between two structural blocks is 3 blocks in 31 percent, 4 in 32 percent, 2 in 19 percent, 5 in 10 percent and 6 or more in 8 percent. So rooms are mostly 3 to 4 blocks high, which is a normal 3 to 4 m room.
- **Stair step**: the stair shapes are full-block shapes in the `Stairs` category of `shapes.xml` (`stairs`, `stairs02`, `stairsHollow`, `halfRampStairs50`...); the mesh is not readable (Unity bundles), so the riser is inferred. In the prefabs, a stairs block has a stairs block diagonally above (one block up per block of run) in 197 cases against 65 for one up per two of run, so a stair climbs one block per block of run, and the shape set carries half-block parts (`cubeHalf`, `Pole0.5m`, `halfRampStairs50`). Not measured: the step height inside the stair block. A 0.5 m riser, two steps per block, is consistent with all of the above and is the usual size.
- The player collider height is not in the config (`entityclasses.xml` gives mass and weight only), so the 1.8 m person is assumed.

## 8. Where the figures disagree with the plan

1. **Capital parts: plan sections 3 and 10 disagree.** Section 3 turns today's large part into a 2x2x2 with today's stats (thruster, generator, tank, container and gyro all 8 cells). Section 10 counts Empyrion blocks as 1 m cells, which gives the large container, tank and gyro one or two cells (1x1x1, 1x2x1, 1x1x1). This document follows section 10, since the plan says its rules win. The plan's own examples then change: thruster T3 is Empyrion's 2x4x2 (16 cells) and T4 3x6x3 (54), not 2x2x2 and 2x2x3.
2. **Chairs, cockpits, clone bays and doors on the capital grid come out too small.** The count rule gives Empyrion's capital seat a 1 m cube and its capital doors 1x1x1; neither holds a 1.8 m person. The small-ship counts (cockpit 3x3x3 and larger, door 2x3x1) are the right size. Sizes in 2.1 to 2.3 override the count rule for these.
3. **The count rule halves capital interiors.** Empyrion capital decks are mostly 2 large blocks (4 m) high; counted as cells they would be 2 m, below the owner's 3 to 4 m. Interiors have to be authored in metres, never converted by count (6.1). It does make small-vessel interiors acceptable (3 to 4 cells).
4. **The efficiency rule fails in Empyrion for most families once its small and capital grids share one ladder.** Breaks, and the fix, are listed under each device: generators (capital ones 6 to 7 times less dense per cell than small-ship ones), fuel tanks (16 times), shields (10 times per cell), fabricators (power per speed rises with tier), drills, solar panels (per area), hover plates (power per lift flat), the fold tank, landing gear mass. Thrusters, containers and gyros already comply. The rule needs a 'raise capital outputs' step for generators and tanks, a bigger change than the plan states.
5. **Plan placeholders that don't match Empyrion**: shield 500 hp (Empyrion converted gives 12,600 for the smallest, about 80 steel cells); hyperdrive 2x2x4 (Empyrion 3x2x5); gyro 2x2x2 (Empyrion 1x1x1); T1 thruster about 300 kN (the converted small directional thruster is 600 in Empyrion's index units).
6. **Hull mass per cell is not unique**: the large grid gives 125 kg a cell, the small 64 kg; the proposal is 89 kg. Parts converted at x8 or x1/8 carry that 40 percent mismatch, so small craft come out about 1.4 times heavier than 'flies the same' implies and capital craft 0.7 times, unless the data pass pins hull mass to 64 or 125 instead.
7. **Empyrion has no armour or resistance value and no ride height**, so plate armour, resistances and hover ride heights are GalHack's own, unchecked.
8. **Empyrion has no capacitor, comms array, 2x2 grav lift or harvester**: the figures are estimates, marked.
9. **Speed caps are not in the configs.** Section 5 gives only the mass bands and the 100 t split; the turn caps are estimates.

## 9. Calibration against today's GalHack numbers

GalHack's units: mass in tonnes, thrust in kN (`handling.json`: thrust over mass is m/s^2), power unspecified in `components.json`. Empyrion's thrust has no unit that works against its mass in kg, so the data pass needs one constant per quantity. The rungs that today's parts best match:

| Quantity | Today's large part (GalHack) | Per tonne | Closest converted rung | Per tonne | Ratio |
|---|---|---|---|---|---|
| Thrust | 1,200 kN, 3 t (2x2x2) | 400 | thruster-t3, 2,090 (1x2x1, 0.55 t) | 3,800 | 9.5 |
| Thrust | 1,200 kN, 3 t (2x2x2) | 400 | thruster-t4, 26,200 (2x4x2, 2.8 t) | 9,300 | 23 |
| Thrust, small part x8 (plan bottom rung) | about 300 kN, 1x1x1 | - | thruster-t1, 600 | 408 | 2.0 on the output |
| Generation | 12 per part, 6 t | 2 | generator-t3, 20,300 kW (3x1x1, 1.67 t) | 12,200 | 6 if the 12 is MW |
| Torque | 5,000, 4 t | 1,250 | gyro-t2, 1,250 (1x1x1, 2.0 t) | 627 | 0.5 |
| Container | 8,000 SU, 2 t (2x2x2) | 4,000 | container-t3, 8,000 SU at 1,000 per cell, 75 kg | 13,300 | 3.3 |
| Hull | steel 1.0 t per 2 m cube, 500 hp | - | 1 m cell, 89 kg and 158 hp | - | 0.089 mass, 0.316 hp |

The ratios differ by quantity (thrust 2 to 23, torque 0.5, container 3.3), so no single constant works: pick one constant per quantity, anchored on the rung the plan already fixes (today's small part x8 for the bottom rung, today's large for the rung of the same output). Thrust is the one to settle first, since every vessel test depends on it (the Skiff and Gadfly flight checks in plan section 3, Rebalancing).
