# Weapons design

Design pool for GalHack's arsenal. Not a commitment and not code. All names are original. Tropes are used freely, trademarked weapons are not, and nothing is copied from any game, film or book. Common nouns such as railgun or rocket launcher name the type; the in-game name is GalHack's own.

## 1. Summary

**How the arsenal is organised.** 345 weapons in 42 families, plus a 43rd family of 12 personal shield items, which makes 357 entries. The tech tree holds 145 weapon nodes that open groups, 11 personal shield nodes and the 6 existing ammo nodes; everything else is a recipe, ammo, a fitting or a find (section 9). A family runs up the tech bands (low, mid, high) and up the mounts (hand, placed, hover, small vessel, base or capital turret, capital heavy), so a rocket goes from a paper tube of powder to a shoulder launcher to a swarm rack to a wall of missile hatches on a capital ship. Staples come first and are complete: every expected gun, launcher, mine and ship weapon is here. Exotics sit on top: lightning, living weapons, homing bullets, gravity, phase, time, mind, nanites, antimatter and relics. Rare barely-understood relics are the only thing beyond the high band.

Most weapons are not bespoke code. Each is a row of combinable parts (section 2) plus a look and a sound. Every entry says what situation it solves, what it costs the player and what counters it. Anything that can't be built without new engine work is marked **Needs** and grouped by shared upgrade in section 13; nothing was watered down to fit today's engine. The first 12 to build (section 8) need none of those upgrades.

Settled rules applied throughout: effects on players last seconds during a fight, which keeps "no ailments". Living weapons use biomass as plain ammo with no upkeep. The level cap is 25. "No drones" only means no Empyrion-style drone waves invading you, so other machines are allowed as enemies and world content. Tech bands are low, mid and high. No weapon relies on speech, written logs or readable lore. Anything that lifts objects is a clearly visible anomaly with a visible cause. Wires and cables are always anchored. Hollow-related effects stay understated and are never labelled as such.

**The owner's decisions (section 11).** The player has a personal shield, a bar alongside health that soaks hits first and refills after a short break from taking hits; health doesn't refill (section 5.11). There are no companions: nothing the player owns follows them or fights at their side. Static placed defences stay, and swarms fired as ammo stay, because they're short-lived shots. Weapons never wear out or jam. Antimatter, nuclear and orbital weapons leave permanent craters, with a server setting to turn them off. Self-harm is on by default and friendly fire is a server setting (section 5.12). Comic weapons stay as rare finds.

**What was verified, honestly.**

| Source | What I did | Confidence |
| --- | --- | --- |
| Empyrion, vanilla | Read `ItemsConfig.ecf` (all 699 item names, and full rows for the flamethrower, explosive charge, homing rocket, EMP, ballista, swarm rocket, mortar grenade and charged rail), the weapon blocks in `BlocksConfig.ecf`, `StatusEffects.ecf` names and `DamageMultiplierConfig.ecf` | High |
| Empyrion wiki | Could not open it: the site showed a security check and I didn't try to get past it. A search result warns that older wiki pages are out of date | None; the config files replace it |
| Reforged Eden (first version) and Reforged Galaxy | Read the authors' weapon lists on the Empyrion forum directly: hand weapons, vehicle and capital weapons, damage rules | Good, for those versions |
| Reforged Eden 2 | Read its Steam page, its guide for Reforged Eden 1 players and weapon lines from two pages of change notes, after Steam's rate limit lifted. Not installed, so no weapon data | Fair for its systems, low for its full list |
| Star Salvage: Reborn | Read its Steam page and change notes. Neither lists any weapon; it's about salvage, junkyards and ghost ships. Nexus was blocked and it's not installed | Nothing to compare |

So where section 10 says "new", it's checked against vanilla and the first Reforged Eden's list, and mostly unchecked against Reforged Eden 2 and Star Salvage: Reborn. Research sources are in section 12.

---

## 2. The parts

A weapon is a data row: trigger, delivery, path, damage, effects, ammo, plus a feel. Anything the parts can't express is a signal to add a part, not a bespoke weapon. Every entry in section 4 carries its parts in a code span, and a script confirmed all 357 entries use only the words below.

**Trigger** (how it fires): `semi` one per click. `auto` held. `burst` a set group. `pump` a manual action between shots, or pumped pressure. `charge` hold, then release. `hold` fires while held. `crank` muscle-driven. `lockon` hold on target, then fire. `timer` fuse or countdown. `det` fired later by a remote button. `worn` always on while fitted to a suit.

**Delivery** (how the effect travels): `ray` instant line. `bolt` a real projectile with speed and life. `pellets` a spread of bolts. `lob` a bolt under gravity. `beam` continuous ray with ticks and heat. `cone` a short wedge. `arc` a chain that jumps to nearby targets. `pulse` a ring or sphere from a point. `field` a volume that lasts. `tether` a line that stays attached and is always anchored. `placed` a thing set down in the world. `thrown` by hand. `melee` a sweep or thrust. `swarm` many short-lived small seekers. `strike` arrives from the sky. `guided` steered by the user.

**Path** (how it moves or behaves on the way): `pierce` passes through. `bounce` ricochets. `stick` attaches. `split` breaks into more. `drill` eats into what it hits. `float` drifts or holds position. `orbit` circles the user. `return` comes back. `delay` acts seconds later. `phase` ignores walls. Seeking is one word per rule: `seek-lock`, `seek-heat`, `seek-sound`, `seek-flesh`, `seek-metal`, `seek-mark`, `seek-memory`, `seek-wire`.

**Damage class**: `kin` kinetic, `exp` explosive, `en` energy. These are the design doc's three types, and every weapon maps to them for armour and shield multipliers. Everything else is a tag on top, so the multiplier table stays small.

**Tags** (what kind of harm): `fire`, `cold`, `acid`, `sonic`, `force`, `grav`, `em`, `light`, `nanite`, `bio`, `rad`, `void`. A tag decides how the world reacts (section 5). `void` ignores material resistance inside its core.

**Status** (timed, seconds on players, always ends): `stun`, `shock` (devices and machines stop), `slow`, `freeze`, `blind`, `deaf`, `stagger`, `pin`, `glue`, `lift`, `mark`, `confuse`, `fear`, `calm`, `sleep`, `lure`, `decoy`, `corrode` (armour value falls), `strip` (shield falls faster), `stall` (a personal shield's refill can't start for seconds), `ward` (gives or refills a personal shield), `mend`, `haste`, `shunt` (moved), `rewind`, `shrink`.

**Hit rules**: `ignite`, `crater`, `breach` (strong on blocks and devices), `shatter`, `dig`, `glass`, `noblock` (blocks unharmed), `fleshonly`, `machineonly`, `crewonly`, `overload` (extra on shields and generators, and empties a personal shield), `shieldpass` (goes past a shield, vessel or personal).

**Ammo source**: `rounds`, `shells`, `slugs`, `grenades`, `rockets`, `mines`, `bolts`, `cells`, `power` (vessel generator), `charge` (recharges itself), `fuel`, `coolant`, `chem`, `water`, `biomass`, `scrap`, `capsule` (single-use item), `nanopack`, `foldcrystal`, `antimatter`, `none`.

**Feel**: a `GunFeel` row (flash style, colour, size, kick, smoke) plus a tracer, an impact and a sound id. Three flash styles exist (Star, Bloom, Rocket). Add five, once, shared by many weapons: Crackle (branching arc), Mist (soft puff), Pulse (expanding ring), Wet (organic burst, no flash) and Dim (a brief darkening). Stats such as damage, rate, heat, noise radius, recoil and block multiplier are plain numbers, as in the boarding prototype's `data/weapons.txt`.

---

## 3. Families

