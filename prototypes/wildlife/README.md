# Prototype: procedural wildlife and the harvester hovercraft

Proves that every planet can roll its own fauna on a handful of reliable procedural rigs, from near-familiar grazers to properly weird things, and that they come out looking like animals rather than accidents. It also covers:
- herds and hunting packs
- telegraphed, dodgeable attacks
- the harvester hovercraft: mow through a forest or run down wildlife and watch the hold fill

All of it runs cheaply enough for a world full of creatures on a low-end PC.

![Creatures of three planets, with their giants behind, for scale](screenshots/lineup.png)
![A pack closing in from every side](screenshots/pack.png)
![A temperate herd below the hills](screenshots/herd.png)
![A colossus dropping its head for a charge](screenshots/giant.png)
![A Bloom-corrupted hunter rearing into its first lunge](screenshots/bloom.png)
![The harvester chewing into a grove](screenshots/harvester.png)

## How it works

**The generator** (`src/Core/FaunaGenerator.cs`) is plain C# with no nodes. Its inputs are the planet seed, alienness (0 to 1) and a Bloom flag; its output is 6 to 10 species, identical on every machine. Each species gets:
- a body plan and form (walkers, crawlers and slitherers, leapers, fliers and floaters, swarms, giants, oddities: 19 forms in all)
- a behaviour and a temperament, rolled first so the body is built for the job
- swappable parts (heads, horns, backs, tails, extra limbs, tendrils)
- proportions, palette, skin pattern and glow
- a signature attack, size, stats and drops
- a name from syllables that each planet narrows to its own flavour

Alienness pulls every roll towards options of matching weirdness, so temperate worlds get near-familiar animals and alien worlds get crystal-backed, lamprey-mouthed things. The chance of an off-script pairing also rises with alienness: a skittish giant, a herd that hunts. A Bloom world rolls the same local fauna and then corrupts it: same bodies, darker, veined, glowing pink, tendrils sprouting, angrier.

**Keeping it cool, not broken.** Infinite variety is fine; silly combinations are not. The rules are mostly data (`data/wildlife.json`), and `FaunaQuality` checks them:
- **Roles:** parts carry the behaviours they suit. Hunters get jaws, beaks, maws and claws, never a grazer's snout. Antlers, ram horns and hooves go on grazers. Scythes and stingers go on hunters.
- **Forms:** some parts only go on some bodies. Insect heads, antennae and feelers only go on arthropod forms (skitterers, crawlers, leapers, swarms), never on a mammal-like strider.
- **Excludes and requires:** pairs that clash can't be assembled, whichever is rolled first. Examples: horns on a lamprey mouth, tusks under a beak, antennae without an insect head, feelers on top of antennae, back tendrils through a shell.
- **Proportions:** alien worlds may push a form's ranges only slightly. Heads stay inside a band so nothing reads as a baby animal, and legs, necks and tails have upper limits. Stilts may be long on a small body, but a giant's stilts never outreach 1.2 times its body length. Giants also get thick, knee-capped stilts under a heavy hull, not poles under a pebble.
- **Gone for good:**
  - odd leg counts, except the tripod form, which is built round three legs
  - checkerboard skins, which read as a missing texture
  - eye-stalks, pom-pom and disc tails, frog throat sacs and big pale cartoon eyes

**Meshes** (`src/Rigs/Shapes.cs`, `Parts.cs`, `src/View/MeshBuilder.cs`) go beyond overlapping capsules:
- **Lofted bodies:** elliptical sections along the body, shaped by job:
  - grazers are deep-bellied barrels
  - hunters are deep-chested with a tucked waist
  - giants carry a shoulder hump
  - skitterers have a front body and a swollen abdomen joined at a waist
  - leapers are hunched
  - floaters are long bladders with a crest, not balloons
- **Lofted heads:** a cranium, a brow, a hinged lower jaw, rows of teeth, and small deep-set eyes (glowing species get slit eyes).
- **Sphere-swept tapers** for legs, necks, tails, horns, claws and teeth. Joints share radii, so knees and neck bends meet without a seam at any angle.
- **One material per species**, with each surface's kind baked into the mesh (`shaders/creature.gdshader`):
  - pebbled hide with a fine bump
  - glossy horn with growth rings
  - clear-coated shell
  - thin wing and bag membrane lit from behind
  - wet eyes with a glint

  Undersides darken as if shadowed by the body.

