# Solar systems

How GalHack should generate star systems that look right and fly well. The target is believable and readable, not a simulation: compact systems, full of places worth going to, quick to cross. Real rules supply the logic and the flavour, and they get bent freely for play. Every bend is listed in section 4.

The Sun, Earth and other solar-system names appear here only as scientific reference. None of them is game content. "AU" on a GalHack chart means the chart unit defined in section 6.

Marks in section 2: **[M]** measured from files, **[R]** read on a page, **[F]** from memory.

---

## 1. Summary and recommendations

1. **One number places everything.** Divide a body's real orbit by the square root of its star's brightness to get its *h*, or habitable distance: h = a / √L. Sunlight is then just S = 1 / h². Both the habitable zone and the frost line scale with √L, so in h they sit in the same place round every star: the temperate band at about 0.95 to 1.7, the frost line at 2.7. One layout template serves every star, and one zone table turns position into world type.
2. **Position first, type second.** A generator picks the star, lays out orbits, then reads each world's type off its zone. Today the code runs the other way: `SkySystem.PickStar` chooses a star to suit a planet that already exists. That has to flip.
3. **Do all the science in h, then squeeze once at the end.** Chart distance = spread × h^0.7. The squeeze keeps the order of the bodies and the evenness of their spacing, and it keeps angles exact. A sunlike system ends up about 4.4 chart AU to the last planet, against 30 AU for the real outer planet.
4. **Keep systems small.** Aim for 4 to 7 planets, 9 to 13 worlds on average, 1 to 2 belts, and warps of about 20 seconds. In a throwaway check of this recipe, the typical warp took 14 to 24 seconds and the worst one seen took 82.
5. **Use one scale for body sizes and one for distances.** Bodies stay at the true ratio set by the 2.5 km start world (1:2,500), so a gas giant is about 28 km in radius. Moons sit 12 to 40 radii out on rock worlds and 3 to 30 radii out on giants. One chart AU is 4,000 km. With those numbers the sky can use plain true geometry, and it comes out looking good: the moon sizes it gives match what `SkySystem` already draws.
6. **One timetable for the whole system.** Every body moves on rails from a shared clock, with no gravity simulation. Every zone asks the same function where things are. That's what makes positions right from the flat ground, through the climb, out to space: the flat map already converts to a place on a globe (`GlobeMap`), so the ground's sky is simply the sky from that place on that globe.
7. **Belts: sparse in truth, dense where you stop.** A belt is a wide, nearly empty band on the chart, as in reality. Inside it sit 3 to 5 *fields*, each a tight cluster of rocks that reads as a freshly broken parent body. You warp to a field, not to the belt. Trojan swarms give a second, easy-to-find kind of field, and an ice belt past the last giant gives a third.

---

## 2. What the games do

### Frontier: Elite II and its open-source descendant

- [R] Frontier was the first game with procedurally generated star systems, built by gathering the mass in a young system into planets and moons that obey physics, with a little randomness. I found no primary description of its algorithm. Fan tools that reproduce its data exist, but I did not read them.
- [F] It kept real sizes and distances, with true Newtonian flight and a time-acceleration control to make the distances bearable.
- [R] Pioneer is the open-source game built in Frontier's spirit, and its generator is public. I read a fetched summary of the source, for its approach only. It gives a parent a disc whose mass is a share of the parent's, walks outward placing each body a multiple beyond the last one's far point, takes each body's mass from the slice of disc it sweeps, and decides the body's type from mass. Temperature only decides what its volatiles do. Moons use the same routine at a smaller scale, capped by the parent's Hill radius. Its source is GPL, so it is a reference only.

Good for us: walking outward by a multiple is simple and always gives believable spacing, and the same routine can make moons. Bad for us: real scale needs time acceleration, and type-from-mass gives no direct control over the habitable worlds that GalHack is built around.

### Elite Dangerous and Stellar Forge

- [R] A disc round a young star holds density and temperature data, with lighter material pushed further out. Simulated epochs run over it, stable orbits are found for what clumps together, passing comets and rogue bodies disturb things, and planets are classified at the end from what they have become. Co-orbiting and binary planets can result. The whole of this runs in a fraction of a second per system.
- [R] Galaxy-level mass and age functions decide what a region can hold, catalogue stars seed the real neighbourhood, and the disc is set by what's left after making the star.
- [R] The game is 1:1 in scale. One outpost, about 0.22 light years from its arrival star, usually takes around an hour and a half in a straight flight, and the trip became a community joke and a rite of passage.
- [R] Belts are thinly spread clusters of rock round a star, and rings are a separate thing round planets. Community guides describe four ring types tied to minerals, and persistent mining hotspots inside rings. Players report belts as poorer than rings because they hold few rocks. These come from community sources and wiki excerpts, because the wiki page itself refused my fetch.
- [R] Unofficial forum explanation: bodies sit in nested two-body orbits round a fixed root point, not a gravity simulation.

Good for us: the order of work. Stars first, orbits second, classification last. Also good: rings as a mining place with named hotspots, and rails instead of gravity. Bad for us: 1:1 distance is the opposite of what GalHack wants, and a full formation simulation is far more than a readable game needs. As the owner says, we're much more gamey than that. Take the logic, leave the scale.

### Empyrion

All of this comes from the config files in the local install, read-only, except where marked.

