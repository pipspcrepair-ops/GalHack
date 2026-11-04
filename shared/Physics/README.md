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
  | 8 | `Water`: probe-only surfaces | ship-handling's Water was 4 |
  | 16 | `Player` | terrain-digging's Player was 4 |
  | 32 | `Props`: crates, creatures, loose cargo | terrain-digging's Props was 8 |

  Masks: `VesselMask` (World, Hull: players and props get out of a hull's way, so a crate on the deck never stops a lift-off), `WreckMask`, `PlayerMask`, `PropMask`, `ProbeMask` (World, Hull, Water), `CabinMask`.
- **Gravity:** one `ZoneGravity` per zone (uniform, nothing above the atmosphere ceiling) and one `CoreGravity` per structure core: on by default, toggleable, costs no power, pulls toward the structure's own floor, and only acts on bodies inside its cabin world. Riders on an outer hull feel the zone's gravity.

## One tick

`PhysicsHost.Step` runs the world in a fixed order, bodies always in registration order, so the same inputs give bit-identical trajectories:

1. `BeginTick`; free bodies sync from the engine (and report impacts: velocity changes their forces don't explain, along the contact normal).
2. `ApplyForces`: each body's own drive (thrust, walking), then gravity from its zone or core, then the force providers (weather), then reactors (legs gripping against the wind, a landed ship knocked loose by a tornado).
3. `Integrate`: kinematic bodies get `v += ΣF/m·dt + ΣJ/m`.
4. Kinematic bodies move with collisions: hulls first, then creatures, then characters (a rider's hull has already moved). Contacts go to `ResolveContacts`.
5. `EndTick`: controllers react (landing), queued craters are cut, the stuck guard checks every body.
6. Free bodies get their summed force and impulse handed to the engine, which integrates them in its own step right after (its gravity and damping are off, so acceleration is still exactly force / mass). `FinishTick` clears the sums; anything added between ticks lands in the next one.

## The rules

**Bodies.** `BodyKind` is Vessel, Wreck, Crate, Player or Creature; the rules are the same for all of them. `Motion.Kinematic` bodies are integrated by the core and swept by a mover (vessels fly by the handling model; characters walk); `Motion.Free` bodies are rigid bodies the engine tumbles and stacks, fed by the same force sums.

**Damage.** One formula, `Impacts.Damage`: mass (t) × (closing speed − 8)² × 0.001, zero at or below 8 m/s, never capped and never scaled down. Ramming uses the reduced mass and both sides take it. Bodies differ only in toughness, `Integrity` per tonne (hull 4, crate 4, player 0.5: a 30 m fall costs about half a player's health). A `DamageSink` can route damage into a game's own health; the only floor anywhere is the tornado fling's 15%, which the weather code passes to the player's health.

**Contacts.** Kinematic hulls slide and glance (restitution 0.2), lose 30% of their sliding speed on contacts that did damage, and count touching several terrain collision blocks in one move as one impact. Characters count a new contact's full closing speed, but a surface they were already touching only as far as the move stopped them (leaning on a steep slope is not a hit). Free bodies don't bounce, so their unexplained velocity change along the normal is their closing speed. Events below 2 m/s are dropped.

**Craters.** A ground hit faster than 25 m/s queues a crater of 2 × ∛(kinetic energy in MJ), capped at 6 m (terrain-digging's own crater cap, which also caps it at 900 cells) and skipped under 1 m. Craters are cut at the end of the tick, in order, through `ITerrainEditor.Crater`, which removes cells under the terrain's rules (bedrock, anchors) and logs them; then everything nearby is woken and checked for being stuck.

**Structures as bodies.** A structure is one body, never one per block. Its mass and centre of mass come from its blocks. `PhysicsWorld.Sever` gives a cut-off section its own free body with exactly the parent's rigid motion (same origin, velocity and spin), so total mass and momentum are unchanged at the moment of severing; the parent's mass and centre of mass follow its remaining blocks. A vessel out of hull points loses power and becomes a free wreck (`MakeWreck`).

**Collider strategy: one body per structure, one compound shape, built per chunk.** `StructureCollision` merges each 16³ chunk's solid cells into as few boxes as a greedy sweep finds, plus one convex hull per straight run of sloped blocks. Each chunk's colliders carry a hash; an edit rebuilds only the dirty chunks, and only chunks whose hash changed swap their shape nodes (`StructureShapes`). Unchanged chunks keep their shape resources, so the engine reuses their built shapes and only re-links the body's compound. One body keeps a ship one sweep and one transform and makes a wreck one rigid body; per-chunk rebuilds keep a one-block edit on a big ship from rebuilding thousands of boxes, which building's prototype does. The same chunk colliders furnish the structure's cabin world.

**Riding moving structures: one approach for hulls and cabins.** A rider keeps its state in the structure's frame, and its world pose is always structure × local (`Riding`):
- *Inside*, the rider walks in the structure's cabin world (walkable-ship's proven approach): a never-drawn `SubViewport` with its own physics space and a motionless copy of the structure's colliders. Position and velocity are structure-local, gravity is the core's, and however the ship rolls the character controller only ever sees a still room.
- *On the outer hull*, the rider stays in the main world (terrain, other ships and weather still touch it) and, before it moves each tick, is carried by exactly the hull's motion since the last tick: position and heading, spin included. Its velocity is kept relative to the hull. The engine's own platform velocity is switched off on characters so nothing carries them twice.
- Stepping on or off either one is the same conversion: position, velocity (with the structure's spin at that point) and view direction move between frames.

Why not the engine's moving-platform velocity for the hull: it lags a tick, doesn't turn the rider with the ship, loses floor contact when a ship accelerates downward faster than gravity, and would be a second mechanism beside the cabin, which must never see motion at all. Explicit carry is exact at any speed and deterministic. Riders on a hull feel weather against their velocity relative to the hull (their boots hold them; a tornado still lifts them off a parked ship).

**Stuck-safety everywhere.** `StuckGuard` checks every registered body every tick against every `IStuckField` (terrain's density field, each structure's blocks), and straight away after `NotifyChanged` (terrain and structure edits) and `NotifyZoneLoaded`. Inside for two ticks in a row means moved to the nearest open space, preferring up and preferring somewhere to stand: each field's own suggestion first, then a shared 1 m grid search up to 14 m away, then the top of the column. Structure bodies are checked at their centre of mass against terrain and lifted straight up. A field decides what "inside" means: the sandbox's terrain field also counts falling off the edge of the world and is stricter for rigid boxes (centre below the surface), whose engine can't push them back out of a thin mesh shell.

**Vessels.** `VesselController` is ship-handling's vessel rules, ported: the flight rules (`IFlightRules`, e.g. the handling model) say what velocity and orientation the controls ask for, and the controller turns the difference into a thrust force on the force path, so wind and gravity add up the same way. Hover engines (`HoverController`, velocity servo with look-ahead) and landing gear (`LandingGear`, self-deploying, stiff probe suspension) are ported unchanged, except that a landed ship now tilts to sit on its feet on uneven ground (at most 12°). A landed ship stops simulating until thrust, a ram or a strong enough tornado wakes it; a parked ship knocked loose brakes back to a stop instead of holding course away.

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
Riding.EnterCabin(rider, ship); Riding.LeaveCabin(rider);            // cabin world
Riding.StandOn(rider, ship);    Riding.StepOff(rider);               // outer hull
world.NotifyChanged(region);  world.NotifyZoneLoaded();              // wake and stuck-check
```

## Known limits

- Kinematic hulls don't spin from hits (deliberate, as in ship-handling); free wrecks and crates do.
- Players and props don't block a hull's sweep, so a ship flown into a standing player pushes them aside without damage.
- Free bodies are integrated by Jolt; determinism is proven same-build, same-machine (two worlds side by side match bit for bit), not across machines.
- Bodies inside a cabin world are left to it: the stuck guard doesn't check them, and loose cargo in cabins isn't registered yet.
- The weather force module's tornado lift carries the weight of its own 9.8 m/s² constant, not the zone's gravity; they agree on every planet so far.
- Hover and gear numbers moved here from handling.json; ship-handling's own prototype still reads its copy.