**Rigs** (`src/Rigs`). Six rigs carry every body plan; variety comes from parts and proportions, not new animation:

| Rig | Used by | Animation |
| --- | --- | --- |
| Legged | walkers, giants, walking plants, crystal grazers | IK foot stepping with a diagonal gait for 2 to 8 legs, the body sagging onto planted feet; FK necks, tails, tendrils, extra limbs |
| Chain | crawlers, slitherers, burrowers | follow-the-leader segments, a travelling wave, rowing legs, sinking to burrow, cobra rear, mantis scythes |
| Hopper | leapers, living boulders | spring hops with squash and stretch; boulders tumble a quarter turn per hop |
| Winged | fliers | flapping with the outer panel lagging, gliding, banking, folding wings to land and eat |
| Gas bag | floaters | breathing bladders, a bobbing drift on the wind, trailing stinging tendrils |
| Swarm | insect clouds, skitter packs | boids round one shared brain and health pool |

Each rig is pure C# that writes a world transform per rigid piece. `src/View` builds one shared mesh per piece and copies poses on. Distant creatures re-pose every 2, 4 or 12 frames, and the farthest are hidden.

**Behaviour** (`src/Sim/Brain.cs`) covers:
- **Grazing and wandering, and herds:** boids with a shared goal and a shared alarm. Spooked herds bolt together and regroup.
- **Pack hunters:** 3 to 6 that roam behind a leader and share one prey. See below.
- **Lone predators:** stalk crouched, then chase.
- **Ambushers:** stay buried or curled by day and strike at night.
- **Scavengers** find corpses, and **swarms** hunt as one cloud.
- **Territorial giants:** rear and roar before they charge.
- **Oddities:** walking plants root by day, living boulders only move while you're not looking, others trail you curiously.

Movement is simple steering over the terrain. It goes straight at the goal when a line test on a 3 m nav grid is clear, otherwise along a bounded A* path. Felled trees free their cells, and regrown trees block them again.

**Pack hunters.** A planet's second species is a pack 60% of the time (otherwise a lone predator); packs can also roll later. They appear on 71 of 100 test planets. Packs are only ever ground walkers, never giants. A pack:
1. **Roams** in a wedge behind its leader.
2. **Stalks** together, crouched, once any member spots prey.
3. **Surrounds** the prey inside 20 m. The member nearest the side they came from holds the front. The others take the flanks at ±110°, then the rear quarters, then behind, circling round the ring rather than across it.
4. **Engages.** The front member feints: it darts in snarling and backs off without biting. Meanwhile the flankers close from their own sides and attack in turn.
5. **Retreats** together when the leader dies or half the pack is hurt, then regroups somewhere else before hunting again.

**Signature attacks and fairness.** Each behaviour has a signature attack in data: a name, a style, its telegraph, its reach and lunge time, and its wind-up sound. Each rig adapts it to the body. Every attack is wind-up, lunge, recover (`src/Sim/Attacks.cs`).

| Behaviour | Attack | Wind-up | Telegraph | Sound |
| --- | --- | --- | --- | --- |
| Herd | Stampede charge | head down, pawing the ground | 1.2 s | bellow |
| Grazer | Head butt | head down | 1.0 s | bellow |
| Predator | Pounce | crouched low and coiled | 1.1 s | growl |
| Pack hunter | Flank bite | rears back | 1.0 s | snarl |
| Ambusher | Eruption | bursts up out of hiding | 0.9 s | hiss |
| Scavenger | Snap | rears back | 1.0 s | chitter |
| Swarm | Sting cloud | cloud pulls in tight | 1.0 s | buzz |
| Territorial giant | Trample | rears right up | 1.8 s | roar |

Rig adaptations: fliers turn their attack into a dive, crawlers coil and strike like a snake, leapers leap, and floaters lash with their tendrils.

The rules that keep it fair:
- **First attack:** a creature's first attack on a player always uses the full telegraph, with its pose and its sound swelling until the lunge.
- **Commit:** part way through the wind-up the creature commits to where you stood. The lunge flies at that spot, so stepping aside dodges it.
- **Later attacks** use the short normal wind-up. A creature that hasn't attacked you for 20 s must telegraph again.
- **Packs take turns:**
  - only one member winds up at a time
  - each commit comes at least a full telegraph after the last
  - so several lunges never land on you at once without warning
