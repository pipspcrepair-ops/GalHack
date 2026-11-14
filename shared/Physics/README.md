# Shared physics module

`shared/Physics` (namespace `GalHack.Physics`) is the one physics module every GalHack system builds on. It unifies what the ship-handling, weather, terrain-digging, building and walkable-ship prototypes each invented: one set of units, one collision-layer table, one force path, one damage formula, one set of contact events, craters that really edit the terrain, structures as bodies, riding moving hulls, and stuck-safety for everything.

`prototypes/physics-sandbox` proves it all together; read its tests for worked examples of every rule here.

## Layout

| Folder | What | Depends on |
| --- | --- | --- |
| `Core/` | The rules: bodies, forces, contacts, damage, craters, severing, riding, stuck-safety, hover engines and landing gear | Plain C# and `System.Numerics` only. No Godot types. |
| `Engine/` | Thin Godot adapters: the node that steps the world, bindings from bodies to nodes, ray queries, per-chunk structure shapes, cabin worlds | Godot 4.7 |
| `physics.json` | Every number the module uses, with a comment per section | |

Core never calls into a game system. What a game knows (its flight model, its terrain, its weather, its health) reaches the module through small interfaces; the integration prototype's `src/Bridges` shows one of each.

## Adopting it

1. Compile it in (never copy it). In the prototype's `.csproj`:
   ```xml
   <ItemGroup>
     <Compile Include="..\..\shared\Physics\**\*.cs" />
   </ItemGroup>
   ```
2. Make the world and its host node, at physics priority -10 (before anything reads a transform):
   ```csharp
   var rules = PhysicsRules.FromJson(File.ReadAllText(".../shared/Physics/physics.json"));
   var world = new PhysicsWorld(rules);
   world.Zone.Strength = 9.8f;            // this zone's gravity
   world.Zone.Ceiling = 600f;             // no weight above the atmosphere (the handling model's switch)
   world.Terrain = myTerrainEditor;       // ITerrainEditor: craters go through the terrain's own edit API
   world.StuckFields.Add(myTerrainField); // IStuckField: what bodies must be kept out of
   world.ForceProviders.Add(myWeather);   // IForceProvider: wind, tornadoes, anything that pushes
   AddChild(new PhysicsHost(world));
   ```
3. Register each body once, with its mass and drag area, and bind it to its node:
   ```csharp
   Body crate = world.Register(new BodySpec { Kind = BodyKind.Crate, Motion = Motion.Free,
       Mass = 60f, DragArea = 0.75f, Shape = StuckShape.Box(0.8f), Position = at });
   host.Bind(crate, new FreeBinding(rigidBody, crate));
   ```
   Vessels get a `VesselController` (with your `IFlightRules`) on a `HullBinding`; players and creatures a `WalkController` on a `CharacterBinding`; crates and wrecks a `FreeBinding`.
4. Push things only with `world.AddForce(body, force, kind)` and `AddImpulse`/`AddImpulseAt`. Listen to `Impact`, `Landed`, `LiftedOff`, `Cratered`, `Severed`, `Rescued`.

## Conventions

