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
6. **Variety everywhere, inside and out.** The owner (16 Nov): "ideally, we'd have a lot of procgen variety in ALL ships regardless of size or faction, inside and out." So:
   - each faction has several body plans per size, not one;
   - each roll varies its length, module count, layout and which traits appear;
   - interiors vary too: deck count and layout, how the corridors run, which rooms appear and how they're furnished.
   - No two rolls may look like copies, from outside or inside. This holds for the smallest craft and the biggest capital ship alike.
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
| Husks | Any faction's ship, hijacked. The drive section is torn open and the prow is crumpled from ramming. Flesh masses bulge out through hull breaches outside as well as inside, and its own lights flicker wrong. Worms stick out of the meat, and once the ship sets a heading to ram, they reach out toward the ship it's going for (owner, 16 Nov: "The Husk ships should have warms sticking out of the meat, which reach out to the ship they're going to ram once they set a heading") | That faction's form, wounded and overgrown, writhing |

Stations follow the same body plans scaled up: a Meridian slab arcology, an Exchange pod-ring market, a Kessari lattice spire, a Vharn hive of segments, a Freeholder raft of clamped modules, a Reaver haven welded into a hollowed asteroid.


**Samples (26 Nov):** the first forms sheet, five rolls per faction plus Husk hulls: [capital-forms-sheet.jpg](ship-forms/capital-forms-sheet.jpg). Its generator is not in git yet (see root-fix-tasks.md F13).
## Small craft

The lone Freeholder trader or scout in the start sky (`SmallCraft` in building, merged 16 Nov in batch2 c9e3a8be) is still the old box on stilts. It flies, lands, runs and burns fuel correctly, but its form doesn't follow these rules yet. The owner accepted it as it is (16 Nov): "It's fine for the starter planet trader ship, in this case it's acceptable." So it stays. Each faction's fighters and gunships still come later from that faction's own body plan, through the same body-plan code at small scale.

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

## Interiors that work

Owner, 26 Nov: every ship, of every size and faction, is laid out as a working vessel, with crew and passenger living quarters, operational areas, and storage and drive spaces, arranged the way a real crew would need them. The reference is the Traveller deck plans at https://www.ace-dog.com/Traveller/DeckPlans/DECKPLAN.HTM (free traders, scouts, liners, escorts, mercenary cruisers and troop carriers, deck by deck): for layout ideas only, with GalHack's own names and figures, as DESIGN.md asks of Traveller ideas.

- **Space by role and size, before rooms.** A hull's volume is budgeted by its role first, then fitted inside the body plan: a freighter is mostly cargo hold, a liner mostly staterooms and lounges, a warship mostly weapons, armour, magazines and crew, a carrier mostly hangar and launch bays, a miner mostly ore holds and processing. Small craft get the same rule scaled down: a cockpit, a bunk or two, a galley corner, a hold and the drive.
- **Crew from the ship.** Crew size follows hull size and role (pilots, engineers, gunners, hangar and mining crews, stewards on a liner). Every crew member and passenger has a berth, so berths are at least crew plus passengers.
- **Living quarters:** staterooms (officers apart from crew on bigger ships), washrooms, a galley and mess or common room, a medbay, and low berths (cryopods, F8's block) where the role carries cold passengers. Passenger staterooms cluster round their own lounge.
- **Operational areas:** the bridge (never a tower on a deck: rule 3), sensors, comms and the ship's computer; engineering with the power plant and drives; fire control and turrets on warships; hangars and launch bays on carriers; processing bays on miners; life support.
- **Storage and drive spaces:** cargo holds by the big doors and ramps, ship's stores, lockers and an armoury; fuel tanks; the drives where the body plan puts its drive section (a Freeholder's stern cluster, a Kessari's thrust axis, a Vharn's rear segment).
- **Faction ways of carrying cargo.** The Ledger Exchange keeps its cargo outside the hull, in the containers racked all round its core (the coloured blocks on the forms sheet; owner, 26 Nov). So an Exchange freighter's inside has little or no hold: its storage is the container racks, worked from a handling room or control station with an airlock or crawlway out to the racks, and its interior budget goes to crew, the container handling gear and a small secure hold for valuables. A container is its own thing on the rack: it can be shot off, dropped or cut free and looted. The Freeholders do the same (owner, 26 Nov): the containers and tanks clamped along their spine are their cargo and fuel, outside the hull, so a Freeholder freighter's inside is its habitat cans, command pod and drive cluster, joined by a passage along the spine, with the clamped containers reached from that passage or by suit. Being salvaged and mismatched, theirs come in odd sizes and can be lost the same way.
- **The same parts as a player ship** (owner, 26 Nov). Every ship is built from the player's own blocks and devices, placed where they work, so a ship that is boarded, claimed or salvaged runs as it is: a core and command chair or cockpit at the bridge; generators and fuel tanks enough for what it draws (power draw is the only limit: DESIGN.md, no CPU); thrusters on every axis it needs, with enough thrust for its mass, plus landing gear on ships that land; life support and oxygen; constructors and stores by role; turrets and fixed weapons with clear firing arcs that don't fire through the ship or each other; and shield generators (shared/Items/data/blocks.json `shield-generator`) whose ovaloid bubbles cover the hull between them without touching: bubbles that touch fry every generator involved (owner, 12 Nov; DESIGN.md, shields), so a big ship's generators are spaced along it, any part left outside the bubbles is unprotected, and a ship's bubbles are kept clear of what it docks or parks beside. Device figures are the player's (blocks.json, power.json), never special NPC values.
- **Getting about:** corridors link every space; airlocks at every hatch; ladders or lifts between decks; and a crew can reach every drive, tank and plant room for repair (one of the reference plans, as first published, had no access hatch to its drives).
- **Starting point:** the hull builder's room fitting today (`HullVessel.Rooms.cs`, `HullVessel.Fit.cs`: bays, corridors, purposes such as `engine-room`), which already fits rooms inside a hull.

## Gates

1. **Forms.** Before any interior, livery or test work, build only the six outer forms plus a Husk one, five rolls each, greyed out, at a 3/4 view from a little above. Put them on one contact sheet. The lead checks it, then the owner sees it.
2. **Crashes.** One contact sheet of 12 crash sites from fresh seeds. No two may share pose, faction and breakup.
3. **Checks.** The silhouette check counts as proof only alongside the sheets, never instead of them. It weighs outlines without the hull under test and with livery off, and needs 30 of 30 on two fresh runs.
4. **Interiors.** A deck-plan sheet for one roll of each faction and role, top-down per deck with rooms labelled, and a check over 50 fresh rolls: every space the role needs is present, every space is reachable from an airlock, berths cover crew and passengers, and every drive, tank and plant room has crew access. The same check also flies each roll on its own thrusters under the player's flight model, balances its power (generators cover full draw), and finds no turret arc blocked by its own hull and no two shields' coverage overlapping.