- **Damage** is never capped or reduced. There is no last stand. No creature is ranged yet.

**First sightings are silent.** Meeting a creature for the first time puts nothing on screen and plays nothing: no card, banner, toast or chime. The codex records who saw each species first without raising any event. Names show only when you ask: hold Q on a creature to scan it.

**Harvesting** (`src/Core/Harvest.cs`):
- The blades are a box on the front of the vessel. Plants in it pay out wood and plant matter as they are chipped, and creatures in it are chewed and their meat (and the odd rare part) goes straight into storage.
- Storage is one shared inventory with a capacity, and nothing harvested is ever thrown away. The blades only bite into a plant or creature whose whole remaining yield fits in the hold; otherwise they idle and the vessel shoves it aside.
- Felled plants regrow on a timer, but only while no player is within 70 m.
- Killing something on foot leaves a corpse: E butchers it into your pack, which empties into the hold when you board. Scavengers eat whatever you leave.

**Terrain.** A gentle plain in the middle rises into rolling hills. The height function is baked into a 2 m grid at the ground mesh's own spacing and read back bilinearly, which is cheaper for creature legs and matches the drawn ground.

All sounds are synthesised at start-up (`src/Game/ProceduralAudio.cs`). The prototype uses no third-party assets.

**Quitting** goes through `src/Game/Shutdown.cs` in a fixed order, from the test, the screenshot harness and the game window alike:
1. Stop every sound.
2. Wait a quarter of a second of real time for the engine's audio mixing thread to retire the playbacks. The hovercraft's hum is a looping stream; quitting straight after freeing the scene used to leave it live on the mixer thread during engine teardown, an intermittent crash on exit.
3. Dispose the shared meshes and materials on the main thread.
4. Flush pending finalizers while the engine is still whole.
5. Quit.

The prototype starts no threads of its own.

## Play it

From the repo root, in PowerShell:

```powershell
.\tools\godot.ps1 -Project prototypes/wildlife -Play
```

| Key | Action |
| --- | --- |
| WASD, mouse | Walk and look; drive and orbit the camera in the hovercraft |
| Shift | Sprint, or boost the hovercraft |
| Space | Jump |
| Ctrl (hold) | Crouch: creatures find you less alarming |
| Left mouse | Fire the blaster |
| Q (hold) | Scan the creature under the crosshair: name, behaviour, signature attack |
| E | Butcher a kill into your pack |
| F | Board or leave the harvester hovercraft (parked to your right at the start) |
| 1 / 2 / 3 | Switch the planet: temperate, alien, Bloom-infested (new fauna and forest) |
| N | Skip a quarter day (ambushers strike at night, walking plants wander) |
| Esc | Free the mouse |

You start on the alien world with a herd on the plain ahead and a grove to the left to drive the harvester into. Packs roam 50 to 120 m out.

## Test it

```powershell
.\tools\test.ps1 -Project prototypes/wildlife -Scene res://tests/WildlifeTest.tscn
```

Runs headless and exits 0 or 1. `tests/QuitProbe.tscn` checks the game's own way out: it drives the hovercraft with its sounds playing, closes the window, and must exit 0 with nothing leaked. `WildlifeTest` checks:
- **Generator:**
  - The same seed gives the same species, and a different one doesn't.
  - Every planet rolls 6 to 10 species, with at least 4 body plans in 8 and no two identical part sets (120 planets).
  - Weirdness rises with alienness (0.28 at 0, 0.47 at 0.5, 0.63 at 1).
  - Bloom species are marked corrupted and are corrupted versions of the local fauna.
- **Quality:**
  - All 4,904 species from 600 planets (alienness 0 to 1, a fifth Bloom-infested) pass every part, form, behaviour and proportion rule.
  - Spot checks find no grazer snouts on hunters, scythes on grazers, insect heads on beasts, odd-legged walkers or horned lamprey mouths.