- **Units:** metres, kilograms, seconds, newtons, always. Radians in code, degrees only in data and on screens. Cores that keep tonnes and kN (the handling model, the building catalogue) convert at the bridge with `Units`; the ratio is the same, so thrust / mass reads as m/s² in both.
- **Axes:** +Y up, −Z forward, +X right (Godot's). A body's angular velocity is in the same frame as its velocity. Quaternion products are Hamilton products: `MathX.ThenWorld` turns about world axes, `MathX.ThenLocal` about the body's own.
- **Collision layers** (`Layers`), reconciled from the prototypes:

  | Bit | Layer | Was |
  | --- | --- | --- |
  | 1 | `World`: terrain, rocks, POIs | World / Terrain = 1 everywhere |
  | 2 | `Hull`: one body per structure (vessels, bases, wrecks) | ShipHull / Structure = 2 everywhere |
  | 4 | `Interior`: only ever inside a cabin world (its own physics space) | walkable-ship's Interior = 4 |
  | 8 | `Water`: water surfaces, seen only by hover probes (the water itself is a volume, below) | ship-handling's Water was 4 |
  | 16 | `Player` | terrain-digging's Player was 4 |
  | 32 | `Props`: crates, creatures, loose cargo | terrain-digging's Props was 8 |
  | 64 | `Flora`: a planet's trunks, crowns and boulders (apart from `World`, so ground probes see only the ground) | new |

  Masks: `VesselMask` (World, Hull: players and props get out of a hull's way, so a crate on the deck never stops a lift-off), `WreckMask`, `PlayerMask`, `PropMask`, `ProbeMask` (World, Hull, Water: hover engines ride water), `GearMask` (World, Hull, Flora: legs stand on solid things only, and on a tree only if it takes the craft's weight), `CabinMask`.
- **Gravity:** one `ZoneGravity` per zone (uniform, nothing above the atmosphere ceiling) and one `CoreGravity` per structure core: on by default, toggleable, costs no power, and pulls toward the structure's own floor on bodies in its cabin world. With the core off, the zone's gravity reaches into the cabin, turned into the structure's frame (none in space). Riders out on the hull feel exactly the same gravity as inside (see riding).

## One tick

`PhysicsHost.Step` runs the world in a fixed order, bodies always in registration order, so the same inputs give bit-identical trajectories:

1. `BeginTick`; free bodies sync from the engine (and report impacts: velocity changes their forces don't explain, along the contact normal).
2. `ApplyForces`: each body's own drive (thrust, walking), then gravity from its zone or core and the water's buoyancy and drag (each body's `Water` state is updated here, before anything reacts to it), then the force providers (weather), then reactors (legs gripping against the wind, swimmers' strokes, a landed ship knocked loose by a tornado).
3. `Integrate`: kinematic bodies get `v += ΣF/m·dt + ΣJ/m`.
4. Kinematic bodies move with collisions: hulls first, then creatures, then characters (a rider's hull has already moved). Contacts go to `ResolveContacts`. Then `CarryParked`: every landed vessel parked on a host moves by exactly what its host did this tick.
5. `EndTick`: controllers react (landing), queued craters are cut, the stuck guard checks every body.
6. Free bodies get their summed force and impulse handed to the engine, which integrates them in its own step right after (its gravity and damping are off, so acceleration is still exactly force / mass). `FinishTick` clears the sums; anything added between ticks lands in the next one.

## The rules

**Bodies.** `BodyKind` is Vessel, Wreck, Crate, Player, Creature or Base; the rules are the same for all of them. `Motion.Kinematic` bodies are integrated by the core and swept by a mover (vessels fly by the handling model; characters walk); `Motion.Free` bodies are rigid bodies the engine tumbles and stacks, fed by the same force sums; `Motion.Fixed` bodies (bases) are never moved by anything: no gravity, wind or rescue, and a hit on one is a hit on something fixed to the planet (infinite mass).

**Wind and shelter.** Wind pushes players, creatures, crates, wrecks and hover, small and capital vessels alike, by ½ρ·CdA·|w|·w, so acceleration goes as 1/mass: a 200 t capital ship is pushed like any vessel, just less. Who is sheltered from weather is one rule, `PhysicsWorld.IsSheltered`, in the one force path: `AddForce` and `AddImpulse` drop any `ForceKind.Weather` push on a sheltered body, whichever provider sends it, and providers may ask first to skip the work. Sheltered are a base (structures in base mode never move in wind), a vessel parked on another structure, and a rider inside a structure's interior.

**Parking.** Any structure hosts any craft (`PhysicsWorld.CanHost`: a base or a vessel, whatever its size or mode). A vessel is parked on a host (`Body.ParkedOn`) when its landing gear is attached to it, it rests on it on its belly with nobody pushing the controls, or it is docked to it (`VesselController.Dock`). Parking is one way only: two craft never park on each other, and no chain loops back. Chains carry down: A parked on B parked on C all ride C, and a host carries the mass of everything parked on it, chains included (`Body.Carried`), which the handling model counts toward its thrust. Moving under its own power unparks a craft, and it takes what is parked on it along. Vessel controllers park and unpark themselves: settling onto a host's deck, placed down landed on one, lifting off. Parked, it feels no wind of its own. Landed (not simulating), it rides its host exactly: each tick `CarryParked` moves it by the host's whole motion over the tick, turning included, and gives it the host's velocity at that point, so it lifts off with the deck's motion. The host and what it carries ignore each other in the engine (a collision exception), so the deck's sweep never runs into what rides it. Landing on a moving deck counts its speed against the deck's, not the ground's.

**Damage.** One formula, `Impacts.Damage`: mass (t) × (closing speed − safe speed)² × 0.001, zero at or below the safe speed, never capped and never scaled down. The safe speed is 8 m/s for everything but players, who land on their legs: `contacts.playerSafeSpeed`, 15.5 m/s, so small hops, jetpack landings and drops up to about 12 m never hurt. Ramming uses the reduced mass and both sides take it (each with its own safe speed). Bodies differ only in that and in toughness, `Integrity` per tonne (hull 4, crate 4, player 0.6). For a 100 kg player, in free fall: 20 m costs 3% of their health, 30 m 13%, 50 m 42%, and about 80 m kills; air drag makes real falls a little gentler (a 30 m fall lands at about 22 m/s and costs 6%). A `DamageSink` can route damage into a game's own health; the only floor anywhere is the tornado fling's 15%, which the weather code passes to the player's health.

**Contacts.** Kinematic hulls slide and glance (restitution 0.2), lose 30% of their sliding speed on contacts that did damage, and count touching several terrain collision blocks in one move as one impact. Characters count a new contact's full closing speed, but a surface they were already touching only as far as the move stopped them (leaning on a steep slope is not a hit). Free bodies don't bounce, so their unexplained velocity change along the normal is their closing speed. Events below 2 m/s are dropped.

**Trees.** A vessel breaks a tree (`PhysicsWorld.ResolveContacts`, through the game's `IFloraField`: felled for good at once, as harvesting fells it) when the load on it is more than its trunk takes: `flora.strength` (200 kPa) × the trunk's cross-section. The load is the weight resting on it (the craft and all it carries, by the share of the contact under it) plus the blow into it (its momentum stopped over `flora.blowTime`, 0.1 s). So a 227 t frigate breaks anything thinner than 1.9 m by its weight alone, and a 20 t skiff rests on a 0.6 m trunk. The gear looks through a tree too thin for the craft's weight and the blow it is coming down with (`PhysicsWorld.Footing`), so gear and hull always agree; a craft woken off its landing by the world changing under it (a tree it stood on broken, the ground dug out) comes down again, its lift idle until a pilot asks for thrust, and one let go against what its hull came down on lands there; a hover craft with no gear can't stand on a crown, so its weight on one breaks it. Creatures and players never break trees; rock never gives way.

**Craters.** A ground hit faster than 25 m/s queues a crater of 2 × ∛(kinetic energy in MJ), capped at 6 m (terrain-digging's own crater cap, which also caps it at 900 cells) and skipped under 1 m. Craters are cut at the end of the tick, in order, through `ITerrainEditor.Crater`, which removes cells under the terrain's rules (only bedrock holds, right up to and under structures) and logs them; then everything nearby is woken and checked for being stuck.

**Structures as bodies.** A structure is one body, never one per block. Its mass and centre of mass come from its blocks. `PhysicsWorld.Sever` gives a cut-off section its own free body with exactly the parent's rigid motion (same origin, velocity and spin), so total mass and momentum are unchanged at the moment of severing; the parent's mass and centre of mass follow its remaining blocks. A vessel out of hull points loses power and becomes a free wreck (`MakeWreck`).

**Collider strategy: one body per structure, one compound shape, built per chunk.** `StructureCollision` merges each 16³ chunk's solid cells into as few boxes as a greedy sweep finds, plus one convex hull per straight run of sloped blocks. Each chunk's colliders carry a hash; an edit rebuilds only the dirty chunks, and only chunks whose hash changed swap their shape nodes (`StructureShapes`). Unchanged chunks keep their shape resources, so the engine reuses their built shapes and only re-links the body's compound. One body keeps a ship one sweep and one transform and makes a wreck one rigid body; per-chunk rebuilds keep a one-block edit on a big ship from rebuilding thousands of boxes, which building's prototype does. The same chunk colliders furnish the structure's cabin world.

**Riding moving structures: one mechanism, the cabin world.** A rider of a structure lives in that structure's cabin world (walkable-ship's proven approach), inside it or out on its hull alike: a never-drawn `SubViewport` with its own physics space and a motionless copy of all the structure's colliders, outer hull and interior. Position, velocity and walk frame are structure-local (`Body.Frame`), and the world pose is always structure × local, so however the ship flies, rolls or orbits, the character controller only ever sees a still ship. You can walk round the outside of a ship in flight or in space, and in and out of its door, without ever leaving its cabin world (`Riding`).
- *Joining.* A body in the zone's world that lands on a hull, or touches one in the air, joins that structure's cabin world (`Riding.EnterCabin`): world position, velocity (with the structure's spin at that point) and orientation are kept, so landing on a moving ship has no jolt. Leaning on a parked ship from the ground doesn't join it.
- *Inside* (an interior cell: covered, floored and walled on three sides; a doorway counts), down is the core's, or the zone's when the core is off. Jumping never lets go.
- *On the outer hull*, the rider lets go of the ship when it jumps (at once), or once it has been out of contact for `riding.letGoAfter` (0.4 s) or is more than `riding.letGoDistance` (1.5 m) off the hull, so bumps and edges never drop anyone; it can't rejoin the same ship for `rejoinAfter` (0.5 s). Letting go (`Riding.LeaveCabin`) puts it back in the zone's world with its world velocity, the ship's at that point plus its own, and in space it drifts away on that inertia.
- *Down on the outer hull* is the gravity the rider is in, as inside: the ship's core gravity, or the zone's (the planet's) when the core is off, and none in space, where they float. So a ship's walls and underside can't be walked on: walking into a wall taller than a step (`walking.stepHeight`, 0.6 m) stops you, walking off an edge drops you off it, and a lip up to a step tall is stepped over like anywhere else. A rider on the roof stays put whatever the ship does, held by its core gravity. Walking out of an airlock onto the hull changes nothing.
- *Mag boots* (`riding.magBoots`) are switched off. The code is kept behind the switch: on, down would be the face under the feet (followed at `upFollow`, pulling at `magBootGravity`), sharp outside edges would be walked round by a probe `edgeProbe` below the face, and with `insideCorners` walls taller than a step would be climbed. The camera eases its roll on any change of up (`viewEase`).
- *Weather* reaches riders on the outer hull, bridged into the cabin world: the wind acts against their velocity relative to the hull, not the ship's airspeed, and its push is turned into the ship's frame before it joins the force path. A storm pushes them and a tornado can tear them off; flying through still air does nothing. Riders inside feel no weather, and above the atmosphere nobody does.

Why one mechanism, and not the engine's moving-platform velocity for the outside: a platform lags a tick, doesn't turn the rider with the ship, loses floor contact when a ship accelerates downward faster than gravity, can't hold anyone to a ship in space, and would be a second mechanism beside the cabin. The cabin world is exact at any speed, deterministic, and the same inside and out. Its cost: a rider on the hull doesn't collide with the zone's world (terrain, other ships, crates), only with its own ship. The stuck guard checks hull riders against the terrain, so a ship flown through a hill can't bury anyone; it lets them go to open ground.

**Steps.** Every walking body, on terrain, a cabin floor or an outer hull and whichever way up is, steps up onto anything up to `walking.stepHeight` (0.6 m) tall. Blocked by a wall while walking, the controller tries the move again lifted by the step height (or what headroom there is), on by this tick's walk, and back down; if all three are clear and it comes down on a floor, it is up the step, still walking at the same speed. Anything taller stays a wall. The floor snap reaches a step deep; under gravity, walking off a square-edged step is a short drop (about 0.3 s), as off any step. The step itself is one helper, `Steps.Up` (`Engine/Steps.cs`), which any character controller can use: the planet surface's walker does.

**Stuck-safety everywhere.** `StuckGuard` checks every registered body every tick against every `IStuckField` (terrain's density field, each structure's blocks), and straight away after `NotifyChanged` (terrain and structure edits) and `NotifyZoneLoaded`. Inside for two ticks in a row means moved to the nearest open space, preferring up and preferring somewhere to stand: each field's own suggestion first, then a shared 1 m grid search up to 14 m away, then the top of the column. Structure bodies are checked at their centre of mass against terrain and lifted straight up. Riders are checked along their own up (in mag boots it may point sideways or down) against their own ship's blocks, and out on the hull against the terrain too; one found inside its own ship is moved within it and stays aboard, one found inside anything else lets go of the ship. A field decides what "inside" means: the sandbox's terrain field also counts falling off the edge of the world (below the top of the atmosphere; nothing falls in space) and is stricter for rigid boxes (centre below the surface), whose engine can't push them back out of a thin mesh shell.

**Vessels.** `VesselController` is ship-handling's vessel rules, ported: the flight rules (`IFlightRules`, e.g. the handling model) say what velocity and orientation the controls ask for, and the controller turns the difference into a thrust force on the force path, so wind and gravity add up the same way. **A flying vessel never orients itself:** in air and in space it keeps whatever attitude the pilot leaves it in. There is no horizon assist and no self-levelling anywhere in the flight path; assist only brakes (to a stop, or holding course on the autopilot and in cruise). Two things still tilt by themselves: hover vessels, which aren't flying vessels, follow the ground they ride on, and a landing ship settles onto its gear. Landing gear (`LandingGear`) is an attachment point, not a load-bearing part: it comes out by itself below the deploy height and speed and goes in again past them, with no button; each telescoping leg reaches down to its own ground; a foot on a surface attaches the craft there (to the ground, or parked on another structure). It carries no load and nothing counts legs or mass: what holds a craft up is its hull's collision, and once attached and settled, being landed. A craft attached by its gear or resting on its hull, slow and with hands off, settles and stops simulating.

**Hover vessels follow the ground.** `HoverController` is ship-handling's velocity servo with look-ahead, holding the engines at ride height, with two changes so a hover vessel never gets stuck in a hole:
- *Attitude.* It pitches and rolls to the ground's attitude, the plane through what its engine probes and look-ahead probes see (least squares), at `levelRate`, never past `maxSlope`. Its nose comes up as a rise arrives and goes down into a dip, so it drives down into and back out of dips, dug pits and craters. The look-ahead points keep a short gap from tipping it: a trench narrower than its footprint is still a dip its engines average over.
- *Slopes as grades.* Ground ahead is judged by the grade across the vessel's own length (the rise from the ground under it over the run to the ground ahead), not by the face at one point. Steeper than `maxSlope` (45°) is refused and it slides off ground that steep; anything gentler is climbed. A crater's lip or a dug pit's edge, short and steep, rises little over that run and is climbed; a cliff is not. The look-ahead's early lift for rocks and steps is capped at `maxLift` × ride height, so it never hops a cliff.
In the physics sandbox the rover drives at a crawl into a 3.2 m tool-dug pit and into the biggest crater a crash cuts (4.1 m deep), stops at the bottom and drives out over the far rim, nose about 25° down going in and up to 38° up coming out, and still stops at the foot of the 11 m bank. A landed ship stops simulating until thrust, a ram or a strong enough tornado wakes it; a parked ship knocked loose brakes back to a stop instead of holding course away.

**Water: a volume you go into.** A zone's `IWaterField` (`world.Water`) says where water is: a surface height over each column that holds it (the planet generator's sea level and rivers, the sandbox's lake), decided by the natural ground alone, so digging never moves water. Everything below the surface and above the ground is water, and every body in the zone's world goes into it; only hover probes see the surface itself (the `Water` layer). Each tick a body's `WaterState` is the share of its height (feet to top, upright) under the surface, and the water adds, through the one force path as `ForceKind.Water`:
- buoyancy, the displaced water's weight: water density / the body's density × its weight × that share, against gravity. Densities are data (`water.bodies` in physics.json, or a body's own `BodySpec.Density`): a crate lighter than water floats with that share under, a heavier one sinks to the bed;
- drag, ½ρ·(CdA × `dragScale`)·|v|·v plus `damping`, times the share, applied implicitly (the velocity goes to v / (1 + k·dt)), so a body can hit the water at any speed and is only ever slowed.

Vessels: one that holds itself up (it can lift off) holds its weight less its buoyancy, so it flies underwater exactly as in air, only crawling through the drag; one that can't (no power, too heavy) has its engines flat out already, and sinks or floats by its density (vessels 1600 kg/m³: they sink) until its legs, which see only solid things, settle it on the bed. Hover vessels ride the surface while their hover is powered, as they ride the ground. Walkers (`WalkController`) wade until `swimDepth` of them is under, then swim: Move strokes along the heading and Vertical goes up or down (Space and Ctrl), the strokes holding against weight, buoyancy and drag up to `swimGrip`; left alone, a swimmer drifts and floats up (players are 800 kg/m³, so they float with their head out). `WaterState.Underwater` (wholly under) is the vitals hook: the suit breathes its own oxygen. Being in water is never being stuck: the stuck guard asks only about solid matter.

Swimming's feel follows Empyrion's (used as a reference only): you float unless you dive, two keys take you down and up, and you swim at about 2 m/s, 3 sprinting ([Empyrion wiki: Movement and Navigation](https://empyrion.fandom.com/wiki/Guide/Movement_and_Navigation)).

## API in a few lines

```csharp
PhysicsWorld world = new(rules);                    // Zone, Terrain, Query, StuckFields, ForceProviders
Body b = world.Register(new BodySpec { Kind, Motion, Mass, DragArea, CenterOfMass, Shape, Position });
world.AddForce(b, force, ForceKind.Weather);        // the only way anything pushes a body
world.AddImpulse(b, impulse, ForceKind.Contact);    // AddImpulseAt also spins free bodies
float dmg = Impacts.Damage(massKg, closingSpeed, rules.Contacts);   // the one formula
world.Impact += i => ...;  world.Landed += ...;  world.LiftedOff += ...;
world.Cratered += c => ...; world.Severed += s => ...; world.Rescued += r => ...;
Body wreck = world.Sever(parent, parentMass, parentCom, wreckSpec);  // keeps mass and momentum
Riding.EnterCabin(rider, ship); Riding.LeaveCabin(rider);            // join a ship / let go
Riding.LetGo(rules.Riding, rider, jumped, touching, distance, dt);   // the outer hull's let-go rule
Riding.SetUp(walker, faceNormal);                                    // mag boots: a new down
world.NotifyChanged(region);  world.NotifyZoneLoaded();              // wake and stuck-check
world.IsSheltered(b);                                                // does weather reach it at all?
world.Park(vessel, host); world.Unpark(vessel); vessel.ParkedOn;     // on any other structure: no wind, rides it, its mass in host.Carried
vesselController.Dock(world, vessel, host);                          // docking clamps: frozen and parked
world.Water = myWaterField;                                          // IWaterField: seas, rivers, lakes
b.Water.Fraction; b.Water.Underwater;                                // how it sits in water (the vitals hook)
WaterForces.Buoyancy(...); WaterForces.Drag(...);                    // the formulas, for a game's own controller
```

## Known limits

- Kinematic hulls don't spin from hits (deliberate, as in ship-handling); free wrecks and crates do.
- Players and props don't block a hull's sweep, so a ship flown into a standing player pushes them aside without damage.
- Free bodies are integrated by Jolt; determinism is proven same-build, same-machine (two worlds side by side match bit for bit), not across machines.
- Riders on the outer hull collide only with their own ship (it is their cabin world), not with terrain, other ships or crates; the stuck guard keeps them out of terrain. Loose cargo in cabins isn't registered yet.
- The weather force module's tornado lift carries the weight of its own 9.8 m/s² constant, not the zone's gravity; they agree on every planet so far.
- Hover and gear numbers moved here from handling.json; ship-handling's own prototype still reads its copy and keeps its own hover controller, which levels over the ground's normals and refuses slopes over 30°.
- Pushed against a cliff, a hover vessel hangs a couple of metres up its foot (the look-ahead's lift) and wobbles a little as its look-ahead swings along the face.
- Only a landed (or docked) parked vessel is carried by its host. One resting on a moving deck while still simulating (the moments before it settles, or after a knock) moves itself, and a vessel lifting off a moving deck keeps the deck's velocity but its assist then brakes against the ground, not the deck.
- Water is still: no currents, waves or flow into dug holes. Buoyancy acts at the centre of mass and the submerged share is taken from a body's upright height, so a floating wreck doesn't right itself and a tumbling crate's share is approximate. Riders in a cabin world don't feel the water outside their ship.