Bands: L low (castles to diesel), M mid (modern), H high (the game's own level), R relic.

| Family | Role | Low | Mid | High | Mounts |
| --- | --- | --- | --- | --- | --- |
| 4.1 Sidearms | Always-ready backup that works anywhere | Cap Pair, Tack Pistol | Six-Chamber, Glint, Hush | Three-Step, Kick Pocket | Hand |
| 4.2 SMGs, carbines | Close spray, boarding | Stamp Gun | Corridor Gun, Pingback, Strobe | Strobe | Hand |
| 4.3 Rifles | The all-round standard | Long Arm | Line Rifle, Breaker, Long Glint | Dial Rifle | Hand |
| 4.4 Shotguns | Close burst, shell variety | Scatterbuss, Both Barrels | Pump, Sweeper | Shardcaster | Hand |
| 4.5 Long guns | Reach, device-killing on foot | Long Arm covers it | Spotter's, Stalking, Bench Gun | Accelerators take over | Hand |
| 4.6 Machine guns | Suppression and volume | Hand Organ | Squad Belt, Tripod, Spin Gun | Lamp Rotary | Hand, placed, hover, small, turret |
| 4.7 Bows, air, spring | Silent, ammo you can make anywhere | All but one | Pump Gun, Pin Driver | Kin Bow (reward) | Hand, turret (ballista, trebuchet) |
| 4.8 Hand grenades | Reach behind cover; every effect in a throw | Stick Bomb, Smoke | Frag and the rest | Pulse Ball | Hand |
| 4.9 Grenade launchers | Indirect fire on foot | Stovepipe | Break, Drum, Gum, Set-Range, Seed | Hop | Hand, hover |
| 4.10 Rockets | Anti-armour; lock, steer, saturate | Fire Rack | Throwaway, Shoulder, Lock, Wire, Twin, Hot Breath, Pod, Sky Battery | Flock, Eye, Salvo Wall, Long Reach | Hand to capital |
| 4.11 Mortars, artillery | Out-range and siege | Iron Bombard | Knee, Field, Crawler, Fortress, Bomb Bay | | Hand, placed, hover, small, turret |
| 4.12 Mines | Area denial and ambush | Keg, Trip Line, Caltrops, Pit | Pressure, Prox, Bounding, Fan, Satchel, Limpet, Cutting, Tide, Seeder | Picky, Drift | Placed, small, capital |
| 4.13 Accelerators | Reach and pierce; power as the cost | Scrap Coil | Coil Carbine, Stage Rifle, Mass Driver | Rail Lance, Sled, Fighter, Line Battery, Spinal | Hand to capital |
| 4.14 Lasers, masers | Instant, ammo-free; weather is the cost | | Cutter, Stutter Lamp, Scald Horn, Dazzler | Scorch Line, Prism, Cook Box, Burn Tower, Lance Array | Hand to capital |
| 4.15 Plasma | Shield strip, heat as the cost | | | All seven | Hand to turret |
| 4.16 Particle, ion, EM | Disable and capture | | | Sleet, Marrow, Static, Choke, Jam Spike | Hand, hover, small, turret |
| 4.17 Lightning | Crowds and machines | Dynamo Snapper | Wire Biter, Skycall, Fence, Lure Rod | Fork, Rope, Guide-Line, Ball, Coil Tower, Rake, Web | Hand to capital |
| 4.18 Sonic | Round corners, brittle things | | Basslance, Thump, Ring | Thin Note, Resonator, Scream, Hush | Hand, hover, small |
| 4.19 Flame | Area denial, nests, plants, fear | Bottle, Lance, Sprayer, Scald, Torch | Pressure, Jelly, Mist, Drag, Curtain, Gate, Burial, Drive Burn | Star Gel, Void Burner | Hand to capital |
| 4.20 Cold | Hold still, then break | | Snap, Frost Lance, Rime Wall | Still Beam, Frost Shell | Hand to turret |
| 4.21 Acid, goo, water | Corrode, stick, soak | Reek, Slip, Salt | Etch, Rust, Tangle, Foam, Water, Slag, Flood | | Hand, placed, hover, turret |
| 4.22 Melee | Silent, stamina, last resort | Knife, Hatchet, Spear, Maul, Wire, Saw | Baton, Fist, Spike, Cleaver | Hum, Light, Filament | Hand |
| 4.23 Seeking | Shots that choose | | Heat Dart, Return Disc | Bend Rounds, Tag, Needles, Memory, Whisper, Blood, Orbit | Hand, hover |
| 4.24 Living | Owner's worms and hornets; biomass ammo | Wasp Jar, Bait Worm | Hive, Spitter, Spore, Bile, Egg, Vine, Claw, Tooth, Tick, Stalk, Thorn, Nest, Gullet | Moths, Swimmer, Rider, Spawn Tube | Hand to capital |
| 4.25 Gravity, force | Move things; the world does the harm | | Shove, Tow Beam | Well, Lift, Drift, Weight, Press, Catch, Flip, Snare, Magnet, Deep Well | Hand to capital |
| 4.26 Tethers, nets | Hold and haul | Net, Bola, Grapple, Harpoon | Tow Hook, Boarding Claw | | Hand to capital |
| 4.27 Nanite | Eat, mend, turn | | | All six | Hand, small, turret |
| 4.28 Phase, fold | Ignore walls, move targets | | | All eight | Hand, placed, turret, capital |
| 4.29 Time, perception | Control the fight | | | Seven, plus a relic | Hand, hover, placed |
| 4.30 Mind | Fear, sleep, confuse, forget | | Horn, Calm, Sleep, Lure | Lens, Forget, Stare | Hand, placed |
| 4.31 Disintegrators, rays | One target gone or changed; the comic rays are rare finds | Bubble Gun | | Ash, Cross, Shrink, Meek, Growth | Hand, turret, capital |
| 4.32 Defence | Answers to each attack family | Bull Shield | Flare and Chaff, Sand Burst, Needle Curtain | Mirror, Pop Dome, Thread Laser | Hand to capital |
| 4.33 Ship guns | Vehicle and capital main armament | Ram Spike | Drum, Chain, Deck, Flak, Torpedo, Depth Rack | Overload Torpedo, Thumper | Hover to capital |
| 4.34 Nuclear | Big, and leaves a mark | | Hand Bomb, Sun Shell, Salt | Ghost, Cone, Star, Blackout | Hand, hover, turret, capital |
| 4.35 Antimatter | The top; containment is the cost | | | All seven | Hand, placed, hover to capital |
| 4.36 Electronic, cyber | Attack sensors, power, control | | Paint Lamp, Jam Pod, Wrist Blade | Drag, Leech, Spoof, Palm, Shoulder | Hand, hover, small, turret |
| 4.37 Block, terrain | The base and the ground are the target | | Pipe, Quake, Wall, Root | Sink, Glasser, Bore | Hand, placed, hover, small, turret |
| 4.38 Improvised | Whatever's lying around | Saw, Cell Bomb, Gas Rocket, Rigged Bike | Junk Cannon, Ripped Gun | | Hand, placed |
| 4.39 Support | Healing and repair in the fight | | Patch, Weld, Pep | Mender | Hand, small |
| 4.40 Superweapons | World-scale events | | | All six | Capital, turret |
| 4.41 Placed machines | Placed weapons that stay put or go off once; nothing follows you | | Stalk, Crawl Mine, Picket | | Placed |
| 4.42 Relics | Found, never researched | | | Found only | Hand, placed, small, capital |
| 4.43 Personal shields | The bar that takes hits before health | Crash | Mesh, Field, Quick, Bulk, Cell, Spark, Post | Line, Lattice, Snap, Sink | Worn, hand, placed |

---

## 4. The weapons

How to read an entry. The first line gives code and name (type), then band, mount, build size and parts, and any engine upgrade it needs. Bands: L low, M mid, H high, R relic. Mounts: Hand, Placed, Hover, Small (small vessel), Turret (base or capital), Capital (heavy, spine or bay), Worn (a suit's shield slot). Build size: S is a data row, M is one new behaviour in shared code, L is a new system or a lot of art. *Why* says what situation it solves that nothing else solves. *Cost* is what the player pays beyond ammo. Parts use the words in section 2. Hand and placed entries with no Needs tag rest on the hand weapon layer; vessel entries rest on the vessel gun layer (both in section 13).

### 4.1 Sidearms

**SA1 Cap Pair** (twin black-powder pistols) | L | Hand | S | `semi bolt kin rounds`
*Does:* two single-shot pistols, left and right click, one heavy ball each. *Look and sound:* brass and walnut, a snap, a flat crack, a ball of white smoke. *Why:* the cheapest gun that can stop a charging animal, and the first lesson that shots are precious. *Cost:* two shots, then a long reload that needs both hands. *Ammo:* powder and ball, made at a fire pit. *Counter:* close the gap during the reload; rain wets the powder, so each ball carries less range and punch.

**SA2 Tack Pistol** (projectile pistol) | L-M | Hand | S | `semi bolt kin rounds`
*Does:* the design doc's first crafted sidearm: light, accurate, ten-plus rounds. *Look and sound:* a stamped steel slab, a clean sharp pop. *Why:* it works when everything else doesn't: underwater, in vacuum and after a power failure. *Cost:* poor against armour; useless on blocks. *Ammo:* pistol rounds. *Counter:* any plate armour, or range.

**SA3 Six-Chamber** (heavy revolver) | M | Hand | S | `semi bolt kin rounds`
*Does:* six heavy rounds that fire in the order you load them, and each chamber can hold a different round. *Look and sound:* long barrel, oiled cylinder that clicks round on screen, a deep bang. *Why:* you plan a sequence: stun, stun, armour-piercing, or a tracer then three heavy. No other gun rewards loading as a decision. *Cost:* slow fire, big kick, and the wrong round comes up if you lose count. *Ammo:* heavy pistol rounds, any shell type in section 6. *Counter:* rush it after six; reloads are one at a time.

**SA4 Glint Pistol** (laser pistol) | M | Hand | S | `semi ray en light cells`
*Does:* an instant, no-drop bead of light. *Look and sound:* white ceramic, a thin red line for two frames, a dry tick. *Why:* the efficient sidearm: no recoil, no travel time, and quiet. *Cost:* heat after a dozen fast shots; loses range in rain, fog and smoke; no use underwater. *Ammo:* energy cells. *Counter:* smoke, sand bursts, mirrored plate.

**SA5 Hush Pistol** (suppressed subsonic pistol) | M | Hand | S | `semi bolt kin rounds`
*Does:* a slow subsonic round with almost no report. *Look and sound:* a fat tube of a barrel, a cough and a click. *Why:* stealth without melee range: guards and creatures hear little of it, so one shot doesn't wake the room. *Cost:* low damage, drop at range, a headshot or nothing. *Ammo:* subsonic pistol rounds. *Counter:* helmets and personal shields; a body falling in view of a friend raises the alarm anyway.

**SA6 Three-Step Pistol** (stun, kill, unmake sidearm) | H | Hand | M | `semi bolt en stun cells`
*Does:* the first hit stuns, the second on the same target within three seconds kills most soft targets, the third turns the corpse to ash so nobody finds it. *Look and sound:* a snake-curved grip, a soft blue crawl over the target, a rising chirp per step. *Why:* rewards landing the same target, not spraying, and gives stealth players body removal. *Cost:* almost nothing on a first hit, so groups are dangerous. *Ammo:* cells. *Counter:* armoured targets ignore the first step; a second enemy breaks your rhythm.

**SA7 Kick Pocket** (tiny gun with absurd recoil) | H | Hand | S | `semi bolt exp force crater charge`
*Does:* a palm-sized pistol that fires one shot as strong as a rocket and throws the user backwards. *Look and sound:* looks like a toy; a white punch of light and a thump you feel. *Why:* the surprise answer and a movement tool: in low gravity it's a jump, in zero g it's a thruster. *Cost:* it knocks you over, off ledges, and can hurt you if you're braced against a wall. *Ammo:* recharges slowly. *Counter:* a shield wall behind which the user is thrown into the open. A rare find: never crafted and not in the tree.

### 4.2 Submachine guns and carbines

**SM1 Stamp Gun** (crude blowback SMG) | L | Hand | S | `auto bolt kin scrap`
*Does:* a stamped sheet-metal tube that hoses pistol-weight slugs. *Look and sound:* welded seams, a wire stock, a fast rattling clatter. *Why:* the gang's gun and the player's first automatic: it can be made from wreck scrap on the spot. *Cost:* climbs fast, empties in two seconds, and rough rounds hit soft. *Ammo:* rough rounds cast from scrap. *Counter:* it can't reach 30 m with any accuracy; any armour shrugs it off.

**SM2 Corridor Gun** (boarding SMG) | M | Hand | S | `auto bolt kin noblock rounds`
*Does:* the design doc's boarding weapon: frangible rounds that break on hull. *Look and sound:* short and square, a tight fast buzz. *Why:* you can fight inside a ship you plan to keep: it doesn't breach hull or windows. *Cost:* falls apart beyond a room's length. *Ammo:* pistol rounds, frangible. *Counter:* body armour; range.

**SM3 Strobe** (laser SMG) | M-H | Hand | S | `auto ray en light cells`
*Does:* a flicker of short pulses, no recoil at all. *Look and sound:* a slim tube, a stuttering line of light, a dry zipping hum. *Why:* perfect tracking at close range, and it also lights a dark room in flashes. *Cost:* heats fast; the strobe gives you away and spoils your own night vision. *Ammo:* cells, drinks them. *Counter:* smoke, rain, mirrored plate.

**SM4 Pingback Carbine** (ricochet carbine) | M | Hand | S | `auto bolt bounce kin rounds`
*Does:* hard rounds that skip off steel and rock up to three times. *Look and sound:* a ringing ping after each shot. *Why:* lets you shoot round a corner by angle, so tunnels and ship corridors become geometry. *Cost:* rounds come back at you and at allies; loses power each skip; soft ground eats them. *Ammo:* hard-tip rounds. *Counter:* open ground and soft cover.

### 4.3 Rifles

**RF1 Long Arm** (bolt-action rifle) | L | Hand | S | `semi bolt kin rounds`
*Does:* one heavy round, cycle the bolt, repeat. *Look and sound:* blued steel and hardwood, a crack that rolls across the valley. *Why:* the frontier hunter's tool: reach and stopping power at a low tech cost; the first gun that lets you pick a fight from a distance. *Cost:* slow, loud, and it draws everything in earshot. *Ammo:* rifle rounds. *Counter:* close quarters; packs.

**RF2 Line Rifle** (service or assault rifle) | M | Hand | S | `auto bolt kin rounds`
*Does:* the workhorse: single, three-round burst or full auto, good to mid range. *Look and sound:* a matte polymer body, a sharp steady crack. *Why:* it does nothing special and always helps; every exotic is measured against it. *Cost:* nothing dramatic: it's outclassed at each extreme. *Ammo:* rifle rounds. *Counter:* heavy armour and shields.

**RF3 Breaker** (battle rifle) | M | Hand | S | `burst bolt pierce kin rounds`
*Does:* a two-round burst of full-power rounds that punch through light cover. *Look and sound:* long, heavy, wood-effect furniture, a hard double thud. *Why:* shoots through crates, doors and thin plate, so hiding stops working. *Cost:* heavy, big climb between bursts, small magazine. *Ammo:* heavy rounds. *Counter:* stone and thick steel; stay moving.

**RF4 Long Glint** (laser carbine and rifle) | M | Hand | S | `semi ray en light cells`
*Does:* the design doc's laser line: carbine stock for handling, rifle stock for reach, one body. *Look and sound:* a long white rail, a thin beam, a click and a hiss. *Why:* flat, instant, no lead on a moving target at long range, so it's the skill-free long gun. *Cost:* air eats it: dust storms, rain and fog cut range to a fraction. *Ammo:* cells. *Counter:* bad weather, smoke, mirrored plate.

**RF5 Dial Rifle** (programmable rounds) | H | Hand | M | `semi bolt split kin exp charge cells`
*Does:* one blank smart round; a thumb dial sets it to pierce, burst overhead, ignite or bounce before each shot. *Look and sound:* a small screen on the stock, a coil click, then the right effect. *Why:* one gun for a long trip, when weight and storage matter more than peak power. *Cost:* every mode is weaker than the specialist; dialling takes a moment; rounds are expensive. *Ammo:* blank smart rounds, a little cell charge. *Counter:* catch the user on the wrong setting.

### 4.4 Shotguns

**SG1 Scatterbuss** (blunderbuss) | L | Hand | S | `semi pellets kin scrap`
*Does:* a flared brass bell throws whatever you stuff in it. *Look and sound:* thunder, a wall of smoke that hides you for two seconds. *Why:* ammo is anything small: nails, gravel, glass, bolts. You're never out of rounds in a ruin. *Cost:* one shot, very long reload, wild spread. *Ammo:* powder plus junk. *Counter:* step back ten metres; wet powder.

**SG2 Pump Scattergun** (pump shotgun) | M | Hand | S | `pump pellets kin shells`
*Does:* the close-range staple, one shell at a time. *Look and sound:* the pump is half the fun: rack, boom. *Why:* it takes shell types (section 6): slug, flechette, salt, fire, breach, so it's five tools. *Cost:* slow to refill, little reach. *Ammo:* shells. *Counter:* range; hard armour against pellets.

**SG3 Both Barrels** (sawn-off double) | L-M | Hand | S | `semi pellets kin force shells`
*Does:* one or both barrels; both barrels knock a person flat and shove you back. *Look and sound:* a stubby brute, a double slam, a long pause while you thumb in two shells. *Why:* the alpha strike: the last thing a charging beast meets, and the only gun that staggers big creatures. *Cost:* two shots; the recoil spoils your footing. *Ammo:* shells. *Counter:* make it fire early.

**SG4 Drum Sweeper** (auto shotgun) | M | Hand | S | `auto pellets kin shells`
*Does:* the design doc's auto shotgun: a drum of shells held on the trigger. *Look and sound:* a fat drum, a pounding roar. *Why:* clears a corridor or a nest room when things come from everywhere. *Cost:* eats shells, heavy, long drum swap. *Ammo:* shells. *Counter:* run it dry; stay beyond 15 m.

**SG5 Shardcaster** (bouncing fragment gun) | H | Hand | S | `auto pellets bounce kin scrap`
*Does:* fires a fan of hot metal slivers that skip off walls, floors and ceilings. *Look and sound:* glowing tracks that fade, a shrieking whine off every bounce. *Why:* fills a room or vent with no aim needed, and it's the answer to things that hide in walls and ducts. *Cost:* you're in the room too; it's strong against soft targets only. *Ammo:* any scrap steel, fed in as a bar. *Counter:* armour, and leaving the room.

### 4.5 Long guns

**LG1 Spotter's Rifle** (marksman rifle) | M | Hand | S | `semi bolt kin mark rounds`
*Does:* a semi-auto rifle with a small scope; a hit outlines the target on your squad's visors for ten seconds. *Look and sound:* lean, a medium crack, a tiny tick as the mark lands. *Why:* the co-op long gun and the seeker's partner: its mark feeds lock-ons and homing rounds. *Cost:* doesn't kill in one. *Ammo:* rifle rounds. *Counter:* break line of sight; marks fade in cover after a few seconds.

**LG2 Stalking Rifle** (sniper rifle) | M | Hand | S | `semi bolt kin rounds`
*Does:* the design doc's sniper: picks off guards and turrets from far beyond their range. *Look and sound:* long, scoped, a whip-crack and then the sound arrives. *Why:* lets you thin a base before it knows. *Cost:* slow, sways, and a scope glints in sunlight. *Ammo:* heavy rounds. *Counter:* glint gives a warning; counter-snipers; fog and night.

**LG3 Bench Gun** (anti-materiel rifle) | M | Hand | M | `charge bolt pierce kin breach rounds`
*Does:* a five-foot rifle that must be set on a bipod or a ledge, and goes through walls, drives and gunners' seats. *Look and sound:* a muzzle brake that throws dust, a slam that stuns birds out of trees. *Why:* lets a person on foot kill a device on a vehicle: thrusters, turrets, seats. *Cost:* can't be fired standing; so loud it draws the whole site. *Ammo:* anti-materiel rounds, heavy, five a stack. *Counter:* move: it can't track; shields.

### 4.6 Machine guns and rotaries

**MG1 Hand Organ** (crank volley gun) | L | Hand, Placed, Hover | S | `crank bolt kin rounds`
*Does:* a bundle of barrels turned by a hand crank; faster cranking fires faster and throws the shots wider. *Look and sound:* brass, a clattering rhythm you set yourself. *Why:* low-tech firepower that can go on a cart or hover deck before you own any turret. *Cost:* needs two hands, standing still, and it loses all accuracy if pushed. *Ammo:* rifle rounds on a strip. *Counter:* flank it: it swings slowly.

**MG2 Squad Belt** (light machine gun) | M | Hand | S | `auto bolt kin stagger rounds`
*Does:* a belt-fed gun with a bipod; fire near people and they duck. *Look and sound:* a long ripping burr, brass spilling. *Why:* suppression: AI soldiers pinned by near misses stop shooting, so a team can move. *Cost:* slow to carry, very long reload, can't hip-fire well. *Ammo:* 12 mm belt shared with vessels. *Counter:* flank; rockets at its position.

**MG3 Tripod Heavy** (heavy machine gun) | M | Placed, Hover | S | `auto bolt pierce kin rounds`
*Does:* a tripod gun you carry in two trips, set up, and sit behind. *Look and sound:* a slow heavy thudding, ground dust jumping. *Why:* the hold-this-door weapon during an invasion: damage of a vehicle gun for the price of being fixed. *Cost:* set-up time, narrow arc, you're exposed behind it. *Ammo:* heavy belt. *Counter:* grenades, mortars, snipers.

**MG4 Spin Gun** (rotary gun) | M-H | Hand, Hover, Small, Turret | M | `hold auto bolt kin rounds`
*Does:* barrels spin up over a second, then a hose of rounds; a limiter switch trades climb for speed. *Look and sound:* a rising whine then a solid tearing roar, spent cases in a stream. *Why:* the biggest damage on foot, with a real wind-up you must plan around. *Cost:* heavy suit only by hand; walking pace while firing; ammo melts away. *Ammo:* belt. *Counter:* make it spin down and up again; get behind it.

**MG5 Lamp Rotary** (laser rotary) | H | Hand, Turret | M | `hold beam en light cells`
*Does:* a spinning lens stack that throws a near-solid pale beam; superb on shields, weak on plate. *Look and sound:* a glassy humming wheel, a pale column. *Why:* a shield-breaker on foot, for when your team needs a ship's shield down before the boarding. *Cost:* huge cell use; heat lock at the end of a long burst. *Ammo:* cells or a backpack cell rack. *Counter:* wait out the heat; plate.

**MG6 Chatter Gun** (vessel gatling) | M | Hover, Small | S | `hold auto bolt kin rounds`
*Does:* the existing gatling block given its feel: fixed forward hose for strafing runs. *Look and sound:* a stutter that shakes the chair, a line of ground dust. *Why:* the first vehicle gun: cheap, plentiful, kills soft targets from the air. *Cost:* poor on armour; belts empty fast on a long trip. *Ammo:* 12 mm belt from shared storage. *Counter:* armour; pop up behind the run.

### 4.7 Bows, slings, air and spring

**BW1 Cord Sling** (sling) | L | Hand | S | `charge lob kin none`
*Does:* whirl and release any stone or scrap. *Look and sound:* a rising whirr, a flat snap. *Why:* no ammo item at all, so a sleeper always has a ranged answer. *Cost:* hard to aim, weak. *Ammo:* any rock. *Counter:* armour.

**BW2 Hunter's Bow** (recurve bow) | L | Hand | S | `charge bolt kin bolts`
*Does:* hold to draw; arrows drop and can be picked up. Takes trick heads: fire, rope, whistle, blunt. *Look and sound:* a creak, a soft thrum, feathers. *Why:* silent hunting that doesn't scare the herd, and trick heads make it a tool. *Cost:* a full draw tires you. *Ammo:* arrows; recoverable. *Counter:* armour, range.

**BW3 Strap Crossbow** (crossbow) | L-M | Hand | S | `semi bolt stick kin bolts`
*Does:* a loaded bow you can carry cocked; heavy bolts stick in things. *Look and sound:* a clunk, a whistle, a thud. *Why:* a ready silent shot, stronger than a bow, that pins light creatures to walls. *Cost:* slow to crank back. *Ammo:* bolts; recoverable. *Counter:* armour; the slow reload.

**BW4 Rack Bow** (repeating crossbow) | L | Hand | S | `auto bolt kin bolts`
*Does:* a lever-fed box of light bolts, one every half second. *Look and sound:* a wooden clack, like a loom. *Why:* a whole low-tech squad's volley from one person, silent enough for ambushes. *Cost:* light bolts; useless on anything plated. *Ammo:* short bolts. *Counter:* armour; shields.

**BW5 Blow Tube** (blowgun) | L | Hand | S | `semi bolt kin rounds`
*Does:* a breath-driven dart: alone it scratches, loaded with sleep or tick capsules it does their job. *Look and sound:* a short hollow puff. *Why:* the quietest delivery in the game for low-tech players and swamp folk. *Cost:* very short range. *Ammo:* darts. *Counter:* armour, helmets.

**BW6 Pump Gun** (pneumatic rifle) | L-M | Hand | M | `pump bolt kin rounds`
*Does:* pump pressure by hand between fights; power falls as the tank empties; over-pump and accuracy goes and it bleeds off. *Look and sound:* a pressure gauge on the stock, a pneumatic spit. *Why:* near-silent, no powder, works anywhere, and rewards keeping an eye on the gauge. *Cost:* pumping in the open. *Ammo:* ball bearings, plus elbow grease. *Counter:* catch the user pumping; armour.

**BW7 Boiler Rifle** (steam rifle) | L | Hand | M | `charge bolt kin water`
*Does:* a boiler pack feeds a piston barrel; hold to build pressure, release to fire. *Look and sound:* copper, a hiss, a plume of steam from the shoulder. *Why:* ammo from water and fuel, so an iron-age settlement can arm itself with no factory. *Cost:* the plume gives your position away; empty boiler, empty gun. *Ammo:* water and biofuel. *Counter:* follow the plume; freezing weather.

**BW8 Crank Spitter** (clockwork needle gun) | L | Hand | S | `crank pellets kin scrap`
*Does:* a mainspring wound by hand throws thirty light needles. *Look and sound:* a tick-tick wind, a purring whirr. *Why:* it works in total power failure and EM pulses, when every smart gun in the party is dead. *Cost:* winding takes as long as firing. *Ammo:* needles cut from scrap wire. *Counter:* armour.

**BW9 Pin Driver** (nail or stake gun) | L-M | Hand | S | `semi bolt stick kin pin scrap`
*Does:* a construction tool that drives long spikes; a hit near a surface pins the target there for a few seconds. *Look and sound:* a pneumatic whump and a ring of steel. *Why:* a tool you already own turned weapon, and the only low-tech way to hold a creature in place. *Cost:* slow, short. *Ammo:* spikes from iron. *Counter:* open ground: nothing to pin to.

**BW10 Kin Bow** (reward crossbow) | H | Hand | S | `semi bolt kin fleshonly bolts`
*Does:* the design doc's reward weapon: bolts harm only living targets and pass through machines and hull as if they were mist. *Look and sound:* bone and sinew, no sound at all on impact. *Why:* kills a crew or a boarding party without scratching the ship you want. *Cost:* can't be crafted; useless on robots and turrets. *Ammo:* special bolts, a reward item. *Counter:* machines.

**BW11 Winch Ballista** (bolt thrower) | L | Turret | M | `charge bolt pierce kin bolts`
*Does:* a winched bow on a wall throws a heavy bolt through several things in a line. *Look and sound:* timber groans, a deep twang. *Why:* gives low-tech settlements a real answer to hover raiders and big beasts, and it's a crewed gun the player can sit at. *Cost:* very slow. *Ammo:* iron bolts. *Counter:* keep moving.

**BW12 Counterweight Thrower** (trebuchet) | L | Turret | L | `charge lob exp kin scrap`
*Does:* throws boulders, fire pots or a dead animal over walls. *Look and sound:* a long creak, a pause, a far-off crump. *Why:* the low-band siege engine: the way to crack a stone keep with no powder at all. A carcass load draws creatures to the impact point. *Cost:* huge, slow, obvious. *Ammo:* stone, fire pots, carcasses. *Counter:* it can't hit anything that moves.

### 4.8 Hand grenades

**GR1 Stick Bomb** (fused stick grenade) | L | Hand | S | `timer thrown exp grenades`
*Does:* a tin on a handle with a burning fuse; hold it to cook. *Look and sound:* a fizzing spark, a dull bang and dirt. *Why:* the first explosive anyone can make, and the first way to kill something behind a rock. *Cost:* fuses vary by a second; cook too long and it's yours. *Ammo:* magnesium powder, tin. *Counter:* run; throw it back.

**GR2 Frag Ball** (fragmentation grenade) | M | Hand | S | `timer thrown exp grenades`
*Does:* the staple four-second grenade. *Look and sound:* a pin ping, a sharp crack, a grey puff. *Why:* flushes people from cover and clears a room through a door. *Cost:* bounces back from door frames; friendly fire. *Ammo:* grenades. *Counter:* a wall between you and it.

**GR3 Skip Ball** (bounding grenade) | M | Hand | S | `timer thrown split exp grenades`
*Does:* bursts upward at waist height after landing, so lying down doesn't help. *Look and sound:* a sproing, then a crack at chest level. *Why:* beats low cover and prone soldiers; good over a parapet. *Cost:* low blocks and ceilings set it off early. *Ammo:* grenades. *Counter:* solid overhead cover.

**GR4 Clinger** (sticky grenade) | M | Hand | S | `timer thrown stick exp grenades`
*Does:* sticks to whatever it hits, including creatures and vehicles. *Look and sound:* a wet slap, a rising beep. *Why:* the on-foot answer to a moving hover vessel, or to a beast that won't stay still. *Cost:* short range; a stuck beast comes for you, carrying it. *Ammo:* grenades. *Counter:* scrape it off in time, or jump in water.

**GR5 Thump Ball** (concussion grenade) | M | Hand | S | `timer thrown exp force stagger noblock grenades`
*Does:* a pressure pulse with little shrapnel: throws bodies, leaves walls standing. *Look and sound:* a thud you feel in your chest, dust jumping off walls. *Why:* pushes things off ledges and out of doorways, and a player can ride it as a jump. *Cost:* almost no damage. *Ammo:* grenades. *Counter:* heavy targets, bracing; no effect in vacuum beyond the shell.

**GR6 Smoke Pot** (smoke grenade) | L-M | Hand | S | `timer thrown field blind capsule`
*Does:* a drifting wall of smoke that blocks sight, lasers and lock-ons. *Look and sound:* a hiss and a rolling bank that bends with the wind. *Why:* the universal escape, and it stops the first shots of an ambush cold. *Cost:* blinds you too, and wind takes it. *Ammo:* single use, low-tech recipe. *Counter:* heat sight; hurricanes.

**GR7 Flash Cracker** (flashbang) | M | Hand | S | `timer thrown pulse light blind deaf capsule`
*Does:* blinds and deafens anything facing it for a few seconds. *Look and sound:* a white wipe and a high ringing tone. *Why:* breaking a held room without killing anyone; hostages, captures and stealth. *Cost:* you must look away too. *Ammo:* single use. *Counter:* visors with flash filters; machines recover at once.

**GR8 Pulse Ball** (EMP grenade) | H | Hand | S | `timer thrown pulse em shock overload capsule`
*Does:* a sphere of pulse that stops devices, robots and cyborg implants for seconds, empties personal shields and strips small vessel shields. *Look and sound:* a dull blue flash and every light in the room dims. *Why:* the room-clearer against machines, turrets and Husks, which drop until it wears off. *Cost:* kills your own shield, lights, scanner and any smart weapon in range. *Ammo:* single use. *Counter:* creatures don't care; shielded circuits.

**GR9 Choke Pot** (gas grenade) | M | Hand | M | `timer thrown field acid slow capsule` | Needs ROOM
*Does:* heavy gas that lies low and fills a room; breathing things choke and take damage while they're in it. Any flame sets it off. *Look and sound:* a yellow-green creep along the floor, coughing. *Why:* flushes a sealed bunker without touching the structure, and it stacks with fire into a blast. *Cost:* doors, wind and vents move it; useless on anything with sealed suits. *Ammo:* single use. *Counter:* open a door, wear a helmet, robots. It stops harming the moment you leave.

**GR10 Brood Bomb** (cluster grenade) | M | Hand | S | `timer thrown split exp grenades`
*Does:* the shell splits into six small bouncing bomblets. *Look and sound:* a pop and a scatter of clicks, then a string of small bangs. *Why:* covers a whole floor or a pack in the open. *Cost:* bomblets roll where they like, and duds stay live. *Ammo:* grenades. *Counter:* spacing; slopes carry them away.

**GR11 Chatterbox** (noise decoy) | M | Hand | S | `timer thrown placed lure decoy capsule`
*Does:* a ball that plays fake gunfire and footsteps for fifteen seconds. *Look and sound:* a tinny firefight in a tin. *Why:* pulls guards and creatures through the noise system, with no violence. *Cost:* draws things you didn't know were there. *Ammo:* single use, reusable if you retrieve it. *Counter:* it's found; machines check twice.

### 4.9 Grenade launchers

**GL1 Stovepipe** (black-powder grenade tube) | L | Hand | S | `semi lob exp grenades`
*Does:* a capped iron pipe that lobs a fused bomb. *Look and sound:* a hollow cough, a lazy tumbling arc. *Why:* doubles grenade range for a raider with no factory. *Cost:* single shot, high arc, fuse timing is a guess. *Ammo:* stick bombs. *Counter:* dodge the arc.

**GL2 Break Launcher** (single-shot launcher) | M | Hand | S | `semi lob bounce exp grenades`
*Does:* the design doc's launcher: a toggle sets impact or bounce, and it's the door-breacher. *Look and sound:* a thoomp, a smoking chamber. *Why:* indirect fire on foot: drop rounds behind cover and through windows. *Cost:* one at a time; bounces come back. *Ammo:* grenades, any shell in section 6. *Counter:* stay moving; close the distance.

**GL3 Drum Launcher** (revolver launcher) | M | Hand | S | `auto lob exp grenades`
*Does:* six rounds in a drum as fast as you can pull. *Look and sound:* a heavy clunk-clunk-clunk, a thudding walk of blasts. *Why:* saturates a street or a hive mouth in seconds. *Cost:* heavy, slow reload, shells gone in one fight. *Ammo:* grenades. *Counter:* run it dry.

**GL4 Gum Launcher** (sticky pod launcher) | M | Hand | S | `semi lob stick exp det grenades`
*Does:* pods stick and sit until you press detonate, in any order. *Look and sound:* wet thumps, a blinking row, a click, a chain of bangs. *Why:* turns a corridor into a prepared trap in seconds, and it's how you beat a thing that's faster than your aim. *Cost:* pods are visible and can be shot; lose line to them and they go dead. *Ammo:* pods. *Counter:* shoot or avoid the pods.

**GL5 Set-Range Launcher** (airburst launcher) | M-H | Hand | M | `semi lob split exp grenades`
*Does:* laze a distance, and the shell bursts in the air at that distance. *Look and sound:* a range figure on the sight, an overhead crack and a downward fan. *Why:* the answer to trenches and parapets: it bursts past the wall. *Cost:* needs a beat to laze; ceilings ruin it. *Ammo:* smart grenades. *Counter:* roofs.

**GL6 Seed Launcher** (cluster launcher) | M | Hand, Hover | S | `semi lob split exp grenades`
*Does:* a carrier shell bursts above the target and drops a dozen bomblets. *Look and sound:* a high pop, a rain of small flashes. *Why:* wide area, for packs in the open and dug-in crews. *Cost:* duds linger as hazards and can hit you later. *Ammo:* carrier shells. *Counter:* spread out, get under anything.

**GL7 Hop Launcher** (smart hopping grenade) | H | Hand | M | `semi lob bounce seek-lock exp grenades`
*Does:* the grenade hops along the ground toward the nearest enemy and goes off on contact. *Look and sound:* a beetle-sized click-hop-click, faster each hop. *Why:* takes grenades round corners with no skill, and scares anyone behind cover. *Cost:* expensive, and it can pick the wrong target, you included if alone. *Ammo:* smart grenades. *Counter:* shoot it, or give it a nearer decoy.

### 4.10 Rockets and missiles

**RK1 Fire Rack** (black-powder rocket rack) | L | Placed, Hover | S | `auto bolt exp fire rockets`
*Does:* twenty paper tubes on a cart fired in a ripple, each with a mind of its own. *Look and sound:* a hissing shriek, sparks and smoke everywhere. *Why:* the low-band terror weapon: wide, loud and frightening; animals flee and raiders scatter. *Cost:* wildly inaccurate, sets fire to you on a bad day. *Ammo:* powder tubes. *Counter:* anywhere but the middle of the field.

**RK2 Throwaway Tube** (disposable launcher) | M | Hand | S | `semi bolt exp rockets`
*Does:* one rocket in a sealed tube; fire and drop it. *Look and sound:* a pop-out sight, a backblast bark. *Why:* gives any light loadout a one-time tank answer. *Cost:* one shot, and you carried the weight all day. *Ammo:* it is the ammo. *Counter:* kill the shooter on the way up.

**RK3 Shoulder Tube** (rocket launcher) | M | Hand | S | `semi bolt exp breach rockets`
*Does:* the design doc's anti-armour rocket: damages blocks and vehicles. *Look and sound:* a blinding backblast, a roaring trail. *Why:* the on-foot way to crack vehicles, doors and devices. *Cost:* backblast hurts anyone behind you; self-harm up close. *Ammo:* rockets, also fed to rocket pods. *Counter:* it's slow enough to dodge at range; point defence.

**RK4 Lock Tube** (lock-on launcher) | M | Hand, Hover | S | `lockon bolt seek-lock exp rockets`
*Does:* hold on target until the tone turns solid, then the rocket chases it. *Look and sound:* a rising warble, then a steady note heard by the target. *Why:* a reasonable chance of hitting a flyer from the ground. *Cost:* you must hold still in the open during the lock; the target gets the warning. *Ammo:* guided rockets. *Counter:* flares, terrain, breaking line of sight.

**RK5 Wire Tube** (wire-guided launcher) | M | Hand, Hover | M | `hold guided seek-wire exp rockets`
*Does:* a rocket that follows your crosshair down a thin wire laid out behind it. *Look and sound:* a fine wire on the ground for a minute afterwards, a throaty whirr. *Why:* can't be jammed or flared, and you can steer it round a corner or in through a window. *Cost:* you stand still; if you die or move far, it flies straight. The wire must sag and lie on the ground, never float. *Ammo:* wire rockets. *Counter:* shoot the gunner; cut the wire.

**RK6 Twin Charge Tube** (tandem warhead) | M-H | Hand | S | `semi bolt split exp breach strip rockets`
*Does:* a small lead charge then a big follow-through a moment later: pops shield ripple or reactive plate, then the main charge lands. *Look and sound:* a double bang, tick-boom. *Why:* the specialist against combat steel and small shields. *Cost:* costly, poor against soft targets, and useless at very short range. *Ammo:* tandem rockets. *Counter:* spaced armour with a gap.

**RK7 Hot Breath Tube** (thermobaric launcher) | M | Hand | M | `semi bolt exp fire ignite rockets`
*Does:* a fuel cloud goes off a moment after the hit; the blast rolls round corners and is huge in sealed rooms. *Look and sound:* a flat pop, a beat, a rolling orange thump that sucks the air. *Why:* kills what's inside a building without hitting it directly. *Cost:* useless in vacuum, thin air and open wind; indoors it reaches you too. *Ammo:* thermobaric rockets. *Counter:* open-air positions, vacuum, airlocks.

**RK8 Flock Launcher** (swarm launcher) | H | Hand, Hover, Small | M | `lockon bolt split seek-lock exp rockets`
*Does:* tag up to eight targets, fire a salvo of finger-sized rockets that weave to them. *Look and sound:* a rack of clicks, a chorus of whooshes. *Why:* one press for a mob of small targets. *Cost:* small warheads; bad against armour; a long reload. *Ammo:* mini rockets. *Counter:* flak, flares, armour.

**RK9 Eye Rocket** (camera rocket) | H | Hand | M | `hold guided seek-wire exp rockets` | Needs VIEW
*Does:* your view jumps into the rocket's nose; you fly it. Your body stands there meanwhile. *Look and sound:* a fisheye feed, a rising roar, static on impact. *Why:* scout and strike through a base's corridors, or kill a target that never shows itself. *Cost:* your body is defenceless while you fly; a co-op weapon or a safe-room weapon. *Ammo:* camera rockets. *Counter:* find the body; shoot the rocket in a corridor.

**RK10 Rocket Pod** (vessel rocket pod) | M | Hover, Small | S | `semi bolt exp rockets`
*Does:* the existing small and large pods given a real role: paired dumb rockets. *Look and sound:* a double whoosh past the canopy. *Why:* the first vessel weapon that hurts armour and bases. *Cost:* few shots; must point the whole craft. *Ammo:* rockets. *Counter:* flak; dodge.

**RK11 Sky Battery** (anti-air missile turret) | M | Turret | M | `lockon bolt seek-lock exp rockets` | Needs ORDNANCE
*Does:* a base launcher that guides missiles at flyers and small vessels. *Look and sound:* a rail swings, a tone on the pilot's panel, a white climbing line. *Why:* turns any base into a no-fly zone; the world's old SAM sites become playable. *Cost:* CPU, ammo, and a long minimum range. *Ammo:* missiles. *Counter:* fly low, flares, kill the radar.

**RK12 Salvo Wall** (capital rocket ripple) | M-H | Turret, Capital | M | `auto bolt split seek-lock exp rockets` | Needs ORDNANCE
*Does:* a wall of hatches opens and throws a hundred curling missiles. *Look and sound:* the hull covered in small lights, white trails braiding across the sky. *Why:* the moment the fight becomes a painting: it saturates point defence by numbers. *Cost:* the magazine is gone in one strike and costs a fortune to refill. *Ammo:* mini rockets by the crate. *Counter:* point defence in depth, armour, jump out.

**RK13 Long Reach** (cruise missile) | M-H | Turret, Capital | M | `lockon bolt seek-mark exp breach rockets` | Needs ORDNANCE
*Does:* a slow, heavy missile with a huge range that flies to a marked spot. *Look and sound:* a booster bang, then a dull jet drone that you hear from far off. *Why:* hits beyond any turret's reach, so you can snipe a base from over the hill. *Cost:* slow, so every point-defence gun gets a turn at it; needs a spotter's mark. *Ammo:* cruise missiles. *Counter:* shoot it down. Warned, never ambushed.

### 4.11 Mortars, artillery and bombs

**AR1 Iron Bombard** (powder siege gun) | L | Turret | M | `charge lob exp rockets`
*Does:* a huge iron barrel throws a ball that bounces and rolls. *Look and sound:* a boom with a smoke ring, a ball visibly rolling. *Why:* low-tech settlements can defend a wall against hover raiders. *Cost:* minutes to reload, can't hit moving targets. *Ammo:* iron balls and powder. *Counter:* move.

**AR2 Knee Mortar** (hand mortar) | M | Hand | S | `semi lob exp shells`
*Does:* a short tube braced on the ground or knee that drops shells 200 m away. *Look and sound:* a tonk, a two-second whistle. *Why:* lets a person out-range a turret from behind a hill. *Cost:* you must crouch and can't aim directly; needs a line-of-sight spotter or a good guess. *Ammo:* mortar shells. *Counter:* move on the whistle.

**AR3 Field Mortar** (placed mortar) | M | Placed | M | `semi lob exp shells`
*Does:* a baseplate tube that fires at a map point or a spotter's mark. *Look and sound:* a heavy thunk and a rising whistle. *Why:* a real siege tool for a small team: one spots, one fires. *Cost:* you set it up; can't defend itself. *Ammo:* mortar shells: blast, smoke, fire, light. *Counter:* mobile targets; counter-fire.

**AR4 Crawler Gun** (hover howitzer) | M | Hover | S | `charge lob exp shells`
*Does:* a long gun on a hover hull; can't fire while moving. *Look and sound:* the craft drops on its skirts, then the ground jumps. *Why:* a mobile base-cracker with a big dead zone, so it needs escorts. *Cost:* power to settle; slow. *Ammo:* artillery shells. *Counter:* get inside its minimum range.

**AR5 Fortress Howitzer** (artillery turret) | M | Turret | S | `charge lob exp breach shells`
*Does:* the design doc's artillery: long-range siege against big slow targets. *Look and sound:* a slow barrel traverse, a blast that shakes the camera. *Why:* takes bases apart from outside their range. *Cost:* slow shells, poor against small targets. *Ammo:* artillery shells; EM, smoke and fire shells as extras. *Counter:* be small; be fast; shields.

**AR6 Bomb Bay** (dumb bombs) | M | Small | S | `semi lob exp shells`
*Does:* a belly door drops free-fall bombs. *Look and sound:* a clunk under the seat, a long fall, a blast behind you. *Why:* huge payload for a small vessel; a whole different flying skill; vanilla keeps bombs for enemy machines only. *Cost:* you must fly over the target. *Ammo:* bombs. *Counter:* anti-air on the way in.

### 4.12 Mines and placed explosives

**MN1 Powder Keg** (fused barrel) | L | Placed | S | `timer placed exp fire ignite grenades`
*Does:* a barrel you roll into place and light, or that goes off when shot. *Look and sound:* a slow fizz, a roar and burning staves. *Why:* the frontier's demolition charge, and also a trap you can set off from afar with one rifle round. *Cost:* heavy; a stray bullet sets it off beside you. *Ammo:* powder, a cask. *Counter:* shoot it early.

**MN2 Pressure Mine** (pressure plate) | M | Placed | S | `placed exp mines`
*Does:* buried in soil or under a floor; goes off when something heavy steps on it. *Look and sound:* a faint disturbed patch, a click, a wall of dirt. *Why:* area denial on roads and approaches; patient defence. *Cost:* stays until found, including by you. *Ammo:* mines, stacked. *Counter:* detectors; shoot it; light creatures don't set it off.

**MN3 Trip Line** (tripwire charge) | L-M | Placed | S | `placed tether exp mines`
*Does:* a fine wire between two anchor points sets off a charge. *Look and sound:* a thin glint in the torch, a soft ping. *Why:* doors and corridors become traps that work on sight lines, not stepping. The wire is anchored at both ends and sags. *Cost:* two anchors, and allies trip it. *Ammo:* mine plus wire. *Counter:* look for the glint; step over; cut it.

**MN4 Bounding Mine** (jumping mine) | M | Placed | S | `placed split exp mines`
*Does:* a spring throws it to chest height before it bursts. *Look and sound:* a metallic twang, a half-second of dread, then a crack. *Why:* the tension mine: a sound that gives you a beat to dive. *Cost:* no use in low rooms. *Ammo:* mines. *Counter:* drop flat on the twang.

**MN5 Fan Mine** (directional mine) | M | Placed | S | `placed cone kin exp det mines`
*Does:* a curved plate on legs throws a fan of steel balls in one direction. *Look and sound:* a small curved plate on wire legs, a bright arrow stencil, a wide bang. *Why:* covers a hall or road, fired by you or by a tripwire. *Cost:* backblast; needs to face the right way. *Ammo:* mines. *Counter:* come from behind; heavy armour.

**MN6 Remote Satchel** (remote charge) | M | Placed | S | `placed exp det breach mines`
*Does:* a pack you throw down and set off by handset. *Look and sound:* canvas, a red light, one click. *Why:* the ambush weapon: wait for the patrol, then press. *Cost:* the handset takes a hand slot; a pulse grenade kills the link. *Ammo:* satchels. *Counter:* spot and shoot it; EM.

**MN7 Limpet** (magnetic mine) | M | Placed | S | `timer placed stick exp breach mines`
*Does:* clamps to hull and bursts inward on a timer. *Look and sound:* a heavy clang, a slow beep. *Why:* a swimmer or EVA player's answer to a parked capital ship. *Cost:* you must reach the hull. *Ammo:* limpets. *Counter:* crew walking the hull; shake it off with a jump.

**MN8 Cutting Charge** (shaped demolition charge) | M | Placed | S | `timer placed exp breach mines`
*Does:* a strip you stick on a wall that blows one neat door-sized hole. *Look and sound:* a line of putty, a sharp crack, a rectangle of hull falling in. *Why:* the boarding door: an entry you choose, not the one they've guarded. *Cost:* slow placement and a fuse; little use against people. *Ammo:* charges. *Counter:* armour two plates thick, or a surprise on the other side.

**MN9 Picky Mine** (selective mine) | H | Placed | M | `placed seek-metal exp mines`
*Does:* set it to ignore light things, or flesh, or metal, or anything below a size. *Look and sound:* a little dial, a green eye. *Why:* can sit in a creature-rich valley for days waiting only for vehicles. *Cost:* expensive; the setting is the risk. *Ammo:* smart mines. *Counter:* drive something cheap across it.

**MN10 Drift Mine** (space mine) | M-H | Placed, Small, Capital | M | `placed float seek-metal exp mines` | Needs ORDNANCE
*Does:* a fist-sized body sits in orbit and nudges toward passing ships. *Look and sound:* a lone blinking point, a soft radar ping. *Why:* blockades, ambushes and defended orbits; a mine field reads on the scanner like a dotted line. *Cost:* mines don't know whose side you're on when you come back. *Ammo:* mines. *Counter:* flak, or sending one cheap hull first.

**MN11 Tide Mine** (sea mine) | M | Placed, Hover | S | `placed float exp mines`
*Does:* a horned ball moored to the seabed on a cable, floats just under the surface. *Look and sound:* a dark round shape, a low clank, a column of water. *Why:* closes straits on ocean worlds; hover craft treat water as ground and must learn it. The cable stays anchored. *Cost:* you need a boat or hover to lay it. *Ammo:* mines. *Counter:* fly over; sweep.

**MN12 Seeder Rack** (mine layer) | M | Small, Capital | S | `auto placed exp mines` | Needs ORDNANCE
*Does:* a rack that strews mines in a line behind a vessel. *Look and sound:* a thud-thud and a row of blinking points. *Why:* covers a retreat: a chase turns into a minefield. *Cost:* storage and mass; can't aim. *Ammo:* drift mines. *Counter:* don't follow directly behind.

**MN13 Prox Mine** (proximity mine) | M | Placed | S | `placed exp mines`
*Does:* a thrown puck that arms in three seconds and goes off when anything comes within two metres. *Look and sound:* a small red eye that blinks faster as you near it, a sharp crack. *Why:* the plain staple: it works on any surface, in a hallway or under a parked vessel, and guards your back while you loot. *Cost:* it doesn't care whose feet they are. *Ammo:* mines. *Counter:* the blink warns you; shoot it from range.

**MN14 Caltrop Scatter** (foot spikes) | L | Placed | S | `thrown placed kin slow scrap`
*Does:* a handful of four-point spikes that slow and scratch anything on foot. *Look and sound:* a rattle on stone, a limp. *Why:* a cheap chase-breaker, and it spoils the feet of tyred or skirted vehicles a little too. Slows for a few seconds only. *Cost:* you can step on them yourself. *Ammo:* scrap iron. *Counter:* heavy boots, hover, sweeping them aside.

**MN15 Spike Pit** (covered pit) | L | Placed | M | `placed kin dig pin scrap`
*Does:* a dug hole with stakes and a thin cover. *Look and sound:* a slightly wrong patch of ground, a crack and a fall. *Why:* a low-tech trap that catches large creatures, and it uses the existing dig rule. *Cost:* slow to dig and hard to dig in rock. *Ammo:* wood stakes. *Counter:* look at the ground; flying.

### 4.13 Accelerators

**AC1 Scrap Coil** (home-built coilgun) | L-M | Hand | M | `charge bolt kin scrap cells`
*Does:* wound from salvage, runs off a vehicle battery, throws a bolt or nail with a loud snap. *Look and sound:* bare copper windings on a pipe, a rising whine, a flat crack and a smell of hot varnish. *Why:* the earliest taste of the high-tech gun; a survivor's project from what's lying in the ruins. *Cost:* slow charge, a long cool-down after each shot, and any charge on it goes to waste if you hesitate. *Ammo:* any steel rod. *Counter:* close in during the whine.

**AC2 Coil Carbine** (coil gun) | M | Hand | S | `auto bolt kin slugs cells`
*Does:* silent muzzle, only a supersonic crack at the target; full auto. *Look and sound:* a smooth tube with no ports, a soft thuddering tick. *Why:* an automatic you can't locate by muzzle sound or flash. *Cost:* needs both slugs and cells, so two things can run out. *Ammo:* slugs, cells. *Counter:* armour; follow the tracer-less crack by watching dust.

**AC3 Stage Rifle** (Gauss rifle) | M-H | Hand | M | `charge bolt pierce kin slugs cells`
*Does:* hold to climb through four capacitor stages; each stage adds speed and pierce. *Look and sound:* ascending clicks, four rings glowing up the barrel, a bark. *Why:* you choose between many light shots and one wall-piercer in the middle of a fight. *Cost:* the full stage makes you stand still with a glow that's seen from far off. *Ammo:* slugs, cells. *Counter:* the glow gives you time.

**AC4 Rail Lance** (hand railgun) | H | Hand | M | `charge ray pierce kin breach slugs cells`
*Does:* one shot that passes through everything in a line for its full reach, walls included. *Look and sound:* an arc-blue spit at the muzzle, a bright trail that hangs in the air, a crack like a split sky. *Why:* lets you hit what's behind cover if you know it's there; pairs with heat sight and marks. *Cost:* each shot empties a full cell and locks the gun for a long cool-down; the trail shows everyone where you are. *Ammo:* slugs, cells. *Counter:* the trail; don't line up.

**AC5 Sled Rail** (hover railgun) | M-H | Hover | S | `charge ray pierce kin breach slugs power`
*Does:* a long gun along a hover hull that draws on spare power. *Look and sound:* the lights dim, a white thread, the craft jolts backwards. *Why:* the ground-assault sniper: hover speed plus one huge hit. *Cost:* per the design, thrusters use up spare power, so you stop to shoot. *Ammo:* slugs. *Counter:* catch it while it recharges.

**AC6 Fighter Rail** (small-vessel railgun) | M-H | Small | S | `charge ray pierce kin breach slugs power`
*Does:* the design's railgun: charges from the generator's surplus and pierces armour. *Look and sound:* a shudder in the chair and a line of light. *Why:* picks devices out of a hull; chooses a turret and kills it. *Cost:* few shots, power draw. *Ammo:* slugs. *Counter:* shields, jinking at range.

**AC7 Line Battery** (rail turret) | H | Turret | M | `charge ray pierce kin breach slugs power`
*Does:* a twin rail on a heavy mount, long range, accurate, slow to turn. *Look and sound:* barrels recoiling in a block, deck lights flickering. *Why:* the capital duel gun: wins by reach, loses to small fast targets. *Cost:* power and turn rate. *Ammo:* large slugs. *Counter:* get inside it.

**AC8 Mass Driver** (slow heavy launcher) | M | Turret, Small | M | `charge lob kin force scrap`
*Does:* a big coil throws a slow lump of anything: ore, ice, wreck. *Look and sound:* a long hum, a thud in the deck, a dark dot visibly arcing. *Why:* ammo from rock: a mining ship can fight, and a base can use its own tailings. *Cost:* slow, easy to dodge. *Ammo:* raw ore or scrap. *Counter:* move.

**AC9 Spinal Lance** (spinal mount) | H | Capital | L | `charge ray pierce kin breach slugs power`
*Does:* a rail the length of the ship, fixed forward; the whole ship must turn to aim. *Look and sound:* the ship's lights go out section by section, the hull rings, the target simply has a hole down its length. *Why:* the capital player's event: a duel of turning, with one shot that can decide it. A vessel built around this looks different. *Cost:* the hull is the gun: needs a straight run of blocks and huge power. *Ammo:* one enormous slug. *Counter:* get off the axis; go broadside.

### 4.14 Lasers and masers

**LS1 Cutter Beam** (mining and salvage laser) | M | Small, Turret | S | `hold beam en cells`
*Does:* the design doc's tool that doubles as a weak weapon. *Look and sound:* a steady orange line and sparks. *Why:* a working ship isn't unarmed. *Cost:* short, low damage. *Ammo:* power. *Counter:* range.

**LS2 Stutter Lamp** (pulse laser) | M | Hover, Small, Turret | S | `auto ray en power`
*Does:* rapid pulses for stripping shields. *Look and sound:* a flickering beam, a harsh stutter. *Why:* the vessel shield-stripper before plasma. Two settings: long burn for blocks, short burn for quick shield work. *Cost:* power and heat; weak on plate. *Ammo:* power or laser charges. *Counter:* armour with shields down.

**LS3 Scorch Line** (held hand beam) | M-H | Hand | S | `hold beam en fire ignite cells`
*Does:* a continuous beam: burns a line and sets fire to soft stuff it crosses. *Look and sound:* a hard ruler-straight line in the dust, a rising sizzle. *Why:* holds on target to rewards steady aim over twitch aim; also writes on walls and lights tinder. *Cost:* builds heat; pinned to your own line. *Ammo:* cells. *Counter:* reflect, block line of sight.

**LS4 Dazzler** (blinding laser) | M | Hand, Turret | S | `hold ray light blind cells`
*Does:* a green glare that blinds whoever's looking at you: guards, gunners and turret sensors. *Look and sound:* an emerald pinpoint that fills the view. *Why:* suppresses a gunner or a sniper with no kill. Works seconds only on players. *Cost:* line of sight in both directions. *Ammo:* cells. *Counter:* flash visors, look away.

**LS5 Prism Rifle** (splitting beam) | H | Hand | M | `semi ray split en light cells`
*Does:* a shot through a glass block or a thrown crystal fans into three, and a shot through voidglass fans into more. *Look and sound:* a coloured fan. *Why:* windows and crystal worlds become weapons, and it can hit a pack with one pull. *Cost:* unsplit it's a mediocre laser. *Ammo:* cells; prism tokens. *Counter:* dark glass, no glass.

**LS6 Cook Box** (maser) | H | Hand, Turret | M | `hold cone en shieldpass cells`
*Does:* an invisible cone that heats water: flesh, wet things, and suits. Goes through thin plastic and wood. *Look and sound:* nothing to see, a thin whine, targets starting to steam. *Why:* heats people behind soft cover and goes straight past a personal shield, and it's a horror to meet in a dark room. *Cost:* metal stops it and bounces it; short range. *Ammo:* cells. *Counter:* steel plate; distance; dry machines.

**LS7 Scald Horn** (pain ray) | M | Hand, Turret | S | `hold cone en fear cells`
*Does:* a skin-deep heat beam, no damage, creatures and people run. *Look and sound:* a dull boxy dish, a low hum. *Why:* crowd control and base defence without kills, which keeps standing with Freeholders. Rain cuts its range. *Cost:* slow to take effect; not on machines. *Ammo:* cells. *Counter:* armour, machines, rain.

**LS8 Burn Tower** (laser turret) | M-H | Turret | S | `hold beam en light power`
*Does:* a tracking beam that cooks whatever it holds. *Look and sound:* a thin bright thread that wanders onto you. *Why:* a turret with no ammo, for bases with spare power. *Cost:* heavy power; weather shortens it. *Ammo:* power. *Counter:* smoke, sand, weather.

**LS9 Lance Array** (capital beam) | H | Capital | M | `charge beam en light power`
*Does:* a two-second visible charge, then a thin bright beam you can sweep along a hull. *Look and sound:* a growing star at the muzzle, a line drawn across the sky. *Why:* the capital's cutting tool; aim is a held sweep, so it can open a ship end to end. *Cost:* needs huge power and a long cool-down. *Ammo:* power. *Counter:* the charge is your warning; sand bursts.

### 4.15 Plasma

**PL1 Spatter Gun** (plasma shotgun) | H | Hand | S | `semi pellets en fire ignite cells`
*Does:* a short burst of hot blobs that stick and burn out. *Look and sound:* green gobs, a wet hiss. *Why:* close range with shield-stripping force. *Cost:* slow blobs, drop fast. *Ammo:* plasma charges. *Counter:* range.

**PL2 Ember Rifle** (plasma rifle) | H | Hand | S | `auto bolt en strip stall cells`
*Does:* a stream of slow bright bolts that build heat; at full heat the gun vents and locks up. *Look and sound:* green bolts, a rising hum, a jet of steam from side ports. *Why:* the standard answer to a shielded soldier: it strips personal shields and small vessel shields, and stalls their refill; plays by managing a heat bar instead of a magazine. *Cost:* overheating burns your hand: a small health hit. *Ammo:* plasma charges. *Counter:* wait for the vent.

**PL3 Slow Sun** (hand plasma cannon) | H | Hand | M | `charge bolt exp en crater cells`
*Does:* two modes: a slow blast ball, or a beam that leaves blocks alone and is safe indoors. *Look and sound:* a growing ball at the muzzle, a soft thump. *Why:* one gun for outdoors and inside your own ship. *Cost:* heavy, slow, costly. *Ammo:* plasma charges. *Counter:* dodge; the ball can be seen coming.

**PL4 Furnace Cannon** (vessel plasma cannon) | H | Hover, Small | S | `semi bolt en exp strip power`
*Does:* the vessel plasma gun: area burn, strong on big devices. *Look and sound:* a green comet, a bloom on the shield. *Why:* a vessel's shield-stripper, with splash that catches thrusters and turrets. *Cost:* slow bolts. *Ammo:* plasma charges. *Counter:* dodge at range.

**PL5 Strip Battery** (plasma turret) | H | Turret | S | `semi bolt en exp strip power`
*Does:* the design's capital main battery: strips shields. *Look and sound:* twin barrels, fat green bolts easy to read. *Why:* the capital's opening move. *Cost:* slow projectiles; rare ammo. *Ammo:* plasma charges, emberite. *Counter:* be fast; armour.

**PL6 Ring Thrower** (plasmoid gun) | H | Hand, Turret | M | `charge bolt en shock overload cells`
*Does:* throws a spinning smoke-ring of plasma that holds together, then bursts as heat and a little EM. *Look and sound:* a hissing halo that rolls through the air. *Why:* hits with both heat and a device jolt; a turret version knocks missiles out of the air. *Cost:* falls apart quickly in thick air. *Ammo:* plasma charges. *Counter:* range, or wind.

**PL7 Bottled Sun** (fusion gun) | H | Hand | M | `charge bolt en exp crater fire cells`
*Does:* a backpack-fed fusion bolt that needs heavy armour, or it hurts you to fire it. *Look and sound:* your suit visor dims itself, a white point, a hard flash. *Why:* the top of the hand-weapon ladder: a tank-killer you can walk with, but only in the slowest suit. *Cost:* heavy armour, heat, self-harm in light suits. *Ammo:* fusion cells. *Counter:* run from the slow walker.

### 4.16 Particle, ion and EM

**PB1 Sleet Gun** (particle beam) | H | Small, Turret | M | `semi ray en pierce shieldpass power`
*Does:* a hard streak of fast particles that goes partly through shields and armour. *Look and sound:* a blue-white hiss, a Cherenkov glow on the hull. *Why:* hurts what's behind the shield: some damage lands on devices while the shield's still up. *Cost:* power, range falls fast in air. *Ammo:* power. *Counter:* thick hull; atmosphere.

**PB2 Marrow Beam** (crew-killing beam) | H | Small, Turret | M | `hold beam en rad crewonly noblock power` | Needs ROOM
*Does:* passes through hull and cooks living crew at stations; no block damage. Kills a gunner and the turret goes quiet. *Look and sound:* nothing but a dosimeter chatter inside the target. *Why:* the design says crew are real: this is the capture weapon. A ship emptied with this is intact. *Cost:* slow; lots of hull slows it; useless on Kessari. *Ammo:* power. *Counter:* heavy plate around seats; rad-shielded rooms; move crew. It ends when the beam ends.

**PB3 Static Cannon** (ion gun) | H | Small, Turret | M | `semi bolt en em shock overload noblock power` | Needs DEVICE
*Does:* no block damage; big damage to shields and a chance to knock nearby devices offline. *Look and sound:* a slow blue bolt, shield flicker, thrusters coughing. *Why:* for capture: the design's "disable, don't destroy", from range. *Cost:* slow bolts; no kills. *Ammo:* ion cells. *Counter:* shielded generators, spare generators.

**PB4 Pulse Choke** (EM ring) | H | Hand, Hover | M | `charge pulse em shock overload cells` | Needs DEVICE
*Does:* a ring around you that drops devices, robots, Husks and turrets for seconds and empties personal shields. *Look and sound:* a blue crackle at your feet, then a soft pop and silence. *Why:* the panic button in a machine-held site. *Cost:* drops your own shield, suit lights, hover and smart gun. *Ammo:* cells. *Counter:* creatures and low-tech enemies; distance.

**PB5 Jam Spike** (device-jamming dart) | H | Hand | S | `semi bolt stick em shock capsule` | Needs DEVICE
*Does:* a dart sticks in a turret or door and keeps it off while it's there. *Look and sound:* a tiny crackling thorn. *Why:* switches off one device quietly, on a stealth run. *Cost:* small stack. *Ammo:* single use. *Counter:* someone pulls it out.

### 4.17 Lightning

**LT1 Dynamo Snapper** (crank lightning pistol) | L | Hand | S | `crank ray en shock none`
*Does:* wind a hand dynamo, one short blue snap, enough to stun a small beast. *Look and sound:* a whine you make yourself, a blue spit. *Why:* lightning at the bottom of the tree, with no ammo at all. *Cost:* one shot per wind. *Ammo:* muscle. *Counter:* it's short.

**LT2 Wire Biter** (dart stun gun) | M | Hand | S | `semi tether en stun cells`
*Does:* two darts on wires hold a target in shock while you hold the trigger. *Look and sound:* a click-click-click, wires on the floor. *Why:* a real one-on-one takedown; live captures; wires stay anchored from gun to dart. *Cost:* one target, short range. *Ammo:* cartridges, cell. *Counter:* a friend cuts the wire; armour.

**LT3 Fork Gun** (chain lightning gun) | H | Hand | M | `semi arc en shock cells`
*Does:* a bolt that forks to the three nearest targets, jumping further on wet ones. *Look and sound:* blue-white branches, a tearing crack, ozone in the sound. *Why:* the owner's lightning gun. One pull for a pack; aiming is less the point than positioning. It's the answer to many small things. *Cost:* low damage per target; arcs reach allies and you, if you're nearer than the next enemy. *Ammo:* cells. *Counter:* spacing, insulated suits, grounding rods.

**LT4 Storm Rope** (continuous lightning) | H | Hand | M | `hold beam arc en shock cells`
*Does:* a short twisting rope of lightning from a glove, wrecking machines in seconds. *Look and sound:* a roaring fizz, your arm's bones lit up on the visor. *Why:* a hand beam that doesn't need aim at close range, and the strongest anti-robot tool. *Cost:* it holds you close; heat. *Ammo:* cells. *Counter:* step out of reach; insulated suits.

**LT5 Guide-Line** (laser-guided lightning) | H | Hand, Turret | M | `charge ray arc en shock cells`
*Does:* a faint laser opens a path in the air and a bolt runs down it to exactly where you aimed. *Look and sound:* a thin pale line, a beat, a white stroke. *Why:* lightning for marksmen: no forking, long reach, no travel time. *Cost:* doesn't work in vacuum: there's no air to ionise. Rain and fog scatter it. *Ammo:* cells. *Counter:* vacuum, weather, the beat before the bolt.

**LT6 Skycall Rod** (calls real lightning) | M-H | Hand | M | `charge strike en shock crater cells`
*Does:* a rod held up marks a spot; after a two-second build, a real strike lands. Needs open sky and a cloud base; during a storm it's far stronger. *Look and sound:* hair and grass stand up, a blue crawl at the spot, then the sky falls on it. *Why:* the world's weather becomes your artillery, and the game already has strike events with damage radius. *Cost:* useless indoors or under a clear sky without a charge; a storm means the strike can stray to the highest point, maybe you. *Ammo:* cells; free in a storm. *Counter:* a roof, a lightning rod.

**LT7 Ball Lantern** (ball lightning) | H | Hand | M | `semi bolt float arc en shock capsule`
*Does:* a crackling orb rolls along where it's thrown, spitting arcs to the ground that hold it up, and zaps anything near for eight seconds. *Look and sound:* a humming blue sphere that sways, sparks and lights the room. *Why:* zones a doorway; a slow threat others must walk round. *Cost:* drifts with wind; doesn't care who's near it. *Ammo:* capsules. *Counter:* shoot it from afar; wait it out.

**LT8 Conductor Spike** (lightning set-up) | H | Hand | S | `semi bolt stick mark en capsule`
*Does:* a metal spike that sticks: the next lightning from anyone jumps to it first, doubled. *Look and sound:* a thin silver thorn, a pale glow as charge gathers. *Why:* a setter in the shoot-it-then-pop-it style: makes lightning accurate and rewards teamwork. *Cost:* no damage alone. *Ammo:* spikes. *Counter:* pull it out; spread out.

**LT9 Coil Tower** (lightning turret) | M-H | Turret | M | `charge arc en shock power`
*Does:* a tall coil charges and lashes anything within reach, jumping target to target. *Look and sound:* a slow crescendo, an arc at the top, a boom. *Why:* base defence with no ammo and a terrifying approach, for bases rich in power. *Cost:* blackout risk: every shot draws from the base's capacitors. *Ammo:* power. *Counter:* out-range it; cut power; wait for recharge.

**LT10 Arc Rake** (vehicle lightning) | H | Hover, Small | M | `hold arc en shock power`
*Does:* mounted arc emitters that hit anything close alongside: a ramming, strafing weapon. *Look and sound:* lightning crawling along the hull. *Why:* punishes boarders and creatures that climb on your craft, and rakes people on the ground. *Cost:* very short; draws power from flight. *Ammo:* power. *Counter:* range; insulated plate.

**LT11 Web Array** (capital lightning field) | H | Capital | L | `hold pulse arc en shock strip power`
*Does:* the hull flares with a lightning web that catches small craft in a shell around it. *Look and sound:* a deep rolling roar, a net of white over the ship. *Why:* close defence against swarms of fighters and boarding pods. *Cost:* huge power; it's a shell with gaps. *Ammo:* power. *Counter:* stay at range; find the gaps.

**LT12 Lure Rod** (lightning decoy) | M | Placed | S | `placed lure en charge`
*Does:* a tall rod that draws lightning, natural or fired, to itself. *Look and sound:* a metal stake with a copper tail in the ground, an arc that swerves to it. *Why:* the counter to a whole family; also protects a base in a storm world. *Cost:* the strike wrecks it in a few hits. *Ammo:* single stake. *Counter:* knock it over.

**LT13 Shock Fence** (powered perimeter) | M | Placed, Turret | S | `placed arc en shock power`
*Does:* posts that arc to each other: anything crossing the line gets shocked and thrown back. *Look and sound:* a line of posts, a low buzz, a snapping flash. *Why:* the settlement's simple defence against wildlife, and it needs no ammo. *Cost:* power, and a broken post opens a gap. *Ammo:* power. *Counter:* shoot a post; cut power; jump it.

### 4.18 Sonic

**SN1 Basslance** (sound cannon) | M | Hand, Hover | S | `hold cone kin sonic stagger shatter cells`
*Does:* a rolling wave that shakes people and breaks glass and brittle blocks. *Look and sound:* a visible air-ripple, a note you feel in your teeth. *Why:* goes round corners and through cover at reduced strength; shatters frozen things. *Cost:* the whole valley hears you; needs air or water to carry. *Ammo:* cells. *Counter:* vacuum; ear protection; machines.

**SN2 Thin Note** (sound cutter) | H | Hand | M | `hold beam kin sonic cells`
*Does:* a focused ultrasonic needle that cuts and punches silently at close range. *Look and sound:* no sound for bystanders, a faint glassy shimmer. *Why:* a silent, ammo-light assassin's tool. *Cost:* short; air only. *Ammo:* cells. *Counter:* armour plate, vacuum.

**SN3 Resonator** (tuned breaker) | H | Hand, Hover, Small | M | `hold beam kin sonic shatter breach cells`
*Does:* tune to a material, hold on one block or hull until it rings and shatters. *Look and sound:* a pure note climbing, the target singing, then a burst of dust. *Why:* a slow answer to any armour, and a thief's tool for one wall. *Cost:* you must stand there for seconds; soft materials shrug it off. *Ammo:* cells. *Counter:* mixed-material plate; layered construction.

**SN4 Thump Pad** (ground shock) | M | Hand | S | `charge pulse kin sonic stagger charge`
*Does:* a stamped ground wave that topples things near you and rattles burrowers out. *Look and sound:* a boot stamp, a ring of dust. *Why:* shakes off grabbers; forces burrowing creatures to the surface. *Cost:* no use against flyers. *Ammo:* recharges. *Counter:* flying enemies.

**SN5 Ring Gun** (air vortex gun) | M | Hand | S | `semi bolt kin force stagger cells`
*Does:* fires a doughnut of air that knocks people, blows out flames, pushes gas and smoke. *Look and sound:* a soft fwump, a visible ring in dust or smoke. *Why:* moves the air: clears a room of gas, or sends a cloud at the enemy. *Cost:* does nothing in vacuum, little damage. *Ammo:* cells. *Counter:* heavy things; vacuum.

**SN6 Scream Coil** (hearing jammer) | H | Hand | S | `hold cone sonic deaf cells`
*Does:* a rising whine that blocks hearing and scrambles sound-seekers and creature hearing. *Look and sound:* a tooth-rattling whistle. *Why:* blinds things that hunt by sound: cave fauna, sound-seeking rounds. *Cost:* marks you for anything with eyes. *Ammo:* cells. *Counter:* eyes; deaf robots.

**SN7 Hush Dart** (silence patch) | H | Hand | S | `semi bolt field sonic deaf capsule`
*Does:* a dart that plants a dome of dead quiet for twenty seconds. Sound inside doesn't reach the noise system, alarms included. *Look and sound:* a soft tick, then all sound falls away. *Why:* a stealth tool that lets you go loud inside it. *Cost:* you can't hear what's coming either. *Ammo:* capsules. *Counter:* eyes; silence doesn't hide movement.

### 4.19 Flame

The fire rules for the whole family are in section 5.2. In short: every flame weapon throws burning fuel that can lie on the ground, and how far the fire goes depends on wind, rain, wetness, plants, water and air. As a first version, each hand thrower works without ground fire as a cone of short bolts plus a burn timer; the ground fire comes with the FIRE upgrade.

**FL1 Fire Bottle** (thrown fuel bomb) | L | Hand | S | `thrown lob en fire ignite fuel` | Needs FIRE (ground fire only)
*Does:* a bottle of fuel and a burning rag; bursts into a spreading pool. *Look and sound:* a tumbling flame, a smash, a whoomph. *Why:* the first area denial anybody can make: blocks a doorway, sets a crop or a hut alight. *Cost:* the rag is lit in your hand; drop it and you're the target. *Ammo:* a bottle and biofuel. *Counter:* step back; water; wet ground.

**FL2 Fire Lance** (gout tube on a pole) | L | Hand | S | `semi cone en fire ignite fuel`
*Does:* a tube of powder and resin on a spear shaft gives one roaring ten-metre gout, then it's a spear. *Look and sound:* a spitting jet, sparks, thick black smoke. *Why:* the first flame weapon: a one-shot scare that breaks a charge and makes wildlife turn. *Cost:* one shot, then a long refill. *Ammo:* powder tube. *Counter:* wait for the gout; wet powder.

**FL3 Flare Pistol** (signal and igniter) | L-M | Hand | S | `semi lob en fire light mark rounds`
*Does:* a slow glowing ball that lights fuel, marks a spot for allies and lights a cave. *Look and sound:* a fat pistol, a popping thock, a red drifting star. *Why:* lets you ignite gas, oil or a fuel pool from a safe distance, and is a call for help that patrols can see. *Cost:* almost no damage, gives your position away. *Ammo:* flares. *Counter:* it's bright: they see you too.

**FL4 Tallow Sprayer** (crude fuel sprayer) | L | Hand | S | `hold cone en fire ignite fuel` | Needs FIRE (ground fire only)
*Does:* a hand-pumped drum of rendered fat and biofuel squirts a short greasy flame. *Look and sound:* a dented tank, a garden-pump wheeze, a sooty orange lick. *Why:* an early flamethrower made from a farm: wildlife panics, plants burn, nests clear. *Cost:* you must pump, five seconds of fire, the smell draws scavengers. *Ammo:* biofuel. *Counter:* range: it reaches 6 m at most.

**FL5 Pressure Thrower** (military flamethrower) | M | Hand | S | `hold cone en fire ignite fuel`
*Does:* the design doc's flamethrower: a gas-pressurised backpack and a pilot flame, a long burning jet that sticks to things. *Look and sound:* twin tanks, a blue pilot light always on, a roaring tear. *Why:* the room-clearer and nest-burner; creatures flee it and it's stronger on flesh, wood and plastic, and does nothing to stone, steel or vessel shields. It goes straight past a personal shield, which makes it the low-tech answer to shielded soldiers. *Cost:* short burst time, heavy tank, the pilot light shows you in the dark; a tank hit bursts into a fireball on you (warned by a hiss). *Ammo:* fuel. *Counter:* range, sealed armour, water, a shot to the tank.

**FL6 Jelly Thrower** (thickened fuel) | M | Hand, Hover | S | `hold lob en fire stick ignite fuel` | Needs FIRE (ground fire only)
*Does:* gelled fuel thrown in a long stream that arcs over cover, clings to walls and creatures and burns where it lands. *Look and sound:* a heavy rope of fire falling in a curve, burning drips. *Why:* a flamethrower at 50 m. It reaches over a parapet and paints a slope or a bunker slit. *Cost:* you see the arc, so does everyone; it's slow to shift aim; fuel is a real expense. *Ammo:* gel fuel, made from biofuel and magnesium. *Counter:* a deep overhead roof; moving; water.

**FL7 Mist Thrower** (aerosol, then spark) | M-H | Hand | M | `hold cone field en fire exp ignite fuel` | Needs FIRE, ROOM
*Does:* mode one fills a room with fuel mist; mode two sparks it, and it goes off as a fuel-air blast. *Look and sound:* a hiss and a shimmering haze, a click, a rolling pressure wave. *Why:* a flamethrower you play as a trap: fill a corridor, back out, light it. Round corners, down vents. *Cost:* any stray spark, muzzle flash or lightning does it early; useless in wind or vacuum. *Ammo:* fuel, lots. *Counter:* shoot into the mist early while they're in it.

**FL8 Star Gel** (high-tech plasma gel) | H | Hand, Hover | M | `hold beam en fire stick ignite breach cells` | Needs FIRE (ground fire only)
*Does:* a magnetically held stream of plasma gel, white-hot, that burns on steel and melts through plate. *Look and sound:* a white rope, the air shaking around it, a dripping glow on hull. *Why:* the only thrower that hurts armour: where the Pressure Thrower bounces off a bunker door, this cuts it. *Cost:* rare ammo; the heat haze blinds your scope; heavy suit recommended. *Ammo:* plasma gel, emberite. *Counter:* range, shields, charged armour.

**FL9 Void Burner** (self-oxidising thrower) | H | Hand, Small | M | `hold cone en fire fuel` | Needs FIRE (ground fire only)
*Does:* carries its own oxidiser, so it burns in vacuum, underwater and in thin air. *Look and sound:* a tight blue-white spike with no smoke; a silent cone in space. *Why:* ordinary flame is useless off-planet. This is the one that's still fire on an airless moon or in a flooded lower deck. *Cost:* twice the tank weight for half the burn; in water you sit in a cloud of steam. *Ammo:* fuel and oxidiser cans. *Counter:* range; hull plate.

**FL10 Drag Flame** (hover tail jet) | M | Hover | S | `hold cone en fire ignite fuel` | Needs FIRE (ground fire only)
*Does:* a rear-facing jet leaves a burning track behind a hover vessel. *Look and sound:* a line of fire unspooling behind you. *Why:* a chase weapon: draw a fence of flame across a pursuer's path, or burn a firebreak. *Cost:* burns fuel from the craft's own supply. *Ammo:* vessel fuel. *Counter:* hover over it; rain; come from the side.

**FL11 Fire Curtain** (small vessel fuel bombs) | M | Small | S | `semi lob en fire ignite fuel` | Needs FIRE (ground fire only)
*Does:* tanks of gelled fuel tumble from a bay and splash a long stripe. *Look and sound:* a wobble of silver, then a rolling line of orange along the ground. *Why:* burns a tree line, a nest field or a gang's camp in one pass. *Cost:* you fly low and straight. *Ammo:* fuel tanks. *Counter:* water, wet weather, anti-air.

**FL12 Gate Jet** (base flame turret) | M | Turret | S | `hold cone en fire ignite fuel` | Needs FIRE (ground fire only)
*Does:* a nozzle set in a wall or gate that floods a corridor or a killing ground when triggered. *Look and sound:* a pipe in the wall with a pilot light, a hissing whoosh. *Why:* the fearsome base defence: invasions stall at a door. Runs off a fuel line. *Cost:* burns fuel from the base; can scorch your own wooden decor. *Ammo:* fuel. *Counter:* a shot to the pilot line; sealed suits; machines.

**FL13 Burial Jet** (buried flame mine) | M | Placed | S | `placed cone en fire ignite fuel` | Needs FIRE (ground fire only)
*Does:* a buried drum aimed along a road, set off by tripwire or button. *Look and sound:* the ground coughs, a sheet of fire, a burnt fan on the road for days. *Why:* a one-off road ambush that scares vehicles' crews out into the open. *Cost:* one shot, heavy to bury. *Ammo:* a drum of fuel. *Counter:* detectors; wet ground; send a scout.

**FL14 Drive Burn** (weaponised exhaust) | M | Small, Capital | M | `hold cone en fire power`
*Does:* the thruster plume itself hurts what's behind it, and a capital ship can turn its stern and cut. *Look and sound:* the plume hardens, nearby blocks glow. *Why:* every vessel already owns this. It costs nothing to fit and it's the desperate last move, and it makes standing behind a ship a real risk. *Cost:* you point your weak end at the enemy. *Ammo:* drive fuel. *Counter:* don't sit in the exhaust.

**FL15 Back-Burn Torch** (fire tool) | L | Hand | S | `hold cone en fire ignite fuel` | Needs FIRE
*Does:* a low sputtering torch for starting fires on purpose. *Look and sound:* a short blue tongue. *Why:* fights wildfire with a firebreak, clears a field, flushes a nest from cover, and lights a fuse from a safe place. *Cost:* nothing but your attention, and the wind turning. *Ammo:* a pinch of fuel. *Counter:* rain.

**FL16 Scald Jet** (steam lance) | L | Hand, Hover | S | `hold cone en fire blind water`
*Does:* a boiler jet that scalds and fills the area with white steam. *Look and sound:* a shrieking valve and a blank white wall. *Why:* heat that works in the rain and where fire won't take, and it hides you. *Cost:* blinds you too; needs a boiler. *Ammo:* water and biofuel. *Counter:* wind, heavy clothing, range.

### 4.20 Cold

**CD1 Cold Snap** (freezing grenade) | M | Hand | S | `timer thrown pulse en cold freeze shatter capsule`
*Does:* a burst that freezes creatures solid for seconds; frozen ones shatter from a hard hit. *Look and sound:* a crackle, a star of frost on the ground, statues. *Why:* holds a pack still and sets up one big hit. *Cost:* frost on your own boots if close; bigger creatures only slow. *Ammo:* single use. *Counter:* fire breaks it; heat-adapted creatures.

**CD2 Frost Lance** (cryo sprayer) | M | Hand, Hover | S | `hold cone en cold slow freeze coolant`
*Does:* a white spray that slows, then freezes, and puts out fires. *Look and sound:* a falling white mist, a creaking sound. *Why:* the cold twin of the flamethrower, and it's the fire extinguisher for burning vessels; on Husks it stops the slow walkers. *Cost:* short reach; no use on hot worlds at full power. *Ammo:* coolant. *Counter:* heat; range.

**CD3 Rime Wall** (ice wall caster) | M | Hand | M | `placed cold coolant` | Needs GROUND
*Does:* a line of ice blocks grows out of the ground and stands for a minute. *Look and sound:* a splitting crackle, a jagged blue wall. *Why:* instant cover in the open, and a bridge across a gap or river. *Cost:* melts in sun and to any fire. *Ammo:* coolant, plus any water around. *Counter:* fire, explosives, patience.

**CD4 Still Beam** (freeze ray) | H | Hand, Turret | M | `hold beam en cold freeze shatter cells`
*Does:* a pale blue beam that freezes a target solid over a few seconds; freezing water bridges it. *Look and sound:* a singing thin line, frost spreading out from the point of contact. *Why:* the single-target stop: holds a heavy creature or a person while a teammate breaks them. On a player it lasts seconds only. *Cost:* slow, you must hold it. *Ammo:* cells. *Counter:* fire; a second target.

**CD5 Frost Shell** (vessel cold round) | H | Small, Turret | S | `semi bolt en cold slow shells` | Needs DEVICE
*Does:* a shell that bursts cryogen over thrusters and gun mounts, slowing them for a time. *Look and sound:* a white flower on the hull, ice on the nozzles. *Why:* a soft-kill alternative for a chase. *Cost:* little hull damage. *Ammo:* cold shells, rimefire for the strong form. *Counter:* heat from firing; drive burns clear it.

### 4.21 Acid, goo and water

**CH1 Etch Sprayer** (acid spray) | M | Hand | S | `hold cone en acid corrode chem`
*Does:* lowers a target's armour value for a time, and eats plate slowly. *Look and sound:* a yellow mist, a sizzling hiss, bubbling paint. *Why:* the sapper's set-up for a heavy: spray first, then the rifle works. *Cost:* melts your gear if you stand in it. *Ammo:* acid flasks, from Acid Reaches pools or by recipe. *Counter:* coated armour; water.

**CH2 Rust Haze** (corrosion cloud) | M | Hand, Turret | S | `thrown lob field en acid corrode chem`
*Does:* a cloud that rusts steel while doing nothing to flesh. *Look and sound:* a brown fog, a tick-tick of flaking metal. *Why:* a siege tool that attacks a base's blocks over minutes while you're elsewhere. Doesn't touch stone, wood or Kessari-grade alloy. *Cost:* slow, wind moves it. *Ammo:* capsules. *Counter:* rain washes it; coated hull.

**CH3 Reek Pot** (stink bomb) | L | Hand | S | `thrown lob field lure fear capsule`
*Does:* a pot of rotten something. Two kinds: one drives creatures away, one draws them in. *Look and sound:* a brown smear, buzzing flies; your own view slightly greens. *Why:* steers wildlife without killing it. *Cost:* sticks to you for a minute if you're caught in it. *Ammo:* single use, made from offal. *Counter:* wind; things with no nose.

**CH4 Slip Pot** (grease bomb) | L-M | Hand | S | `thrown lob field fuel slow capsule` | Needs FIRE (ignition only)
*Does:* a slick patch on floors and ramps: walkers skid, and it burns if lit. *Look and sound:* a dark gleam, feet going out from under. *Why:* turns a stairwell into a trap, and it's an oil set-up for fire. *Cost:* you can slip too. *Ammo:* oil pots. *Counter:* hover; jump; sand.

**CH5 Tangle Gun** (quick-set glue) | M | Hand | M | `semi lob stick glue pin chem`
*Does:* blobs of foam that harden in seconds: holds a target, plugs a hull breach, or stack on a wall as a climbable step. *Look and sound:* a thick wet glop, a hardening crackle. *Why:* a weapon, a repair tool and a climbing tool in one. Hardened lumps are small placed blocks. *Cost:* slow; hardened lumps block your own way. *Ammo:* resin. *Counter:* fire melts it; blades cut it.

**CH6 Foam Thrower** (fire foam) | M | Hand, Turret | S | `hold cone stick slow chem`
*Does:* expanding foam that smothers fire and slows anyone in it. *Look and sound:* a white rope, a soft pop-pop-pop. *Why:* the fire brigade: every base with flame or fuel needs this. *Cost:* no damage. *Ammo:* foam canister. *Counter:* step out.

**CH7 Water Cannon** (pressure hose) | M | Hand, Hover, Turret | S | `hold beam kin force stagger water`
*Does:* a hard jet that knocks people down, puts out fires and soaks targets, so they count as wet for lightning. *Look and sound:* a roaring silver rod, spray and rainbow in sunlight. *Why:* a riot gun and a combo starter. *Cost:* needs water to hand: a tank, or a hose in a lake. *Ammo:* water. *Counter:* heavy targets; dead-cold air turns the spray to ice on you.

**CH8 Salt Thrower** (salt spray) | L | Hand | S | `pump pellets en bio fleshonly water`
*Does:* a scatter of coarse salt, strong against slugs, plant creatures and soft-bodied things. *Look and sound:* a dry rattle, targets curling up. *Why:* a cheap counter to one kind of nest. *Cost:* barely hurts anything else. *Ammo:* salt, from flats or the sea. *Counter:* anything hard.

**CH9 Slag Cannon** (molten ore thrower) | M | Turret | M | `semi lob en fire ignite scrap`
*Does:* a smelter on a mount throws molten slag. *Look and sound:* a heavy orange glob, a smoking splat that keeps burning for seconds. *Why:* mining bases and refinery towns can defend themselves from their own waste. *Cost:* needs a working furnace; short range. *Ammo:* slag and fuel. *Counter:* distance.

**CH10 Flood Charge** (water burst) | M | Placed | L | `timer placed water slow` | Needs FLUID
*Does:* a capsule of compressed water that fills a corridor to the waist for twelve seconds. *Look and sound:* a bang, a brown wave, gurgling. *Why:* puts out fires, slows a boarding party, and makes everyone wet for lightning. *Cost:* it floods your side too. *Ammo:* single use. *Counter:* climb; open a door downhill.

### 4.22 Melee

**ME1 Work Knife** (combat knife) | L | Hand | S | `melee kin none`
*Does:* the design's silent takedown tool, costs stamina. *Look and sound:* a dull blade, a short whip of air. *Why:* no noise, no ammo, always on you. A takedown from behind goes past a personal shield. *Cost:* you must get behind them. *Ammo:* none. *Counter:* be facing it.

**ME2 Throwing Hatchet** (throwing axe) | L | Hand | S | `thrown bolt kin none`
*Does:* melee in the hand, thrown with a good drop. Pick it up again after. *Look and sound:* a spinning whup-whup and a thunk. *Why:* silent ranged kill with no ammo. *Cost:* you have to go and fetch it. *Ammo:* none. *Counter:* armour; dodge.

**ME3 Boar Spear** (spear or pike) | L | Hand | S | `melee kin stagger none`
*Does:* long reach; braced, a charger runs onto it and hurts itself by its own speed. *Look and sound:* a heavy ash shaft, a grunt. *Why:* a low-tech answer to big animals that charge; rewards timing against their telegraph. *Cost:* slow, poor in tight rooms. *Ammo:* none. *Counter:* flank it.

**ME4 Maul** (club and war hammer) | L | Hand | S | `melee kin stagger shatter none`
*Does:* a club at the low end, a long hammer at the high end: a slow overhead that rings armour and cracks brittle blocks and frozen targets. *Look and sound:* a long wind-up, a clang felt through the screen. *Why:* breaks armour on a low-tech budget; the shatter finisher. *Cost:* heavy stamina cost, a long recovery. *Ammo:* none. *Counter:* step away from the wind-up.

**ME5 Cutting Wire** (garrote wire) | L | Hand | S | `melee tether kin none`
*Does:* a silent kill from behind, and stretched across a passage it snags a speeder. *Look and sound:* a thin glint, a gasp. *Why:* the quietest kill there is. *Cost:* the kill takes time. *Ammo:* none. *Counter:* helmets; a second guard. Strung, it's anchored at both ends.

**ME6 Shock Baton** (stun stick) | M | Hand | S | `melee en stun cells`
*Does:* a stick with a blue crackle: stuns people and creatures, jolts robots. *Look and sound:* a bright snap, a twitch. *Why:* takes prisoners and keeps standing with friendly factions. *Cost:* drains cell. *Ammo:* cells. *Counter:* insulation.

**ME7 Saw Bar** (chainsaw and chain cleaver) | L-M | Hand | S | `hold melee kin fuel`
*Does:* the design's chainsaw is also a brutal close weapon: hold to chew through soft things. A heavier cleaver body tears armour. *Look and sound:* an engine snarl, blipping at idle. *Why:* the classic. Fells trees, then whatever comes out of them. *Cost:* so loud it wakes the map; fuel. *Ammo:* biofuel. *Counter:* it's noisy and you can't sneak; reach.

**ME8 Piston Fist** (power fist) | M | Hand | M | `charge melee kin force stagger cells`
*Does:* a gauntlet with a piston charge throws a person across a room or stops a charge dead. *Look and sound:* a heavy glove, a hiss, a thump. *Why:* melee with a real knockback: pushes things off ledges. *Cost:* slow to re-arm; a hit in low gravity sends you backwards. *Ammo:* cell or air cartridge. *Counter:* armour with reach.

**ME9 Pile Spike** (breaching spike) | M-H | Hand | S | `charge melee pierce kin breach cells`
*Does:* an arm-mounted spike fired by a charge into whatever you're touching: door lock, hull, armoured beast. *Look and sound:* a blast of smoke at your wrist, a shock in the frame. *Why:* the biggest single hit on foot, and only at touching range. *Cost:* you must reach it; one charge, then a slow reload. *Ammo:* breach cartridges. *Counter:* never let it touch.

**ME10 Hum Blade** (vibrating blade) | M-H | Hand | S | `melee kin pierce cells`
*Does:* a sword that vibrates; passes through light armour. *Look and sound:* a pitch that rises on contact. *Why:* melee that still matters against plated enemies, without the glow. *Cost:* drains cell; useless on charged armour. *Ammo:* cells. *Counter:* reach; charged plate.

**ME11 Light Blade** (energy blade) | H | Hand | S | `melee en pierce breach cells`
*Does:* the design's energy blade: cuts bulkhead doors slowly and men quickly. *Look and sound:* a glowing bar with a deep hum and a sharp clash. *Why:* the signature boarding weapon: cut in through a hatch, then cut down the guard. *Cost:* the glow is a beacon in the dark; drains while lit. *Ammo:* cells. *Counter:* ranged fire; mirrored armour.

**ME12 Filament Whip** (monofilament) | H | Hand | M | `melee tether kin pierce cells`
*Does:* a glowing tip on a near-invisible cutting line, long reach, lashes through several enemies. *Look and sound:* a flicker of a bright point, a thin whisper. *Why:* melee that reaches across a room and cuts through a crowd, at the price of danger to you. *Cost:* a bad swing wraps back and bites. *Ammo:* cell. *Counter:* armour; rush it inside the arc.

**ME13 Hot Cleaver** (heated blade) | M | Hand | S | `melee en fire ignite cells`
*Does:* a broad blade that glows and ignites what it cuts, and cauterises. *Look and sound:* a dull red edge, a sizzle. *Why:* cheap burn in melee; lights fuses, oil and tinder with a swing. *Cost:* no use in water. *Ammo:* cells. *Counter:* water, reach.

### 4.23 Seeking and homing

**SK1 Bend Rounds** (homing bullets) | H | Hand | M | `auto bolt seek-lock kin rounds cells`
*Does:* the owner's homing bullets: small rounds with a tiny fin that curve up to a hand's width a metre toward what you're aiming near. *Look and sound:* a hissing tracer with a gentle curve, a sonar tick. *Why:* for fliers, jumpers and hover bikes that outrun your aim, and it's fun to see them bend. *Cost:* each round is pricey; less damage than a plain round; easily fooled. *Ammo:* smart rounds. *Counter:* flares, heavy cover, jamming.

**SK2 Tag Rifle** (tag and curve) | H | Hand | M | `semi bolt stick seek-mark kin rounds`
*Does:* one tag dart sticks to a target; every other shot then curves to it, even round cover, even from a teammate. *Look and sound:* a blinking dart, a pale shimmer of bending rounds. *Why:* the mark-and-punish pair: land one hard shot, then hide and keep shooting. *Cost:* tag first, damage second; a missed tag is wasted seconds. *Ammo:* tags and rounds. *Counter:* pull the tag; hard cover catches curved rounds.

**SK3 Needle Swarm** (homing dart volley) | H | Hand | M | `auto swarm seek-flesh kin rounds`
*Does:* a magazine of needles that bend to anything alive, and stick, and after enough stick they burst. *Look and sound:* a hissing shimmer, a pink crystal glow. *Why:* roughly-point combat that keeps pressure on a dodgy creature. Machines ignore it. *Cost:* very weak on armour; slow to kill. *Ammo:* needle magazines. *Counter:* armour; nothing alive; cover.

**SK4 Memory Bolt** (last-seen seeker) | H | Hand | M | `semi bolt seek-memory en cells`
*Does:* a slow glowing shot that goes to where the target was last seen, bending round corners and pillars. *Look and sound:* a low hum drifting past you. *Why:* punishes peeking: step behind cover and it keeps coming to that spot. *Cost:* slow, and low damage. *Ammo:* cells. *Counter:* don't stay where they saw you.

**SK5 Heat Dart** (heat-seeker) | M | Hand, Hover | S | `semi bolt seek-heat exp rounds`
*Does:* chases the warmest thing in front of you: engines, fires, furnaces, warm bodies. *Look and sound:* a sniffing whistle. *Why:* a cheap seeker that loves vessels; fails around fires, which is a real tactic. *Cost:* it can go for your own hot exhaust. *Ammo:* darts. *Counter:* flares, campfires, cold armour.

**SK6 Whisper Round** (sound-seeker) | H | Hand | S | `semi bolt seek-sound kin rounds`
*Does:* the louder the target, the better it tracks. *Look and sound:* a quiet round with an ear-like sensor. *Why:* punishes hover bikes, miniguns and people who run. *Cost:* quiet targets are nearly normal rounds. *Ammo:* rounds. *Counter:* stand still; chatterbox decoys.

**SK7 Blood Round** (gene-seeker) | H | Hand | M | `semi bolt seek-flesh kin rounds`
*Does:* prime the round with a sample of tissue; it only homes on that species or that person. *Look and sound:* a red vial clicked into the receiver, a straight sure line. *Why:* a hunter's weapon. Rare tissue becomes a resource, and in a mixed pack it picks the right one. *Cost:* slow to prime; no effect on anything else. *Ammo:* rounds plus rare tissue. *Counter:* being the wrong species.

**SK8 Orbit Shards** (circling shots) | H | Hand | M | `orbit bolt seek-lock en charge`
*Does:* three crystal shards circle you; each fire sends one at what you're looking at; they also block a shot each. *Look and sound:* glass bells. *Why:* offence and defence in one resource. *Cost:* three shots, then a wait while they re-form. *Ammo:* recharges. *Counter:* bait them away, then rush.

**SK9 Return Disc** (returning blade) | M-H | Hand | S | `thrown bolt return kin none`
*Does:* a spinning disc flies out and comes back to your hand; hits both ways. *Look and sound:* a rising hum, a slap in your palm. *Why:* no ammo, and you can catch a behind-cover enemy on the way back. *Cost:* miss the catch and it clatters away. *Ammo:* none. *Counter:* armour, or catching it first.

### 4.24 Living weapons

These use biomass as plain ammo. No feeding or upkeep. They have a look of their own: wet, ribbed, warm. Fire and cold are strong against all of them. Hornets and worms are the owner's own ideas and are approved living weapons.

**LV1 Wasp Jar** (thrown nest) | L | Hand | S | `thrown swarm seek-flesh kin slow capsule`
*Does:* a clay jar with a real nest in it. Throw it and the angry swarm goes for the nearest warm thing, slowing it for ten seconds. *Look and sound:* a smashed jar and a rising buzz. *Why:* the low-band way into the hornet line, and it clears a position with no guns. *Cost:* throw it short and it's your problem. *Ammo:* jars, gathered from nests. *Counter:* smoke, fire, water, running.

**LV2 Hornet Hive** (arm-mounted hive) | M | Hand | M | `auto swarm seek-flesh kin mark biomass`
*Does:* the owner's hornets: a warm pouch on the arm throws eight short-lived hornets, each one flies at living targets, stings and dies; they last twelve seconds. *Look and sound:* the pouch pulses; a buzz that climbs as it empties. *Why:* homing damage that finds a target behind cover and keeps it busy while you do something else. Every sting restarts a personal shield's break. *Cost:* slow to refill from biomass; machines ignore it; wind blows the swarm off line. *Ammo:* biomass. *Counter:* fire and smoke kill them in the air; sealed armour; closed doors.

**LV3 Cinder Moths** (burning swarm) | H | Hand | M | `auto swarm seek-heat en fire ignite biomass` | Needs SWARM
*Does:* glowing moths fly to warm things and ignite on contact. *Look and sound:* a soft rustle and a slow cloud of embers. *Why:* a slow fire-bringer that carries flame to places a thrower can't reach. *Cost:* they go for any heat, your own fires too. *Ammo:* biomass. *Counter:* rain, water, cold.

**LV4 Nest Tower** (hornet base defence) | M | Turret | M | `auto swarm seek-flesh kin biomass`
*Does:* a grown tower that releases hornets at anything alive near a base. *Look and sound:* a hum you hear from the gate. *Why:* a base defence with no CPU-hungry turret, for a bio-themed base. *Cost:* eats organics from storage; no use on machines. *Ammo:* biomass. *Counter:* fire, machines.

**LV5 Bait Worm** (slow digger) | L | Hand | S | `thrown lob stick drill kin biomass`
*Does:* a fist-thick grub on a sling; it fastens to the target and chews for several seconds. *Look and sound:* a wet slap, a grinding noise. *Why:* the low-band start of the worm line: a damage-over-time you lob. *Cost:* very short range. *Ammo:* biomass. *Counter:* scrape it off, fire.

**LV6 Wormspitter** (worm gun) | M | Hand, Hover | M | `semi bolt stick drill kin bio biomass`
*Does:* the owner's worm gun: fires a fat armoured worm that burrows into flesh, armour or a block and keeps eating for six seconds; soft targets squirm. *Look and sound:* a wet thunk; a segmented body vanishing into a hole. *Why:* damage that keeps working after you turn away, and it eats through a block for a breach. Killing the host releases more worms: one big enemy then helps you clear the pack. *Cost:* each shot is slow and costly; shields stop them. *Ammo:* biomass. *Counter:* shields; fire kills them in flight; hard stone.

**LV7 Sand Swimmer** (burrowing worm) | H | Hand, Placed | M | `semi seek-flesh drill kin stagger biomass`
*Does:* a big worm that dives into the ground, swims along a line of ripples and bursts up under the target. *Look and sound:* a line of moving soil, a rumble, a gaping maw. *Why:* hits people behind walls from below, and it's a terrific thing to see coming. *Cost:* soft ground only; visible ripples warn. *Ammo:* biomass. *Counter:* rock, concrete, jumping aboard a vehicle.

**LV8 Gullet Cannon** (grown vessel gun) | M-H | Hover, Small | M | `semi bolt stick en acid corrode biomass`
*Does:* a living throat mounted on a hull that retches acid-filled sacs. *Look and sound:* a wet heave, a sac spattering on hull. *Why:* a vessel weapon that needs no ammo factory: organics from a hold will do. *Cost:* slow projectile. *Ammo:* biomass. *Counter:* shields; speed.

**LV9 Spore Lobber** (confusion cloud) | M | Hand | S | `semi lob field bio confuse capsule`
*Does:* a bulb bursts into a yellow-green cloud; creatures inside attack each other. *Look and sound:* a soft pop, drifting motes. *Why:* turns a pack on itself. No infection; a player in the cloud only gets a blurred visor for seconds. *Cost:* wind takes it. *Ammo:* spore pods, gathered. *Counter:* wind; robots.

**LV10 Bile Pod** (acid spitter) | M | Hand | S | `semi lob en acid corrode biomass`
*Does:* a pod-gun that spits stomach acid; hisses on metal. *Look and sound:* a wet cough, a green splat, a sizzle. *Why:* cheap anti-armour, grown not made; bug corpses give the ammo. *Cost:* slow bolt, short range. *Ammo:* biomass. *Counter:* shields, range.

**LV11 Egg Grenade** (hatchling bomb) | M | Hand | S | `thrown lob split swarm seek-flesh kin capsule`
*Does:* an egg cracks and five hatchlings bite the nearest flesh for eight seconds, then drop dead. *Look and sound:* a leathery crack, chittering. *Why:* a thrown distraction that holds an enemy's attention. *Cost:* no use on machines; a nearer ally can be the nearest flesh. *Ammo:* eggs, from nests. *Counter:* stomp them; fire.

**LV12 Leech Vine** (drain whip) | M | Hand | M | `hold tether bio mend biomass`
*Does:* a living whip clamps on and drains a target's health to you. *Look and sound:* a slick red tendril, a gulping pulse. *Why:* a field heal that comes from fighting: for far-from-home survival. *Cost:* you must hold on, in the open. *Ammo:* biomass. *Counter:* cut the line, armour, machines.

**LV13 Mantis Claw** (grown arm blade) | M | Hand | S | `melee kin bio biomass`
*Does:* a grown blade worn on the wrist that snaps out with a wet click. *Look and sound:* chitin, a stinging swipe. *Why:* very fast melee with no stamina, for builds that go without guns. *Cost:* a little biomass per kill-chain. *Ammo:* biomass. *Counter:* reach.

**LV14 Tooth Gun** (bone shard rifle) | M | Hand | S | `auto bolt kin biomass`
*Does:* a ribbed gun that grows and spits teeth, recharging from bone and meat. *Look and sound:* a dry wet rattle, shards in the wound. *Why:* an automatic that reloads from hunting, so a wild-country player can stay out. *Cost:* the magazine refills slowly. *Ammo:* biomass or bone. *Counter:* armour.

**LV15 Tick Dart** (living marker) | M | Hand | S | `semi bolt stick mark bio capsule`
*Does:* a tiny creature bites on; for twenty seconds the target takes bonus damage and shows on your visor. *Look and sound:* a syringe click, a slight jump. *Why:* a marker that also tracks. On a player it lasts seconds only and pops off harmlessly. *Cost:* no damage. *Ammo:* capsules. *Counter:* melee scrapes it off.

**LV16 Maw Stalk** (carnivorous plant turret) | M | Turret | M | `semi bolt kin melee biomass` | Needs GROUND
*Does:* a rooted plant that bites what walks near and spits seed shot at range. *Look and sound:* rustling leaves that tilt towards you. *Why:* a living gate guard. Scales in size over days with organics in its soil store. *Cost:* can't be moved. *Ammo:* biomass. *Counter:* fire, salt.

**LV17 Rider Grub** (frenzy parasite) | H | Hand | M | `semi bolt stick bio confuse capsule` | Needs MIND
*Does:* a grub on a creature's neck sends it into a frenzy for twenty seconds: it turns on whatever's nearest. It takes no orders and never follows you. *Look and sound:* a wet click, its eyes dim. *Why:* the biggest beast in a pack becomes the pack's problem for one fight. Stand too close and you're the nearest thing. Never works on players. *Cost:* capsules are rare, and big creatures shake it. *Ammo:* capsule. *Counter:* big creatures, machines.

**LV18 Thorn Sower** (seeded spike trap) | M | Hand | S | `semi lob placed kin stick slow biomass`
*Does:* seeds that sprout a patch of thorn in seconds: slows and pricks walkers. *Look and sound:* a dry rattle, shoots whipping up. *Why:* area denial grown in a moment. Grows only on soil. *Cost:* burns easily, and you can't walk through either. *Ammo:* seeds. *Counter:* fire, flying.

**LV19 Spawn Tube** (living boarding pods) | H | Capital | L | `semi bolt stick swarm seek-metal bio biomass` | Needs ROOM
*Does:* a ship launches leathery pods that fasten to hulls, chew in, and let out biting hatchlings that die within a minute. *Look and sound:* wet thuds on your hull; scratching inside. *Why:* boarding without crew: for bio-ships. The hatchlings are a short-lived shot, not a crew: nothing here stays or takes orders. *Cost:* an entire hold of biomass. *Ammo:* biomass. *Counter:* point defence, crew inside, fire doors.

### 4.25 Gravity and force

Owner rule: floating that looks accidental reads as a bug. Anything here that lifts objects must be a clearly visible anomaly: a glowing cause you can point at, a marked area, and objects that visibly move, turn and drop. Nothing hangs still in the air.

**GV1 Shove Cannon** (force pusher) | M-H | Hand, Hover | S | `charge cone force stagger cells`
*Does:* a thump that throws light things off cliffs; no damage. *Look and sound:* a deep woomph, dust. *Why:* the terrain does the damage. *Cost:* no kill by itself. *Ammo:* cells. *Counter:* bracing, heavy targets.

**GV2 Anchor Hook** (pull beam) | H | Hand | S | `semi tether force shunt cells`
*Does:* pulls a target to you, or you to it. *Look and sound:* a snap and a grunt. *Why:* pulls a sniper off a ledge, or gets you up one. *Cost:* puts you close to it. *Ammo:* cells. *Counter:* heavy anchors; cut it.

**GV3 Well Launcher** (pull grenade) | H | Hand | M | `semi lob field grav pin capsule`
*Does:* a small well that drags everything nearby towards it for four seconds. *Look and sound:* a dark glowing bead, a low rising hum, dust and loose stones spiralling in. *Why:* gathers a pack for an explosive, and pulls enemies out of cover. *Cost:* rare; it pulls you too. *Ammo:* capsules. *Counter:* big things resist; shoot it.

**GV4 Lift Beam** (levitation) | H | Hand | M | `hold beam grav lift charge` | Needs BODY
*Does:* raises a target a few metres inside a bright column; they spin and flail, then fall. *Look and sound:* a visible shaft of pale light with dust streaming up it, a swelling sigh. *Why:* takes a heavy out of a fight and sets up falling damage. *Cost:* you must hold on. *Ammo:* cells. *Counter:* ceilings, huge mass.

**GV5 Drift Charge** (zero-g grenade) | H | Hand | M | `timer thrown field grav lift capsule` | Needs BODY
*Does:* everything in a marked sphere lifts, turns and drifts for six seconds, then drops. *Look and sound:* a glowing core at the centre, a pale ring on the ground, loose stones and bodies rising and slowly tumbling round it. *Why:* helpless enemies; and in vacuum, doomed ones. *Cost:* you float if you're in it. *Ammo:* capsules. *Counter:* step out.

**GV6 Weight Projector** (heavy field) | H | Hand, Hover | M | `charge field grav slow charge`
*Does:* doubles gravity in a dome for eight seconds: everything slows, jumps shrink, flyers sink. *Look and sound:* a dull pressure, things crunching down. *Why:* brings low flyers to the ground, and it's the mirror of drift. Uses the existing per-body gravity. *Cost:* you're slowed if inside. *Ammo:* cells. *Counter:* stay out.

**GV7 Press Cannon** (crush round) | H | Small, Turret | M | `semi bolt grav exp breach power`
*Does:* crushes a fist-sized spot of a hull inward, ignoring plating. *Look and sound:* a creaking collapse. *Why:* armour value counts for nothing; a shot that cuts a block out of a thick hull. *Cost:* small area. *Ammo:* power, fold crystal per shot. *Counter:* shields.

**GV8 Catch Gun** (grab and throw) | H | Hand | L | `hold beam grav force cells` | Needs BODY
*Does:* picks up crates, barrels, drums and small creatures and flings them. *Look and sound:* a lock tone, things straining and shaking in midair. *Why:* the world becomes your ammo; a barrel becomes a grenade, a saw blade a spear. *Cost:* needs loose bodies in the world; heavy things slow you. *Ammo:* cells. *Counter:* an empty room.

**GV9 Flip Mine** (gravity reversal) | H | Placed | M | `placed field grav lift capsule` | Needs BODY
*Does:* inverts gravity in a small lit column for three seconds: anyone over it falls up, then down. *Look and sound:* a click, a bright ring on floor and ceiling, boots and loose gear flying upward. *Why:* a ceiling is now a floor: fall damage from a place with no fall. *Cost:* single use. *Ammo:* capsules. *Counter:* low ceilings; hover.

**GV10 Snare Well** (warp interdictor) | H | Small, Turret | M | `field grav pin power` | Needs DEVICE
*Does:* a field that stops a vessel warping or jumping out of a fight while it runs. *Look and sound:* the star field tugs and blurs. *Why:* stops an escape: needed for capture and pirates. *Cost:* huge power, and it's a big glowing target. *Ammo:* power, fold crystal. *Counter:* kill the generator.

**GV11 Deep Well** (capital black-well) | H-R | Capital | L | `charge bolt float grav exp void power` | Needs BLAST, BODY
*Does:* a slow grey point that drags everything to it, then collapses. *Look and sound:* stars bending, a silence. *Why:* a fleet-fight end game: pulls small ships off course and into each other. *Cost:* rare, and it doesn't take sides. *Ammo:* a precursor core, single use. *Counter:* run, jump out.

**GV12 Tow Beam** (tractor beam) | M-H | Small, Capital | M | `hold beam grav pin power`
*Does:* holds a vessel, a wreck or a pod to you. *Look and sound:* a pale cone, a creak of strain. *Why:* salvage, capture and slowing a target for the guns. *Cost:* slows you too. *Ammo:* power. *Counter:* thrust; cut power.

**GV13 Magnet Glove** (disarming magnet) | H | Hand | S | `hold beam em force shunt cells`
*Does:* yanks a held metal weapon out of an enemy's hands, and pulls loose steel towards you. *Look and sound:* a heavy palm plate, a humming pull, a gun skidding across the floor. *Why:* a soldier without a gun has to run for it, and you've just been handed a free gun. *Cost:* drags your own metal gear about; short reach. *Ammo:* cells. *Counter:* ceramic and bone weapons, slings on the gun.

### 4.26 Tethers, nets and snares

**TE1 Harpoon Rig** (harpoon gun) | L-M | Hand, Hover, Small | M | `semi tether stick kin pin bolts`
*Does:* an air gun throws a barbed head on a line; the line hauls prey in or holds a flyer down. *Look and sound:* a pneumatic chunk, the whine of a line paying out. *Why:* a hover craft can snare a big creature; a small vessel can spear a fleeing one. *Cost:* one line, one target, and it drags you if the target is bigger. Line always stays anchored at both ends. *Ammo:* harpoon heads. *Counter:* cut the line.

**TE2 Net Gun** (net launcher) | L-M | Hand | S | `semi lob glue pin bolts`
*Does:* a weighted net that entangles for seconds. *Look and sound:* a thump, a spreading web. *Why:* takes a creature alive or stops a runner; gangs love it. *Cost:* a short range; useless on big things. *Ammo:* nets. *Counter:* knives; heavy strength.

**TE3 Bola Thrower** (leg snare) | L | Hand | S | `thrown bolt tether slow none`
*Does:* whirled weighted cords that wrap legs. *Look and sound:* a thrum, a clack of stones. *Why:* no-tech way to bring down a runner for hunters and scavengers. *Cost:* poor aim, can't wrap anything with no legs. *Ammo:* reusable. *Counter:* flight; knives.

**TE4 Grapple Hook** (traversal tether) | L-M | Hand | S | `semi tether kin none`
*Does:* a hook on a line: climb, swing, pull a door off, drag a light enemy from a ledge. *Look and sound:* a clang and a reel. *Why:* movement first, weapon second; rewards vertical POIs. *Cost:* occupied hands. *Ammo:* line. *Counter:* cut it; anything heavy.

**TE5 Tow Hook** (vessel grapple) | M | Hover, Small, Capital | M | `semi tether kin pin power`
*Does:* a hull-mounted hook on a heavy chain to tow a wreck or hold a vessel alongside. *Look and sound:* a chain rattling out, a hull clang. *Why:* boarding at range: two ships held side by side. *Cost:* both vessels share their handling. *Ammo:* power. *Counter:* shoot the chain.

**TE6 Boarding Claw** (boarding pod) | M | Small, Capital | L | `semi bolt stick exp breach capsule` | Needs ROOM
*Does:* a pod rams a hull, grips, cuts in, and opens an airlock for your squad. *Look and sound:* a shock through the hull, a cutter's spit. *Why:* a real boarding entry, and the design's "boarding pod". *Cost:* the pod can be shot down. *Ammo:* pods. *Counter:* point defence; internal guards.

### 4.27 Nanite

**NA1 Mote Cloud** (block-eating nanite cloud) | H | Hand, Turret | L | `thrown lob field nanite corrode dig capsule` | Needs SWARM
*Does:* a grey cloud that eats blocks, slowly, in the shape of the cloud. *Look and sound:* a hanging haze, a hiss, edges turning to dust. *Why:* a siege tool that needs no explosive at all and eats anything that's not stone or shielded. *Cost:* rare; the cloud can drift back. *Ammo:* nanite pack. *Counter:* sealed shields, EM kills it.

**NA2 Unmaker Beam** (salvage disassembler) | H | Hand, Small | M | `hold beam nanite noblock dig cells`
*Does:* breaks wrecks and blocks into salvage fast, weak on anything alive. *Look and sound:* a grey-gold fizz, things sagging into powder. *Why:* a tool that doubles as a door opener. *Cost:* slow against living enemies. *Ammo:* nanite pack. *Counter:* live targets ignore it.

**NA3 Quiet Code** (Husk shutdown dart) | H | Hand | S | `semi bolt stick nanite machineonly capsule`
*Does:* a dart carrying a shutdown order for old medical nanites and implants: Husks drop, and stay down; cyborgs stutter. *Look and sound:* a soft chime; a Husk folds where it stands. *Why:* a mercy tool built on the world's backstory, and worth finding on a Husk world. *Cost:* single use; works on one. *Ammo:* single use, found in old hospitals. *Counter:* creatures and robots with other code.

**NA4 Turncoat Dart** (robot scrambler) | H | Hand | S | `semi bolt stick nanite confuse capsule` | Needs MIND
*Does:* scrambles a feral robot for ten seconds: it fires on the nearest machine, then shuts down. It takes no orders from you. *Look and sound:* its lights switch from amber to white. *Why:* a robot turned at the right time wins a room. *Cost:* small stack. *Ammo:* single use. *Counter:* hardened circuits; Kessari.

**NA5 Hull Paste** (repair foam under fire) | H | Small, Turret | M | `hold cone nanite mend charge`
*Does:* sprays quick armour paste onto a hull. *Look and sound:* a silver foam. *Why:* the design's Field repair doesn't run under attack, so this is the emergency patch that does a little, in a fight. *Cost:* weak, heavy. *Ammo:* nanite pack. *Counter:* fire burns it off.

**NA6 Scrap Locust** (metal-eating machine swarm) | H | Small, Turret | M | `semi swarm seek-metal nanite corrode nanopack` | Needs SWARM
*Does:* a pod bursts into a cloud of small machines that fly to the nearest metal and strip plate. *Look and sound:* a glittering grey cloud, a sound like hail on a tin roof. *Why:* a fight against armour that keeps working after the launcher has gone, and it can also be set loose on a wreck for salvage. *Cost:* they strip anything metal, and that can be you. *Ammo:* nanite pack. *Counter:* shields, EM pulses, stone and wood.

### 4.28 Phase and fold

**PH1 Swap Rifle** (position swap) | H | Hand | M | `semi ray shunt charge`
*Does:* swaps your place with the target's. *Look and sound:* a blink, a sharp inrush of air. *Why:* puts a sniper in the kill zone and you on his tower; wild tactical plays. *Cost:* you're now where they were: maybe in the blast. *Ammo:* cells; recharge. *Counter:* big shields; bait.

**PH2 Shunt Gun** (displacement) | H | Hand, Turret | M | `semi ray shunt charge`
*Does:* jumps a target a few metres in the direction hit. *Look and sound:* a pop. *Why:* moves a guard off a ledge, or an enemy into a lava pool. Stuck-safe rules apply. *Cost:* small hop; needs terrain to matter. *Ammo:* cells. *Counter:* heavy bodies.

**PH3 Ghost Round** (through-wall round) | H | Hand | M | `semi bolt phase kin fleshonly rounds` | Needs PHASE
*Does:* passes through blocks, harms only flesh. *Look and sound:* a faint blur, a soundless flash. *Why:* wall-hunts with a heat scope. *Cost:* no effect on machines; a scope is needed to know. *Ammo:* rounds, echo shards. *Counter:* robots; lead-lined rooms.

**PH4 Phase Lance** (wall-ignoring beam) | H | Hand | M | `hold beam phase en pierce cells` | Needs PHASE
*Does:* a low hum that passes through a wall to hit what's behind it. *Look and sound:* no flash, a colourless thread. *Why:* cover stops mattering in one line. *Cost:* slow to build. *Ammo:* cells. *Counter:* dense shielding.

**PH5 Rift Pocket** (hole in space) | H | Hand | M | `thrown lob field shunt capsule`
*Does:* pulls things in a marked circle into a hole and spits them out a few seconds later somewhere nearby. *Look and sound:* a dark seam, a thump as they reappear. *Why:* removes attackers from a fight for a few seconds. *Cost:* they come back; a stuck-safe guard is needed. *Ammo:* capsules. *Counter:* step out of the ring.

**PH6 Fold Shell** (shell that arrives inside) | H | Turret, Capital | M | `semi bolt phase exp shieldpass foldcrystal` | Needs SHIELD
*Does:* a shell that folds past the shield and arrives a metre inside the hull. *Look and sound:* a spark of blue, then an explosion in a corridor. *Why:* the shield-passing round for capital duels. Uses fold crystal, so firing it empties your jump fuel. *Cost:* every shot is a jump you won't make. *Ammo:* fold crystal. *Counter:* interior armour; a charged armour layer scrambles it.

**PH7 Gate Pair** (two linked gates) | H | Hand | L | `semi field shunt phase charge` | Needs VIEW, PHASE
*Does:* two marks you shoot on walls; walk through one, exit the other; so can rounds and rockets. *Look and sound:* two shimmering ovals; through one you see the other side. *Why:* a building-block tool for movement and for trick shots; a whole new way to attack a base. *Cost:* the engine cost. *Ammo:* recharge. *Counter:* close the pair by shooting one.

**PH8 Return Stake** (recall beacon) | H | Placed | M | `placed shunt charge`
*Does:* drive a stake; within a minute, pressing the button throws you back to it. *Look and sound:* a small hum, a sudden wrench. *Why:* a dive-and-escape tool for raids. Doesn't work across a jump. *Cost:* one stake, and enemies can find and move it. *Ammo:* recharge. *Counter:* find it.

### 4.29 Time and perception

**TM1 Slow Bubble** (slow-time field) | H | Hand, Hover | M | `charge field slow charge` | Needs ACTOR
*Does:* a bubble in which things run at half speed for six seconds, bolts included. *Look and sound:* sound drops an octave inside. *Why:* catches rockets, holds a doorway. *Cost:* you're slow if you step in. *Ammo:* cells. *Counter:* stay out.

**TM2 Stutter Dart** (rewind) | H | Hand | M | `semi bolt rewind capsule` | Needs ACTOR
*Does:* sends a target back to where it stood two seconds ago. *Look and sound:* a short tape-rewind. *Why:* undoes a charge or a dodge; drags a target back into a trap. *Cost:* small stack. *Ammo:* capsules. *Counter:* shields; big targets.

**TM3 Delay Pistol** (delayed effect gun) | H | Hand | M | `semi ray delay en cells`
*Does:* the shot makes no mark; three seconds later, the target bursts. *Look and sound:* a thin click and silence, then a wet pop. *Why:* you must trust your aim and plan your exit, and a roomful of bodies falls in order. *Cost:* three seconds of being shot at. *Ammo:* cells. *Counter:* a medkit; a shield over the delay.

**TM4 Echo Cannon** (repeat strike) | H | Hand, Hover | M | `semi bolt delay en cells`
*Does:* a bolt that hits again at the same spot two seconds later. *Look and sound:* a double-flash. *Why:* punishes staying where you were, or rewards anticipating. *Cost:* less up-front. *Ammo:* cells. *Counter:* not standing there.

**TM5 Hold Capsule** (stasis) | R | Hand | M | `thrown lob field freeze capsule` | Needs ACTOR
*Does:* holds a target still and untouchable for four seconds. *Look and sound:* a glass-still shell. *Why:* saves an ally, or delays a boss. A found object only. *Cost:* rare, can't be crafted. *Ammo:* single use. *Counter:* timer.

**TM6 Afterimage** (body decoy) | H | Hand | S | `placed decoy charge`
*Does:* leaves a short-lived image of you that draws fire. *Look and sound:* a flicker, a double. *Why:* breaks a target lock on foot. *Cost:* it runs out quickly. *Ammo:* recharge. *Counter:* sound, heat.

**TM7 Holo Squad** (fake soldiers) | H | Placed | M | `placed decoy charge`
*Does:* projects a false squad that patrols a short route. *Look and sound:* faintly flickering figures. *Why:* pulls a garrison off the real attack. Voidglass in the recipe. *Cost:* can't shoot; a touch breaks it. *Ammo:* charge. *Counter:* touch; heat sight.

**TM8 Dusk Dart** (darkness) | H | Hand | S | `semi bolt field blind capsule`
*Does:* a patch of unlit dark: kills lights and blocks sight in a small sphere. *Look and sound:* a drop of black ink. *Why:* stealth in a lit room. *Cost:* you can't see either without a heat scope. *Ammo:* capsules. *Counter:* heat sight.

### 4.30 Mind

**MI1 Dread Horn** (fear horn) | M | Hand | S | `charge cone sonic fear charge`
*Does:* a low horn that sends creatures running. *Look and sound:* a bone horn, a note that lifts your hair. *Why:* an escape from a pack using the flee state they already have. *Cost:* draws everything else. *Ammo:* recharge. *Counter:* fearless things, machines.

**MI2 Calming Bolt** (pacify) | M | Hand | S | `semi bolt calm capsule`
*Does:* pacifies an animal for thirty seconds. *Look and sound:* a soft dart. *Why:* pass a nest without a fight. *Cost:* single use. *Ammo:* capsule. *Counter:* bosses; other pack members.

**MI3 Sleep Dart** (tranquilliser) | M | Hand | S | `semi bolt sleep capsule`
*Does:* puts flesh to sleep for fifteen seconds. *Look and sound:* a pfft, a slump. *Why:* silent take-down; live capture; harvesting from a living creature. On a player, seconds and a dimmed visor. *Cost:* takes three seconds to act; noise wakes them. *Ammo:* capsules. *Counter:* armour, machines, noise.

**MI4 Lure Beacon** (creature lure) | M | Placed | S | `placed lure capsule`
*Does:* a call that draws creatures to a spot. *Look and sound:* a low throb. *Why:* sets wildlife on a Reaver camp. *Cost:* you don't choose what arrives. *Ammo:* capsule. *Counter:* ignore it; shoot it.

**MI5 Confusion Lens** (panic beam) | H | Hand | M | `hold beam light confuse cells` | Needs MIND
*Does:* a coloured strobe that makes soldiers shoot each other for seconds. *Look and sound:* a slow spiral of colour on the target's face. *Why:* one person turns a squad. *Cost:* you need to hold on target. *Ammo:* cells. *Counter:* machines; visors.

**MI6 Forget Lamp** (memory flash) | H | Hand | M | `charge pulse light blind calm cells` | Needs MIND
*Does:* a soft flash that resets guards to unaware: they forget the last ten seconds. *Look and sound:* a small lamp, a pale white wipe. *Why:* a do-over for stealth players: you were never here. *Cost:* slow to recharge. Bodies are still bodies. *Ammo:* cells. *Counter:* a second guard round the corner.

**MI7 Stare Sign** (image that stops the mind) | H-R | Placed | M | `placed light freeze blind capsule` | Needs MIND
*Does:* a placed panel with a shape that freezes anyone who looks at it for seconds. *Look and sound:* a wrongly-angled figure on a plate, a mild headache on your own screen. *Why:* a defensive tile for doors: pure line-of-sight. *Cost:* you can't look at it yourself. *Ammo:* single use. *Counter:* machines; a blindfold; walk in backwards.

### 4.31 Disintegrators and transformation rays

**DS1 Ash Ray** (disintegrator) | H | Hand | M | `charge beam en void cells`
*Does:* a soft crawling beam that turns a soft target to a falling drift of ash in about a second. *Look and sound:* a pale green outline, a sigh, a heap on the floor. *Why:* one target gone, no corpse and no loot: you trade the harvest for the kill. *Cost:* leaves nothing to salvage or harvest, and it can't reach through plate. *Ammo:* cells. *Counter:* plated targets; the slow first second.

**DS2 Cross Beam** (converging stream) | H | Hand, Turret | M | `hold beam en pierce cells`
*Does:* two harmless beams that cut only where they meet, at a focal distance you set. *Look and sound:* two thin lines closing to a bright knot. *Why:* burns the thing behind glass, or inside a shield's ripple, without harming the glass. A turret version can reach past a friendly hull. *Cost:* needs distance set before each shot; useless closer than focal range. *Ammo:* cells. *Counter:* close the distance; moving targets.

**DS3 Unbinding Lance** (chain-reaction disruptor) | R | Capital | L | `charge beam en void breach power` | Needs BLAST
*Does:* a capital relic whose beam sets off a spreading failure in solid matter: a hull comes apart in a wave from the impact point. *Look and sound:* a dull white spreading edge, silence. *Why:* one sight of it makes a player very afraid of that ship. *Cost:* found only, one or two shots. *Ammo:* a found charge. *Counter:* a gap in the hull stops the wave.

**DS4 Shrink Ray** (shrink) | H | Hand | M | `hold beam shrink cells` | Needs ACTOR
*Does:* makes a target smaller and weaker for fifteen seconds. *Look and sound:* a rising cartoon warble. *Why:* a boss becomes a joke for a moment; a stomp finishes small things. *Cost:* you must hold it on them. On a player it lasts seconds. *Ammo:* cells. *Counter:* duration. A rare find: never crafted and not in the tree.

**DS5 Meek Ray** (transformation) | H | Hand | M | `semi ray confuse cells` | Needs ACTOR
*Does:* turns a target into a harmless small animal for eight seconds. *Look and sound:* a puff, a bleat. *Why:* takes the strongest thing out of a fight, and it's funny. A named tonal item. *Cost:* large targets resist. *Ammo:* cells. *Counter:* wait it out. A rare find: never crafted and not in the tree.

**DS6 Growth Dart** (growth) | H | Hand | S | `semi bolt mend capsule` | Needs ACTOR
*Does:* enlarges a squadmate for a while. *Look and sound:* a pop and a creak. *Why:* a co-op buff with a sense of humour; also makes low doors a problem. *Cost:* doors. *Ammo:* capsules. *Counter:* a bigger target is easier to hit. A rare find: never crafted and not in the tree.

**DS7 Bubble Gun** (soap trap) | L-M | Hand | S | `semi lob float glue pin water`
*Does:* traps a small target in a drifting tough bubble for seconds. *Look and sound:* wobbling rainbow spheres, a pop. *Why:* harmless looking, and a real snare for small creatures and thrown grenades. *Cost:* any sharp thing pops it. *Ammo:* soapy water. *Counter:* pop it. A rare find: never crafted and not in the tree.

### 4.32 Defence and countermeasures

**DF1 Bull Shield** (riot shield) | L-M | Hand | S | `melee kin stagger none`
*Does:* a heavy plate on your arm: blocks fire from the front, and bashes. *Look and sound:* a thick slab, shots clanging. *Why:* a boarding spearhead; lets a team advance down a corridor. A shot stopped by the plate isn't a hit on you, so your personal shield keeps refilling behind it. *Cost:* one hand gone, slow walk. *Ammo:* none. *Counter:* grenades; flanks.

**DF2 Mirror Shield** (reflecting guard) | H | Hand | S | `melee light none`
*Does:* a silvered shield that throws back lasers and energy bolts if you time it. *Look and sound:* a ringing flash. *Why:* the skilled counter to beam weapons, and a good parry saves the bar as well as the shooter's day. *Cost:* nothing against kinetics. *Ammo:* none. *Counter:* bullets.

**DF3 Pop Dome** (deployable shield) | H | Placed | M | `placed field charge` | Needs SHIELD
*Does:* a thrown puck raises a small dome for ten seconds. Shots and blasts stop at its skin, both ways. *Look and sound:* a hex shimmer. *Why:* a personal shield already covers you, so the dome's job is to buy the break. A hit stopped at the dome isn't a hit on anyone inside, so every bar in it starts to refill. It's how a pinned squad gets its shields back, and how you cover a squadmate whose bar has gone. *Cost:* short; blocks your outgoing fire too, and it shows the enemy exactly where you are. *Ammo:* single use. *Counter:* a grenade rolled in at your feet; plasma; walking in; fire and gas, which don't stop at its skin.

**DF4 Flare and Chaff Pod** (vessel decoys) | M | Hover, Small, Capital | S | `pulse decoy capsule`
*Does:* throws heat flares and foil. *Look and sound:* a scatter of hot stars. *Why:* the seeker's counter, on a short supply. *Cost:* few shots. *Ammo:* capsules. *Counter:* wire-guided, tag-following.

**DF5 Sand Burst** (anti-laser shot) | M | Small, Capital | S | `semi pulse field blind scrap`
*Does:* a cloud of reflective grit that blocks lasers and beams for a few seconds. *Look and sound:* a glittering cloud. *Why:* the counter to the whole laser line: ships turn into their own cloud. *Cost:* blinds your own lasers. *Ammo:* sand, literally: crushed rock. *Counter:* kinetics.

**DF6 Needle Curtain** (point-defence gun) | M | Turret | S | `auto bolt kin rounds` | Needs ORDNANCE
*Does:* a very fast small gun that picks rockets and mines out of the air. *Look and sound:* a fine hissing line. *Why:* the design's point defence. *Cost:* eats rounds. *Ammo:* belt. *Counter:* saturation.

**DF7 Thread Laser** (point-defence laser) | H | Turret | S | `hold ray en light power` | Needs ORDNANCE
*Does:* a thin beam that touches each missile in turn. *Look and sound:* a light flicking from dot to dot. *Why:* no ammo, and clean. *Cost:* weather, sand. *Ammo:* power. *Counter:* armoured missile noses, sand burst.

### 4.33 Ship and heavy guns

**HV1 Drum Cannon** (vehicle cannon) | M | Hover, Small | S | `semi bolt exp kin shells`
*Does:* the design's fixed cannon: a slower, heavier round than the gatling. *Look and sound:* a thud felt through the seat, a flash on the ground. *Why:* the ground-attack punch. *Cost:* fewer shots. *Ammo:* 40 mm shells. *Counter:* speed.

**HV2 Chain Cannon** (autocannon) | M | Hover, Small, Turret | S | `auto bolt pierce exp kin shells`
*Does:* a belt-fed 30-odd mm gun with explosive and armour-piercing rounds, mixed on the belt. *Look and sound:* a heavy slow beat. *Why:* covers all light vehicles and most soft blocks. *Cost:* big magazines take up storage. *Ammo:* shells, mixed. *Counter:* heavy plate; shields.

**HV3 Deck Gun** (turret cannon) | M | Turret | S | `semi bolt exp kin shells`
*Does:* the existing large turret, given a role: flat-firing main gun. *Look and sound:* a slam, a bright streak. *Why:* the plain capital and base gun. *Cost:* slow projectile (design: 250 to 350 m/s). *Ammo:* 40 mm shells. *Counter:* jink.

**HV4 Flak Burst** (anti-air cannon) | M | Turret | S | `auto bolt split exp shells` | Needs ORDNANCE
*Does:* the design's flak: shells burst near flyers and rockets. *Look and sound:* a sky of black puffs and a crump-crump. *Why:* area anti-air; fighters have to think. *Cost:* poor on the ground. *Ammo:* flak shells. *Counter:* fly low, or high.

**HV5 Torpedo Tube** (heavy torpedo) | M-H | Small, Capital | M | `lockon bolt seek-lock exp breach rockets` | Needs ORDNANCE
*Does:* a slow, heavy torpedo that kills what it reaches. *Look and sound:* a tube clank, a long shining line. *Why:* a small vessel with a torpedo is a real threat to a capital ship. *Cost:* few, big, costly, slow. *Ammo:* torpedoes. *Counter:* point defence; dodging.

**HV6 Overload Torpedo** (EM torpedo) | H | Small, Capital | M | `lockon bolt seek-lock em shock overload noblock rockets` | Needs ORDNANCE, DEVICE
*Does:* bursts as a huge pulse: blacks out a vessel for seconds. *Look and sound:* a blue sphere, then a dark ship. *Why:* the capture round. *Cost:* no structural damage. *Ammo:* torpedoes. *Counter:* spare generators; point defence.

**HV7 Crew Torpedo** (boarding torpedo) | M-H | Small, Capital | L | `semi bolt stick breach capsule` | Needs ROOM
*Does:* a torpedo with a seat: one player or one squad member rides in and cuts through. *Look and sound:* a lurch, then black. *Why:* the most exciting way in. *Cost:* a one-way ride. *Ammo:* single use. *Counter:* point defence.

**HV8 Depth Rack** (depth charges) | M | Hover, Small | S | `semi lob exp shells`
*Does:* barrels set to burst at a depth. *Look and sound:* a splash, a dome of water. *Why:* the ocean world's answer to sea creatures and subs. *Cost:* no use above water. *Ammo:* barrels. *Counter:* deep water.

**HV9 Ram Spike** (ramming prow) | L-M | Small, Capital | S | `melee kin breach none`
*Does:* a welded spike that makes the existing collision rule better for the rammer. *Look and sound:* a grinding shriek. *Why:* the Reavers' signature. Ramming works and hurts: this tilts it. *Cost:* mass; both of you get hurt. *Ammo:* none. *Counter:* get out of the way.

**HV10 Hull Thumper** (shock wave gun) | H | Capital | L | `charge pulse force stagger power` | Needs BODY
*Does:* a ring of force that pushes small vessels and missiles away from the hull. *Look and sound:* a deep gong, debris pushed out in a ring. *Why:* clears an attached boarding pod or a swarm. *Cost:* a long recharge. *Ammo:* power. *Counter:* go back in after the ring.

### 4.34 Nuclear

**NU1 Hand Bomb** (recoilless mini-nuke) | M-H | Hand | M | `semi lob exp rad crater rockets` | Needs BLAST
*Does:* a shoulder launcher throws a very small atomic round, a few hundred metres at most. *Look and sound:* a fat yellow can, a flash, a small mushroom. *Why:* the biggest thing a single person can carry, and the memorable moment. *Cost:* you need distance and cover, and it leaves a warned radiation field. *Ammo:* single rounds. *Counter:* close on the shooter before they fire.

**NU2 Sun Shell** (tactical atomic shell) | M-H | Hover, Turret | M | `charge lob exp rad crater shells` | Needs BLAST
*Does:* an artillery-fired atomic shell. *Look and sound:* a false sunrise. *Why:* ends a siege. *Cost:* Faction reaction; heavy; fallout. *Ammo:* single rounds. *Counter:* intercept.

**NU3 Ghost Warhead** (neutron warhead) | H | Capital | M | `lockon bolt seek-lock rad crewonly noblock rockets` | Needs ROOM, ORDNANCE
*Does:* kills crew with little structural harm. *Look and sound:* a thin flash. *Why:* a ship with no crew is a prize. *Cost:* the ship stays hot for a few minutes. *Ammo:* warheads. *Counter:* rad-shielded rooms.

**NU4 Cone Warhead** (nuclear shaped charge) | H | Capital | M | `lockon bolt seek-lock exp pierce breach rockets` | Needs ORDNANCE
*Does:* the warhead throws a narrow jet of plasma from standoff range at one target. *Look and sound:* a white needle across the sky. *Why:* a warhead with reach: it doesn't need to touch. *Cost:* expensive, narrow. *Ammo:* warheads. *Counter:* move off the line.

**NU5 Star Array** (bomb-pumped laser) | H | Capital | L | `lockon pulse beam split en rad rockets` | Needs ORDNANCE
*Does:* a warhead whose blast drives a dozen one-shot x-ray rods, each at its own target. *Look and sound:* a flash and a dozen thin lines. *Why:* a one-time fleet strike at many targets. *Cost:* the most expensive round in the game. *Ammo:* warheads. *Counter:* kill it before it gets close.

**NU6 Salt Warhead** (dirty warhead) | M-H | Capital | M | `lockon lob exp rad field rockets` | Needs BLAST
*Does:* a modest blast that leaves a large, long-lasting, clearly marked radiation field. *Look and sound:* a dirty mushroom and a sick glow. *Why:* denies an area or a world region. Harm stops when you leave: it's exposure, not an ailment. *Cost:* every faction hates you. *Ammo:* warheads. *Counter:* radiation boosters.

**NU7 Blackout Warhead** (high-altitude EM) | H | Capital | M | `lockon pulse em shock overload noblock rockets` | Needs DEVICE, ORDNANCE
*Does:* high air burst that shuts off devices across a large area for a minute. *Look and sound:* a false aurora; lights die. *Why:* a whole base goes dark before you land. *Cost:* yours too, if nearby. *Ammo:* warheads. *Counter:* shielded rooms; low-tech defenders don't care.

### 4.35 Antimatter

The family rule is containment, and the rest follows from it. Antimatter sits in a powered bottle. A bottle that loses power goes off: unlike a nuclear warhead, which won't explode by accident, this one's safe state needs effort. So every antimatter weapon has three shared rules.

- **Storage:** antimatter can only sit in a powered Bottle Bay on a structure, or in a hand case with its own short-life battery (about ten minutes off power, with a visor countdown). No plain containers.
- **Failure:** if a carrier dies or a bay loses power, a warning tone runs for a grace period (reference 30 seconds on a vessel, 60 on a dropped hand case), then whatever is stored goes off, scaled by the amount. Warned, never ambushed. A player can eject bottles in that window. An enemy ship's bay shows on a good scanner, so shooting it is a real tactic.
- **Cost:** made only at a top-tier containment plant from fusion cells and stormcore over a long time, or found sealed in precursor stores. A few units an hour at best. Damage ignores material resistance inside its core radius.

Limits are in section 13 under BLAST. Today's `Damage.BlastInto` tests every cell in the cube around the blast in one frame, with no budget, and craters are capped at 6 m and 900 cells. Antimatter blasts beyond a 10-cell radius need that upgrade. The Speck Rifle fits inside today's limits.

**AM1 Speck Rifle** (hand-held antimatter round) | H | Hand | M | `charge bolt exp void breach crater antimatter`
*Does:* a magnetic bead a grain across, thrown by a coil. It removes a sphere of a metre or two: person, door or block, resistance ignored. *Look and sound:* a heavy case on your hip with a live needle; a violet spark, then a clean bite out of the world with a white flash and a gamma tick on your visor. *Why:* the surest hit in the game on one thing, for when you'd pay anything to end it. *Cost:* the case must stay powered; a dropped gun is a bomb on a timer; a few rounds is a fortune; close shots dose you with seconds of radiation. *Ammo:* antimatter beads. *Counter:* kill the carrier in a crowd of his friends; the shot's slow enough to dodge at range.

**AM2 Anti-Stream** (antimatter beam) | H | Turret, Capital | L | `hold beam exp en void strip antimatter` | Needs BLAST
*Does:* a stream of antiparticles that burns away in air after 30 m in a glowing rod, but runs for kilometres in vacuum. *Look and sound:* a violet line with sparks along it where dust burns. *Why:* an opposite to lasers: it's weak on a planet and supreme in space, and it's the only hold-to-fire weapon that ignores armour. *Cost:* it burns stored antimatter by the second. Hard-SF writers call this a waste of antimatter, and that's the trade. *Ammo:* antimatter. *Counter:* fight in atmosphere; a nebula; shields burn it off for a while.

**AM3 Bottle Shell** (vehicle shell) | H | Hover, Small | M | `semi bolt exp void breach crater antimatter` | Needs BLAST
*Does:* a palm-sized bottle in a shell: one shot removes a room or a device stack. *Look and sound:* the magazine hums on your panel; a double crack: casing, then flash. *Why:* a hover or small vessel can trade itself for a capital ship's reactor. *Cost:* a magazine in your own hold is a bomb; a hit on your bay ends you and anyone with you. *Ammo:* antimatter. *Counter:* shoot the bay; fighters' flak.

**AM4 Bright Torpedo** (capital torpedo) | H | Capital | M | `lockon bolt seek-lock exp void breach antimatter` | Needs BLAST, ORDNANCE
*Does:* a slow torpedo that carries enough to gut a capital ship. *Look and sound:* a short star that stays burned on your view. *Why:* the fleet fight's decider. If it's shot down it still bursts, so point defence must kill it far out. *Cost:* one torpedo is worth a small fleet, and each one in your tube is a risk. *Ammo:* antimatter. *Counter:* far-out interception; decoys.

**AM5 Last Bottle** (base-killing charge) | H | Placed | L | `timer placed exp void crater breach antimatter` | Needs BLAST
*Does:* a two-person case carried into a base core room and set. On the timer, its power cuts. Everything in a huge radius is gone, and the ground holds a glass-floored crater. *Look and sound:* a rack of green lights going red, a countdown through the visor, then nothing, then the crater. *Why:* the mission that ends a fortress, and the moment the game's biggest crater appears on the map. *Cost:* the whole mission's stake; it can be found and carried off, and it's a bomb in your own hold on the way. *Ammo:* antimatter, a lot. *Counter:* turn off its timer; drag it to an airlock; shoot the carriers.

**AM6 Trigger Shell** (catalysed warhead) | H | Hand, Hover, Turret | M | `semi lob exp rad crater antimatter` | Needs BLAST
*Does:* a speck sets off a small fusion burn, so it uses far less antimatter than a pure round. *Look and sound:* a white sphere with a thin cold edge. *Why:* a cheap entry to the family: a clean small warhead with no lingering field. *Cost:* still needs a bay. *Ammo:* antimatter trace. *Counter:* intercept.

**AM7 Dumped Bottle** (ejected bay as a mine) | H | Small, Capital | M | `timer placed float exp void antimatter` | Needs BLAST
*Does:* ejects your stored bottles out of a hatch on a timer. *Look and sound:* a small silver flask tumbling behind you. *Why:* your own risk becomes a parting shot; also how you save a ship with a failing generator. *Cost:* everything you stored. *Ammo:* whatever's in the bay. *Counter:* stop chasing.

### 4.36 Electronic war and cyber arms

**EW1 Paint Lamp** (designator) | M | Hand | S | `hold ray light mark cells`
*Does:* a beam that marks a target for artillery, cruise missiles and any tag-following round. *Look and sound:* an invisible dot in the scope; a tone. *Why:* one person becomes a fire controller, and the only safe way to land a Long Reach. *Cost:* stay in line of sight. *Ammo:* cells. *Counter:* smoke, detection of the beam.

**EW2 Jam Pod** (ECM) | M-H | Hover, Small, Turret | S | `pulse em blind power` | Needs DEVICE
*Does:* blocks locks and seekers near you. *Look and sound:* static on rival screens. *Why:* defeats the seeker line. *Cost:* a bright radar source: it tells people you're there. *Ammo:* power. *Counter:* home-on-jam; wire guidance.

**EW3 Drag Field** (speed snare) | H | Small, Turret | M | `beam em slow power` | Needs DEVICE
*Does:* locks onto a vessel and bleeds its top speed. *Look and sound:* a lead-weight shudder. *Why:* holds a fast target for slower guns. *Cost:* short range. *Ammo:* power. *Counter:* thrust; kill it.

**EW4 Power Leech** (capacitor drain) | H | Small, Turret | M | `beam em strip power` | Needs DEVICE
*Does:* drains a target's power; its guns and thrusters starve. Some of the power comes to you. *Look and sound:* a red thread. *Why:* an attack on the design's power budget. *Cost:* close range. *Ammo:* power. *Counter:* fuel reserves; capacitors.

**EW5 Spoof Gun** (network attack) | H | Hand, Small | M | `hold ray em confuse cells` | Needs DEVICE, MIND
*Does:* holds a turret or door on a false friend signal for a few seconds. The turret stops firing at you and does nothing else. *Look and sound:* a progress ring, the turret's lamp turning from red to green. *Why:* a thinking player's way through turret lines; a lighter cousin of the Hijack Tool. *Cost:* slow; each target needs a fresh try. *Ammo:* cells. *Counter:* hardened cores; manned guns.

**EW6 Palm Emitter** (cyber hand gun) | H | Hand | S | `semi ray en cells`
*Does:* a cyber module: fire a small energy shot from your open hand, weapon holstered. *Look and sound:* a small lit port in the palm. *Why:* an unarmed last resort, and walks past a weapons check. *Cost:* takes a cyber slot. *Ammo:* suit power. *Counter:* it's weak.

**EW7 Wrist Blade** (cyber hidden blade) | M | Hand | S | `melee kin none`
*Does:* a cyber module: a blade from the forearm, instant and silent. *Look and sound:* a tiny click. *Why:* a never-empty melee that skips the weapon swap. *Cost:* a cyber slot. *Ammo:* none. *Counter:* armour.

**EW8 Shoulder Rack** (cyber rocket rack) | H | Hand | M | `semi bolt exp rockets`
*Does:* a cyber-slot shoulder mount fires a small rocket while your hands hold something else. *Look and sound:* a hiss by your ear. *Why:* a second gun for a builder or a carrier. *Cost:* heavy, a cyber slot, your own flash. *Ammo:* mini rockets. *Counter:* it's small.

### 4.37 Block and terrain

**BT1 Breaching Pipe** (extending demolition tube) | M | Placed | S | `placed exp breach mines`
*Does:* screw sections together and slide it under a wire, wall or minefield. *Look and sound:* a long rattle, a straight line of blast. *Why:* opens a lane through mines and barricades. *Cost:* slow to assemble, in the open. *Ammo:* pipe sections. *Counter:* shoot the sapper.

**BT2 Quake Spike** (local tremor) | M | Placed | M | `timer placed force stagger crater dig capsule`
*Does:* a spike driven in the ground that shakes people down and digs a capped crater. *Look and sound:* a deep hum, rising; dust on the ground dancing. *Why:* takes the ground out from under a position, and it's a mining tool too. *Cost:* capped: one crater's worth. *Ammo:* single use. *Counter:* get off the ground.

**BT3 Sink Charge** (pit maker) | H | Hand | M | `thrown lob dig crater capsule`
*Does:* a pellet that opens a pit under a target. *Look and sound:* a thump and a vanishing floor. *Why:* a trap for a tank. Uses remove-only digging, so it fits the rules. *Cost:* soil only. *Ammo:* single use. *Counter:* fly.

**BT4 Wall Thrower** (instant barricade) | M | Hand | M | `semi lob placed scrap` | Needs GROUND
*Does:* a launcher that throws a folding steel barricade that stands up as real blocks. *Look and sound:* a clattering unfold. *Why:* instant cover without a build mode; a defensive gun. *Cost:* slow, and it takes space in your pack. *Ammo:* barricades. *Counter:* rockets.

**BT5 Glasser** (ground melter) | H | Hover, Small | M | `hold beam en fire glass` | Needs GROUND
*Does:* a beam that turns ground to slick glass. *Look and sound:* a heat shimmer, a sharp chiming crackle as it cools. *Why:* a slippery lane, and it brings a world theme to your own hand. *Cost:* a slow sweep. *Ammo:* power. *Counter:* it shatters.

**BT6 Root Seed** (root barrier) | M | Hand | S | `semi lob placed bio capsule` | Needs GROUND
*Does:* a seed that throws up a tangle of roots as a wall. *Look and sound:* a creak and a swelling rustle. *Why:* a grown cover for bio-builds. *Cost:* burns. *Ammo:* seeds. *Counter:* fire.

**BT7 Demolition Bore** (tunnelling beam) | H | Small, Turret | L | `hold beam kin dig power`
*Does:* cuts a long clean tunnel in terrain. *Look and sound:* a roaring cone of dust. *Why:* the design allows tunnelling under a base: this makes it a siege plan. *Cost:* slow; bedrock stops it. *Ammo:* power. *Counter:* listen for it.

### 4.38 Improvised and junk

**JK1 Junk Cannon** (feeds on anything) | M | Hand | S | `semi bolt kin scrap`
*Does:* an air and coil gun that fires whatever's in your inventory: ore, cans, food. *Look and sound:* a garbage hopper, a clattering cough. *Why:* inventory is ammo; damage follows the item's mass. *Cost:* wastes things you might want. *Ammo:* anything. *Counter:* armour.

**JK2 Saw Thrower** (saw blade launcher) | L-M | Hand | S | `semi bolt bounce kin scrap`
*Does:* a spring arm flings sharpened blades that bounce and stick. *Look and sound:* a whirring shriek. *Why:* a gang favourite; blades can be picked up. *Cost:* blades bounce back. *Ammo:* blades, recoverable. *Counter:* soft cover.

**JK3 Ripped Gun** (torn-off vehicle gun) | M | Hand | S | `auto bolt kin rounds`
*Does:* a turret gun taken from a wreck by a person in heavy armour and carried. *Look and sound:* cables dangling, a jury-rigged grip. *Why:* the design lets you salvage turrets; this lets you use one on foot. *Cost:* crawl speed; no reload. *Ammo:* only what's in it. *Counter:* run away.

**JK4 Cell Bomb** (overloaded battery) | L-M | Hand | S | `timer thrown exp en cells`
*Does:* an energy cell jury-rigged to burst. *Look and sound:* a rising whine; a blue-white pop. *Why:* turns a spare cell into a grenade anywhere. *Cost:* uses power you might need. *Ammo:* a cell. *Counter:* run.

**JK5 Gas Rocket** (burst cylinder) | L | Hand | S | `semi bolt kin force stagger scrap`
*Does:* a gas cylinder with its valve knocked off flies across a room. *Look and sound:* a shrill scream, a wild path. *Why:* a found weapon: any oxygen bottle, any pressurised tank. *Cost:* unaimed. *Ammo:* a cylinder. *Counter:* get out of the way.

**JK6 Rigged Bike** (rolling bomb) | L-M | Placed | M | `timer placed exp fuel`
*Does:* a hover bike or wreck packed with fuel. Point it, jump off. *Look and sound:* a screaming engine with nobody aboard. *Why:* the gang's siege opener. *Cost:* a whole bike. *Ammo:* fuel. *Counter:* shoot it early.

### 4.39 Support

**SU1 Patch Gun** (healing injector) | M | Hand | S | `semi bolt mend capsule`
*Does:* shoots a healing dart at an ally. *Look and sound:* a soft pfft and a green tick. *Why:* co-op medic role. *Cost:* can't heal yourself. *Ammo:* medkit charges. *Counter:* be shot first.

**SU2 Mender Dart** (repair nanite) | H | Hand | S | `semi bolt nanite mend capsule`
*Does:* repairs a block or a suit from a distance. *Look and sound:* a silver bead spreading. *Why:* mid-fight repairs for a co-op engineer. *Cost:* a little. *Ammo:* nanite pack. *Counter:* not during a heavy hit.

**SU3 Weld Beam** (repair beam) | M | Hand, Small | S | `hold beam mend cells`
*Does:* a beam that closes up block damage, and burns at short range. *Look and sound:* a blue spitting point. *Why:* the repair gun that's also a surprise. *Cost:* slow. *Ammo:* cells. *Counter:* range.

**SU4 Pep Dart** (ally haste) | M | Hand | S | `semi bolt haste capsule`
*Does:* an ally moves and reloads faster for twenty seconds. *Look and sound:* a green spark and a heartbeat sound. *Why:* a buff for co-op squads. *Cost:* slower for a short while after, seconds only. *Ammo:* capsules. *Counter:* none: it's a time-limited boost.

### 4.40 Superweapons

**SW1 Bow Gun** (drive-fed wave cannon) | H | Capital | L | `charge beam en crater power` | Needs BLAST
*Does:* the ship dumps its whole drive through a bow aperture: one huge straight beam. *Look and sound:* all lights to the bow, a long rising tone, a line of white. *Why:* a designed-in finish: it leaves you dead in space afterwards, engines cold, shields down. *Cost:* drifting for a long minute. *Ammo:* fold crystal and drive power. *Counter:* leave the line; live through it.

**SW2 Sky Lance** (orbital beam) | H | Capital | L | `charge strike en crater power` | Needs ORBIT, BLAST
*Does:* a station beam fired from orbit at a surface mark. *Look and sound:* a thin line through cloud, a warning hum on the visor, a boiling spot. *Why:* a base you own in orbit becomes a surface weapon. *Cost:* a whole station; a spotter on the ground. *Ammo:* power. *Counter:* kill the station; cloud cover cuts it.

**SW3 Rod Drop** (orbital rod) | H | Capital | L | `semi strike kin crater breach rockets` | Needs ORBIT, BLAST
*Does:* a dense metal rod dropped from orbit. *Look and sound:* a thin bright scratch on the sky, the sound arriving after. *Why:* a simple pure-kinetic version of a nuke, with no fallout. *Cost:* needs orbit and a spotter. *Ammo:* rods. *Counter:* be gone; warned by a streak.

**SW4 Rock Drop** (asteroid fall) | H | Capital | L | `charge strike kin crater scrap` | Needs ORBIT, BLAST
*Does:* a tug drags a chosen asteroid out of its orbit and drops it. *Look and sound:* a slow new star. *Why:* a whole-map event, and the Colony Drop trope. *Cost:* hours of preparation. *Ammo:* a rock. *Counter:* intercept it long before.

**SW5 Storm Seeder** (weather machine) | H | Turret | L | `charge field light power`
*Does:* a base device that seeds clouds and calls a storm to this region. *Look and sound:* a tower puffing into the sky, dark clouds gathering. *Why:* the world's weather as a siege: blots out lasers, gives you lightning, ruins fire. Uses the existing weather sim's forced segments. *Cost:* the storm doesn't take sides. *Ammo:* power, stormcore. *Counter:* kill the tower.

**SW6 Tremor Engine** (earthquake machine) | H | Turret | L | `charge pulse force stagger crater power` | Needs GROUND
*Does:* a deep drill rig that shakes the region and can open a capped chasm. *Look and sound:* a thud under your feet, every bottle rattling. *Why:* a land weapon for a land base. *Cost:* huge power; damages your own foundation. *Ammo:* power. *Counter:* a raid.

### 4.41 Placed machines

These are simply placed weapons. Each stays where you put it or goes off once: nothing here follows you, takes orders or fights at your side, and none of it needs an owned-machine system. The no-companions decision cut the Shoulder Minder, Hunter Needle and Wasp Bay from the player's list, and the Picket Flyer became a fixed post.

Enemies keep those machines. Machine-held places can field gun drones that circle a guard, self-flying blades and carrier bays that launch small gun machines, by the world's rules. They're local machines, never Empyrion-style invasion waves. A captured or salvaged enemy bay gives parts only: it can't be fitted to launch machines for the player.

**MA1 Sentry Stalk** (deployable turret) | M | Placed | M | `placed auto bolt kin rounds`
*Does:* a folded tripod you set down; it stands up, sweeps and shoots at what it sees. It stays where you put it. *Look and sound:* three legs unfolding, a small eye, a measured burst. *Why:* covers your back while you dig, loot or build, for less than a base turret. *Cost:* the ammo inside is all it has; can be turned or stolen. *Ammo:* rounds. *Counter:* it's static: flank it or shoot it from outside its reach.

**MA3 Crawl Mine** (walking mine) | M-H | Placed | M | `placed seek-metal exp mines`
*Does:* a mine on six legs that wakes at noise, walks towards it and bursts. *Look and sound:* a metal scuttle in a duct. *Why:* a mine that doesn't wait. Set at the mouth of a vent, it goes in after the first noise inside. It takes no orders: it's a one-shot round on legs. *Cost:* slow, and it can be led astray. *Ammo:* mines. *Counter:* shoot it; a chatterbox.

**MA5 Picket Post** (fixed spotter) | M | Placed | M | `placed mark charge`
*Does:* a sensor on a spike you drive in; it marks what it sees on your map. *Look and sound:* a faint hum, a lens that turns to follow. *Why:* gives artillery and cruise missiles a spotter, and it watches a site before you commit. *Cost:* short battery life, and it can't move: you pick one view. *Ammo:* recharges. *Counter:* shoot it; jam it.

### 4.42 Relics

Found objects only: barely understood, never researched, never crafted. The interface gives them plain names and no faction. A few of them are the faint edge of the incomprehensible; nothing here is ever explained.

**RL1 Seam Cutter** (relic) | R | Hand | L | `semi bolt delay void capsule` | Needs PHASE
*Does:* leaves a faint crack in the air that pulls light slowly, and anything that crosses it is hurt. *Look and sound:* a thin dark line, no sound, light leaning into it. *Why:* a strange wall in the air. Nobody knows why. *Cost:* the crack is hard to see and doesn't take sides. *Ammo:* a few charges. *Counter:* see it first.

**RL2 Tallow Lantern** (relic) | R | Hand | M | `hold cone light fear calm none`
*Does:* a lamp in which shadows lean away from you; wildlife will not come near. *Look and sound:* a dirty yellow light, a slight lean of every shadow in the wrong direction. *Why:* a refuge on a bad world, not a gun. *Cost:* you can't fight with it in hand. *Ammo:* none. *Counter:* nothing that cares.

**RL3 Quiet Spear** (relic) | R | Hand | M | `melee kin void none`
*Does:* a dull spear; what it strikes makes no sound at all. *Look and sound:* the absence of sound. *Why:* no noise ever reaches the noise system. *Cost:* only melee range. *Ammo:* none. *Counter:* eyes.

**RL4 Glass Seed** (relic) | R | Placed | M | `placed field glass capsule` | Needs GROUND
*Does:* turns a slow growing circle of ground to glass. *Look and sound:* a faint chime. *Why:* the glass deserts have a reason. *Cost:* it doesn't stop until it's done. *Ammo:* one seed. *Counter:* leave.

**RL5 Dusk Round** (relic) | R | Hand | M | `semi bolt light blind rounds`
*Does:* a small dark shot; a hit target's lights go out for a time, and something nearby seems to lean closer. *Look and sound:* a dimming on impact, not a flash. *Why:* the world's oddness in your hand. *Cost:* the rounds run out. *Ammo:* a handful, found. *Counter:* unknown.

**RL6 Answering Pistol** (relic) | R | Hand | M | `semi ray en charge`
*Does:* fires back the last type of damage you took. *Look and sound:* a seamless grey stone that warms up. *Why:* a mirror that rewards being hit. *Cost:* nothing until you've been hurt. *Ammo:* recharges. *Counter:* don't hit its owner.

**RL7 Odd Stick** (random-effect relic) | R | Hand | M | `semi ray confuse charge`
*Does:* something different each time: a fall of sand, a sudden silence, a target gone. *Look and sound:* a dull rod, one small sound or none. *Why:* the pure-luck weapon players always love. *Cost:* some effects fall on you. *Ammo:* recharges. *Counter:* be elsewhere.

**RL8 Absence Round** (relic) | R | Small, Capital | L | `semi bolt void crater antimatter` | Needs BLAST
*Does:* takes a clean, perfect sphere out of whatever it hits. *Look and sound:* a pop, then the curved edge of a hole. *Why:* a thing so wrong that you'll want to know more. *Cost:* three shots in the world. *Ammo:* found. *Counter:* none known.

**RL9 Far Reach** (relic) | R | Hand | M | `charge ray en delay charge`
*Does:* a staff that hits a chosen far point on the map, seconds later. *Look and sound:* a hum, and far away, a flash. *Why:* distance doesn't matter to it, and no one will say why. *Cost:* a long, long recharge. *Ammo:* recharges. *Counter:* none known.

### 4.43 Personal shields

The rules are in section 5.11. Every suit has one shield slot, separate from its booster slots. A tier sets the bar's size and refill speed. A tuning (PS6 to PS9) is fitted to a tier at a weapon bench and changes how it plays, so these aren't plain upgrades. Numbers are references against the player's 500 health; they go in data.

**PS1 Crash Ward** (starter shield) | L | Worn | S | `worn field ward charge` | Needs SHIELD
*Does:* the small emitter in the crash suit: a bar of 100 with a five-second break and a refill of 25 a second. *Look and sound:* a faint sheen that ripples where you're hit, a soft rising tone as it fills. *Why:* every player has a shield from the first minute, so the bar is part of how the game teaches a fight: hit, take cover, wait, go again. *Cost:* it stops one good bite. Interior wear has no emitter, so the bar is gone when you change out of your suit. *Ammo:* none. *Counter:* almost anything.

**PS2 Mesh Ward** (tier 2) | M | Worn | S | `worn field ward charge` | Needs SHIELD
*Does:* a bar of 175, refilling at 40 a second. *Look and sound:* a fine grid in the sheen when it's hit. *Why:* the first shield you buy, in time for the first armed patrols. *Cost:* copper and silicon parts. *Ammo:* none. *Counter:* plasma, EM, fire.

**PS3 Field Ward** (tier 3) | M | Worn | S | `worn field ward charge` | Needs SHIELD
*Does:* a bar of 260, refilling at 55 a second. *Look and sound:* a deeper tone, a broader ripple. *Why:* holds against a squad long enough to reach cover. *Cost:* gold in the recipe. *Ammo:* none. *Counter:* plasma, EM, fire.

**PS4 Line Ward** (tier 4) | H | Worn | S | `worn field ward charge` | Needs SHIELD
*Does:* a bar of 360, refilling at 75 a second. *Look and sound:* a pale blue edge around you when it's hit. *Why:* the boarding shield: a corridor fight no longer ends in the first exchange. *Cost:* fold crystal in the recipe. *Ammo:* none. *Counter:* plasma, EM, fire.

**PS5 Lattice Ward** (tier 5) | H | Worn | S | `worn field ward charge` | Needs SHIELD
*Does:* a bar of 480, refilling at 100 a second. It's nearly as big as your health. *Look and sound:* a hard white ripple, a sound like a struck glass. *Why:* the top of the line, and a second life in a fight if you can get five seconds' peace. *Cost:* stormcore in the recipe. *Ammo:* none. *Counter:* plasma, EM, fire; capital guns still go straight through it.

**PS6 Quick Ward** (fast tuning) | M | Worn | S | `worn field ward charge` | Needs SHIELD
*Does:* a tuning for any tier: 70% of the bar, a three-second break. *Look and sound:* a short high tone as the refill starts early. *Why:* for skirmishers who dart between cover and never stay hit. *Cost:* a small bar breaks fast, and a swarm or a burn holds it down like any other. *Ammo:* none. *Counter:* steady fire.

**PS7 Bulk Ward** (deep tuning) | M | Worn | S | `worn field ward charge` | Needs SHIELD
*Does:* a tuning for any tier: 150% of the bar, a nine-second break, in medium and heavy suits only. *Look and sound:* a low hum, a slow heavy ripple. *Why:* for holding a door or leading a boarding party. *Cost:* once it's down you wait a long time, and the suit is slow. *Ammo:* none. *Counter:* get it down, then press.

**PS8 Snap Ward** (breaking tuning) | H | Worn | S | `worn pulse force stagger ward charge` | Needs SHIELD
*Does:* a tuning for any tier: 90% of the bar, and when it breaks it throws a ring that shoves and staggers everything within four metres. *Look and sound:* a sharp crack, dust jumping outward. *Why:* the answer to being rushed: the worst moment of a fight gives you a second back. *Cost:* the ring shoves your squad and your own footing, and a broken bar is loud. *Ammo:* none. *Counter:* break it from range.

**PS9 Sink Ward** (energy tuning) | H | Worn | S | `worn field ward charge` | Needs SHIELD
*Does:* a tuning for any tier: 80% of the bar. Energy hits it absorbs charge your weapon's cells, and kinetic and explosive hits cost it a quarter more. *Look and sound:* a thirsty crackle as a bolt soaks in. *Why:* turns a laser or plasma fight round, and lets a cell gun fight on far from a charger. *Cost:* bullets and rockets go through it fast. *Ammo:* none. *Counter:* bullets, rockets.

**PS10 Ward Cell** (instant refill) | M | Hand | S | `timer pulse ward capsule` | Needs SHIELD
*Does:* snap one on your chest and half the bar comes back at once, break or no break. *Look and sound:* a click, a bright pulse over the suit. *Why:* the only way to get shield back while you're still being shot at. *Cost:* a half-second with your hands full. *Ammo:* single use. *Counter:* a stall stops it working until it ends.

**PS11 Ward Spark** (squad refill dart) | M | Hand | S | `semi bolt ward capsule` | Needs SHIELD
*Does:* a dart that gives a squadmate a third of their bar. *Look and sound:* a pale spark that runs over them. *Why:* the co-op medic's second job. *Cost:* can't be used on yourself. *Ammo:* capsules. *Counter:* a stall.

**PS12 Ward Post** (placed recharger) | M | Placed | S | `placed field ward power` | Needs SHIELD
*Does:* a post on a base or vessel floor that cuts the break to two seconds and doubles the refill for anyone within eight metres. *Look and sound:* a slow pulsing ring on the deck. *Why:* a place to fall back to, so a defended room is worth holding. *Cost:* CPU and power, and it does the same for anyone standing there, including boarders. *Ammo:* power. *Counter:* shoot it; cut the power.

---

## 5. How weapons interact with each other and the world

### 5.1 Damage classes, shields and armour

The design doc gives three damage classes, resistances per material (hardened steel against kinetic, combat steel against explosive, charged armour against energy) and a shield multiplier per weapon (plasma strips shields, rockets punish hull and devices). Code already has this for blocks: `DamageType` is Kinetic, Explosive, Energy, and each material has a `resist` value per type. Shields are designed but not in the slice's code. Personal shields are a separate, smaller layer with its own column here and its own rules in section 5.11. These are starting multipliers, in words. Numbers go in data.

| Attack | Vessel shield | Personal shield | Plate | Devices | Flesh |
| --- | --- | --- | --- | --- | --- |
| Bullets, pellets, slugs | Weak | Fair; no headshot bonus while it's up | Weak to fair (hardened steel resists) | Fair | Strong |
| Rail and coil | Fair, with a little passing through | Fair, with a share passing through | Strong in a line | Strong | Strong |
| Rockets, shells, explosives | Weak | Fair; the knockback still lands | Strong (combat steel resists) | Very strong | Fair |
| Lasers, pulse lasers | Strong on pulse, fair on beam | Strong on pulse, fair on beam | Fair (charged armour resists) | Fair | Fair |
| Plasma | Very strong | Very strong, and it stalls the refill | Fair | Fair | Fair |
| Ion and EM | Very strong | Emptied, and the refill stalls | None | Stuns them | None, except implants |
| Particle and fold rounds | A share passes through | A share passes through | Fair | Strong | Fair |
| Lightning | Fair | Fair; the stun waits until it's empty | Weak | Strong, jumps along hull | Fair, stuns |
| Fire | None | Goes straight past; every tick holds the break | None on stone, steel, concrete | None | Strong; strong on plants, wood, plastic |
| Star Gel | Weak | Weak, and its burn goes past | Strong | Fair | Strong |
| Cold | None | Goes past as a slow; freeze waits until it's empty | None; slows devices | Slows | Holds |
| Acid, corrosion | Weak | Weak; corrode waits until it's empty | Lowers armour over time | Fair | Fair |
| Sonic | None | Goes past; stagger lands | Only brittle blocks | None | Stagger |
| Marrow and neutron | Passes | Passes | None | None | Crew only |
| Antimatter, `void` | Very strong | Gone in one hit; the rest lands | Ignores resistance in the core | Removes | Removes |

Empyrion's own flamethrower is the model for the fire line: its data sets flame to zero against rock, concrete, hull, metal and shields, double against wood, plastic and people, at only 6 m range.

**Shield penetration** is a plain percentage stat rather than a rule about speed, and it means the same on a personal shield as on a vessel's. Reforged Eden 2 uses the same idea (its railguns and positron guns keep a share through the shield), and it keeps the table above simple.

**Set up, then pay off.** The strongest fights in the reference games pair a setter with a finisher: strip the shield then land the headshot, ion down the shield then beam the hull. GalHack's pairs:

| Setter | Payoff |
| --- | --- |
| Plasma or ion strips the shield | Rail, rocket, bullet land on what's behind |
| Ember Rifle, Pulse Ball or Pulse Choke takes a personal shield down | Headshot, stun, sleep dart, stuck grenade: anything that waits for an empty bar |
| Hornets, moths or a burn keep the break from ending | Whoever is waiting for that bar to stay down |
| Etch Sprayer lowers armour | Any rifle |
| Water Cannon, rain or swamp makes the target wet | Lightning jumps further and hits harder |
| Tag, Spotter's mark, Paint Lamp, Tick | Seekers, curved rounds, artillery |
| Conductor Spike | Next lightning goes there, doubled |
| Cold Snap or Still Beam freezes | Any hard hit shatters: Maul, shotgun, Thump Ball |
| Fuel mist, gas, grease, oil pool | Any flame, spark or lightning |
| Well Launcher gathers a pack | Rocket, grenade, mine |
| Drift Charge or Lift Beam lifts | Fall damage, or a shot while they tumble |
| Net, Pin Driver, Tangle Gun holds | Knife, Maul, big slow shots |
| Sleep Dart | Harvest alive, or knife |
| Rust Haze softens a hull | Mortars, then a door charge |

### 5.2 Fire

**What the game can do today.** There is no fire in the code: no burning state, no flammable material and no spread. Shots leave a fading scorch decal, and the weather data has a FireStorm kind with burning debris. What exists to build on: wind (`Wind2D` and `WindSpeed`), rain amount, ground wetness (`GroundCover.Wetness`), snow and dust cover, a water query (`IWaterField.SurfaceAt`), and wildlife with a Flee state and a noise system. So the rules below are what the FIRE upgrade would add, with these existing values feeding it. Each hand thrower works before that as a cone of short bolts plus a burn timer.

Rules, simple on purpose (no fluid or gas simulation, as the design requires):

- **Fire patches.** Burning fuel leaves short-lived patches from a pool with a hard cap (start at about 64 in a zone). Each has fuel and heat. When the cap's full, the oldest go first. Spread must stop: Far Cry 2's programmers found unlimited spread wasn't fun and hurt the frame rate.
- **Fuel on the ground.** Thrown fuel makes a pool; a pool burns as long as its fuel lasts. Sprayed fat burns for seconds, jelly much longer. Fuel pools can sit unlit until a spark, shot, lightning or flare finds them. Fuel can be painted in a line as a fuse.
- **Plants.** Plants and wood are fuel. Dry grass goes fast, jungle is hard to light, and dead forest goes up at once. Each biome has one flammability number. A burnt plant swaps to a charred stump and regrows in time. A patch lights its neighbours every half second by chance.
- **Wind.** The chance to spread is weighted downwind, and strong wind throws embers a few metres ahead. Light wind carries smoke and flame clouds; a hurricane puts out small fires and spreads large ones. Fire climbs a slope faster than it descends.
- **Rain and wet.** Rain and wetness cut the chance to light, and a monsoon puts open fire out. Recent rain leaves ground hard to light until `Wetness` falls. Snow cover stops ground fire. Dust storms smother it.
- **Water.** Standing in water puts a burning creature or player out. Fire can't sit in water, except oil: oil patches burn on the surface. Flame jets die at the water line, and the Void Burner makes steam.
- **Vacuum and thin air.** No air, no fire. Pressure Throwers fail outright in vacuum, only a Void Burner or plasma gel works. Thin air burns weakly. With the ROOM upgrade, a sealed room's fire uses up its air and goes out, and opening a door feeds it.
- **Materials.** Wood, plastic and furniture burn and take fire damage. Stone, concrete and steel don't. A burning block chars through the existing damage look. Fuel tanks and ammo stores in a fire cook off with warning.
- **Living things.** Fire is a scare: creatures flee from it, and a burning creature keeps running, spreading fire through dry grass. Soldiers avoid patches. Fire is the strong counter to all living weapons, hornets, swarms, webs and nests.
- **Players.** A player on fire takes health loss for a few seconds and it stops, keeping "no ailments". Water, foam, coolant and airblast put it out at once. It's never a lasting burn.
- **Smoke.** Big fires make a smoke patch that drifts with the wind and blocks sight, lasers and lock-ons.
- **Fire storm worlds and lava.** On a fire storm, debris lights things as a normal fire source. Heat-adapted creatures ignore fire.
- **Aftermath.** Burnt ground stays dark as a decal, so a fire reads as a story when you come back.

### 5.3 Water, wet and lightning

Rain, swamp, snow melt, Water Cannon, Flood Charge and being in water make a target wet. Wet targets take stronger lightning, and arcs jump further between them. Firing a lightning gun while standing in water hurts you, and the arc reaches anyone else in that water. The weather sim's `Wetness` and `SurfaceAt` give this with no new system. Under water: pistols and needle guns work at short range, bullets slow down fast, lasers and flame fail, sound carries further, and explosions hit harder.

### 5.4 Freeze and shatter

A frozen creature uses the existing Freeze state. A hard hit on a frozen target does bonus damage and breaks it. Fire thaws it. Brittle blocks (glass, ice, frozen hull parts, voidglass) break to sonic and hard hits. Cold beams on water leave walkable ice only with the FLUID upgrade, since water can't change today: `IWaterField` is static.

### 5.5 Gravity plus everything

Gravity weapons work with what's already placed: a Well Launcher drags enemies over a mine field, a Flip Mine throws boots at a ceiling full of Gum pods, a Weight Projector brings hover craft down onto caltrops, and a Shunt Gun moves a guard onto a pressure mine. A planet's gravity changes every lobbed arc, so grenades go further on a moon and fall short on a heavy world. Lifted things must always read as an anomaly (section 4.25).

### 5.6 Explosives and block damage

Blasts already fall off with distance through `Damage.BlastInto`, and blocks that lose their support split off through `Connectivity.SplitOff`, so a cut strut drops a wing. Good placement matters: a Cutting Charge on the right spot beats ten rockets on plate. Terrain is remove-only and capped at 6 m radius and 900 cells a crater, and there are no cave-ins. Reforged Eden's rule that explosions only hit the outer armour unless they get through to a device is a good one to try. A fuel tank, ammo store or Bottle Bay on fire or hit bursts with a warning first.

Antimatter, nuclear and orbital weapons leave permanent craters by default, stored as big-crater records (section 13, BLAST). A server setting turns those craters off; the blast still does its damage and the ground is left as it was.

### 5.7 Weather

| Weather | Effect on weapons |
| --- | --- |
| Clear | Baseline. Sun flare spoils scopes facing it, and scopes glint |
| Rain, monsoon | Lasers lose range, fire struggles, wet bonus for lightning, wet powder loses punch, sound carries less |
| Fog | Lasers and sight fall, heat seekers fine |
| Snow, blizzard | Soft ground muffles grenades; cold weapons stronger, water jets freeze |
| Dust storm | Sight and lasers near zero, guide-lines scatter |
| Thunderstorm | Skycall is free and strong; tall aerials and rail lances draw real strikes, as tall things already do |
| Fire storm | Burning debris lights fuel; cold weapons weaker |
| Hurricane, tornado | Gas, smoke, swarms and lobbed rounds blow off course; wind already pushes light craft |
| Ion storm, magnetar flare (space) | An ion storm drains personal shields and holds their refill; a magnetar flare knocks them out for a few seconds (section 5.11) |

### 5.8 Vacuum

No sound in vacuum except through the suit: you feel your guns and hear only your own breath and impacts through contact. Sonic, gas, smoke and ordinary flame don't work. Guide-Line doesn't work, as there's nothing to ionise. Thump Ball is nearly useless. Lasers go further and don't scatter, and Anti-Stream reaches kilometres. In zero gravity guns push you backwards, and a Kick Pocket or Spin Gun is a thruster. A bolt never falls and keeps going until its life runs out.

### 5.9 Line of sight, noise and light

The wildlife sim has a noise system (`Noise(at, radius)`), and the boarding prototype gives each weapon a noise stat. So every weapon has a noise radius. Silent: knife, bow, Blow Tube, Pump Gun, Hush Pistol. Normal: rifles. Whole-site: Bench Gun, Saw Bar, Spin Gun, Basslance. Loud fights draw creatures and alarms, and the Whisper Round uses the same number. Muzzle flash lights a dark room, a pilot light gives you away, a Light Blade is a beacon, and a laser dot shows on walls. Chatterbox, Reek Pot and Lure Beacon feed the same system on purpose.

### 5.10 Living things, machines and Husks

Living weapons die to fire, smoke and cold, and don't bother machines. EM and lightning stop machines, cyborgs and Husks, while creatures don't care about EM. Husks run on leftover nanites: Quiet Code shuts one down and EM drops it for a few seconds. Low-tech enemies and all clockwork, air and spring guns work through any pulse. Machine swarms eat metal and ignore stone and wood.

### 5.11 Personal shields

The player has a personal shield alongside health: a second bar that takes damage first and refills after a short break from taking hits. The idea is the recharging shield of Halo and Borderlands. Only the concept is used.

**The rules.**

- **Order.** Personal shield, then suit armour, then health. Armour only matters once the bar is down, so a shield also saves armour durability.
- **Size.** 100 at the start and 480 at the top tier, against 500 health (section 4.43).
- **The break.** Five seconds after the last hit, the bar starts to refill. Firing your own weapons doesn't restart the break: only being hit does.
- **What counts as a hit.** Anything from a weapon, a creature, a vehicle or yourself. Every tick of a burn and every sting of a swarm counts, even when it does almost nothing. Hazards don't count.
- **Health doesn't refill.** Only medkits, a healing booth and `mend` restore it, as they do today. They don't touch the bar.
- **Spill-over.** Damage beyond the bar carries on to armour and health. The design's rule stands: a capital turret still kills a person on foot, and damage is never scaled down.
- **Free to run.** No fuel and no fold crystal. A personal shield is too small to draw on a vessel's tank.
- **Statuses wait.** While the bar is above zero, hit-delivered statuses don't land: `stun`, `shock`, `freeze`, `pin`, `glue`, `sleep`, `corrode`, `mark` from a dart and damage over time from a stuck thing. Sense and field effects land anyway, see below.
- **Headshots.** No headshot bonus while the bar is up.
- **Friend and foe.** The same rules apply to the player, to shielded soldiers and to machines that wear one. Vessel seats are covered by the vessel: a seated player's bar only takes what reaches the seat.

**How each family meets it.**

| Family | What it does to the bar | What gets past |
| --- | --- | --- |
| Bullets, pellets, bolts, thrown blades | Plain damage, a little less per pellet | Nothing until it's empty |
| Rail and coil | Plain damage | A share goes through |
| Rockets, shells, grenades, mines | Plain damage | Knockback and stagger land. A stuck charge goes off against the bar |
| Lasers | Strong on pulse, plain on beam | Nothing. Blinding beams land |
| Plasma | Very strong, with `strip` and `stall` | The heat of a stuck gob, once the bar is gone |
| Ion, EM, Pulse Ball, Pulse Choke | Empties it, then `stall` for several seconds | Nothing, but your shield is off |
| Particle, fold | Reduced | A share goes through |
| Lightning | Plain damage | `shock` and stun wait; arcs still jump to and from you |
| Fire, Star Gel, steam | Nothing from flame; Star Gel drains it a little | Burn goes straight to health, and every tick holds the break. This is why flame stays useful late, and why low-tech fighters aren't helpless against shields |
| Cold | Nothing | `slow` lands; `freeze` waits |
| Acid, goo, nets, glue | Weak; a net or glue blob burns off and costs a chunk of the bar | `corrode`, `pin` and `glue` wait |
| Sonic | Nothing | Stagger, deafness and its small damage go to health |
| Gas, smoke, spores, radiation, Marrow Beam, Ghost Warhead | Nothing | All of it. A shield doesn't filter air, so helmets and sealed suits are the answer |
| Swarms: hornets, moths, needles, machine swarms | Tiny, but each hit restarts the break | Nothing until it's empty. Swarms don't break a shield; they stop it coming back |
| Worms, Clinger, Tick Dart, Tag Rifle, Conductor Spike | A worm burns off against the bar and costs a chunk of it. A dart fails. A Clinger sticks to the ripple and goes off there | Nothing until it's empty, which is why a worm should follow a stripper |
| Melee | Plain damage; energy blades strong | Stagger. A takedown from behind goes past altogether |
| Gravity, force, shunt, phase moves | Nothing | All of it. A shield doesn't stop you being moved, and the fall goes to health |
| Phase rounds, `shieldpass` | Nothing | All of it. Cook Box, Ghost Round and Phase Lance ignore the bar |
| Mind: Horn, Lens, Forget Lamp, Stare Sign | Nothing | All of it: they work through the eyes and ears. Sleep, Calming and Tick darts are stopped |
| Time | Darts stop at the bar | Slow Bubble and other fields land. A Delay Pistol's burst lands on the bar if it has refilled by then |
| Antimatter, `void` | Gone in one hit | The rest lands in full |

**What strips it.** Plasma (`strip`) and ion or EM (`overload`), with Twin Charge rockets a distant third. Stripping is the setter half of a fight against a shielded enemy, in the same way plasma strips a vessel's shield.

**What goes round it.** Fire, gas, radiation, `shieldpass`, phase rounds, falls, force and takedowns. So shields never shut a band out: low and mid-band weapons get through with fire, melee, swarms and falls, and plasma and EM arrive in the high band.

**Hazards.** Hazards ignore the bar and don't restart the break, so it refills while they hurt you.

| Hazard | Effect on a personal shield |
| --- | --- |
| Falls, crushing, drowning, hunger | None. Straight to health |
| Heat, cold, radiation zones, toxic air, gas clouds, lava, acid pools | None. Straight to health, and suit boosters are the answer |
| Vacuum | None. A shield doesn't hold air |
| Natural lightning, fire storm debris | A hit: plain damage to the bar, and any burn goes past |
| Rammed by a vehicle, a bursting fuel tank | A hit: the blast takes the bar, the burn goes past |
| Ion storm | Drains the bar slowly and holds the refill while you're in it |
| Magnetar flare | Knocks the shield out for a few seconds, with a visor warning first |

**Other defences.** A Pop Dome buys the break for everyone inside (DF3). A Bull Shield, a cover wall or an Orbit Shard stops a shot before it counts as a hit, so physical cover keeps the refill going. Ward Cells and Ward Posts speed it up, and a `stall` blocks both.

**Stealth.** A silent gun doesn't beat a shield: a Hush Pistol or a bow leaves a bright ripple and an alerted guard. Takedowns from behind and the Cook Box are the quiet ways through. A refilling bar makes a low hum that carries five metres.

**HUD.** The shield bar sits directly above the health bar, the same width, in a pale colour of its own, so the two read as one stack. A hit flashes it and shows the direction of the hit. During the break, a thin line runs along the bar and shows the time left; when the refill starts, the bar pulses with a rising tone. At zero the outline cracks, with a sharp glassy sound. A stall hatches the bar until it ends. No text is needed. Every warning is a sound, a shape or a colour, and it follows the design's rule of warned, never ambushed. Squadmates' bars show in the same pair. On other people, the ripple and a fading sheen tell you how a shield is doing, and a break throws a ring you can see across a room.

**Enemy use.** See section 7.

### 5.12 Self-harm and friendly fire

Self-harm is on by default. Backblast, arcs in water, a burst fuel tank, a bounced rocket, a Kick Pocket's recoil and every other "it can hurt you" in this list are real. Self-inflicted hits take the bar first, like any other hit, and they restart the break. Heat burns from your own gun go past it, as fire does. Friendly fire is a server setting. With it off, your shots and blasts don't hurt squadmates, but your own self-harm stays on.

---

## 6. Ammo, economy and modification

### 6.1 Where ammo comes from

Ammo on a vessel comes from the structure's shared storage by calibre, as designed, so running dry on a long trip is a real risk. Existing items keep their ids: `pistol-rounds`, `belt-12mm`, `shells-40mm`, `rocket`, `artillery-shell`, `plasma-charge`.

| Class | Made from | Where | Band |
| --- | --- | --- | --- |
| Powder and ball, fused bombs | Magnesium, iron, fibre | Survival constructor | L |
| Arrows, bolts, stakes, darts, nets | Wood, fibre, iron; often recoverable | Survival constructor | L |
| Rough rounds, scrap needles, junk | Scrap from wrecks and ruins | Survival or portable constructor | L |
| Biofuel, fat, fire bottles | Plant matter and meat | Any constructor | L |
| Rounds, shells, belts | Iron, copper, magnesium | Small fabricator | M |
| Rockets, grenades, mines | Magnesium, iron, circuit parts | Large fabricator | M |
| Slugs | Iron or scrap; heavy slugs need cobalt | Large fabricator | M |
| Energy cells | Copper, silicon | Early recipe, shared with fuel | M |
| Coolant, acid, chem, resin, salt | Ice, gathered pools, plants, salt flats | Portable or small fabricator | L-M |
| Biomass | `meat`, `hide`, plant matter, `rare-tissue` for the good stuff | No crafting: it's raw organics, loaded directly | L-H |
| Plasma charges, gel | Emberite | Advanced fabricator | H |
| Capacitor weapons, Coil Tower | Stormcore | Advanced fabricator | H |
| Fold rounds, Snare Well | Fold crystal, shared with the jump tank | Advanced fabricator | H |
| Nanite packs | Gold, lattice parts, Precursor salvage | Advanced fabricator | H |
| Warheads | Rare fuel plus a large fabricator | Faction reward, salvage or capture first | M-H |
| Antimatter | Fusion cells, stormcore, a containment plant, long time | Bottle Bay only | H |
| Relic ammo | Nothing | Found only | R |
| Personal shield modules, Ward Cells | Copper and silicon, then gold, fold crystal and stormcore by tier | Small to advanced fabricator; tunings at a weapon bench | L-H |

Power weapons use the design's rule: heavy guns charge from the generator's surplus, thrusters use it too, so choosing to shoot means choosing to fly less. Fuel weapons draw on the same fuel system, so a burn on a long trip is a real choice. Water weapons fill from a tank or at a lake.

What's craftable early: all low-band rows. What's salvage or reward only: relic ammo, the Kin Bow's bolts, Quiet Code, Precursor parts, and most warheads at first. On low-tech worlds, traders sell only low and mid-band ammo, and scrap is what makes an arms economy on a dead world.

### 6.2 Shell and round types

Ammo types multiply the staples at low cost. Each is one data row with a stated trade-off, and they're unlocked in groups.

- **Shotgun shells:** buckshot, slug, flechette (fast, no armour), salt, fire, breach, stun bag, and flak (bursts in a fan).
- **Rifle and pistol rounds:** full metal, soft point (flesh), armour-piercing (less on flesh), tracer (shows the path both ways), incendiary (lights fuel and grass), subsonic, frangible (no hull damage).
- **Grenade shells:** impact, bounce, sticky, airburst, cluster, smoke, fire, gas, flash, EM.
- **Rockets:** blast, armour-piercing, fragmentation, smoke, fire, tandem.
- **Artillery and mortar:** blast, smoke, fire, light (a slow hanging flare under a visible parachute), EM, cold.
- **Cannon and belt:** mixed belts, one tracer in five, so the player reads their fire.

### 6.3 Modifying weapons

The design says weapons have jobs, not tier numbers, and this keeps to that: a mod changes how a gun plays, never a plain +10%. Reforged Eden 2 added weapon mods through a weapon locker, and Deep Rock Galactic's overclocks are the best model: every strong mod has a stated cost.

- **Fittings** at a weapon bench: sights, heat scope, suppressor, drum, bipod, stock, bayonet, underbarrel launcher, shoulder sling. A fitting takes a slot and adds weight.
- **Overhauls** swap the gun's behaviour: Spin Gun with a limiter removed, Pump Gun with an auto pump, Coil with overcharge, Fork Gun with longer jumps and weaker first hit, Flamethrower with sticky fuel and a smaller tank.
- **Maker traits.** The same type built by a faction keeps a trait on all its guns, the way Borderlands gives each maker an identity. Reaver: loud, hits hard, kicks wildly. Dominion: heavy, stable, big magazines. Meridian: clean, cheap to run, expensive to build. Kessari: charge-based, precise, no brass. Freeholder: simple, cheap to build from scrap. Faction reward weapons sit on top with unique effects, as the design says.
- **No wear.** Weapons never wear out or jam, so a gun's real costs are ammo, heat, weight, noise and handling. Nothing in this list needs a repair bench.

### 6.4 Slots and limits

Vessels need a cap on guns or everyone stacks one best weapon. Reforged Eden 2 uses three weapon sizes with a shared limit per class, and equal damage per minute within a size, with the difference being how the damage arrives. That's a useful target: small, medium and heavy weapon classes, with damage normalised inside a class, so a Fighter Rail and a Chatter Gun differ by what they do, not a bigger number. CPU already costs devices.

---

## 7. Enemy use by faction, and what the player sees coming

Every enemy weapon is the same data row the player can use, so it can be learned and captured. All of them follow the design's rule: warned, never ambushed. First shots are wild, accuracy ramps over about two seconds, and scale sets how well they hit. Nobody speaks: warnings are sounds, lights and shapes. No enemy weapon relies on dialogue or text.

| Who | Band | What they carry | What you see and hear first |
| --- | --- | --- | --- |
| Scavengers (SCV) | L | Stamp Gun, Long Arm, Bow, Scatterbuss, Sling, Net Gun, Fire Bottle, Caltrops | Cook fires, tracks, boot prints; a trip wire's glint; often leave you alone |
| Raiders and gangs (GNG), Carrion Reavers | L-M | Saw Thrower, Rigged Bike, Harpoon Rig, Fire Rack, Tallow Sprayer, Pressure Thrower, Keg, Trip Line, Ram Spike, Boarding Claw, Hand Organ | Engines and a dust plume, lit fuses, a burning barrel rolling, a taut line, a roaring pilot flame, ram spikes in a hull's silhouette |
| Vharn Dominion (FAC) | M-H | Line Rifle, Ember Rifle, Squad Belt, Tripod Heavy, Spin Gun, Lock Tube, Field Mortar, Fortress Howitzer, Flak, Strip Battery, Lance Array, Sky Battery, Line Battery, Torpedo | Sky patrols, searchlights, a mortar's thunk and whistle, a lock tone on your panel, a lance charging for two seconds |
| Meridian Combine | M-H | Corridor Gun, Long Glint, Shock Baton, Scald Horn, Stutter Lamp, Needle Curtain, Pop Dome | Warning lights, a bright marker beam on you first |
| Ledger Exchange | M-H | Guard ships with Deck Guns and Static Cannons | Only if you attack |
| Freeholders, squatters (SQT) | L-M | Long Arm, Crossbow, Shock Fence, Boar Spear, Ballista | Fences, watch fires, a hound's bark |
| Kessari Clans, tidy machines (MCH) | H | Coil Carbine, Stage Rifle, Magnet Glove, Fork Gun, Pulse Choke, Jam Spike, Sentry Stalk | Coil whine, a laser sight, a row of status lights changing colour |
| Feral robots (BOT) | M | Junk Cannon, Saw Thrower, Ripped Gun, Pin Driver, Cutter Beam, Crawl Mine, Scrap Locust | Scrap piles, grinding, a limping shape dragging a gun barrel |
| Cyborgs (CYB) | M-H | Line Rifle, Wire Biter, Pulse Choke, Needle Swarm, Palm Emitter, Shoulder Rack, Marrow Beam on ships | A cold steady walk, a shoulder hatch opening, a click before the arc |
| Husks (HSK) | None | Bare hands and worm-like growths; a few still carry a Stamp Gun or a Shock Baton from their old job, barely used | Slumped figures rising, a dry rattle |
| Bugs and nesters (NST) | Anatomy | Bile Pod spit, Web as Net Gun, Wasp swarms, Egg Grenade bursts, Burrowers as Sand Swimmer, Thorn | A swelling throat, a rising buzz, ground ripples, a pulsing sac |
| Mutated predators (MUT) | Anatomy | Tooth spit, Leech Vine tongue, Mantis Claw | A telegraphed lunge, as all creatures have |
| Wild creatures | Anatomy | Hornet nests, Moths, Maw Stalks, Spore clouds in jungle and swamp | Leaves tilt, a yellow-green cloud visible a second before it matters |
| Cultists (CLT) | L | Reek Pot, Bone horn (Dread Horn), Fire Lance | Drums, smoke, painted marks. Rarely hostile |
| Hollow Bloom | R, unnamed | Never a named weapon. Dimming as a shot passes, a lean of shadows, a faint seam in the air, a hit from no one. Corrupted crews use whatever they carried | Lights dim first. The interface says nothing |

Machine-held places and worlds where robots hold ground can field placed machines (4.41), plus the gun drones, self-flying blades and carrier bays the player can't own, by the world's rules. Enemy drones are ordinary local machines, never Empyrion-style invasion waves.

**Who wears a personal shield.** Enemy shields use the same rules and the same tiers as the player's (section 5.11). They're a mid and high band item, so low-tech enemies and wildlife never have one.

| Who | Shield | How they use it |
| --- | --- | --- |
| Scavengers, raiders, Reavers, Freeholders, cultists | None. A gang boss may wear one looted Crash Ward | Fire and numbers. A Fire Bottle is their answer to your bar, so low-band fights still bite |
| Vharn Dominion | Mesh for soldiers, Field for officers | When a soldier's bar breaks, they fall back for the break, and a squadmate covers them. Officers throw Pop Domes. Ember Rifles appear on higher-danger worlds, with a green glow first |
| Meridian Combine | Mesh with Quick tuning | Short bursts from cover, stepping out as soon as the bar is back |
| Ledger Exchange guards | Field | Only if you attack |
| Kessari Clans, tidy machines | Line with Sink tuning | Lasers do little to them, so bring bullets. Pulse Choke takes yours down, with a blue crackle at their feet first |
| Cyborgs | Mesh | They don't fall back: they keep walking while it breaks |
| Feral robots, Husks, bugs, creatures | None | They wear yours down instead: swarms hold your break, and burrowers and bile wait for it to go |
| Hollow Bloom | None | Its hits never move the bar. The interface says nothing |

An enemy's shield shows as a faint sheen, and its break is a bright ring and a glassy crack, so you always know when to press. A dead soldier's module can be looted, as any kit can.

**What the player sees coming, in order.** Sound first: each weapon has its own charge or idle noise. Then silhouette: a lit pouch, a coil, a wound line, a fat tube. Then a short tell light of 0.3 to 2 seconds. Seekers show a lock tone, area weapons show a ground ring, and big guns take the wild-shot rule. Antimatter and warheads show a hard-to-miss cue and a countdown tone on the visor.

---

## 8. The first 12 to build

Chosen for the widest range from the least engine work. All twelve avoid every **Needs** upgrade. They rest on one piece of integration: moving the boarding prototype's data-driven `WeaponDef` and swept `Projectile` into the slice, which already has `GunFeel`, `ShotFx`, the wildlife noise and damage calls, and `VesselDamage.Shot` and `Blast`.

| # | Weapon | Why it's here | New shared part it adds |
| --- | --- | --- | --- |
| 1 | SA2 Tack Pistol | The first crafted gun; proves hand weapons, magazine and reload in the slice | The weapon layer itself, as a port |
| 2 | RF2 Line Rifle | The yardstick every other gun is tuned against | Fire modes |
| 3 | SG2 Pump Scattergun | Owner's staple; shell types become data | Pellets, ammo variants |
| 4 | GL2 Break Launcher | The explosive launcher: indirect fire, door breaching | Gravity and bounce on `Projectile`, `Blast` on hit |
| 5 | RK3 Shoulder Tube | The anti-armour staple; the same ammo feeds the rocket pod next | Backblast, slow bolt with smoke |
| 6 | MN13 Prox Mine | The mine: reuses blast with a small placed object | Placed thing with a trigger radius |
| 7 | AC3 Stage Rifle | The accelerator: charge stages and piercing, which every rail needs | Charge, repeat raycast for pierce |
| 8 | LT3 Fork Gun | The owner's lightning | Arc: a target search plus existing lightning look |
| 9 | LV2 Hornet Hive | The owner's hornets | Swarm: a few homing bolts with wander. `Tracking` is already on `Projectile`. Biomass is existing `meat` |
| 10 | LV6 Wormspitter | The owner's worms | Stick, then damage over time on a creature or block (`Damage` per second) |
| 11 | SK1 Bend Rounds | The owner's homing bullets | Reuses `Tracking`; shows seeking on a plain gun |
| 12 | FL5 Pressure Thrower | Owner's named family. First version is a short cone with a burn timer and creature flee, and it already gives the feel | Cone, damage over time, Flee on burn |

Together they add twelve shared behaviours that most of the list reuses: charge, pierce, bounce, gravity, pellets, arc, stick, damage over time, homing, swarm, cone and placed. Vessel mounts come next, with the vessel gun layer (section 13), starting from the existing gatling, turret and rocket pod blocks.

The Pressure Thrower's ground fire is the first upgrade to add after these, because it adds the most to the world for the least. Personal shields aren't in the twelve: they arrive with SHIELD, third in the upgrade order, and the twelve work unchanged when they do.

---

## 9. Tree fit

Level cap 25. This list and the level map in `docs/tech-tree-pacing.md` section 3b are the same data, and a script checked that they agree node for node. Existing unlocks hold: pistol rounds at 2, 12 mm belt and small gatling at 6, shells and large turret at 8, large gatling, rockets and small pod at 9, small turret at 10, large pod at 11, artillery shells at 15, plasma charge at 20. Design gates hold too: hover 1, portable constructor 3, small vessel 5, capital 7.

**Principle.** A 25-level tree can't hold 357 separate nodes, and it shouldn't. Each family gets a few nodes by importance, and each node opens a group: its recipes come with it. The rest are ammo types, fittings or finds. That gives 145 weapon nodes plus 11 personal shield nodes, 156 in all, and the tab also keeps its 6 existing ammo nodes, so it holds 162 nodes, about six and a half a level. Bands sit as: low 1 to 9, mid 6 to 17, high 14 to 25, relic never. A new node's parent is the node before it in its family, so a family reads as a ladder; existing nodes keep their parents. A node's cost follows its level, as in section 3d of the pacing note.

Weapons have real slots, so the tab has something on every level for hand guns and something regular for melee, placed, living, support and shield lines (pacing note, section 4).

| Family | Importance | Low levels | Mid levels | High levels | Nodes | Mostly from outside the tree |
| --- | --- | --- | --- | --- | --- | --- |
| Sidearms | Core | 2 | 7, 9 | 19 | 4 | Kick Pocket is a rare find |
| SMGs, carbines | Core | 4 | 8, 13 |  | 3 | Strobe is a recipe |
| Rifles | Core | 5 | 7, 12 | 22 | 4 | Breaker is a recipe |
| Shotguns | Core | 4 | 6, 11 | 17 | 4 | Shells come in groups |
| Long guns | Core |  | 10, 14 |  | 2 | Scopes are fittings |
| Machine guns | Core | 3 | 6, 9, 11, 15 | 21 | 6 | Chatter Gun keeps the two existing gatling nodes |
| Bows, air, spring | Early | 1, 3, 5, 8 |  |  | 4 | Kin Bow is a reward |
| Hand grenades | Core | 2 | 7, 10, 13 | 18 | 5 | Single-use items are recipes |
| Grenade launchers | Core | 5 | 8, 12 | 17 | 4 | Shells come in groups |
| Rockets | Core | 3 | 9, 9, 11, 13, 17 | 20, 24 | 8 | Twin Charge and Hot Breath are recipes under the Shoulder Tube |
| Artillery | Core | 5 | 10, 15 |  | 3 | Bomb Bay comes with the Drum Cannon |
| Mines | Core | 3, 7 | 10, 14 | 20 | 5 | Most mines are recipes |
| Accelerators | Core | 7 | 14, 17 | 19, 23, 25 | 6 | Owner's railguns start by hand at 7 |
| Lasers | Core |  | 9, 13 | 18, 21, 22 | 5 |  |
| Plasma | Core |  |  | 20, 20, 23 | 3 | Design sets plasma at 20 |
| Particle, ion, EM | Second |  |  | 16, 22 | 2 | Marrow is capture tech |
| Lightning | Core | 3 | 10, 13 | 16, 18, 21 | 6 | Owner's ask; Fork Gun at 16 |
| Sonic | Second |  | 12 | 19 | 2 |  |
| Flame | Core | 2, 4 | 7, 11, 14, 17 | 22 | 7 | Owner's ask. Bottle 2, Sprayer 4, Pressure 7 |
| Cold | Second |  | 12 | 21 | 2 |  |
| Acid, goo, water | Second | 3 | 9, 13 |  | 3 |  |
| Melee | Core | 1, 4 | 6, 10, 16 | 20 | 6 | Knife free; blades later |
| Seeking | Second |  | 12 | 16, 20 | 3 | Owner's homing bullets at 16 |
| Living | Core | 3 | 9, 11, 14 | 19, 23 | 6 | Owner's hornets 11, worms 14 |
| Gravity | Second |  | 15 | 19, 23 | 3 | Deep Well is a find |
| Tethers | Second | 2, 8 |  |  | 2 |  |
| Nanite | Second |  |  | 24 | 1 | Quiet Code is a find |
| Phase, fold | Late |  |  | 22 | 1 | Ghost Round, Phase Lance and Gate Pair are finds |
| Time | Late |  |  | 21, 24 | 2 | Stutter Dart and Hold Capsule are finds |
| Mind | Second |  | 8 | 22 | 2 | Stare Sign is a find |
| Rays | Late |  |  | 22 | 1 | Unbinding Lance is a relic; the comic rays are rare finds |
| Defence | Core | 5 | 9, 17 | 15, 21 | 5 | Anti-air and point defence sit here |
| Ship guns | Core | 5 | 8, 10, 11, 13, 13, 15 | 20 | 8 | Deck Gun keeps the two existing turret nodes |
| Nuclear | Late |  | 17 | 23 | 2 | Warheads come from capture and reward first |
| Antimatter | Capstone |  |  | 24, 25 | 2 | Rest are recipes under the Bottle Bay |
| Electronic, cyber | Second |  | 11, 16 |  | 2 | Palm Emitter, Wrist Blade and Shoulder Rack are cyber modules, outside the tree |
| Block, terrain | Second |  | 8, 15 | 19 | 3 |  |
| Improvised | Early | 2 | 11 |  | 2 | Ripped Gun is salvage and needs no node |
| Support | Second |  | 7, 12 | 21 | 3 |  |
| Superweapons | Capstone |  |  | 25 | 1 | Other five are station and base projects under this node |
| Placed machines | Second |  | 10, 14 |  | 2 | Static only; nothing follows you |
| Relics | Found |  |  |  | 0 | Never in the tree |
| Personal shields | Core | 1 | 6, 8, 9, 11, 13, 14 | 16, 18, 22, 24 | 11 | Tiers at 1, 6, 11, 16, 22; Ward Spark is a recipe |
| **All families** | | | | | **156** | |

**A weapon every level.** Every node, by level. (E) marks a node that exists today; it keeps its level and cost.

| Lv | Node | Cost | Also opens, as recipes |
| --- | --- | --- | --- |
| 1 | BW1 Cord Sling | 1 |  |
| 1 | ME1 Work Knife | 0 | ME2 Throwing Hatchet, ME5 Cutting Wire |
| 1 | PS1 Crash Ward | 0 |  |
| 2 | FL1 Fire Bottle | 1 | FL2 Fire Lance, FL15 Back-Burn Torch |
| 2 | GR1 Stick Bomb | 1 | GR6 Smoke Pot |
| 2 | JK2 Saw Thrower | 1 | JK4 Cell Bomb, JK5 Gas Rocket |
| 2 | SA2 Tack Pistol | 1 | SA1 Cap Pair |
| 2 | TE3 Bola Thrower | 1 | TE2 Net Gun |
| 3 | BW2 Hunter's Bow | 1 | BW3 Strap Crossbow, BW5 Blow Tube |
| 3 | CH3 Reek Pot | 1 | CH4 Slip Pot, CH8 Salt Thrower |
| 3 | LT1 Dynamo Snapper | 1 |  |
| 3 | LV1 Wasp Jar | 1 | LV5 Bait Worm |
| 3 | MG1 Hand Organ | 1 |  |
| 3 | MN1 Powder Keg | 1 | MN14 Caltrop Scatter, MN15 Spike Pit |
| 3 | RK1 Fire Rack | 1 |  |
| 4 | FL4 Tallow Sprayer | 1 | FL3 Flare Pistol, FL16 Scald Jet |
| 4 | ME4 Maul | 1 | ME3 Boar Spear |
| 4 | SG1 Scatterbuss | 1 |  |
| 4 | SM1 Stamp Gun | 1 |  |
| 5 | AR1 Iron Bombard | 2 |  |
| 5 | BW4 Rack Bow | 2 | BW11 Winch Ballista, BW12 Counterweight Thrower |
| 5 | DF1 Bull Shield | 2 |  |
| 5 | GL1 Stovepipe | 2 |  |
| 5 | HV9 Ram Spike | 2 |  |
| 5 | RF1 Long Arm | 2 |  |
| 6 | ME6 Shock Baton | 2 | ME7 Saw Bar |
| 6 | MG6 Chatter Gun, small mount (E) | 2 |  |
| 6 | PS2 Mesh Ward | 2 |  |
| 6 | SG2 Pump Scattergun | 2 | SG3 Both Barrels |
| 7 | AC1 Scrap Coil | 2 |  |
| 7 | FL5 Pressure Thrower | 2 |  |
| 7 | GR2 Frag Ball | 2 | GR3 Skip Ball |
| 7 | MN3 Trip Line | 2 | MN2 Pressure Mine |
| 7 | RF2 Line Rifle | 2 | RF3 Breaker |
| 7 | SA3 Six-Chamber | 2 | SA5 Hush Pistol |
| 7 | SU1 Patch Gun | 2 |  |
| 8 | BT1 Breaching Pipe | 2 | BT4 Wall Thrower |
| 8 | BW6 Pump Gun | 2 | BW7 Boiler Rifle, BW8 Crank Spitter, BW9 Pin Driver |
| 8 | GL2 Break Launcher | 2 | GL3 Drum Launcher |
| 8 | HV3 Deck Gun, large mount (E) | 2 |  |
| 8 | MI1 Dread Horn | 2 | MI2 Calming Bolt, MI3 Sleep Dart, MI4 Lure Beacon |
| 8 | PS10 Ward Cell | 2 | PS11 Ward Spark |
| 8 | SM2 Corridor Gun | 2 |  |
| 8 | TE1 Harpoon Rig | 2 | TE4 Grapple Hook, TE5 Tow Hook, TE6 Boarding Claw |
| 9 | CH1 Etch Sprayer | 2 | CH2 Rust Haze, CH9 Slag Cannon |
| 9 | DF4 Flare and Chaff Pod | 2 | DF5 Sand Burst |
| 9 | LS1 Cutter Beam | 2 | LS4 Dazzler, LS7 Scald Horn |
| 9 | LV10 Bile Pod | 2 | LV9 Spore Lobber, LV13 Mantis Claw, LV18 Thorn Sower |
| 9 | MG6L Chatter Gun, large mount (E) | 2 |  |
| 9 | PS6 Quick Ward | 2 |  |
| 9 | RK10 Rocket Pod, small mount (E) | 2 |  |
| 9 | RK3 Shoulder Tube | 2 | RK2 Throwaway Tube, RK6 Twin Charge Tube, RK7 Hot Breath Tube |
| 9 | SA4 Glint Pistol | 2 |  |
| 10 | AR2 Knee Mortar | 2 | AR3 Field Mortar |
| 10 | GR4 Clinger | 2 | GR10 Brood Bomb |
| 10 | HV3S Deck Gun, small mount (E) | 2 |  |
| 10 | LG2 Stalking Rifle | 2 | LG1 Spotter's Rifle |
| 10 | LT2 Wire Biter | 2 | LT12 Lure Rod, LT13 Shock Fence |
| 10 | MA1 Sentry Stalk | 2 | MA5 Picket Post |
| 10 | ME8 Piston Fist | 2 | ME13 Hot Cleaver |
| 10 | MN13 Prox Mine | 2 | MN4 Bounding Mine, MN5 Fan Mine |
| 11 | EW1 Paint Lamp | 3 |  |
| 11 | FL10 Drag Flame | 3 | FL11 Fire Curtain |
| 11 | HV1 Drum Cannon | 3 | AR6 Bomb Bay, HV8 Depth Rack |
| 11 | JK1 Junk Cannon | 3 | JK6 Rigged Bike |
| 11 | LV2 Hornet Hive | 3 | LV11 Egg Grenade, LV14 Tooth Gun, LV15 Tick Dart |
| 11 | MG2 Squad Belt | 3 | MG3 Tripod Heavy |
| 11 | PS3 Field Ward | 3 |  |
| 11 | RK10L Rocket Pod, large mount (E) | 3 |  |
| 11 | SG4 Drum Sweeper | 3 |  |
| 12 | CD2 Frost Lance | 3 | CD1 Cold Snap, CD3 Rime Wall |
| 12 | GL4 Gum Launcher | 3 | GL5 Set-Range Launcher, GL6 Seed Launcher |
| 12 | RF4 Long Glint | 3 |  |
| 12 | SK5 Heat Dart | 3 | SK9 Return Disc |
| 12 | SN1 Basslance | 3 | SN4 Thump Pad, SN5 Ring Gun |
| 12 | SU3 Weld Beam | 3 | SU4 Pep Dart |
| 13 | CH5 Tangle Gun | 3 | CH6 Foam Thrower, CH7 Water Cannon, CH10 Flood Charge |
| 13 | GR7 Flash Cracker | 3 | GR5 Thump Ball, GR9 Choke Pot, GR11 Chatterbox |
| 13 | HV2 Chain Cannon | 3 |  |
| 13 | HV4 Flak Burst | 3 |  |
| 13 | LS2 Stutter Lamp | 3 |  |
| 13 | LT6 Skycall Rod | 3 |  |
| 13 | PS7 Bulk Ward | 3 |  |
| 13 | RK4 Lock Tube | 3 | RK5 Wire Tube |
| 13 | SM4 Pingback Carbine | 3 | SM3 Strobe |
| 14 | AC3 Stage Rifle | 3 | AC2 Coil Carbine, AC8 Mass Driver |
| 14 | FL6 Jelly Thrower | 3 | FL7 Mist Thrower |
| 14 | LG3 Bench Gun | 3 |  |
| 14 | LV6 Wormspitter | 3 | LV4 Nest Tower, LV8 Gullet Cannon, LV12 Leech Vine |
| 14 | MA3 Crawl Mine | 3 |  |
| 14 | MN6 Remote Satchel | 3 | MN7 Limpet, MN8 Cutting Charge, MN11 Tide Mine |
| 14 | PS12 Ward Post | 3 |  |
| 15 | AR5 Fortress Howitzer | 3 | AR4 Crawler Gun |
| 15 | BT2 Quake Spike | 3 | BT3 Sink Charge, BT6 Root Seed |
| 15 | DF3 Pop Dome | 3 | DF2 Mirror Shield |
| 15 | GV1 Shove Cannon | 3 | GV12 Tow Beam |
| 15 | HV5 Torpedo Tube | 3 | HV7 Crew Torpedo |
| 15 | MG4 Spin Gun | 3 |  |
| 16 | EW2 Jam Pod | 3 | EW3 Drag Field, EW4 Power Leech, EW5 Spoof Gun |
| 16 | LT3 Fork Gun | 3 | LT8 Conductor Spike |
| 16 | ME10 Hum Blade | 3 | ME9 Pile Spike |
| 16 | PB3 Static Cannon | 3 | PB4 Pulse Choke, PB5 Jam Spike |
| 16 | PS4 Line Ward | 3 |  |
| 16 | SK1 Bend Rounds | 3 | SK2 Tag Rifle, SK6 Whisper Round |
| 17 | AC6 Fighter Rail | 4 | AC5 Sled Rail |
| 17 | DF6 Needle Curtain | 4 |  |
| 17 | FL12 Gate Jet | 4 | FL13 Burial Jet, FL14 Drive Burn |
| 17 | GL7 Hop Launcher | 4 |  |
| 17 | NU1 Hand Bomb | 4 |  |
| 17 | RK11 Sky Battery | 4 |  |
| 17 | SG5 Shardcaster | 4 |  |
| 18 | GR8 Pulse Ball | 4 |  |
| 18 | LS3 Scorch Line | 4 | LS8 Burn Tower |
| 18 | LT4 Storm Rope | 4 | LT5 Guide-Line, LT7 Ball Lantern |
| 18 | PS8 Snap Ward | 4 |  |
| 19 | AC4 Rail Lance | 4 |  |
| 19 | BT7 Demolition Bore | 4 | BT5 Glasser |
| 19 | GV3 Well Launcher | 4 | GV2 Anchor Hook, GV6 Weight Projector, GV13 Magnet Glove |
| 19 | LV7 Sand Swimmer | 4 | LV16 Maw Stalk, LV17 Rider Grub |
| 19 | SA6 Three-Step Pistol | 4 |  |
| 19 | SN3 Resonator | 4 | SN2 Thin Note, SN6 Scream Coil, SN7 Hush Dart |
| 20 | HV6 Overload Torpedo | 4 | HV10 Hull Thumper |
| 20 | ME11 Light Blade | 4 | ME12 Filament Whip |
| 20 | MN9 Picky Mine | 4 | MN10 Drift Mine, MN12 Seeder Rack |
| 20 | PL2 Ember Rifle | 4 | PL1 Spatter Gun |
| 20 | PL5 Strip Battery | 4 | PL4 Furnace Cannon |
| 20 | RK8 Flock Launcher | 4 | RK9 Eye Rocket |
| 20 | SK3 Needle Swarm | 4 | SK4 Memory Bolt, SK7 Blood Round, SK8 Orbit Shards |
| 21 | CD4 Still Beam | 4 | CD5 Frost Shell |
| 21 | DF7 Thread Laser | 4 |  |
| 21 | LS9 Lance Array | 4 |  |
| 21 | LT9 Coil Tower | 4 | LT10 Arc Rake, LT11 Web Array |
| 21 | MG5 Lamp Rotary | 4 |  |
| 21 | SU2 Mender Dart | 4 |  |
| 21 | TM1 Slow Bubble | 4 | TM6 Afterimage, TM8 Dusk Dart |
| 22 | DS1 Ash Ray | 5 | DS2 Cross Beam |
| 22 | FL8 Star Gel | 5 | FL9 Void Burner |
| 22 | LS6 Cook Box | 5 | LS5 Prism Rifle |
| 22 | MI5 Confusion Lens | 5 | MI6 Forget Lamp |
| 22 | PB2 Marrow Beam | 5 | PB1 Sleet Gun |
| 22 | PH1 Swap Rifle | 5 | PH2 Shunt Gun, PH5 Rift Pocket, PH6 Fold Shell, PH8 Return Stake |
| 22 | PS5 Lattice Ward | 5 |  |
| 22 | RF5 Dial Rifle | 5 |  |
| 23 | AC7 Line Battery | 5 |  |
| 23 | GV4 Lift Beam | 5 | GV5 Drift Charge, GV7 Press Cannon, GV8 Catch Gun, GV9 Flip Mine, GV10 Snare Well |
| 23 | LV3 Cinder Moths | 5 | LV19 Spawn Tube |
| 23 | NU2 Sun Shell | 5 | NU3 Ghost Warhead, NU4 Cone Warhead, NU5 Star Array, NU6 Salt Warhead, NU7 Blackout Warhead |
| 23 | PL7 Bottled Sun | 5 | PL3 Slow Sun, PL6 Ring Thrower |
| 24 | AM0 Bottle Bay | 6 | AM2 Anti-Stream, AM3 Bottle Shell, AM4 Bright Torpedo, AM5 Last Bottle, AM6 Trigger Shell, AM7 Dumped Bottle |
| 24 | NA1 Mote Cloud | 5 | NA2 Unmaker Beam, NA4 Turncoat Dart, NA5 Hull Paste, NA6 Scrap Locust |
| 24 | PS9 Sink Ward | 5 |  |
| 24 | RK13 Long Reach | 5 | RK12 Salvo Wall |
| 24 | TM3 Delay Pistol | 5 | TM4 Echo Cannon, TM7 Holo Squad |
| 25 | AC9 Spinal Lance | 8 |  |
| 25 | AM1 Speck Rifle | 8 |  |
| 25 | SW1 Bow Gun | 8 | SW2 Sky Lance, SW3 Rod Drop, SW4 Rock Drop, SW5 Storm Seeder, SW6 Tremor Engine |

**Outside the tree.**

- **Rare finds, comic:** SA7 Kick Pocket, DS4 Shrink Ray, DS5 Meek Ray, DS6 Growth Dart, DS7 Bubble Gun.
- **Finds and rewards:** BW10 Kin Bow, GV11 Deep Well, NA3 Quiet Code, PH3 Ghost Round, PH4 Phase Lance, PH7 Gate Pair, TM2 Stutter Dart, MI7 Stare Sign.
- **Relics:** TM5 Hold Capsule, DS3 Unbinding Lance, RL1 Seam Cutter, RL2 Tallow Lantern, RL3 Quiet Spear, RL4 Glass Seed, RL5 Dusk Round, RL6 Answering Pistol, RL7 Odd Stick, RL8 Absence Round, RL9 Far Reach.
- **Cyber modules:** EW6 Palm Emitter, EW7 Wrist Blade, EW8 Shoulder Rack.
- **Salvage:** JK3 Ripped Gun.

Ammo is unlocked with the weapon that fires it, apart from the 6 existing ammo nodes, which stay as they are: pistol rounds at 2, 12 mm belt at 6, 40 mm shells at 8, rockets at 9, artillery shells at 15 and plasma charges at 20. Shell and round groups (section 6.2) open with their launcher. Fittings need the weapon bench, a Misc node at level 6.

---

## 10. Going beyond the references

What's new compared with Empyrion and the two scenarios. **Checked** means against the vanilla config I read. The Reforged Eden 1 column is from its forum list. **Unchecked** means I can't say: I read Reforged Eden 2's description, its guide for Reforged Eden 1 players and two pages of its change notes, not its weapon data, and Star Salvage: Reborn's description lists no weapons at all.

| Idea | Vanilla | Reforged Eden 1 list | RE2 | Star Salvage: Reborn |
| --- | --- | --- | --- | --- |
| Hand grenades of any kind | Checked: none. Launcher models exist, but they're rockets; the mortar grenade is a drone weapon | Not listed | Unchecked | Unchecked |
| Grenade launcher family with bounce, sticky, airburst, cluster | Checked: none | Not listed | Unchecked | Unchecked |
| Hand-laid mine family | Checked: mine and explosive blocks exist; the only hand explosive is the placed charge | Explosive charge only | Unchecked | Unchecked |
| Wire-guided, camera-guided rockets | Checked: none | Not listed | Unchecked | Unchecked |
| Lightning of any kind | Checked: none for players. Lightning is not a weapon type | Not listed | Unchecked | Unchecked |
| Living weapons: hornets, worms, moths | Checked: none. Creatures spit, players can't | Not listed | Unchecked | Unchecked |
| Homing bullets on small arms | Checked: homing rockets only | Guided rockets only | Unchecked | Unchecked |
| Gravity, phase, time, mind weapons | Checked: none | Not listed | Unchecked | Unchecked |
| Cold and acid for players | Checked: none | Not listed | Unchecked | Unchecked |
| Fire that spreads with wind, rain and plants | Checked: flame has status effects, and config shows no spread | Sets targets on fire only | Unchecked | Unchecked |
| Fuel on the ground, plasma gel, vacuum burner | Checked: none | Not listed | Unchecked | Unchecked |
| Crew-only and capture weapons | Checked: none | EMP overloads generators, close to it | Unchecked | Unchecked |
| Antimatter as a family with containment risk | Checked: antiproton is one legacy turret and one drone gun | Not listed | Partly seen: RE2 has positron cannons and heavy positron turrets, no sign of containment risk | Unchecked |
| Spinal mounts | Checked: none | Not listed | Seen: spinal variants of medium weapons exist | Unchecked |
| Bombs for small vessels | Checked: drones only | Not listed | Seen: small-vessel bombs and EMP bombs | Unchecked |
| Shield penetration as a stat | Checked: not as such | Railgun bonus only | Seen: it's in RE2. Not new | Unchecked |
| Weapon mods | Checked: none | Not listed | Seen: it's in RE2. Not new | Unchecked |
| Weather as a weapon, orbital strikes | Checked: none | Not listed | Unchecked | Unchecked |

Not new at all, and kept because they're staples: rifles, snipers, miniguns, flame, rockets, plasma, pulse lasers, flak, artillery, ion and EMP, sentry guns, explosive charges, tool turrets, the crossbow, two-mode plasma cannon, two-mode miniguns and an anti-tank rifle that draws attention.

---

## 11. Owner's decisions

Decided. These are final, and the list above follows them.

1. **Tree size.** About 150 weapon nodes open groups, and the rest are recipes, ammo, fittings or finds (section 9).
2. **Personal shields are in.** The player has a bar alongside health that takes damage first and refills after a short break from taking hits. Health doesn't refill. The concept comes from Halo and Borderlands, and nothing is copied (section 5.11, family 4.43).
3. **Big craters.** Antimatter, nuclear and orbital weapons leave permanent craters, with a server setting to turn them off (sections 5.6 and 13).
4. **No companions.** The player owns no creatures or machines that fight alongside them. Cut: the Taming Dart, the ALLY upgrade, the Shoulder Minder, the Hunter Needle and the Wasp Bay. Static placed defences stay, with no ALLY need. Enemies keep the cut machines. Swarms fired as ammo stay.
5. **Self-harm and friendly fire.** Self-harm is on by default, and friendly fire is a server setting (section 5.12).
6. **No weapon wear.** Weapons never wear out or jam (section 6.3).
7. **Comic weapons stay as rare finds.** Shrink Ray, Meek Ray, Growth Dart, Kick Pocket and Bubble Gun.
8. **Engine upgrade order.** FIRE, ORDNANCE, SHIELD, DEVICE, then BLAST.

9. **Turn effects stay.** Rider Grub and Turncoat Dart turn a creature or robot on its neighbours for 10 to 20 seconds. Nothing is owned and nothing takes orders, so they aren't companions.
10. **A shield from the first minute.** The crash suit gives a small bar at level 1, and modules upgrade it.
11. **Fire past the bar.** Flame, gas and radiation go straight to health, so low-tech weapons and hazards stay dangerous late in the game.

---

## 12. Research

Done before the list was final, for concepts and roles only. Nothing was copied.

**What I read.**

- **Sci-fi weapons:** the Encyclopedia of Science Fiction's weapons entry; Wikipedia's weapons in science fiction, flamethrower and antimatter weapon pages; Atomic Rockets' exotic sidearm, spacecraft weapon and exotic spacecraft weapon pages; TV Tropes' index of weapon tropes plus Abnormal Ammo, Fantastic Firearms, Superweapon, Weaponized Tropes, Grenade Tropes, Ranged Energy Attack Tropes, Standard FPS Guns, Trick Bullet, Trick Bomb, Lightning Gun and Bee-Bee Gun. Search extracts on Dune's shield rule, Three-Body, Revelation Space, Ender's Game, Warhammer 40,000 families and living ships.
- **Videogame weapons:** Inverse's 50 best list and The Savvy Gamer's creative systems list; Stellaris, Starsector, Noita, Team Fortress 2 and RimWorld wiki pages; EVE University's electronic warfare page; a Far Cry 2 fire interview. Search extracts on Halo's combo, Unreal's Shock Rifle, Resistance, Ratchet & Clank, Perfect Dark, Borderlands, Warframe, Destiny 2, Deep Rock Galactic, Elite Dangerous, BattleTech, FTL, Homeworld, Metro Exodus, Prey's GLOO Cannon, Earth Defense Force, Enter the Gungeon, No Man's Sky and Subnautica.
- **Empyrion and mods:** the local config files; the Reforged Eden and Reforged Galaxy forum posts; the Reforged Eden 2 Steam page, its guide for Reforged Eden 1 players and two pages of its change notes; the Star Salvage: Reborn Steam page and change notes.

**What it changed in the list.**

- Standard FPS Guns gave a roster to check the staples against. It's why the list has anti-materiel rifles, target designators, a healing device, a repair gun and placed heavy guns.
- Atomic Rockets added Marrow Beam (crew-killing particle beams), Cone Warhead, Star Array, Ghost Warhead, Sand Burst, Drive Burn, Spinal Lance, Ring Thrower, Three-Step Pistol (multi-setting sidearms), Bottle Shell's fail-dangerous idea and the Mass Driver's rock-throwing.
- The weapon trope index added Pile Spike, Cross Beam (converging stream), Filament Whip, Stare Sign (an image that harms the mind), Blood Round (gene-seeking), Rock Drop, Storm Seeder, Tremor Engine, Spoof Gun and Ripped Gun. Sleep Dart, Net Gun, Tangle Gun and the idea of ordered mixed rounds came via Atomic Rockets' stun and entangling sections.
- Halo, Unreal, FTL and Starsector gave the setter and payoff table in section 5, and shield-against-armour thinking. BattleTech gave TAG-style Paint Lamp and the ammo cook-off.
- Resistance gave the tag-and-curve idea (Tag Rifle) and the sticky organic mine; Perfect Dark gave secondary modes (Dial Rifle, Sentry Stalk) and fly-by-wire rockets; Ratchet & Clank confirmed that area orbs that arc as they travel are worth having (Ball Lantern).
- Metro gave the pumped pressure gauge (Pump Gun). Prey's GLOO Cannon gave Tangle Gun as weapon, repair and climbing tool. Team Fortress 2 gave extinguishing rules, and Far Cry 2 gave the whole fire section, including the lesson that spread must stop. Deep Rock gave sticky-flame fuel, freeze-then-hit and overclocks with costs. Noita gave fuel trails, oil, water and self-harm as a system, and the Worm and Hornet parallels. EVE gave Paint Lamp, Drag Field, Power Leech and Jam Pod. Homeworld gave the Snare Well.
- Halo and Borderlands gave the concept of a personal shield that refills after a break; only the concept is used, and the numbers, tunings and names here are GalHack's own. Borderlands also gave maker traits. Reforged Eden 2 gave the weapon-size limits in 6.4 and showed that shield penetration as a percentage works. Stellaris gave the idea of three weapon roles per damage class.
- Inverse and the listicles gave the recurring reasons people love weapons: versatility, physics, humour, feel and overkill. That's why Catch Gun, Kick Pocket and Odd Stick stayed.
- Antimatter sources gave the fail-dangerous containment rule, the catalysed warhead (Trigger Shell) and the note that a beam of antimatter is wasteful, which became its cost.

**What I couldn't read.** The Empyrion wiki on Fandom showed a security check; I left it. Nexus Mods and StrategyWiki returned 403, the Internet Archive refused fetches, PC Gamer's body text didn't come through, and TechRaptor's list returned 403. Most game facts above come from search extracts, so treat them as concepts, not specifications.

**Sources.**

- [Weapons, Encyclopedia of Science Fiction](https://sf-encyclopedia.com/entry/weapons)
- [Weapons in science fiction, Wikipedia](https://en.wikipedia.org/wiki/Weapons_in_science_fiction), [Flamethrower](https://en.wikipedia.org/wiki/Flamethrower), [Antimatter weapon](https://en.wikipedia.org/wiki/Antimatter_weapon)
- Atomic Rockets: [exotic sidearms](https://www.projectrho.com/public_html/rocket/sidearmexotic.php), [conventional space weapons](https://www.projectrho.com/public_html/rocket/spacegunconvent.php), [exotic space weapons](https://www.projectrho.com/public_html/rocket/spacegunexotic.php)
- TV Tropes: [Weapons and Wielding Tropes](https://tvtropes.org/pmwiki/pmwiki.php/Main/WeaponsAndWieldingTropes), [Standard FPS Guns](https://tvtropes.org/pmwiki/pmwiki.php/Main/StandardFPSGuns), [Abnormal Ammo](https://tvtropes.org/pmwiki/pmwiki.php/Main/AbnormalAmmo), [Fantastic Firearms](https://tvtropes.org/pmwiki/pmwiki.php/Main/FantasticFirearms), [Superweapon](https://tvtropes.org/pmwiki/pmwiki.php/Main/Superweapon), [Trick Bullet](https://tvtropes.org/pmwiki/pmwiki.php/Main/TrickBullet), [Lightning Gun](https://tvtropes.org/pmwiki/pmwiki.php/Main/LightningGun), [Bee-Bee Gun](https://tvtropes.org/pmwiki/pmwiki.php/Main/BeeBeeGun)
- [Far Cry 2 fire interview, Game Developer](https://gamedeveloper.com/game-platforms/interview-how-i-far-cry-2-i-s-fire-fuels-spreads)
- [The 50 best weapons in video game history, Inverse](https://inverse.com/gaming/best-video-game-weapons)
- [20 most creative weapon systems, The Savvy Gamer](https://www.thesavvygamer.com/gaming/20-most-creative-weapon-systems-modern-gaming)
- [Stellaris weapons](https://stellaris.paradoxwikis.com/Weapons), [Starsector weapons](https://starsector.wiki.gg/wiki/Weapons), [Noita spells](https://noita.wiki.gg/wiki/Spell), [TF2 fire](https://wiki.teamfortress.com/wiki/Fire), [RimWorld weapon guide](https://rimworldwiki.com/wiki/Weapon_Guide), [EVE electronic warfare](https://wiki.eveuniversity.org/Electronic_warfare)
- [Halo combo, Halopedia](https://www.halopedia.org/Noob_combo), [Prey's GLOO Cannon, PC Gamer](https://www.pcgamer.com/the-making-of-preys-gloo-cannon) (search extract), [Deep Rock Galactic overclocks](https://deeprockgalactic.wiki.gg/wiki/Overclocks)
- [Reforged Eden forum post](https://empyriononline.com/threads/reforged-eden-scenario.94773/), [Reforged Galaxy post](https://empyriononline.com/posts/381724/), [Reforged Eden SV weapons thread](https://empyriononline.com/threads/reforged-eden-sv-weapons.98069/)
- [Reforged Eden 2 on Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=3143225812), [RE2 change notes](https://steamcommunity.com/sharedfiles/filedetails/changelog/3143225812), [RE2 guide for RE1 players](https://steamcommunity.com/sharedfiles/filedetails/?id=3241608094)
- [Star Salvage: Reborn on Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=3409399573)
- Local, read-only: `Content/Configuration/ItemsConfig.ecf`, `BlocksConfig.ecf`, `StatusEffects.ecf`, `DamageMultiplierConfig.ecf` in the Empyrion install; `docs/DESIGN.md`, `docs/world-themes.md`, `docs/tech-tree-pacing.md`; and the `batch` worktree's slice, boarding, invasions, building, weather, wildlife and terrain code.

---

## 13. Needs engine work

The owner's rule: no weapon is dropped or watered down because of today's engine. Each weapon with a **Needs** tag says which system would change. Here they are by shared upgrade, so that one change unlocks a group. 102 of the 357 entries carry a tag.

The build order is decided: FIRE, ORDNANCE, SHIELD, DEVICE, then BLAST. The other upgrades wait until a weapon that needs them is scheduled.

### Groundwork every weapon shares (integration, not new systems)

- **Hand weapon layer.** The slice's only working gun is the survival tool's shot, which is a raycast with a visual bolt (`SurvivalTool.Shoot`, `ShotFx`). The boarding prototype already has what's needed: data-loaded `WeaponDef` (damage, rate, magazine, pellets, spread, recoil pattern, headshot, range, noise) and a swept `Projectile` with travel time, homing `Tracking`, warning shots and near-miss flinch. Move these into the slice and add a weapon slot in the hands code.
- **Vessel gun layer.** The `gatling`, `turret` and `rocketpod` blocks exist as data with ammo and unlock entries, but nothing fires them in the slice. The invasions prototype has `FireControl` with weapon classes, ramp and spread by target class. Bring it over, with shared-storage ammo draw and a power draw rule.
- **Timed effects.** A small list of timed effects per target. Creature states already include Flee, Freeze, Root and Stalk, so most fit into the existing brain; soldiers and the player need a matching list.

### Shared upgrades

Flame entries marked "ground fire only" work without FIRE as a cone and a burn timer; the upgrade adds the fire on the ground. The Pressure Thrower works the same way but carries no tag, because it's one of the first 12; FIRE gives it ground fire when it lands.

| Upgrade | What changes, and why | System and files today | Weapons |
| --- | --- | --- | --- |
| **FIRE** | Burning state, flammable materials and plants, pooled fire patches, wind, rain and water rules (section 5.2) | Nothing exists. New module reading `WeatherSim.Current`, `GroundCover`, `IWaterField`, wildlife `Flee`; plant swap in the surface flora layer; `ShotFx` for scorch | FL1 Fire Bottle, FL4 Tallow Sprayer, FL6 Jelly Thrower, FL7 Mist Thrower, FL8 Star Gel, FL9 Void Burner, FL10 Drag Flame, FL11 Fire Curtain, FL12 Gate Jet, FL13 Burial Jet, FL15 Back-Burn Torch, CH4 Slip Pot |
| **ORDNANCE** | Missiles, torpedoes and mines as shootable things that persist off screen and in saves; point defence and decoys; long-range flight | `Projectile` is a node with a ray sweep. Needs a pooled ordnance list, a target for point defence, and placed-object records in `prototypes/saves` | RK11 Sky Battery, RK12 Salvo Wall, RK13 Long Reach, MN10 Drift Mine, MN12 Seeder Rack, DF6 Needle Curtain, DF7 Thread Laser, HV4 Flak Burst, HV5 Torpedo Tube, HV6 Overload Torpedo, NU3 Ghost Warhead, NU4 Cone Warhead, NU5 Star Array, NU7 Blackout Warhead, AM4 Bright Torpedo |
| **SHIELD** | Shields themselves, in two layers. Personal: a bar on players and soldiers, the break and refill, the pass-through rules and the HUD bar (section 5.11). Structure: capacity, ripple, recharge delay, fold-crystal burn and passing through, as the design says | Designed in the design doc's section 15 for structures, not in code. The personal layer sits in front of `Suit.Hit`; hazards stay in `Vitals` and go straight to health, so they already bypass it. The bar joins `Visor.DrawVitals`. Soldiers need the same layer in `boarding` and `invasions`. The structure layer is a new device with a damage layer before `VesselDamage` | PH6 Fold Shell, DF3 Pop Dome, PS1 to PS12 |
| **DEVICE** | Devices that can be knocked offline, jammed or drained; locks and sensors that can be broken | `Vessels/Device.cs`, power in `VesselRoles`; radar and lock don't exist yet | PB3 Static Cannon, PB4 Pulse Choke, PB5 Jam Spike, CD5 Frost Shell, GV10 Snare Well, HV6 Overload Torpedo, NU7 Blackout Warhead, EW2 Jam Pod, EW3 Drag Field, EW4 Power Leech, EW5 Spoof Gun |
| **BLAST** | Budgeted large blasts and large craters (see below) | `Damage.BlastInto`, `VesselDamage.Flush` and `Lost`, `ChunkStore` crater caps, terrain edit storage | GV11 Deep Well, DS3 Unbinding Lance, NU1 Hand Bomb, NU2 Sun Shell, NU6 Salt Warhead, AM2 Anti-Stream, AM3 Bottle Shell, AM4 Bright Torpedo, AM5 Last Bottle, AM6 Trigger Shell, AM7 Dumped Bottle, SW1 Bow Gun, SW2 Sky Lance, SW3 Rod Drop, SW4 Rock Drop, RL8 Absence Round |
| **ROOM** | Sealed-room air: gas, fuel mist, breaching and crew in rooms, so a thing can harm what's inside | Design has sealed rooms and venting; `boarding` has crew and rooms. Needs a room graph with a value per room, not a gas simulation | GR9 Choke Pot, PB2 Marrow Beam, FL7 Mist Thrower, LV19 Spawn Tube, TE6 Boarding Claw, HV7 Crew Torpedo, NU3 Ghost Warhead |
| **BODY** | Loose objects and thrown bodies: lifting creatures, picking up barrels, moving small things by force | `PhysicsWorld` has one rigid body per structure and never per block. Needs a small capped pool of loose bodies, and lift and ragdoll states on creatures and players | GV4 Lift Beam, GV5 Drift Charge, GV8 Catch Gun, GV9 Flip Mine, GV11 Deep Well, HV10 Hull Thumper |
| **GROUND** | Temporary raised ground, walls and growth, and glass as a surface; terrain itself stays remove-only | Remove-only rule in `PlanetTerrain`. Raised things become temporary block structures; glass needs a surface-material edit | CD3 Rime Wall, LV16 Maw Stalk, BT4 Wall Thrower, BT5 Glasser, BT6 Root Seed, SW6 Tremor Engine, RL4 Glass Seed |
| **MIND** | Soldier and robot AI states for confuse, forget, freeze and control; player-side faction flip for seconds | Wildlife `Brain` covers creatures. `boarding` guards and invasions soldiers have no such states | LV17 Rider Grub, NA4 Turncoat Dart, MI5 Confusion Lens, MI6 Forget Lamp, MI7 Stare Sign, EW5 Spoof Gun |
| **ACTOR** | Per-actor time scale, size and position history | Creature and player update loops; no scaling or rewind today | TM1 Slow Bubble, TM2 Stutter Dart, TM5 Hold Capsule, DS4 Shrink Ray, DS5 Meek Ray, DS6 Growth Dart |
| **PHASE** | Rays and bolts that pass through blocks by rule, and two linked gates | Ray masks in `Projectile`, `ShotFx`; Gate Pair also needs a second camera | PH3 Ghost Round, PH4 Phase Lance, PH7 Gate Pair, RL1 Seam Cutter |
| **VIEW** | Camera handed to a remote body, with the player's body left in the world | `Hands`, camera rig in `Game` | RK9 Eye Rocket, PH7 Gate Pair |
| **SWARM** | Big cheap clouds of small flyers with simple flocking | `Tracking` handles eight hornets. Hundreds need a MultiMesh agent pool | LV3 Cinder Moths, NA1 Mote Cloud, NA6 Scrap Locust |
| **ORBIT** | Strikes across the surface and orbit boundary | `Spaceflight.Seam`, `OrbitZone`, `SurfaceZone` are separate zones | SW2 Sky Lance, SW3 Rod Drop, SW4 Rock Drop |
| **FLUID** | Water that can change: flood, freeze, oil film | `IWaterField` is static by design; digging never moves water | CH10 Flood Charge |

### Limits to design around (BLAST)

`Damage.BlastInto` loops through every cell in the cube around a blast in one frame, with no budget, so cost grows with radius cubed: a radius of 10 cells tests about 9,300 cells, and 30 cells about 227,000, before any block is lost. After that, `VesselDamage.Lost` runs connectivity, cargo and mesh updates. Terrain craters are capped at 6 m and 900 cells on purpose (`ChunkStore.MaxCraterRadius`, `MaxCraterCells`), edits are stored cell by cell, and the design wants debris pooled and capped. So a big blast needs all of this:

- Expand as a shell over many frames with a per-frame cell budget, and a whole-chunk shortcut in the inner core so a chunk can go in one step.
- Run the connectivity split once, at the end.
- Store a big crater as one record (centre, radius, seed) rather than cells, stamped into the heightfield, with a decal for the glass floor. Terrain stays remove-only.
- Cap debris and flashes through the existing pools, and show the scale with light, sound and camera instead.
- Secondary explosions (a Bottle Bay going off in a fleet) queue up, so a chain can't all land in one frame.
- Below a 10-cell radius and within the 6 m crater cap, antimatter works today: the Speck Rifle needs nothing new.
- Big craters are permanent by default. A server setting skips the stored crater record, so the blast still damages what's there and the terrain stays as it was.

### Notes on SHIELD

- **Personal first.** The personal layer can land before the structure layer, because it needs no device. `Suit.Hit` is already the one way a weapon or creature hit reaches the player's health.
- **Damage kinds.** A hit needs to carry its class, its tags and its hit rules as far as the shield, so that fire and gas can go past and plasma can strip. Today `Suit.Hit` takes an amount and a `DamageKind`.
- **Timed effects.** The player's list of timed effects (groundwork, above) needs a check for "bar above zero" before a hit-delivered status lands.
- **Weapons already in the list.** `strip`, `stall` and `overload` do nothing extra until SHIELD lands, so those weapons work without it and gain more when it arrives.
- **Removed.** The ALLY upgrade is gone with the companions. Sentry Stalk, Crawl Mine and Picket Post only need the placed-object records already planned, and the Sentry Stalk uses the same target rules as the vessel gun layer.