- **Signature attacks:** every attacker carries one, in 7 styles and 13 named attacks.
- **Animation:**
  - Every species on five planets animates for 60 s, through day, night, hunts and deaths, with no NaNs, bodies in range and nothing flying off.
  - Planted walker feet stay within contact tolerance (worst 0.13 m of 0.21 m over 578,000 checks).
- **Combat:** every attacking species (85 across 12 planets) opens with a wind-up of at least its telegraph. It is dodged by stepping aside after the commit (85/85), hits a target that stands still at full damage (85/85), and attacks at the normal rate afterwards.
- **Herds:** they stay together over 60 s, and bolt and regroup when spooked.
- **Packs:**
  - They appear on most planets (71 of 100).
  - A roaming pack stays together (widest 4.8 m from the middle).
  - Members surround the prey before anyone commits (spread over 218 degrees, on both sides of the front).
  - Commits are at least a telegraph apart (closest 1.02 s for a 1.0 s telegraph).
  - Every member's first bite lands full damage.
  - The pack breaks off together when its leader dies, or when half of it is hurt.
- **Ambushers:** they stay hidden by day and strike at night.
- **Harvesting:**
  - A harvested tree adds its wood, is removed, frees the nav grid, and regrows only when no player is near.
  - Running a creature down puts its meat in the hold.
  - The storage cap is enforced, and the blades leave alone anything the hold can't take in full.
- **Silent first sightings:**
  - The codex records the first finder and has no events.
  - No announcement-like code remains.
  - In the real scene, a never-seen species placed in front of the camera is recorded with nothing new on screen and no UI sound.
- **Performance:** 200 creatures plus a swarm must update in under 4 ms per frame with every rig posed every frame.
- **Playable scene:** the real scene, through its input path. Keys 1 to 3 switch worlds, and F, W, F board, drive and leave the hovercraft.

On a Ryzen 7 5800X3D, 200 creatures plus a 51-insect swarm cost about 3.3 ms per frame at full rate, almost all of it rigs. With the distance LOD, and syncing about 5,500 mesh nodes, it's 1.7 ms. A real planet has 60 to 80 creatures about.

Screenshots run windowed, from the prototype folder:

```
godot --path prototypes/wildlife res://tests/Screenshot.tscn -- --view=<view> --out=file.png
```

- **Views:** `lineup`, `herd`, `pack`, `giant`, `bloom`, `predator`, `harvester`.
- **For checking the generator by eye:**
  - `sheet` renders a contact sheet with one camera per species. `--pick=0,3` narrows it to chosen species.
  - `--worlds=temperate,alien,bloom` or `seed123:0.6[:bloom]` picks the planets for `lineup` and `sheet`.
  - `survey` (headless) lists seeds that would make good demo worlds.
- `--hud` keeps the debug HUD.

## Known limits

- **Rigid pieces, no skinning.** Lofts and tapers hide most joints, but a piece is still rigid: bellies don't bulge and skin doesn't slide. Each piece is a draw call (about 30 per creature on average); a skinned single mesh per creature would be cheaper on the GPU.
- **Some combinations are still plain or odd.** Leapers still read a little like round frogs on stilts. Variety means the odd creature is merely dull rather than broken.
- **Animation quality is uneven:**
  - Legged walkers are the best: feet plant and step properly. Very fast turns and lunges can still make a foot slide for a step.
  - A pounce on legs is a deep crouch and a long forward lunge, not a leap: planted feet can't leave the ground.
  - Leapers' spring legs are posed, not planted.
  - Crawler legs row on a sine wave, without ground contact.
  - Tendrils, tails and necks are procedural sway, not physics.
- **Few collisions.** Creatures don't collide with each other (herd and pack members keep apart) or with the player on foot. The hovercraft shoves them aside.
- **Behaviour scope.**
  - Day and night only change ambushers, walking plants and lighting.
  - No creature is ranged.
  - Nothing fights anything else; predators and packs only hunt players.
  - A pack's feinter never bites while it has flankers left.
- **Single player.** The codex finder is always "Player 1"; nothing is networked or saved.
- **Placeholder world.**
  - The terrain is a height function with hills, not the planet generator's.
  - Every planet shares the same land shape and forest layout, restyled.
- **Debug JIT optimiser.** The project enables the JIT optimiser in Debug builds (`Wildlife.csproj`), because play-testing from the editor was noticeably slower without it.