- [M] Each star class has five ordered distance bands: inner, hot, temperate, cold and outer. Planet types attach to bands, so a world's type depends on its band.
- [M] Counts are rolled per category (gas giants, scorched, temperate, ice and so on), and each category is then placed inside its band. Nothing checks spacing between neighbours.
- [M] The bands grow with the star, but far more gently than in reality. Taking the temperate band's middle against a G star: M is 0.53 times, K 0.72, F 1.32, A 1.74 and O about 2.8. The true √L ratios for the luminosities the files list would be about 0.045, 0.55, 2.1, 5 and 700. So Empyrion's distance grows about as L^0.12 between M and A, not L^0.5. A red dwarf's temperate world sits about half as far out as a yellow star's, not a twentieth. This is the clearest precedent for squeezing.
- [M] Asteroid fields and stations are separate sites placed in bands, in several flavours (few, rich, junkyard, pirate), and in the fallback config close to half of the field rolls come up empty. Each field is its own playfield, and a rich field's rocks are rolled in low counts per ore type, so tens of minable rocks at most.
- [M] Gas giants roll several moons, and rocky planets mostly roll none. A config comment warns that giant moons add generation time.
- [M] Systems are squashed nearly flat by a setting, like a cookie in the file's own words. A second star is decoration only. A config note says it treats a light year as 10,000 AU, not the true 63,000 or so, for readability. It also says 1 AU is 10 sectors, and in one sample scenario a moon sits one sector from its giant.
- [M] The band numbers are labelled AU, but they may act as tenths of an AU [R: a modder's forum note]. I could not settle this without running the game, and it doesn't change the ratios above.
- [R] A 2019 changelog says the developers made systems physically larger and flatter, and spread planets out, to make navigation easier. I could not find an official wiki page on any of this, so the files are the source.
- [F] Planets stay put, and what you see in the sky is largely set dressing rather than the system's true layout.

Good for us: bands are simple, readable and easy to tune, and they reward learning. Empty rolls make finds feel earned. Bad for us: no spacing rule, static bodies, and a sky that doesn't reflect the system. That last one is exactly the owner's complaint to solve.

### Others, briefly

- **Space Engine.** [R] Classes all bodies by time-averaged distance from their stars, with separate size, composition and volatile labels. So a world's name states where it sits and what it's made of. It also once had to fix a generator habit of alternating giant, rock, giant, rock, which is a warning to check layouts for patterns.
- **Kerbal Space Program.** [R] Roughly 1/10 scale, with planets shrunk further than the star and moons closer than true, chosen to cut orbit times and rocket size. The reasoning comes from wikis and forums; I found no official statement. It proves that a compressed system with honest orbits still teaches real intuition.
- **Outer Wilds.** [R] Critics describe tiny, handcrafted planets in an intimate system, [F] crossed in minutes. No developer source found. It proves that small can feel like a whole solar system, if everything is visible from everywhere.
- **Star Citizen.** [R] One dense belt acts as a barrier to fast travel in some builds. [F] EVE Online treats belts as named warp points. Both treat a belt as a place on the map, which is the right instinct. The barrier is not for us.

---

## 3. The science in short

**Stars.** A star's class sets its temperature, colour, size and brightness. Share of real main-sequence stars: M 76%, K 12%, G 7.6%, F 3%, A 0.6%, B 0.12%, O almost none. Brightness climbs steeply with mass: L ≈ M⁴ for mid-sized stars, L ≈ 1.4 M^3.5 for heavy ones, L ≈ 0.23 M^2.3 for the lightest.

**Sunlight and temperature.** Light falls with distance squared: S = L / a², in units of what the real Earth gets. An airless dark body's temperature is about 278 K / √h. Air adds to that.

**Habitable zone.** Published limits come from climate models. As a flux, the conservative zone is S = 1.107 (inner, runaway greenhouse) down to 0.356 (outer, maximum greenhouse). The wider, optimistic zone is 1.776 down to 0.32. For a G star that gives 0.95 to 1.68 AU, or 0.75 to 1.77 AU. Exact form, for star temperatures of 2,600 to 7,200 K, with T = Teff - 5780:

    S_eff = S0 + a·T + b·T² + c·T³ + d·T⁴
    distance in AU = √(L / S_eff)

| Limit | S0 | a | b | c | d |
| --- | --- | --- | --- | --- | --- |
| Runaway greenhouse (inner) | 1.107 | 1.332e-4 | 1.580e-8 | -8.308e-12 | -1.931e-15 |
| Maximum greenhouse (outer) | 0.356 | 6.171e-5 | 1.698e-9 | -3.198e-12 | -5.575e-16 |
| Recent Venus (wide inner) | 1.776 | 2.136e-4 | 2.533e-8 | -1.332e-11 | -3.097e-15 |
| Early Mars (wide outer) | 0.320 | 5.547e-5 | 1.526e-9 | -2.874e-12 | -5.011e-16 |

The polynomial only nudges things: cooler stars' zones sit a little further out in h (about 1.04 to 2.0 for a red dwarf). GalHack can ignore it and use the constant h bands in section 4. Include it only if someone wants the extra correctness.

**Frost line.** The distance beyond which water freezes out of the young disc. A classic model puts it at 2.7 AU for the Sun at 170 K, and since disc temperature falls as √a and rises as L^¼, it scales as 2.7 × √L. Others place it nearer 3 to 5 AU, and it moves as the disc ages. Rule: rock inside, ice outside.

**Where things form.** Rocky planets inside the frost line. Gas giants just past it, where solids are plentiful: around sunlike stars, their numbers peak right there. Ice giants and ice worlds further out. Giants are about 4 times rarer round M dwarfs, and rarest round the smallest of them.

**Spacing.** No law sets it, and the famous Titius-Bode rule is an empirical pattern with no theory behind it, failing at the last planet. What's real is that neighbours tend to keep a steady ratio. Real multi-planet systems typically have a ratio of about 1.5 in orbit size, and sit 10 to 30 mutual Hill radii apart. The real solar system runs from about 1.4 to 2, with one 3.4 step across the asteroid belt. Rule: multiply by about 1.5 to 2 each step.

**Resonances.** Periods in simple ratios. They clear gaps and pile things up, and they're easy to compute: P² ∝ a³, so a body in p:q resonance with a planet at a sits at a × (q/p)^(2/3).

| Resonance | Inside a planet | Outside a planet |
| --- | --- | --- |
| 4:1 | 0.397 a | 2.52 a |
| 3:1 | 0.481 a | 2.08 a |
| 5:2 | 0.543 a | 1.84 a |
| 2:1 | 0.630 a | 1.59 a |
| 3:2 | 0.763 a | 1.31 a |

**Hot giants.** Gas giants on orbits of days. They form past the frost line and migrate in, and such migration scatters the inner disc outward. Rare overall ([F] about 1% of sunlike stars), and very rare round red dwarfs. They are tidally locked.

**Tidal locking.** A close world's spin slows until one face always points at its star or parent. Lower-mass stars lock their habitable-zone planets more strongly: below 0.15 solar masses it's very likely, and it's possible in the habitable zones of most G, K and M stars. Nearly all large close moons are locked to their parents.

**Moons.** A moon must stay inside the parent's Hill sphere, R_H = a × (m / 3M)^⅓, and in practice inside about a third to a half of it ([F] rule of thumb). Giants' big moons together weigh about 10⁻⁴ of the planet, so a Jupiter-mass giant's largest moons are Moon-sized to Mars-sized. Rocky worlds can have far bigger moons relative to themselves. The Galilean moons show a neat pattern: inner moons are heated by tides, the next is cracked ice over water, and the outer ones are cold. They sit at about 6, 9, 15 and 26 parent radii, and the inner three are locked in a 1:2:4 chain, a spacing ratio of 1.59. Moon periods depend only on distance in parent radii and on density, so they survive shrinking the whole system.

**Habitable moons.** A moon of a giant gets the star's light, the giant's reflected light and heat, and tidal heating. Close in, those can tip it into a runaway greenhouse, which sets a minimum distance. One study uses 10 parent radii as its worked example. A giant can only host one in the temperate band if it migrated there.

**Binary stars.** Very common: about 44% of sunlike systems, and about 75% of O stars. Planets can circle both stars of a close pair, staying outside about 2.4 separations for equal stars on a circular orbit. Or they can circle one star of a wide pair, inside about 0.27 separations. The full fits are in Holman and Wiegert (1999), listed in section 7 of this guide. Combined brightness sets the zone for a close pair.

**Rings.** Close in, tides stop rubble forming a moon. That limit is about 2.44 parent radii for a fluid body of equal density, and nearly all rings sit inside it. Saturn's main rings run from about 1.1 to 2.3 planet radii. They're almost pure water ice, mostly in pieces under 10 m, only about 10 m thick locally, and possibly young. A moon's resonance cuts sharp gaps, and small moons shepherd narrow rings. Rings lie in the planet's equatorial plane.

---

## 4. A generation recipe for GalHack

All of this is one seeded function of the system seed, run when the system is first visited, as the design already has it. Do steps 1 to 10 in h. Step 11 converts for the game.

### Step 1: pick the star

The galaxy region weighs the classes first, as in the design. These are the base weights, flattened from reality so the map stays varied.

| Star | Brightness L | Radius R | Colour | Weight | Outer slots | Giant chance | Life limit |
| --- | --- | --- | --- | --- | --- | --- | --- |
| M red dwarf | 0.03 | 0.40 | orange-red | 28 | 1 to 3 | 45% | low to mid; flares |
| K orange | 0.25 | 0.72 | pale orange | 25 | 1 to 3 | 80% | high |
| G yellow | 1 | 1 | yellow-white | 18 | 1 to 3 | 90% | high |
| F white | 3 | 1.35 | white | 12 | 1 to 2 | 90% | mid |
| A blue-white | 16 | 1.7 | blue-white | 6 | 1 to 2 | 80% | low: too young |
| B blue | 500 | 3.5 | blue | 2 | 1 | 60% | none |
| Special (below) | varies | varies | varies | 9 | 1 | low | varies |

Vary L by about ×0.6 to ×1.6 within a class. Start systems are always G or K. Rare objects such as neutron stars, black holes and supergiants are placed by region or as a system's notable feature, not rolled here.

**Special stars use one trick: lay the system out using the star's old brightness, then read zones from today's.**

- **Red giant:** layout from its old L, zones from L of 100 to 1,000. Inner slots are gone, former cold worlds are scorched, and former ice worlds in the new temperate band are thawing. A thaw world is a lovely find.
- **White dwarf:** layout from its old L, zones from L of about 0.005, and the first few slots are gone. Everything left is dead or frozen, with a tight debris ring near the star.
- **Young star:** normal layout, but Z1 to Z4 give lava, nascent and volcanic worlds, and it gets a debris disc in place of the rock belt.
- **Brown dwarf:** L of about 0.0008, nearly always no giant. Dim and cold, as the design has it.
- **Neutron star or black hole:** no zones. All worlds count as Z8, with debris and crystal.

### Step 2: the zones

h = a / √L and S = 1 / h². These are the zones, and every star uses the same table. For a close binary, use L1 + L2.

| Zone | h | Sunlight S | Airless temp. | What it is |
| --- | --- | --- | --- | --- |
| Z1 Furnace | under 0.4 | over 6 | over 170 °C | Stripped rock and lava |
| Z2 Hot | 0.4 to 0.75 | 1.8 to 6 | 50 to 170 °C | Deserts, toxic air, burnt plains |
| Z3 Warm | 0.75 to 0.95 | 1.1 to 1.8 | 10 to 50 °C | Hot edge of habitable: jungle, canyon, swamp |
| Z4 Temperate | 0.95 to 1.35 | 0.55 to 1.1 | -35 to 10 °C | The heart of the goldilocks band |
| Z5 Cool | 1.35 to 1.7 | 0.35 to 0.55 | -60 to -35 °C | Cold edge of habitable: tundra, steppe |
| Z6 Cold | 1.7 to 2.7 | 0.14 to 0.35 | -105 to -60 °C | Ice planets, the rock belt |
| Z7 Ice | 2.7 to 10 | 0.01 to 0.14 | -185 to -105 °C | Giants and their moons |
| Z8 Deep | over 10 | under 0.01 | below -185 °C | Frozen gas worlds, the ice belt |

Air warms a world by 30 °C or more, which is why Z4 is comfortable. Use S directly for surface heat, solar panel output, and orbit temperature and radiation, so a world's climate follows from its orbit in the same sum.

### Step 3: the inner chain

- Start at h = 0.28 to 0.42.
- Multiply by 1.45 to 1.95 for each next slot, until h passes 2.4.
- In 70% of systems, and always in a start system, scale the whole chain so its nearest slot lands on h = 1.0 to 1.3. The rest take their chances. Overall, about 75 to 90% of systems end up with a rock slot in Z4. What keeps a true goldilocks world special is the star's life limit and the rarity of the green types, not the slot.
- Drop any slot under 0.2 or over 3.

### Step 4: the outer chain

- First outer slot at h = 3.4 to 5.5. It's pushed this far so the rock belt in step 7 misses the temperate band.
- Add the star's outer slots in all, multiplying by 1.5 to 2.2 each, and stop by h = 18.

### Step 5: what fills each slot

- Everything under h = 2.7 is rock.
- The first outer slot is a giant on the star's giant chance, always in a start system. Later outer slots are giants 55% of the time if a giant already exists. Otherwise they're ice worlds.
- **Warm giant:** in 20% of G, K and F systems, the first rock slot between h 1.0 and 1.6 becomes a giant that migrated in. This is what makes temperate gas-giant moons possible.
- Giants split about evenly into banded gas giants and blue ice giants, with ice giants more likely further out. Radius is 8 to 11 times a start world's, so 20 to 28 km.
- Rock sizes use the four classes from world-themes.md. Moons are S or M only.

### Step 6: moons

| Parent | How many | Where | Notes |
| --- | --- | --- | --- |
| Giant | 2 to 5 | First at 3 to 4 parent radii, then ×1.4 to ×1.9 each, up to 30 | Close moons get tides and radiation |
| Rock in Z3 and beyond, ice world | 0, 0, 1, 1 or 2 | 12 to 40 parent radii | Size S, or M if the parent is L or larger |
| Rock in Z2 | 0 usually, 1 in 25% | As above | |
| Rock in Z1 | None | | |

Moon rules for giants, by distance in parent radii:

- Under 6: volcanic or lava from tides. Radiation belt.
- 6 to 12: warmed ice, geysers, cracked plains, hot springs. Radiation belt up to 10.
- Over 12: the zone's normal moon type.
- A warm giant's moons at 8 to 20 radii can use any temperate type. A start moon sits at 6 to 10 radii, so the giant fills 11° to 19° of sky, then sits at a fixed place overhead.
- Moons within 10 radii are locked: their day is their month, and the giant hangs still in the sky. A day on a start moon then runs about 40 to 90 real minutes, against 18 to 32 on a planet, so survival pacing there needs a look.

Moon periods are Kepler's own, and they don't depend on scale: 3.8 days × (d / 16 R)^1.5 for rock parents, and 7.6 days × (d / 16 R)^1.5 for giants, in game days.

### Step 7: belts and rings

- **Rock belt:** whenever there's a giant, from 0.40 to 0.63 times the first outer giant's orbit, between its 4:1 and 2:1 resonances. Delete any rock slot in that band unless it's inside h 0.95 to 1.7.
- **Ice belt:** in 60% of systems, from 1.31 to 1.59 times the outermost slot, between its 3:2 and 2:1 resonances.
- **Trojans:** in half of systems with a giant, two swarms at ±60° along the first giant's orbit. If a warm giant exists, it can take them instead.
- **Giant rings:** 45% of giants, from 1.25 to 2.3 radii. Cut one gap at 0.63 times the innermost moon's orbit. No moon inside the ring's outer edge.
- **Rock rings:** only as the ringed-world type on M or larger worlds, from 1.7 to 2.4 radii. An S moon's orbit shell starts at 1.83 radii, so a ring doesn't fit.
- Rings lie in the parent's equator plane, so they're set by its axial tilt, not rolled by themselves.

Section 5 covers what's inside belts.

### Step 8: the guarantees

Add, never reroll, as the design already says for resources.

- At least 8 worlds: while short, add a moon to a random body at h 0.75 or beyond.
- **Start system:** a temperate world of size M in Z4, or a start moon on a warm giant. A cold neighbour in reach (a Z5 or Z6 world, or an ice moon, added if missing). An asteroid field, which any belt or Trojan swarm provides. A trading station, best placed at the start world's own 60° point or in its orbit.

### Step 9: types and exceptions

Roll each world's type from the zone table below, weighted by the rarity in world-themes.md and gated by the star's life limit. Then roll one or two modifiers as the design has it, and spin:

- **Axial tilt:** 5° to 30° usually, rarely 60° to 98°. 20° or more allows the autumn-tilt type.
- **Locking:** red dwarf 60% in Z1 to Z5, K 15% in Z1 to Z3, G and F 30% in Z1 only.
- **Orbit plane:** planets within 3° of the system plane, moons within 5° of their parent's equator. So bodies rise along one narrow band of sky, and eclipses happen.

Then pick the system's notable feature, which the design says every system has. Use at most one of these system-level exceptions:

| Exception | Chance | What it does |
| --- | --- | --- |
| Hot giant | 5% | A giant at h 0.05 to 0.15. Rocks inside h 1 are thinned to 1 or 2 |
| Close chain | 8% of red dwarfs | 6 or 7 rock worlds, ratio 1.3 to 1.5, in period ratios near 3:2. No giants |
| Wanderer | 4% | One world on a stretched orbit crossing two zones, with extreme seasons |
| Double planet | 3% | Two near-equal worlds round a shared point, each huge in the other's sky |
| Shattered world | 3% | A broken planet in a slot, with a debris ring along its orbit |
| Co-orbital pair | 1% | A world at another world's 60° point |
| Visitor | 1% | A rogue body crossing the system. Pairs with the rogue body pass apocalypse |

Per-body exceptions, independent of the above: a captured backwards moon on 10% of giants, and 1 to 2 comets per system on long ellipses, with tails pointing away from the star.

**Binaries.** 18% of systems. Two simple kinds only:

- **Close pair (a third):** the two stars 0.1 to 0.3 AU apart in true distance, circling each other. All slots stay beyond 2.4 separations. You get double shadows and double sunsets. Use L1 + L2.
- **Wide pair (two thirds):** the companion is a very bright star far outside the chart, with no worlds of its own, and it moves on rails. This matches the cheap Empyrion approach, but it's a real light with a true bearing, not decoration.

**History can overrule climate, and that's the fun.** An apocalypse may change what a position would give: a Z4 world in a long winter, a drowned world, a world terraformed out of its zone. Most worlds follow their type and a few break it, as the design already says. A survey should show the mismatch, so a player notices that a world is the wrong colour for its orbit. Near the core, the Hollow can make the sky lie: the wrong-stars theme adds or shifts sky bodies only on that world, while the timetable stays true underneath.

### Step 10: fill the space

- Each world's orbit contents (ice rings, debris, stations, nebula fog) roll as the design already has them.
- Stations sit in a world's orbit or at its 60° points.
- A wormhole mouth and other deep-space sites sit in the wide gaps between orbits, or 1.2 to 1.6 times past the last one.

### Each of the 40 world types by position

Where means the zones it rolls in. Size classes, rarity and start flags stay as in world-themes.md.

| # | Type | Where | Needs |
| --- | --- | --- | --- |
| 1 | Meadow | Z4 | |
| 2 | Rainforest | Z3, Z4 | |
| 3 | Swamp | Z3, Z4 | |
| 4 | Steppe | Z4, Z5 | |
| 5 | Archipelago | Z4 | |
| 6 | Autumn-tilt | Z4, Z5 | Axial tilt of 20° or more |
| 7 | Dune | Z2, Z3 | |
| 8 | Canyon | Z2, Z3 | |
| 9 | Salt flat | Z2, Z3 | A sea that dried out |
| 10 | Scorched | Z1, Z2 | |
| 11 | Glacier | Z5, Z6 | |
| 12 | Tundra | Z5 | |
| 13 | Hot-spring ice | Z5, Z6 | Or a giant's moon at 6 to 12 radii |
| 14 | Deep-freeze | Z7, Z8 | |
| 15 | Ocean | Z4, Z5 | Size L or larger |
| 16 | Drowned | Z4 | A flood apocalypse |
| 17 | Tide | Z4, Z5 | A moon within 16 radii, or a locked moon's giant |
| 18 | Volcanic | Z1 to Z3 | Or a young star, or a giant's moon under 6 radii |
| 19 | Lava ocean | Z1 | Or a moon under 4 radii |
| 20 | Ash-fall | Z2 to Z5 | Heavy volcanism or an apocalypse |
| 21 | Small cratered moon | Any zone | Moon, size S, no air |
| 22 | Large barren | Z1, Z2, Z6 to Z8 | |
| 23 | Low-gravity moon | Z5 to Z8 | Moon, size S |
| 24 | Meteor-pelted | Any | Orbit within 1.3× of a belt edge, or a young star |
| 25 | Barren metal | Z1 | More often round A and B stars |
| 26 | Ice moon | Z6 to Z8 | Moon |
| 27 | High-gravity | Z2 to Z6 | Size L or larger |
| 28 | Nascent | Any | Young star only |
| 29 | Tidally locked | Z1 to Z5 | The locking roll in step 9 |
| 30 | Ringed | Any | Size M or larger; a modifier on its base type |
| 31 | Gas-giant moon | Any | Any moon of a giant; a modifier on its base type |
| 32 | Toxic | Z2, Z3 | |
| 33 | Crystal | Z5 to Z7 | More often round dead and blue stars |
| 34 | Fungal | Z3 to Z5 | More often round K and M stars |
| 35 | Bioluminescent | Z4, Z5 | K or M star, or a locked world's night side |
| 36 | Shell | Z2 to Z6 | Rare; no orbit rule |
| 37 | Shattered | Any | The exception in step 9, or a rare moon |
| 38 | Graveyard | Any | Decided by history, not orbit |
| 39 | Terraced farm | Z4 | |
| 40 | Storm | Z3 to Z5 | Fast spin or a locked world |

### Step 11: turn it into a chart

- spread = L^0.1, kept between 0.75 and 1.6.
- chart distance = spread × h^0.7, in chart AU.
- Bearing: a seeded start angle, moved on by the clock.
- Year: P = Y × h^1.5, with Y about 24 game days for a temperate world on every star, so about 8 real hours. Keeping true h here keeps resonances and 60° points exact.

The stars' distance goes as L^0.1 where Empyrion's goes as about L^0.12 and reality's as L^0.5, so a bright star's system is a little wider and a dim one's a little tighter. Nothing else needs to know about the squeeze.

### What a run looks like, and how it was checked

I ran the layout rules (steps 3 to 8 and step 11) in a throwaway Python script outside the repo, on fresh random systems every run, 5,000 per star class and 20,000 start systems, twice over. Warps were modelled as 6 seconds of spool plus 6 seconds per chart AU in a straight line, with random bearings. This does not test types, exceptions or binaries, and it isn't a game test.

| Star | Worlds | Planets | Giants | Last orbit, chart AU | Typical warp | Longest 5% from | Worst seen |
| --- | --- | --- | --- | --- | --- | --- | --- |
| M | 11.2 | 5.8 | 0.7 | 3.3 | 18 s | 37 s | 63 s |
| K | 12.6 | 5.7 | 1.2 | 3.9 | 20 s | 42 s | 73 s |
| G | 13.0 | 5.6 | 1.4 | 4.4 | 22 s | 47 s | 82 s |
| F | 11.6 | 5.2 | 1.2 | 4.0 | 21 s | 41 s | 66 s |
| A | 10.9 | 5.2 | 1.0 | 4.8 | 24 s | 48 s | 76 s |
| B | 9.3 | 4.8 | 0.6 | 4.5 | 23 s | 42 s | 57 s |
| White dwarf (L only, no special layout) | 9.0 | 5.0 | 0.3 | 2.1 | 14 s | 23 s | 30 s |
| Brown dwarf | 8.8 | 5.1 | 0.1 | 2.1 | 14 s | 23 s | 30 s |

Every start system in both runs met the temperate world, belt and 8-world checks after step 8. The 8-world top-up was needed in 4 to 24% of normal systems and about a third of dead and dim ones.

### Where reality was bent, and why

| What | Real | GalHack | Why |
| --- | --- | --- | --- |
| Star mix | 76% red dwarfs | 28% | Variety on the star map |
| Life-bearing worlds | Rare, and depend on much besides position | About 75 to 90% of systems have a temperate slot; star and rarity decide what fills it | A goldilocks world should be findable |
| Temperate band | Spreads with L | Same h for every star | One template; ignores the small temperature shift |
| Distances | Linear | Squeezed to h^0.7, then 4,000 km per chart AU | Quick warps; even hops |
| System width by star | √L | L^0.1 | A blue star's system would be hundreds of times wider than a red dwarf's |
| Year | Differs by star | Same at h = 1 everywhere | One thing to tune; a red dwarf's real year would be weeks |
| Orbits | Ellipses that drift, moved by gravity | Near-circles on rails | Deterministic, co-op safe, no simulation; only the rare wanderer and comets are stretched |
| Moon distance | Planet's moon at about 60 radii | 12 to 40 | A moon you can see well and fly to |
| Hill limit on rock moons | About 1% of the orbit's size, so 40 km at chart scale | Moons out to 100 km | On rails, nothing can escape |
| Moon size | A giant's moons are tiny against it; a rocky world's rarely match it | A moon can be as big as a start world | Moons are places to stand |
| Warm giants | Uncommon in the temperate band | 20% | A habitable moon under a giant is a design pillar |
| Rocky worlds | Above about 2 Earth radii usually aren't rocky | L and XL classes are rock | Bigger maps for play |
| Gravity | Grows with size | Set by type, not size | Same density would make size XL about 4 g |
| Star's sky size | Falls as 1 / a | Same, but drawn at 2 times true | A readable sun; matches current code |
| Giant's size in others' skies | A point of light | A small disc when near | The sky should show the system |
| Belts | Empty | Dense fields | See section 5 |
| Rings | Metres thick, small pieces | A few hundred metres thick, big rocks | A place to fly and mine |
| Lock | Most big moons and close worlds | Past 10 radii, a moon spins freely | An outer moon's day would run for hours of real time |

---

## 5. Asteroid belts: the recommendation

### What's true

A real belt is nearly nothing. The main belt holds about 3% of the Moon's mass, and over half of that is in four bodies. Asteroids average nearly a million kilometres apart, and spacecraft cross with odds of a hit under one in a billion. From the middle of it, you'd see nothing but stars.

It exists because a giant stirred the region so strongly that collisions shattered rather than built, and it lost about 99.9% of its original mass. It sits between the giant's strong resonances, with gaps cleared where periods fit simple ratios. It sorts by distance: silicate rock inside, metal in the middle, dark carbon rock outside. About a third of asteroids belong to families, the remains of a broken parent body, sharing an orbit. Trojans hold 60° ahead of and behind a giant, in swarms about 26° long and about as numerous as the main belt's large bodies. Past the last giant, an ice belt runs between its 3:2 and 2:1 resonances, several times wider and far heavier, and it's thick like a doughnut rather than flat.

### What that means for us

Every game that makes belts feel good cheats in the same way, and Elite's and Empyrion's clusters show how. Keep reality at chart scale and put the density in specific places.

GalHack's belt has two levels:

1. **The belt is a band on the chart.** Between its resonance limits it's almost empty, which is true. You can fly or warp straight across and hit nothing, and the system map shows it as a soft band with two faint lanes at the 3:1 and 5:2 resonances. No fields sit in those lanes.
2. **A field is the place you go.** Each belt has 3 to 5 fields, on rails at the belt's period, at seeded bearings. Reading them as young families gives the density a reason: one body broke recently, its pieces haven't spread yet, and they share a composition. A cluster like that is believable, and it gives every field one clear personality.

### Field numbers

- A field is its own deep-space site: 6 to 14 km across, 60 to 200 rocks of 20 to 400 m, with one to three giant pieces of about 1 km as landmarks.
- Rocks are about 300 to 800 m apart at the heart, thinning to nothing at the edge. That's about two million times closer than the real average, and that is fine, because the edge shows it thinning out.
- Dust and small grit near the player add speed and depth without adding bodies.
- A hit at speed is the danger. Rocks drift slowly, so a pilot can read the field.

### Kinds of field

| Field | Where | Character |
| --- | --- | --- |
| Metal-rock | Inner rock belt | Iron, cobalt, neodymium. Bright rock. Calmest |
| Metal core | Middle rock belt, rare | The dense heart of a parent body. Rich metals, starsilver near giant stars |
| Dark carbon | Outer rock belt | Burnstone, magnesium. Dim, soot-black rocks, Reaver hideouts |
| Trojan swarm | ±60° on a giant's orbit | Mixed, dark and cold. Easy to find because it's always at the same angle from the giant |
| Ice-rock | Ice belt | Ice, frozen gases. Hollowed Reaver havens fit here |
| Ring band | A giant's ring, in its orbit zone | Ice in a flat sheet, the giant filling the view. Best-looking mining in the game |
| Debris | Round a dead star or broken moon | Fold crystal round dead stars, salvage after a war |

Ore fits the design's existing rules: rich in cobalt, neodymium and burnstone, and fold crystal near dead stars. Fields regrow as the design says. A family's one speciality gives each field a reason to come back.

Roll about one field in five as empty or thin, as Empyrion does with its empty rolls, so a rich one feels earned.

### Sky and system view

- From a world: at most a faint band along the system plane on airless nights, and only in thin air. Real belts are invisible, so don't draw a ring in the sky.
- From system space inside a belt: grit, a slightly dusty glow along the plane, no solid wall.
- A mined-out field stays visibly thinner until it regrows.

### What not to do

- No tube of rock round the star. It would be expensive, wrong and hard to fly.
- No belt that blocks warp. Warp should be free across it.
- No fields in the gap lanes.

---

## 6. Scale: compressing space while keeping positions right

### The worlds set the ruler

The orbit code already makes the globe true to the map: radius = width / 2π.

| Size class | Map | Radius | Orbit shell starts at |
| --- | --- | --- | --- |
| S | 8 × 4 km | 1.27 km | 1.83 R |
| M (start) | 16 × 8 km | 2.55 km | 1.42 R |
| L | 32 × 16 km | 5.09 km | 1.21 R |
| XL | 64 × 32 km | 10.2 km | 1.10 R |

That makes the body ruler 1:2,500 against the real Earth. Kept for distance, the same ruler would put one AU at about 60,000 km and the star 278 km in radius, which at 130 m/s is 53 days of flight. So bodies keep the ruler, and space gets its own, tighter ones.

| Thing | Real | GalHack | Squeezed by |
| --- | --- | --- | --- |
| Start world radius | 6,371 km | 2.55 km | 1:2,500 |
| Gas giant radius | 70,000 km | 20 to 28 km | 1:2,500 |
| Rock world's moon | 60 radii | 12 to 40 radii (30 to 100 km) | 2 to 5 times closer again |
| Giant's moons | 6 to 26 radii, some much further | 3 to 30 radii (60 to 840 km) | About true |
| AU | 150 million km | 4,000 km | 1:37,000 |
| Sunlike system, last planet | 30 AU | About 4.4 chart AU (18,000 km) | Plus the h^0.7 squeeze |

### What stays true, what's scaled, what's bent

**True:** the order of bodies, every zone, bearings, 60° points, resonance ratios, period ratios, phases, conjunctions, eclipses, which bodies are up at which hour, and the shape of the plane across the sky.

**Scaled, with ratios kept:** body sizes against each other, and moon periods. Moon periods survive because they depend only on distance in radii and density.

**Bent:** distances (squeezed), the star's sky size (2 times true), and the items in the table in section 4.

The test for any scene: a pilot should be able to point at a body, then warp and arrive at the thing they pointed at. A player on the ground looking at a giant should find it in that same direction on leaving orbit.

### How big things look

With true geometry at those scales, a body's angular radius is just radius / distance. Pixel sizes below are for 1080p at a 70° field of view.

| Seen from | Body | Across | On screen |
| --- | --- | --- | --- |
| A world | Size S moon at 100 km | 1.5° | 35 px |
| A world | Size S moon at 40 km | 3.6° | 87 px |
| A world | Size S moon at 30 km | 4.9° | 116 px |
| An inner moon | Giant at 3.5 radii | 33° | About three quarters of the screen height |
| A start moon | Giant at 8 radii | 14° | 340 px |
| An outer moon | Giant at 30 radii | 3.8° | 90 px |
| A world | Giant 1.5 chart AU away | 0.5° | 13 px, the real Moon's size |
| A world | Giant 3 chart AU away | 0.27° | 6 px |
| A world | Neighbour world at 0.5 chart AU | 0.15° | 3 px, a bright star-like point with a phase under zoom |
| A world | Neighbour world at 1 chart AU | 0.07° | 2 px, a bright point |

Those moon numbers are not forced. `SkySystem` currently rolls moons from 0.012 to 0.042 radians in radius, and true geometry for an S moon at 30 to 100 km gives 0.042 to 0.013. `orbit.json` stands moons at 40 km. Moon orbits and giant skies from the code's current look already fit this ruler.

**Stars.** Disc radius at h = 1 is 0.0095 radians × (R / √L), falling as 1 / chart distance from there. That is 2 times true, and the existing table is close already:

| Star | This formula | `SkySystem` today |
| --- | --- | --- |
| M | 0.022 | 0.028 |
| K | 0.014 | 0.016 |
| G | 0.0095 | 0.0095 |
| F | 0.0074 | 0.008 |
| A | 0.0040 | 0.006 (blue-white) |
| B | 0.0015 | 0.006 (blue-white) |

So a red dwarf's temperate world has a huge dull sun, and a blue star's has a tiny, fierce point. A star's radius in kilometres then varies oddly from class to class, but no player can see two stars side by side, and heat should keep them off the star. An exclusion radius of about 0.25 chart AU does that. Binaries are the one case to check by eye.

**Floor.** Draw anything naked-eye bright as at least a 1.5 px point, so neighbours don't vanish, with brightness from S, size and phase.

**Rings.** Ring plane comes from the parent's equator and tilt, so a ring's look depends on where you stand. On a ringed world, you see a thin line at the equator, widening arcs at mid latitudes, and nothing from the far pole. Today `RingTilt` is a random number per sky, so this needs changing. Ring shadow bands then land at the right latitudes without further work.

### One timetable, three zones

A single pure function takes a body ID and a time, and gives a position in system space from the seeded elements. Everything reads it: the surface sky, the climb, orbit, the system map, warp targets and the scanner.

**Clock.** One system clock, shared by all players, saved with the world. No zone advances anything on its own.

**Frames.** Each zone stays in its own world's rest frame. The planet's motion shows only as sky directions changing, so a warp arrival needs no velocity matching.

**From the flat map to the sky:**

1. The map point (x, z) gives longitude and latitude through `GlobeMap`, as the crossing already does.
2. Spin turns longitude with the day clock. Axial tilt turns the pole. Together these give the observer's position and local up, east and south on the round globe.
3. Add that position to the world's place in system space, so close moons shift a little with where you stand. A moon at 16 radii shifts about 3.6°, and without this step it would jump at the crossing.
4. For each body, the direction is body minus observer, turned into local sky.
5. Sun, moons, giants and stars are all drawn from this one sum.

The worlds stay flat. The ground never curves. Only the sky reads the globe's place. The orbit zone uses the same function in the same frame, so nothing needs placing on arrival and a body's position across the crossing is already right.

**One honest seam.** The map wraps north to south, but `GlobeMap` runs pole to pole, so crossing the seam jumps from one pole's sky to the other's. The sky can't be right on both sides. Ease the observer's latitude towards a shared value over the last few hundred metres either side of the seam, so that both polar edges see the same sky. Nearly nobody lives there, and it's better than a jump.

**Locked worlds.** The sun or giant simply stops. Sky direction comes straight from map position, so a locked moon's giant stands at one fixed place, and a far-side base never sees it. A locked world's day side, twilight ring and night side are map regions with fixed light, which suits the flat map well.

### Moving between them

| Hop | How long | Notes |
| --- | --- | --- |
| Moon to neighbouring moon, rock world | 5 to 10 minutes at 130 m/s, or a free short warp | Close enough to fly |
| Between a giant's moons | Short warp | 60 to 840 km; a long flight, not impossible |
| Planet to planet | Warp: 6 s spool plus about 6 s per chart AU | So about 670 km/s |
| Whole system, worst case | About 80 seconds | Against a typical 20 |

The design's rate of about 1 warp cell per AU still works if AU means chart AU, though typical trips are then only 2 to 5 cells, so its cost may want tuning upward.

The warp sequence may also run longer to hide loading, which the design allows. These times are a floor.

### What this means for the code

For whoever picks this up. Nothing here has been changed.

- `SkySystem.For` rolls star class and bodies from the planet spec, and each moon's azimuth and inclination independently. Replace with a read from a system record, so all moons share a plane.
- `SkyRig` moves bodies by a small sway, not an orbit. Replace with the timetable. Its sun path uses latitude × 1.3 with a cap near 75°; the timetable replaces that with tilt and true latitude.
- `OrbitZone.Arrive` places the sun and moons once at the first crossing. With a timetable it can read them live.
- `Charts.Bodies` uses fixed made-up orbits for its map; `Charts.Stars` picks neighbour classes evenly. Both should read real records.
- `OrbitPlacement.Place` rolls distances in real-looking km for moon, belt and deep sites. Those should attach to actual moons, fields and chart positions: belt sites go inside fields, and deep sites go in gaps between orbits or out past the last one.
- `orbit.json` has `cameraFar` at 60 km, so moons at up to 100 km need it raised or drawn as far sky objects.
- System space needs 64-bit positions, drawn relative to the observer, with far bodies as sky impostors.

---

## 7. Sources

Game files, read-only: `Content/Configuration/GalaxyConfig.ecf`, `Content/RandomPresets/SolarSystemConfig*.yaml`, `SolarSystemConfig_Defines.yaml`, `SectorTypeDefaults.yaml`, `Content/Playfields/SpaceAsteroidFieldRich/space_dynamic.yaml`, and `Content/Scenarios/Default Random/Sectors/Sectors.yaml`, all in the local Empyrion install.

GalHack: `docs/DESIGN.md`, `docs/world-themes.md`, `planet-surface/src/Sky/SkySystem.cs` and `SkyRig.cs`, `slice/src/Orbit/Charts.cs`, `OrbitZone.cs`, `SpaceSky.cs` and `GlobeMap.cs`, `slice/data/orbit.json`, and `poi-generator/src/Gen/Orbit.cs`, all from the `space` worktree.

Games:

- Frontier: Elite II: https://en.wikipedia.org/wiki/Frontier:_Elite_II
- Pioneer generator source: https://github.com/pioneerspacesim/pioneer/blob/master/src/galaxy/StarSystemGenerator.cpp
- Frontier-data tool mention: https://ffg-forum-archive.entropicdreams.com/topic/25804-frontier-elite-2-as-a-star-system-generator
- Elite Dangerous, Stellar Forge interview: https://www.pcgamer.com/the-mind-bending-science-behind-the-planets-of-elite-dangerous/
- Elite Dangerous, generating the universe: https://80.lv/articles/generating-the-universe-in-elite-dangerous/
- Elite Dangerous scale and catalogue stars: https://www.space.com/31366-elite-dangerous-stellar-forge-interview.html
- Hutton Orbital trip: https://elite-dangerous.fandom.com/wiki/Hutton_Orbital and https://www.pcgamer.com/i-survived-elites-most-gruelling-and-boring-rite-of-passage
- Elite belts (search excerpt; direct fetch refused): https://elite-dangerous.fandom.com/wiki/Asteroids
- Elite ring hotspots (search excerpt): https://elite-dangerous.fandom.com/wiki/Hotspot
- Elite orbits, unofficial: https://forums.frontier.co.uk/threads/are-the-orbits-of-the-planets-physically-correct.394502/
- Empyrion changelog excerpts: https://steamcommunity.com/sharedfiles/filedetails/changelog/1742662471?p=2
- Empyrion modder note on distance units: https://empyriononline.com/threads/solar-system-config-help.94997/
- Space Engine: https://spaceengine.org/news/blog190611/ and https://spaceengine.org/news/blog251118
- Kerbal Space Program: https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/KerbalSpaceProgram and https://forum.kerbalspaceprogram.com/topic/133454-does-anyone-else-find-kerbol-too-big/page/3
- Outer Wilds review: https://www.destructoid.com/reviews/review-outer-wilds/
- Star Citizen belt: https://starcitizen.tools/Aaron_Halo

Science:

- Habitable zone: Kopparapu et al. 2013, https://arxiv.org/abs/1301.6674, and coefficients from Kopparapu's own calculator, https://personal.ems.psu.edu/~jfk4/ruk15/planets/hzcalc.py
- Stellar classes: https://en.wikipedia.org/wiki/Stellar_classification
- Mass and luminosity: https://en.wikipedia.org/wiki/Mass%E2%80%93luminosity_relation
- Frost line: https://en.wikipedia.org/wiki/Frost_line_(astrophysics)
- Spacing: Weiss et al. 2018, https://arxiv.org/abs/1706.06204
- Titius-Bode: https://en.wikipedia.org/wiki/Titius%E2%80%93Bode_law
- Giants round M dwarfs: Clanton and Gaudi, https://arxiv.org/pdf/1404.7500
- Hot giants: https://en.wikipedia.org/wiki/Hot_Jupiter
- Tidal locking: Barnes 2017, https://arxiv.org/abs/1708.02981
- Hill sphere: https://en.wikipedia.org/wiki/Hill_sphere
- Moon system mass: Canup and Ward 2006, https://www.boulder.swri.edu/~robin/canupward2006.pdf
- Habitable moons: Heller and Barnes 2013, https://arxiv.org/abs/1209.5323
- Binaries, stability: Holman and Wiegert 1999, https://ar5iv.labs.arxiv.org/html/astro-ph/9809315
- Binaries, frequency: Raghavan et al. 2010, https://chara.gsu.edu/files/papers/2010_Raghavan_ApJ_190_1.pdf; Duchene and Kraus 2013, https://ar5iv.arxiv.org/html/1303.3028
- Roche limit: https://en.wikipedia.org/wiki/Roche_limit
- Saturn's rings: https://en.wikipedia.org/wiki/Rings_of_Saturn
- Asteroid belt: https://en.wikipedia.org/wiki/Asteroid_belt
- Kirkwood gaps: https://en.wikipedia.org/wiki/Kirkwood_gap
- Trojans: https://en.wikipedia.org/wiki/Jupiter_trojan
- Kuiper belt: https://en.wikipedia.org/wiki/Kuiper_belt

---

## 8. Owner's decisions

A rule for every start system: the start world has at least one moon reachable under thrusters, about 12 radii out, holding warp fuel and warp drive materials. On a moon start, a sibling moon does this. The rest of the system must hold hyperdrive materials, hyperdrive fuel and capital ship materials. A system that fails either rule is fixed or rerolled.

Rings on a moon: real rings only survive close inside a body's small zone of gravity (its Hill sphere), so a world orbiting near a bigger ringed planet would rarely hold rings of its own. Treat a ringed moon of a ringed giant as a very rare sight, not a common result. Rings aren't limited to gas giants: any world can have them, for example from a moon that broke apart, though on rocky worlds they're less common and narrower. Rings are real places: from afar they read as smooth bands, and up close they resolve into a field of tumbling rocks, in the manner of Elite Dangerous. Use the same rock field system as asteroid belts, so rings can be flown through and mined.

1. **Planets move.** On rails, from one clock.
2. **Warps.** About 20 seconds typically, 80 at worst.
3. **Free flight.** The player can drop out of warp and fly anywhere in a system. Suns and gas giants can't be entered: warp stops at a safe distance from a star, closer means heat damage, and a giant's clouds can be skimmed above a crushing limit. To be tuned in play.
4. **Red dwarfs.** About 28% of stars, not the real 76%.
5. **Star table.** Follows the real rule: red dwarfs have close, often locked worlds under a big dull sun.
