# Ship forms: big ships, stations and crashes

This is the lead's spec, written after the owner rejected the first faction styles on 16 Nov 2026. The owner said: "these ship styles in the screenshots are fucking awful! They're the SAME FUCKING SHIP WITH BITS GLUED ON". Also: "THE CRASHES ALL LOOK THE SAME", "THEY DON'T LOOK LIKE A SPACE SHIP, THEY LOOK LIKE A SEA SHIP" and "the laziest fucking styling possible".

DESIGN.md section 5 had already set the direction. The first try ignored it:
- 1970s sci-fi paperback art, after Chris Foss;
- huge, chunky, asymmetric hulls with heavy industrial detail and oversized engines;
- bold livery;
- the more alien the builder, the weirder the ship.

## What went wrong

- **One shape for everyone.** Every faction was the same long box: a flat bottom, a chamfered bow and a little tower on top at the back. That is a sea ship: a hull on a waterline with a bridge on its deck.
- **The hull came from the rooms.** It was built as a box round its rooms, and a "style" only added parts and paint.
- **Crashes were the same too.** Every crash was the same hull lying the same way.

## Rules for every big ship

1. **Shape first.** Each faction has its own body plan, and the body plan makes the outer form before any room exists. Rooms, corridors and stairs are then fitted inside that form. The form never grows to fit the rooms.
2. **A spaceship, not a sea ship:**
   - Ships are built round a thrust axis, with the drives at the stern and mass all round the axis: above, below and to the sides.
   - There is no flat keel, no deck with a tower on it, and no waterline.
   - A ship rolled 90 degrees still reads as a ship: there is no obvious "up".
3. **The bridge is never a tower on a deck.** It sits in the nose, in a blister above or below, in a sponson, or buried deep inside.
4. **The machinery shows.** Oversized drive clusters, propellant tanks (spheres and cylinders), radiator panels, open trusses, masts and dishes, docking collars, hangar mouths, cargo racks, armour slabs, and turrets on several faces. All of it is made from our own blocks and components in the right amounts, and it flies by the same rules as the player's ships.
5. **Asymmetry, Foss style,** for every faction except the Kessari, who are perfectly symmetric.
6. **Variety within a faction.** Rolls of one faction share its body plan and traits but differ in length, module count, layout and which traits appear. No two rolls should look like copies.
7. **Livery comes last.** With livery greyed out, every faction must still read by its form alone, at long range, from any side.

## Body plans by faction

| Faction | Body plan | Reads at range as |
| --- | --- | --- |
| Freeholders | A long open truss spine with mismatched modules clamped on at odd angles and in odd sizes: habitat cans, containers and tanks. A cluster of mismatched salvaged drives of different sizes at the stern, and a small command pod on the spine's nose. Lumpy and lopsided | A thin spine carrying irregular lumps |
| Meridian Combine | Clean, massive slabs. A long, thick spine (square or hex section), an armoured prow block like a hammerhead, and a huge engine block at the stern with 4 to 8 big bells. Sponsons or short wings amidships carry hangars. Few greebles, big flat faces | A hammer or dumbbell: a heavy prow, a long spine and a heavier engine block |
| Ledger Exchange | A short, fat round core inside a cradle frame. Rings of container pods all round it (above, below and on the sides), a bulbous forward hab sphere and round tanks. Wide and round | A fat seed pod bristling with blocks |
| Carrion Reavers | Short, stubby and heavy-nosed. A huge ram prow made of wedges of overlapping armour plates. Often two halves of different ships welded together, so the parts are mismatched in size and colour. Trophy hull pieces from other factions hang on it, spikes stick out, and oversized drives are bolted on unevenly | A wedge-nosed brute with a jagged outline |
| Kessari Clans | Machine builders. Perfect 4-fold or 6-fold radial symmetry round the thrust axis. An open lattice frame of repeated identical modules with bare machinery, rings, and rows of glowing status lights | A see-through symmetric lattice |
| Vharn Dominion | Alien. A chain of 3 to 6 bulbous segments that taper like a thorax and abdomen, joined by narrow waists. Long curved blades and fins sweep back asymmetrically, and the prow is a needle or mandible-like forks. Dark carapace with glowing markings | A wasp or insect |
| Husks | Any faction's ship, hijacked. The drive section is torn open and the prow is crumpled from ramming. Flesh masses bulge out through hull breaches outside as well as inside, and its own lights flicker wrong | That faction's form, wounded and overgrown |

Stations follow the same body plans scaled up: a Meridian slab arcology, an Exchange pod-ring market, a Kessari lattice spire, a Vharn hive of segments, a Freeholder raft of clamped modules, a Reaver haven welded into a hollowed asteroid.

## Small craft

The lone Freeholder trader or scout in the start sky (`SmallCraft` in building, merged 16 Nov in batch2 c9e3a8be) is still the old box on stilts. It flies, lands, runs and burns fuel correctly, but its form doesn't follow these rules yet. It gets the same treatment after the big ships' form gate, through the same body-plan code at small scale:
- **Trader:** a cockpit pod on a short spine, with clamped containers and stubby engine pods.
- **Scout:** slim, long and needle-nosed, with a sensor mast and outrigger engines.

Each faction's fighters and gunships follow later, from that faction's own body plan.

## Crashes

The faction comes from the region: the world's place on the journey to the core and the factions present there (see the "journey to the core" section of DESIGN.md). Every crash rolls each of these:

- **How it came down:**
  - a belly slide, with a long gouged trench and spoil banks;
  - nose first, with the stern up in the air;
  - broken into two or three sections strung along its path;
  - on its side;
  - upside down;
  - half sunk in water, sand, snow or marsh;
  - buried with only the stern showing.
- **What broke:** a crumpled prow; torn sections showing their decks; drives and pods thrown clear into a debris field; scorch on the ground. The terrain itself is cut to match: a crater, a trench or a burn.
- **Age:** fresh (smoke, small fires, bright metal) or old (rust, sand drifts, plants growing through, collapsed sections).

## Gates

1. **Forms.** Before any interior, livery or test work, build only the six outer forms plus a Husk one, five rolls each, greyed out, at a 3/4 view from a little above. Put them on one contact sheet. The lead checks it, then the owner sees it.
2. **Crashes.** One contact sheet of 12 crash sites from fresh seeds. No two may share pose, faction and breakup.
3. **Checks.** The silhouette check counts as proof only alongside the sheets, never instead of them. It weighs outlines without the hull under test and with livery off, and needs 30 of 30 on two fresh runs.
