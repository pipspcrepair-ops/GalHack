# Alien peoples

Status: a plan. No game code was written for it.

Before the collapse the galaxy was mixed-species. Survivors of many peoples live everywhere, often side by side. The owner's brief: keep the human body shape and attach parts to it. Tails, snouts, antennae, beaks, shades of skin, scales, feathers, fur. Keep it simple.

**Where I read.** The Bestiary is not in main yet. I read `shared/Bestiary` from the `proto/batch2` worktree (`G:\GalHack-wt\batch`), and the Husk redesign from `proto/husks` (`3dd6040`). File names below are under `shared/Bestiary`. Design intent comes from `docs/DESIGN.md`, `docs/playtest-list.md` and `docs/body-language-and-sound.md`.

**Words.** I say *folk* for one rolled people (the code's `Folk` class). A *part* is one thing attached at a socket. A *person* is still a `Species` of one, with a `Figure`.

**Rules this plan obeys** (the owner's):

- Nothing bespoke. A folk is parts and settings rolled from a world's founders. No hand-made model, no named species in code.
- No external assets. Every part is primitives: spheres, tapers, cones, boxes, lofts. Faces stay built from spheres, which the owner likes.
- Roles, gestures, silhouettes and contrast stay readable. Human groups cluster, so alien peoples cluster the same way.
- Villains look like their people. Only decoration that changes the silhouette sets them apart. Husks still read as Husks.
- One skinned mesh per person. 20 people in view in 2 ms or less.
- No written logs and no dialogue. Names come from `NameGen`, voices are wordless.
- No folk looks "evil". A tusk or a beak says where a people lives, not how it behaves. Hostility reads from raider decoration, posture and gesture, as for humans.

I assumed the player's own body stays human. That is outside this note.

## 1. The approach

### 1.1 The skeleton stays

A person is twelve bones and a prop: `body`, `head`, `arm0u`, `arm0l`, `arm1u`, `arm1l`, `leg0u`, `leg0l`, `leg0f`, `leg1u`, `leg1l`, `leg1f`. `HumanRig` poses them with two-bone IK. Gestures are hand targets measured from the shoulder (`ActHand`). Nothing in a gesture knows what a hand or a head looks like.

So an alien person keeps all twelve bones, the same IK and the same gesture table. A folk changes only three things:

1. **Numbers.** The `Figure` lengths and widths, inside wider bounds (section 4).
2. **Parts.** Extra primitives added to the existing pieces (sections 2 and 3).
3. **Colour and material.** Vertex colours, plus a per-folk pattern (section 3).

New bones are limited to a tail chain. Later, perhaps a neck bone and two or three display bones (section 8).

**No extra full arms or legs.** Two hands do the talking. Four arms would break the prop slots, the IK count and every gesture. A withered spare limb already exists in the mutation layer, and it stays a mutation.

### 1.2 Parts at sockets

A *socket* is a place on an existing piece. All part sizes use the same units the human code uses: `R` (head radius) for head parts, `u` (height divided by 1.72) for body parts. A part then scales with every body for free.

| Socket | Piece | Where (head space: origin at the neck base, -Z is forward, skull centre `c` = neck + 0.92R up) |
|---|---|---|
| crown | head | top of the skull, (0, +1.0R, 0) from `c` |
| brow | head | (±0.36R, +0.3R, -0.8R) |
| eyes | head | (±0.36R, +0.14R, -0.82R) |
| nose / muzzle root | head | (0, -0.1R, -0.95R) |
| mouth | head | (0, -0.6R, -0.84R) |
| jaw and chin | head | jaw sphere at (0, -0.56R, -0.2R) |
| ear | head | (±0.86R, +0.02R, +0.08R) |
| nape | head | (0, +0.2R, +0.95R) |
| neck | head | the neck taper is part of the head piece |
| shoulder | `arm0u`, `arm1u` | the piece origin |
| back | body | behind the chest, at `ChestD` |
| tail root | body (new bones) | back of the hips |
| forearm, hand | `arm0l`, `arm1l` | along +Y to the hand sphere |
| thigh, shin, foot | `leg*u`, `leg*l`, `leg*f` | along +Y; the foot is posed at the ankle |

A part is data plus one builder function that adds prims to a piece. Its record: the **socket**; its **extents** (so headgear and packs can fit round it); the garment slots it **claims** and what it **hides** (section 6); its **openings** for Husk worms (section 9); a **far stub** of one or two prims (beyond 42 m only `HumanBody.Far` is drawn, so a tail, tall crest or back mass needs one); its **points** (1.5); and what it **excludes**.

Parts merge into the single mesh because they are just prims in the existing pieces. `MeshBuilder.Skinning` already binds every vertex whole to its piece's bone, then `PersonView` draws one skinned mesh. Nothing about that changes.

**Reuse.** The animal part library already holds horns (pair, ram, antlers, spike, brow, tusks, antennae, frill, crown), backs (plates, shell, spines, ridge, sail, crystals, bubbles, fronds), tail tips (quills, club, fin, stinger, spiked), a beak, mandibles and a face plate (`Parts.cs`). Those take a `Paint` and a `HeadShape`. For people they need a small adapter, not a rewrite. The animal heads (maw, hammer, lantern) are too bestial. Skip them.

### 1.3 How a species is rolled

Today `PeopleGenerator.RollFolk` makes one `Folk` per world. It is non-human with chance 0.12 + 0.55 × Alienness. Its colour, build and dark vision come from the world's climate. `FoundersOf(place)` then gives each place a lean in skin, brow, jaw, legs and build. The person rolls inside that lean, and a few per cent are outsiders.

The plan keeps that shape and adds parts to it. Four levels, each with its own random stream:

| Level | Rolled from | Decides |
|---|---|---|
| **Region roster** | the galaxy region and a coarse cell | 5 to 8 recipes that recur, so a trader from the next world looks like the people there |
| **World's folk** | world seed, Alienness, climate | 1 to 3 recipes from the roster, plus rarely a local one |
| **Place founders** | world seed and place | a lean on the folk's settings: hue, size, pattern, fashion |
| **Person** | person seed | jitter, sex, age, outsider or not |

A recipe holds a **frame** (height, limb, neck, head, hand and foot ratios, build bias, stoop, tail length), a **covering** (skin, fur, feathers, scales or plates, with a pattern and its scale), a **palette** (base hue family, accent, point colour, glow colour), **one part or none per socket** with a size range, which parts and colours differ by **sex and age**, and the **points** spent. Climate steers the recipe (section 5).

### 1.4 Clustering and mixing

Humans cluster by region. Skin, brow and build lean by place, with individuals scattered round the lean. Aliens cluster the same way, by the same mechanism:

- The **folk** fixes which parts exist. Everyone in it has the same parts.
- The **place** leans the settings: hue ±0.03, size ±15%, pattern scale, crest dye, fur length. Neighbouring places of one world lean alike more often than not (the existing `lean`).
- The **person** jitters inside that, by sex and age.
- **Outsiders** roll from the whole folk range, or from another folk of the roster, at the current rates: 3 to 12% in a place, 25 to 45% at crossroads and spaceports.

**Mixed places.** A place has one primary folk, 70 to 100% of its people. If the world has a second folk, about a third of places are mixed, with 15 to 30% of the second. Spaceports and markets pull from the region roster. This gives "side by side" without any new rule for settlements.

**No hybrids.** One person is one folk. Half-beak half-snout people would blur the clusters and the silhouettes. The only blending is inside a folk: colour and size.

**Code size.** `PeopleGenerator` holds one `Folk`. It needs a list with weights, and `Person` needs to be told which folk. Small to medium. `Town.cs` already passes founders.

### 1.5 Budget and combination rules

Every part carries *points* for how much it changes the outline:

| Points | Meaning | Examples |
|---|---|---|
| 0 | colour, size or pattern only | skin family, eye colour, pupils |
| 1 | a small change | ears, tusk stubs, claws, webbing, a small crest |
| 2 | a clear change | horns, a tail, a snout or beak, back spines, stalk eyes |
| 3 | a big change | shell, wings, sail, very long neck, digitigrade with a tail |

A folk's budget is about 3 + 7 × Alienness, with a little noise. At Alienness 0.25 that is 4 to 5 points: ears and a tail, or a beak and a crest. At 0.9 it is 9 or 10: two 3-point parts and a few small ones. Colours and patterns are free, so even the nearly human ones can look distinct.

Rules that keep the results believable:

- **One mouth part:** muzzle, beak, mandibles or face plate. Tusks go with a muzzle or a short blunt face, not with a beak.
- **One eye plan:** two eyes (resized or moved), three, four, stalks, or compound.
- **One crown part:** horns, crest, antennae or plume. One small extra (ears, ruff) is fine.
- **One back part:** shell, wings, sail, plates or spines.
- **One covering family:** fur, feathers, scales or plates. Smooth skin is the default.
- **At most two 3-point parts.**
- **Cold-blooded looks stay off ice.** Scaled, plated or gilled folk are never native to Frozen worlds or most Cold ones. The same rule as wildlife. They may be outsiders in furs or suits there.
- **Parts are tidy and symmetrical.** Mutation is lumpy and lopsided. That is how a player tells a native from a mutant.

### 1.6 Reading at 30 m, and cost

`docs/body-language-and-sound.md` gives the figure: a 1.75 m person is about 60 pixels tall at 25 m. That is roughly 34 pixels per metre, or about 29 at 30 m. So:

- A 12 cm feature is about 3 pixels. A 20 cm feature is about 6.
- A head is 6 or 7 pixels across. **Face parts do not read at 30 m.** Snouts, eyes and beaks matter inside 8 to 10 m, where you trade and scan.
- What reads at 30 m is outline: height, width, a tail swinging, a crest or horns above the head, big ears, a back mass, a hunch, long legs.

**Silhouette rule.** Every folk needs at least two "big outline" features of 15 cm or more, or a proportion shift of 8% or more. Choose from: tail, tall crown part, wide ears or frill, back part, leg or neck length, stoop.

**Generation check.** Give each folk a silhouette code (crown, back, tail, height class, width class). In one world, no two folk may share more than two of the five. In tests, render side and front silhouettes at 30 m scale and compare. The slice already stages `--view=folk` and `--view=crowd`.

**Cost.** My count from the mesh builder, not measured. A human with clothes is about 3,000 vertices.

| Primitive | Vertices |
|---|---|
| Cone, 5 sides | 19 |
| Box | 24 |
| Sphere, 5 or 6 sides | 48 to 56 |
| Taper, 5 or 6 sides | 60 to 70 |
| Loft, 10 stations × 11 sides | about 110 |

A rich species adds 1,500 to 3,000 vertices. Twenty of them are about 120,000 vertices in the near mesh, which is small. The cost to watch is build time on the worker thread, and spending spheres where a cone would do. House rules:

- Cones and boxes for spikes, quills, feathers and rays. Spheres only for bulk.
- Sides of 5 or 6 on every small part.
- At most +2,500 vertices and +4 bones per person. Measure with the crowd view, which the batch notes put at about 0.3 ms for 20 people.
- Far stubs: at most 8 prims.

## 2. Part lists, by socket

**Work scale.**

- **Easy:** static prims on an existing piece, no rig change, no new outfit rule, about 12 prims or fewer.
- **Medium:** a new bone or shader class, an outfit rule, a far stub, or up to about 40 prims.
- **Hard:** changes how the rig solves, or breaks a gesture unless reworked.

"pts" are the silhouette points from 1.5.

### 2.1 Head shape

The skull is one sphere, R × (1.68, 1.95, 2.0), plus a jaw sphere. A head shape changes those two and adds at most two spheres.

| Part | Built from | Work |
|---|---|---|
| Round (human today) | skull and jaw as they are | Easy, 0 pts |
| Long, swept back | skull depth 2.0 to 2.6R, centre moved back 0.2R | Easy, 1 pt |
| Tall dome | skull height 1.95 to 2.5R, a forehead sphere forward | Easy, 1 pt |
| Broad | skull width 1.68 to 2.2R, eyes and ears pushed out | Easy, 1 pt |
| Flat-faced | skull depth 1.7R, jaw pulled back, brow forward | Easy, 0 pts |
| Wedge, or small skull with heavy jaw | jaw sphere narrowed to 0.7 with two cheek spheres; or skull ×0.85 and jaw ×1.4 | Easy, 1 pt |
| Smooth dome | skull only, slit mouth, band eyes, no nose or ears | Medium, 2 pts |

Keep a mouth and eyes on the smooth dome. A face with neither reads as a mask, and a mask reads as faceless horror. Wide heads need wider headgear (section 6).

### 2.2 Face: snout, muzzle, beak, mandibles, tusks, whiskers, face plate

| Part | Built from | Work |
|---|---|---|
| Short muzzle (cat, ape) | one ellipsoid pad (0.7R wide, 0.45R tall, 0.6R long) and a nose ball | Easy, 1 pt |
| Long muzzle (hound, deer) | ellipsoid 0.5R wide, 1.1 to 1.6R long, a lower-jaw ellipsoid, a nose pad; the jaw sphere is the hinge | Easy, 2 pts |
| Hooked beak | two horn chains: upper (4 points, hooked), lower (3 points, shorter) | Easy, 2 pts |
| Straight beak (heron), or blunt short beak | two thin tapers 1.2 to 2R, or a thick hooked cone pair 0.6R | Easy, 1 to 2 pts |
| Mandibles | two curved horn chains at the mouth corners, two palp tapers | Easy, 2 pts |
| Tusks | two upturned horn chains from the jaw, 0.3 to 0.8R | Easy, 1 pt |
| Whiskers | 3 to 6 thin tapers (4 sides) per side, fanned from the muzzle | Easy, 0 pts |
| Face plate | a thin carapace loft over brow, cheeks and nose, with holes for the eyes | Medium, 2 pts |
| Noseless | omit the nose taper, add two dark nostril slits | Easy, 0 pts |
| Star nose | 7 to 9 small cones radiating from the nose tip | Easy, 1 pt |
| Trunk nose | a 3 or 4 taper chain hanging from the nose, with sway | Medium, 2 pts |
| Wide slit mouth, jowls | a long dark box across the lower face and a lip ridge; or a sphere at each cheek | Easy, 0 to 1 pt |

Keep the owner's face style: flat colour, spheres for bulges, a darker socket sphere round each eye, no textures. The creepy simplicity stays. The mouth slit stays a dark box.

### 2.3 Eyes: count, size, placement, stalks, compound

Eyes matter most at talking range. They cost almost nothing.

| Part | Built from | Work |
|---|---|---|
| Two eyes, resized | 0.7× (small, deep-set), 1.3×, up to 1.8× (night eyes) | Easy, 0 pts |
| Three, four or a cluster | a third sphere on the brow; two stacked pairs; or 5 or 6 small spheres each side | Easy, 0 pts |
| Side-set | x = 0.62R, z = -0.5R, so a bird's wide view | Easy, 0 pts |
| Top-set | high on the skull, a swamp ambusher | Easy, 0 pts |
| Slit or round pupil | one flat dark ellipsoid on the eye; reads inside 8 m only | Easy, 0 pts |
| Solid colour, no white | eye colour from the folk: amber, gold, green, red, black | Easy, 0 pts |
| Eye stalks | taper (4 sides) 0.8 to 1.5R long from the brow, ball tip | Medium, 2 pts |
| Compound eyes | large sphere with 6 sides, so the facets show, one each side | Easy, 1 pt |
| Visor band | one long thin dark ellipsoid across the eye line, 2 or 3 bright dots | Easy, 1 pt |
| Third-eye gem, eye spots | one glowing sphere at the brow; or flat false-eye discs at the nape | Easy, 0 pts |

Eye shine already exists: `Folk.EyeShine` and `DarkVision` drive a glow on the species colours. Give each folk its own shine hue.

### 2.4 Ears and frills

| Part | Built from | Work |
|---|---|---|
| Round, small | one flattened sphere each side | Easy, 0 pts |
| Pointed, leaf | tapered ellipsoid swept up and back, 0.7 to 1.8× | Easy, 1 pt |
| Tall (hare) | thin ellipsoid 2 to 3R tall | Easy, 2 pts |
| Fan (fennec) | one big thin ellipsoid, 1.5R wide | Easy, 1 to 2 pts |
| Drooping (hound) | flat ellipsoid hanging from the temple | Easy, 0 pts |
| Tufted tip | ear plus three cones at its tip | Easy, 1 pt |
| Fin ears | flat ellipsoid with 3 to 5 ray tapers along it | Easy, 1 pt |
| Ear holes only | a dark socket sphere, no ear | Easy, 0 pts |
| Cheek frills, crown frill | two ray-ribbed discs from the jaw line; or one big disc behind the head (`Parts.Horns "frill"`) | Medium, 1 to 2 pts |

The first dark socket and ear sphere already exist (`HumanBody.Head`), so most of these are a change of size, shape and colour. A frill that flares on a threat needs a display bone (section 8).

### 2.5 Crests, horns, antennae

Biggest silhouette gain on the head, and the raiders' hook.

| Part | Built from | Work |
|---|---|---|
| Horn pair (swept back) | `Parts.Horns "pair"`, scaled to R | Easy, 2 pts |
| Ram curl | `"ram"`: a 7-point chain each side | Easy, 2 pts |
| Straight spears | two long tapers, 1.5 to 3R | Easy, 2 pts |
| Brow horns | `"brow"`: sweep forward over the face | Easy, 2 pts |
| Knobs, stubs, nose horn | two or three short cones; or one on the nose bridge | Easy, 1 pt |
| Antlers | `"antlers"`: 3 or 4 tines per side | Easy, 2 pts |
| Crown of spikes | `"crown"`: a ring of 7 | Easy, 2 pts |
| Median ridge or keel | one thin tall ellipsoid along the skull midline, brow to nape | Easy, 2 pts |
| Comb or casque crest | a domed carapace shape, soft or bony | Easy, 2 pts |
| Spine mohawk | a row of 5 to 9 cones from brow to nape | Easy, 2 pts |
| Plume | a fan of 5 to 9 thin ellipsoids or 4-sided cones | Easy, 2 pts |
| Sail crest | a big ray-ribbed disc standing on the skull | Medium, 3 pts |
| Whip antennae (clubbed or forked) | `"antennae"`: one thin chain each side, with a ball or a Y at the tip | Easy, 2 pts |
| Plumed antennae | a stem with 6 to 10 small leaf ellipsoids | Medium, 2 pts |

Sex and age: antlers and horns larger on one sex, buds on children, tips lopped on elders. Use the `Fit.Dimorph` idea of one sex carrying the horns or the colour.

### 2.6 Hair, quills, fronds

Today: short, long, tied, crop, bald, and a made mohawk for raiders (`HumanBody.Hair`).

| Part | Built from | Work |
|---|---|---|
| Human hair styles, or hairless | as they are, in folk colours (slate, russet, moss, white, rust); `Folk.Hairless` exists | Easy, 0 pts |
| Mane | 7 to 10 cones or leaf ellipsoids from brow, over the nape and down the neck | Easy, 1 pt |
| Quills, bristle brush | rows of stiff cones (5 sides) on crown, nape and neck; or a dense cap of short ones | Easy, 1 pt |
| Fur ruff | 6 to 10 cones lying back at cheeks and neck | Easy, 1 pt |
| Fronds | flat leaf ellipsoids hanging from crown or nape, with sway | Medium, 2 pts |
| Tendril locks | 6 to 12 hanging tapers (4 sides), with sway | Medium, 1 pt |

"Sway" is the vertex-shader trick the Husk branch added for worms (`Mat.Worm`: UV.x carries the item and how far along it is). Generalise it to a `Mat.Sway` class and fronds, locks, antennae and tail tufts move with no bones. See section 8.

### 2.7 Neck

The neck taper lives in the head piece, and the head turns at the neck base.

| Part | Built from | Work |
|---|---|---|
| Short, bull or no neck | `NeckK` 0.4, head on the shoulders; or neck radius ×1.5 | Easy, 0 to 1 pt |
| Long neck, up to 3.5× | `NeckK` up to 3.5 (about 25 cm); rigid, turns as a stick | Easy, 2 pts |
| Very long, S-curved | two neck bones, the head leaning forward | Medium, 3 pts |
| Frilled collar | a ring of ray-ribbed discs at the neck base | Easy, 2 pts |
| Gill slits | 3 or 4 dark slits each side of the neck, small pale fronds | Easy, 0 pts |
| Throat pouch | a sphere under the chin | Easy, 1 pt |
| Neck ruff, collar markings | a cone ring of fur or feathers; or the neck taper split in two colours | Easy, 0 to 1 pt |
| Cowl | a hood-shaped disc from the nape that flares | Medium, 2 pts |

Beyond 3.5× a rigid neck looks like a pole turning. That is when to add a neck bone.

### 2.8 Shoulders and back

| Part | Built from | Work |
|---|---|---|
| Dorsal spines | `Parts.Back "spines"`: 7 to 9 tapers down the back | Easy, 2 pts |
| Ridge scutes | `"ridge"`: two staggered rows of small plates | Easy, 1 pt |
| Plates | `"plates"`: 5 to 9 overlapping flattened ellipsoids | Easy, 2 pts |
| Shoulder pauldrons | a bony sphere on each upper arm | Easy, 1 pt |
| Mantle | a fur or feather cape of cones over shoulders and nape | Easy, 1 pt |
| Hump | one large sphere; `Monstrous` already adds one | Easy, 1 pt |
| Vestigial wings | two folded wings along the back, 0.3 to 0.5 m: two tapers and a flat fan each | Medium, 2 pts |
| Wing-cloak | big folded wings to the hips, 0.8 to 1 m; feather rows | Medium, 3 pts |
| Shell | `"shell"`: a loft over the back with a rim and ridges | Medium, 3 pts |
| Sail | `"sail"`: a big ray-ribbed disc down the spine | Medium, 3 pts |
| Crystals, vents, air sacs | `"crystals"`, `"vents"`, `"bubbles"`: alien flavour | Easy, 1 pt |

The wings do not lift anyone. Flight is out of scope. The back parts need an adapter, because the animal ones read the body loft's `Top` and `HalfWidth`, and the person torso is two lofts turned upright. Back parts claim the back slot (section 6).

### 2.9 Arms and hands

The hand is one ellipsoid, 0.062 × 0.11 × 0.045 u. Fingers show inside 8 m only, so they are cheap detail.

| Part | Built from | Work |
|---|---|---|
| Finger count 3, 4, 5, 6 | 3 to 6 thin tapers (4 sides) on the hand sphere | Easy, 0 pts |
| Long fingers, or a mitten | tapers ×1.6; or no digits and the hand ×1.3 | Easy, 0 pts |
| Claws | a cone (5 sides) on each finger tip, or one big thumb-claw horn chain | Easy, 1 pt |
| Webbing | one or two flat ellipsoids between fingers | Easy, 0 pts |
| Forearm fins | a ray-ribbed flat ellipsoid on the outer edge of the forearm | Easy, 1 pt |
| Arm feather fringe | 4 to 8 flat ellipsoids along the forearm | Medium, 1 pt |
| Chitin gauntlet, elbow spur | forearm taper in `Mat.Carapace` with plates; a cone at the elbow | Easy, 1 pt |

Forearm fins and fringes widen the outline in `HandsUp`, `Celebrate`, `Taunt` and `Startle`, which is a gesture bonus at 30 m. The mitten stays the grip for props.

### 2.10 Legs and feet

| Part | Built from | Work |
|---|---|---|
| Plantigrade (human today) | as it is | Easy, 0 pts |
| Digitigrade | `Ankle` raised to 0.10 to 0.13H, lower leg built with a backward bulge (a hock), a short foot on its toes | Medium, 2 pts |
| Hooves, single or cloven | wedge of one or two half-spheres, dark; goes with digitigrade | Easy, 1 pt |
| Talons | 3 forward tapers, 1 back, claw tips | Easy, 1 pt |
| Paws | a round pad with 3 toe bumps | Easy, 0 pts |
| Webbed or flipper feet | `FootK` 1.5, flat paddle ellipsoid | Easy, 1 pt |
| Pillar legs, leg spur | radii ×1.3 and short feet; a cone at the back of the ankle | Easy, 1 pt |
| Thigh feathers or fur | cones or flat ellipsoids on the upper leg | Medium, 1 pt |

**Digitigrade is a cheat, not a new rig.** `Ankle` is the foot's height above the ground (`foot.Y = ground + Ankle`), and the IK only knows two bones. Raise the ankle, build the lower leg with a kink, and the stance reads as a reversed knee. The IK, the planting and the gait are untouched. A true three-bone leg is a large change. Don't do it.

### 2.11 Tails

Tails give the most outline for the least anything else. They need bones.

| Part | Built from | Work |
|---|---|---|
| None (human) | nothing | Easy, 0 pts |
| Stub | one sphere or short taper on the body piece, no bones | Easy, 1 pt |
| Thin whip or bare rat tail | 4 bones, tapering to a point | Medium, 2 pts |
| Thin tufted | the whip, plus a tuft of 6 to 8 cones | Medium, 2 pts |
| Thick (lizard) | 4 bones from 6 to 9 cm radius to a point; leans the torso forward | Medium, 2 pts |
| Bushy | thick tail, with a cloud of cones (fur look) | Medium, 2 pts |
| Fan | 7 to 11 flat plates in a spread on the last bone | Medium, 2 pts |
| Paddle | a flat ellipsoid on the last two bones, vertical or flat | Medium, 2 pts |
| Club, spiked, quills, fin, stinger | `Parts.TailTip` on the last bone | Easy once tails exist, 1 pt |
| Raised arc, curling | the rig holds the tail up over the back; or curls the last two bones when relaxed | Medium, 1 to 2 pts |

**Tail rig.** `LeggedRig.PoseTails` already does this for animals: a chain with sway from the stride and lag from turning. For people:

- 3 or 4 bones named `tail0` to `tail3`, found by name in the `HumanRig` constructor, like the others.
- Body-space root at the back of the hips. Yaw sway from the stride cycle, pitch from posture.
- Never below the ground: `GroundAt` is already in `HumanRig`.
- Husk: slack, plus the same `jerk` term that throws the shoulders.
- Sit and Down: the tail lies along the seat or floor.

`PersonView` builds the skeleton from the piece list, so extra bones cost nothing there.

## 3. Skin and coverings

**What shows.** Clothes cover most of a person. In `HumanBody`:

- `shirt`, `wraps` and `rags` show forearms and hands.
- `rags` also shows shins and feet.
- `coat`, `furs`, `suit` and `hazard` show the head and neck only.

So a covering is built for the **exposed kit**: head, neck, hands, forearms, feet, ears, tail, and tufts at the collar, cuffs and hem. The bulk of the body does not need fur. It needs tufts at the places where cloth ends.

Vertex colours are baked per prim already. Vertex alpha is glow. `UV.y` is the material class (skin, horn, carapace, membrane, eye, and on the Husk branch, worm). `UV.x` is the pattern weight.

### 3.1 Skin shades

The human range stays: `Folk.SkinHue` 0.05 to 0.09, with its own saturation and value ranges. Alien folk pick from **pigment families**, so that odd hues stay believable. All are real animal colours.

| Family | Hue and feel | Real source |
|---|---|---|
| Browns, tans | the human range, plus russet and umber | melanin |
| Ash and slate | grey, blue-grey, charcoal, olive-grey | lizards, seals |
| Ochre and rust | yellow-brown to copper, brick | clay-dwelling reptiles, foxes |
| Moss and olive | dull green, yellow-green | frogs, geckos |
| Dusk blue and violet | deep blue-grey, mussel purple | shells, some birds |
| Bone and cream | near white, dusty pink, lavender-grey | albino and cave animals |
| Black | very dark, a hint of blue | melanistic coats |

Limits keep them plausible:

- Large areas: saturation 0.55 or less, value 0.2 to 0.85.
- Strong colour (neon, bright cyan, pink): at most 3% of the surface. Crest tips, eye rings, glow dots.
- The species accent stays clear of the job accent. Job accent hues are Worker 0.08 to 0.14, Trader 0.75 to 0.85, Guard 0.55 to 0.62, Farmer 0.12 to 0.17, Medic 0.98 to 1.0, Raider 0.0 to 0.03. The armband stays the role mark.
- **Contrast.** Run the folk's base colour through the existing `Apart` idea with a gap of 0.15 against the world's `Backdrop`. Pale folk on snow need **dark points** (hands, face mask, ears, tail tip). That reads and looks right.

Climate shifts value, as for humans: lighter in cold, darker in heat.

### 3.2 Patterns

The creature shader already has: plain, countershade, spots, stripes, bands, rings, mottle, veins. Today every person shares one material (`CreatureMaterials.Clothed`) with `pattern = 0`, and no person prim carries a pattern weight.

| Pattern | Technique | Work |
|---|---|---|
| Dark points | vertex colour on hands, feet, ears, nose, tail tip | none |
| Torso countershade | the torso lofts already blend two colours round the body (`Color2`, front to back), so a pale front and darker back costs nothing, but only where skin shows | none |
| Limb countershade | existing shader pattern uses piece-local up; use the world-up normal (`wn.y`) instead | small shader edit |
| Stripes, spots, bands, rings, mottle, veins | existing patterns, via a **per-folk material** and a pattern weight on skin prims | medium |
| Feather barring | new pattern: `tri(p.y * freq)` across the piece | small |
| Scale tile | new pattern: staggered cells, darker edges, with a small bump | small to medium |
| Bioluminescent dots | 6 to 12 small spheres (5 sides) with glow = 1 on cheeks, forearms, neck | easy |
| Translucent skin | `Mat.Membrane` (it has backlight), pale colour, vein pattern, faint glow | easy |
| Iridescent sheen | fresnel hue shift on `Mat.Carapace` | small shader edit |

One material per folk, not one shared material. `CreatureMaterials.For(Species)` already caches per species, so a `ForFolk` is the same idea. A world has one to three folk, so it is only a few materials. Draw calls stay one per person. Patterns are drawn in each piece's local space, so stripes ride along with a moving limb. That is right.

True see-through skin would need alpha, a second surface, and a second draw call. Don't. Fake it with pale membrane, veins and backlight.

### 3.3 Coverings

| Covering | Look | Technique | Work |
|---|---|---|---|
| Smooth skin | human default | vertex colour | none |
| Leathery, thick | rough hide | detail bump (exists) | none |
| Wet, slick | gloss | a skin gloss material class | small |
| Short fur | face and hands furred | colour, a soft rim, a few tufts at ears, tail and jaw | easy |
| Long fur, ruffs, mane | collar, cheek ruff, mane, cuffs | cones lying back, as `Coats.Shag`, 20 to 40 | medium |
| Down | soft clouds at neck, chest, thighs | small spheres, as `Coats.Fleece` | easy |
| Feathers | head crest, neck ruff, forearm fringe, thigh, tail | flat ellipsoids or 4-sided cones in rows, with barring | medium |
| Scales | lizard-like | scale-tile pattern, ridge scutes on brow and spine | easy to medium |
| Chitin plates | glossy, segmented | `Mat.Carapace` on skin prims, plates at shoulder, forearm, shin, brow; rings pattern for segments | medium |
| Translucent | glass-like | see above | easy |

Which need geometry: tufts, ruffs, mane, feathers, plates, scutes, dots. Which are shader or vertex colour only: all skin shades, points, countershade, stripes, spots, mottle, bands, scale tile, barring, sheen, translucent, wet, leathery.

`Coats.Decorate` returns early for `RigKind.Humanoid`, so people get no coats and no ears from the animal path today. The exposed-kit builders replace it for people.

**Age and sex.** Children: parts at half size, colours brighter. Elders: horn tips lopped, crest colour faded, fur and feathers greyer, as `Elder` hair goes grey now. Dimorphism follows `Folk.Dimorphism`: one sex bigger, or brighter in the crest.

## 4. Proportions

`Figure.Measure` already has a clamp for each ratio. Folk widen them. Hard rule: **two arms with hands, a head on a neck that turns, legs from the hips.** The ranges below are chosen so the gestures still read, using `HumanRig.Drive` and `ActHand`.

| Ratio | Today | Folk range | Where it stops reading |
|---|---|---|---|
| Height | adults 1.63 to 1.76 m, kin ×0.94 to 1.12 | ×0.78 to 1.22 (adults about 1.3 to 2.1 m) | Below 1.25 m an adult is mistaken for a child. Capped below the doorway height (owner decision, see Open questions 1) |
| Legs (hip height) | 0.525H × 0.88 to 1.10 | ×0.82 to 1.18 | The torso is 0.32H today. At 1.18 it is 0.22H, about 0.45 m on a 2 m person. Beyond that the chest reads as a ball |
| Arms | ×0.93 to 1.07 | ×0.75 to 1.35 | At 1.35 the hands hang at mid-thigh, above the knee. At 0.75 `HandsUp` tops out at about 1.03H, only just over a human head. Shorter than that the hands don't clear it. A tall crest or antlers hide the hands anyway |
| Neck | 0.042H × 0.75 to 1.25 | ×0.4 to 3.5 (rigid) | Past 3.5 it turns as a pole. Add a neck bone. `Cower` and `Grieve` put the hands at the neck base, not the face, so use `neck + 0.7 × Neck` |
| Head radius | 0.066H × 0.92 to 1.08 | ×0.75 to 1.45 | Child heads are 0.083H. A big-headed adult folk looks like a child unless the body proportions and height set it apart |
| Hands | `HandK` ±12% | ×0.7 to 1.6 | Big hands read at 15 m. Tiny hands lose the prop grip |
| Feet | `FootK` ±10% | ×0.8 to 1.8 | Big flat feet read; heel and toe stay in step |
| Shoulders, hips | ×0.88 to 1.12, ×0.86 to 1.14 | ×0.8 to 1.3 | A very wide shell-shoulder needs wider nav radius |
| Build | 0 to 1 | folk bias -0.25 to +0.35 inside 0 to 1 | Heavy arms (limb ×1.35 at Build 1) cut through the torso in `HandsUp` |
| Stoop | 0 to 1 (adds up to 0.55 rad of lean) | baseline -0.15 to 0.6 | At 0.6 the lean is about 19 degrees. Husk "hunch" is 0.55 to 0.8, so they look alike. Husks differ by other cues (section 9) |

**Torso is what's left.** `Torso = Height - Leg - Neck - 1.75 × HeadRadius`. Long legs and a long neck both eat the torso. So a stork-like folk is also *taller*. Give it more height, not a shorter chest.

**Head-to-body.** A human head (skull and jaw) is about 14% of height. Allow about 11% (×0.8) to 21% (×1.45).

**Posture.** Upright and proud: stoop -0.15 to 0, chest forward. Hunched: 0.3 to 0.6, with a thick tail as counterweight; it reads as a hunter, a bird or a brute. Elders add 0.1.

**Hit spheres.** `HumanRig` sets a chest sphere (0.32 m), a head sphere (the head radius) and two thigh spheres (0.2 m). Tails, crests and shells have none. They are not vital, which suits tails. Shell and plates that look like armour should act as armour. That is a later hook (section 8).

**Speed, mass, health.** Walk 1.3 and run 3.6 m/s do not change. Stride is a share of leg length, so long legs step slower and short legs quicker, which is fine inside the ranges. Mass and health come from `Height²` and `Build`, so a big folk is heavier and tougher for free.

## 5. Climate fit

The existing rules (`data/wildlife.json`, `fit`, and `Figure.Measure`) steer wildlife and clothes. They steer alien parts the same way, using the same numbers. A folk's *frame* answers to `ClimateFit` directly. Its *parts* are weighted by band.

| Band | Existing numbers | Covering odds | Parts favoured | Parts rare |
|---|---|---|---|---|
| **Frozen** | bulk 1.2, limbs 0.82, pale 0.6 | fur, down, fat-layered thick skin | stub or bushy tail, small ears with tufts, short muzzle, furred hands and feet, big flat feet, dark points on pale coats, tusks | long thin tails, large ears, frills, gills, scales, plates |
| **Cold** | bulk 1.1, limbs 0.92, pale 0.25 | fur, down, thick skin | bushy tail, antlers, ruffs, mane, quills, shell, hooves | thin skin, big ear fans, sails |
| **Mild** | none | smooth, light fur, feathers, scales | anything | nothing is banned |
| **Hot** | bulk 0.93, limbs 1.08, ears 0.5, tan 0.3 | scales, thin skin, fine feathers | big ears, frills, sails, long limbs, long thin tails, bare forearms, light sandy colours | fat rolls, heavy fur, thick bushy tails |
| **Scorching** | bulk 0.88, limbs 1.14, ears 0.8, tan 0.5 | scales, thin skin | sails, huge ear fans, long necks, digitigrade, very long legs, pale reflective colour | fur, down, shells, plates thick enough to hold heat |

**Other conditions already in `WorldConditions`:**

- **Dark worlds** (`Folk.DarkVision` above 0.5): bigger eyes (up to 1.8×), big ears, whiskers, eye shine, dim glow dots. This is the same rule as for wildlife.
- **Bright or harsh sun:** small or slit-pupil eyes, brow plates, darker skin, hooded eyes, nictitating lid line.
- **Thin air** (`Oxygen` low): barrel chest (shoulder and chest depth ×1.15), wide nostrils or gill slits.
- **Wet, swamp, rain:** webbed hands and feet, gill slits, smooth wet skin, paddle tail, top-set eyes.
- **Dry, desert:** fat tail, pale tan colours, ear fans, nostril slits, wraps.
- **Mutagen:** mutation lumps and asymmetry go on top, from the existing layer. Symmetric tidy parts versus lopsided growths keep native and mutant apart.

**Not in `WorldConditions` today:** gravity. If it is added, high gravity gives short thick folk, and low gravity gives tall thin ones. I did not check whether a planet's gravity is available there.

**Visitors keep their own body.** An outsider from a hot world in a cold town keeps big ears and thin limbs, wrapped in furs. You can read where they are from. It costs nothing.

## 6. Outfits on alien bodies

Clothes are built from the figure's measures (`ChestD`, `ShoulderW`, `HeadRadius`) and each garment fits in a **slot** (`HumanBody.Slot`): head, mouth, eyes, back, front, side, over. The slot system already allows one thing per slot. That is the lever.

Parts act on garments in four ways:

| Verb | Meaning | Examples |
|---|---|---|
| **Pierce** | the part goes through the garment | horns, antennae, quill tips, tails (with a ring) |
| **Hide** | the part is not drawn under a garment | ears under a hood, a frill under a high collar, hair under a helmet (this exists) |
| **Fit** | the garment is resized from the part's extents | helmets, hats, hoods, respirators, goggles |
| **Claim** | the part takes the slot, the garment is dropped or swapped | back parts take the back slot |

### 6.1 Head

Measure a **head box** from the parts: front (snout or beak tip), top (crest), width (ears, frill, horn bases), back (nape crest). It is in R units from the skull centre. Head garments size from it instead of from `R`. Cap it at 1.6× the human size.

| Garment | Rule |
|---|---|
| Open helmet, cap | sits on the skull; horns, crest spikes and antennae pierce it; a tall crest or wide horns (more than 1.3R) means no cap or hat |
| Wide hat, straw hat | brim from the head box width; tall crowns get no hat |
| Hood, fur hood, head wrap | ears and frills hide; horns and antennae pierce; long snouts stretch the hood forward |
| Sealed suit helmet | the bubble is an ellipsoid round the head box, so the helmet's shape is the species read on a vacuum world; horns and antennae longer than 1.2R fold to bulges, because nothing pierces a sealed suit |
| Respirator | a muzzle mask sized from the mouth part; beaks and mandibles get a beak cover. It claims the mouth slot |
| Goggles, shades | a band across the eye line, as wide as the eyes; stalk and compound eyes get a wrap-around visor; four or more eyes get a wide band |
| Eyepatch | one eye, any plan |
| Face paint | any flat part of the face |

### 6.2 Body

| Part | Rule |
|---|---|
| **Tail** | pierces the garment. A dark ring marks the hole (one flat disc in the trim colour). Hems stay as they are. A thick tail (radius over 0.1R) means no long `coat` or `poncho`: they wear a short one, or none. Suits: the tail gets a sleeve (the tail taper in the suit colour, with a collar ring) |
| **Back parts** (shell, wings, sail, plates, spines) | claim the back slot. Packs and tanks are dropped. Scavengers swap the backpack for a `satchel` or a chest rig. A fold-flat part (under 0.08u deep, small wings) lets a pack sit over it |
| **Shell and wings with a seat** | these folk sit on stools and benches, not chairs with backs (a minor rule for the sit idle) |
| **Frill or collar** | claims the `over` slot's high collar; shawls sit under it |
| **Arms** | sleeves fit the arm taper; claws pierce gloves; fins and fringes show outside sleeves; webbing shows between gloved fingers |
| **Legs** | trousers stop at the hock for digitigrade legs, and the lower leg shows in foot colour; boots on hooves and talons colour the foot and let tips show |
| **Belt, apron, vest** | unchanged: they hang on the torso |

### 6.3 The simplest rule

> A part publishes its extents. Garments size from the extents. If a part can't be fitted, it claims the slot and the garment is swapped for one that can.

That is all of it. Four verbs, one box, one substitution table (pack to satchel, respirator to muzzle mask). No garment needs to know what a species is.

### 6.4 Raiders and other decoration

Villains look like their people. What sets raiders apart is decoration that changes the silhouette. The current set is shoulder spikes (`spike_pads`), a made mohawk, bandoleers, trophies, scrap plates, face paint. For alien raiders the same decoration goes on sockets:

- **Shoulder spikes** work on any body, as now.
- **The made crest:** a row of dyed cones along the skull midline from brow to nape. It works on bald, beaked or hairless heads. A folk with a natural crest gets it dyed and stiffened, a stand-in for the mohawk.
- **Horn caps and rings:** bone or metal caps with spikes on horns and tusks.
- **Tail spikes or a blade** on tailed folk.
- **Bone ornaments** on feathers, antennae or ears.

Each adds at least 20% to the outline width or height at its socket. The base folk is unchanged.

**Feral** people use `rags`: the same parts, overgrown. Crest sprawling, fur matted, claws long, one horn broken.

## 7. Twelve example species

Handles are working labels. Real names come from `NameGen`. Each is rolled from the parts above, not hand-made. Points (1.5): the nearly human ones spend 1 to 3, the far-out ones 9 or 10, which is a very alien world.

- **1. Longears** (nearly human). Cold. Human frame, 5% taller and leaner. Human face; large solid dark eyes (1.25×); tall pointed leaf ears (1.8×), swept up. No tail. Pale grey-blue skin with dark points. *30 m:* human in a hood; tall ears and a pale face in the open. The closest to human, on purpose.
- **2. Switch-tails** (nearly human). Hot. Human frame, legs ×1.05. Human face with small round ears set high; a thin tufted tail, 0.9 m. Ochre skin with dark barring on face and forearms (one new pattern). *30 m:* the tail swinging tells them at once.
- **3. Waders.** Hot and wet. Tall (×1.2), thin, legs ×1.15, neck ×3.2 (two bones), stoop 0.3. Straight beak 1.5R, small plume crest, side-set eyes. Digitigrade with a hock, forearm feather fringe, no tail. White and slate feathers, dark fringe. *30 m:* a bent S-neck with the head forward, spindly legs, light colour.
- **4. Shellbacks.** Cold uplands. Short (×0.85), wide (shoulders ×1.2), legs ×0.9, no neck to speak of. Flat broad head with a brow plate, small deep-set eyes, tusk stubs. Dome shell, plated forearms, stub tail. Slate and rust, mottled, thick leathery skin. *30 m:* a low wide hump with no neck.
- **5. Sailbacks.** Scorching. Tall and thin, legs ×1.1. Long lizard muzzle, nostril slits, side-set slit-pupil eyes, no ears. A tall sail crest from brow to nape. Digitigrade, three-finger claws, thick tapering tail. Olive scales with an orange-ray sail, bands pattern. *30 m:* a tall fan above the head and a long tail trailing.
- **6. Stag-kin.** Cold forest. Human frame, ×1.1 tall. Long deer muzzle, big mobile ears, branching antlers (large on males, small on females). Short fur on face, neck and hands; hooves, digitigrade; a short white-tufted tail. Russet with pale points. *30 m:* antlers and a tail flag.
- **7. Fringe-necks.** Mild swamp. Human frame. Wide slit mouth, no nose, top-set eyes, gill slits (3 each side) with pale fronds. A ray-ribbed neck frill that flares, webbed hands, big flat feet, a paddle tail 0.5 m. Smooth wet mottled olive and yellow. *30 m:* a wide collar and a flat tail behind a shuffling walk.
- **8. Carapace folk.** Hot and dry. Thin, stoop 0.35, arms ×1.15. Smooth dome head, large compound eyes, small mandibles, forward-swept antennae. No hair, no tail. Chitin plates with ring bands; forearm gauntlets, elbow spurs. Glossy dark brown with ochre bands. *30 m:* hunched and shiny, with two antennae sweeping forward.
- **9. Cloaks.** Cold highland. Stoop 0.25, big round head (×1.2). A flat face disc, huge forward amber eyes (1.5×), short hooked beak, tall ear tufts. A feather mane, a folded wing-cloak to the hips, plantigrade talon feet, a short fan tail. Barred brown and cream. *30 m:* a big round head with tufts over a cloak-shaped mass.
- **10. Brutes.** Any rough ground. Tall (×1.15), build 0.9, neck ×0.5, small head with a heavy jaw, small deep-set eyes, two tusks. A row of bristle quills over the head, nape and back. A thick short tail with a tuft. Big hands with blunt claws, plantigrade. Dark grey-brown hide, pale bristles. *30 m:* the widest figure, a spiky back line, a tail tuft.
- **11. Periscopes** (far out). Dim worlds and caves. Small (×0.8), slight build, head ×1.2, stoop 0.2. Two eye stalks 1.2R tall, drooping outward, big dark eyes. A star nose, a wide mouth, no ears. Long thin four-finger hands (×1.5). Pale translucent skin with veins and glowing cyan dots on cheeks and forearms. *30 m:* small and pale with stalks over the head; the dots glow after dark.
- **12. Reeds** (far out). Low light, thin air. Very tall (×1.2), very thin, shoulders ×0.85, legs ×1.15, arms ×1.3, neck ×2.5. A tall smooth dome head with one visor band of three eyes, a thin keel crest, six hanging chin tendrils. Four long fingers (×1.6), digitigrade, no tail. Translucent glass-pale skin, veins, glow dots down the arms. *30 m:* very tall and thin with a keel above the head; swaying tendrils.

**Silhouette check at 30 m.**

| # | Top of head | Back | Tail | Height, width | Colour |
|---|---|---|---|---|---|
| 1 Longears | ear points | plain | none | tall, lean | pale blue-grey |
| 2 Switch-tails | round ears | plain | thin tuft | normal | ochre, barred |
| 3 Waders | plume, forward beak | plain | none | tall, thin | white and slate |
| 4 Shellbacks | flat, no neck | shell hump | stub | short, wide | slate, rust |
| 5 Sailbacks | tall sail | plain | thick, long | tall, thin | olive, orange |
| 6 Stag-kin | antlers | plain | white flag | tall | russet |
| 7 Fringe-necks | frill collar | plain | paddle | normal | olive, yellow |
| 8 Carapace | forward antennae | plain | none | thin, hunched | glossy brown |
| 9 Cloaks | round head, ear tufts | wing-cloak | short fan | normal, hunched | cream, brown |
| 10 Brutes | quill row | quill row | tufted | tall, widest | grey-brown |
| 11 Periscopes | stalks | plain | none | short | pale, glowing |
| 12 Reeds | tall keel | plain | none | tallest, thinnest | glass-pale |

**Closest pairs.**

- **Waders and Reeds.** Both are tall and thin. Waders have a forward beak, a bent neck and bright white, while Reeds hold the head upright under a keel, in dull pale. Check contrast: Reeds' pale skin on snow would vanish, so they need dark points or clothes there.
- **Brutes and Shellbacks.** Both are wide. Brutes are tall with a spiky line; Shellbacks are short with a smooth hump.
- **Sailbacks and Switch-tails.** Both have tails. The sail, the long thick tail and the height set Sailbacks apart.
- **Longears and humans.** Hooded, they are alike at 30 m. That is the point of a nearly human folk. Clothes and role decide first.

## 8. Easy wins first

Ranked by variety gained for work spent. Silhouette gains come first, because they read at 30 m.

| # | Win | Work | Why it ranks here |
|---|---|---|---|
| 1 | Skin families, dark points, accent colour | Easy, none | Vertex colour only. Folk already have hue ranges |
| 2 | Ear shapes and sizes (8 shapes, climate-linked) | Easy | One or two prims each side; fits heat and cold rules |
| 3 | Horns and antlers (`Parts.Horns` reuse) | Easy | Six styles ready (pair, ram, antlers, spike, brow, crown); a crown part that reads at 30 m |
| 4 | A tail on bones (thin, tufted, thick, fan, stub) | Medium | The biggest single outline change. One routine, reusing `Parts.TailTip` |
| 5 | Crests, manes and quill rows | Easy | Cones along a line; also the raiders' hook |
| 6 | Eye plans: count, size, placement, colour | Easy | Spheres only; the big change at talking range |
| 7 | Snouts and muzzles | Easy | Two ellipsoids give the animal face |
| 8 | Beaks | Easy | Two horn chains |
| 9 | Wider proportion ranges (height, legs, arms, neck, head) | Easy | Numbers only, inside the checked limits |
| 10 | Tusks | Easy | Two horn chains from the jaw |
| 11 | Per-folk pattern material (stripes, spots, mottle, bands) | Medium | The shader exists; one material per folk |
| 12 | Neck frills, ruffs, gill slits | Easy | Reuses the frill; gills are dark slits |
| 13 | Back spines, plates, ridge (`Parts.Back` reuse) | Easy | Needs the adapter; claims the back slot |
| 14 | Hands: finger count, claws, webbing | Easy | Thin tapers on the hand sphere |
| 15 | Feet: hoof, paw, talon, webbed | Easy | Swap the foot spheres |
| 16 | Antennae | Easy | Reuse `"antennae"`; a strong outline |
| 17 | Bioluminescent dots | Easy | Small glowing spheres; they read at night |
| 18 | Forearm fins and feather fringes | Easy | A gesture amplifier for `HandsUp` and `Celebrate` |
| 19 | Fur ruffs at collar and cuffs | Easy | Shows the covering under clothes |
| 20 | Chitin look: `Mat.Carapace` on skin plus plates | Medium | One material class, a few plates |

### Needs more work

Medium jobs that come after the wins, in this order: **digitigrade legs** (raised ankle, kinked lower leg, short toe foot, no IK change); a **second neck bone** for S-curved necks over 3.5×; **wings and shell** (new geometry, the back-slot rule and the adapter); **fur and feather clouds** of 30 to 40 prims inside the vertex budget; **several folk per place** (`PeopleGenerator` takes a folk list); and the **silhouette test** that renders and compares folk at 30 m.

### Engine work, and what it touches

| Change | System | Size |
|---|---|---|
| Per-folk material, with a pattern weight on skin prims | `CreatureMaterials.Clothed` becomes `ForFolk`; `HumanBody` passes pattern weights | Small |
| New patterns: world-up countershade, barring, scale tile, sheen | `creature.gdshader` | Small to medium, about 40 lines |
| Sway material class (generalise `Mat.Worm`) | shader vertex step, one `Prim` field; builds on the Husk branch | Small |
| Tail bones in `HumanRig` | rig constructor and `Drive`, `HumanBody.Far`, tests for NaN and floating | Small to medium, about 120 lines |
| Neck bone | `HumanRig`, `HumanBody.Head` | Medium |
| Display bones (crest, frill, ears) on `BodyAct` and alert | rig, brain hook, one float per person | Medium |
| Many folk per world | `PeopleGenerator`, `Founders`, `Town.cs` | Small to medium |
| Region roster | `Bestiary`, galaxy region and cell | Small |
| Nav radius per folk | `PeopleGenerator` sets `Radius` 0.35 for everyone; wide shells and thick tails need up to 0.5 | Small |
| Seize reach for small folk, face gap for snouts | `Seizing.CanReach` and `FaceToFace` (Husk branch) | Small; see section 9 |
| Armour hit zones for shell, plates, casque | `Attacks` damage routing | Medium; optional |
| Wordless voice by throat and mouth shape | `Voice.For`: `Throat` scale from neck and mouth part | Small; optional |
| Discovery card for a new folk, name from `NameGen` | UI and codex | Small |
| True see-through skin | alpha pass, second draw call | Don't. Fake it |
| Three-bone digitigrade IK | `LeggedRig.SolveIK`, foot planting | Large. Don't |

**Display bones, in more detail.** The body-language doc says a threat is big, tall, square and forward; appeasement is small, low and rounded. Parts can join in: crest up, frill flared, ears forward, quills raised and tail high for threat; all flat for appeasement. One float per person (from `BodyAct` and temper) drives three optional bones. It extends the same axis the doc already defines.

## 9. What Husk worms do on each head

Husks are people taken by worms that come out of every opening. A Husk is a person rolled the usual way and then taken (`Husks.Take`). It is greyed, torn, hunched, and moved like a puppet, with one or two "wrong" shapes. The worms are the wet ringed `Mat.Worm`, in pale flesh to dark red, swaying on their own.

This works unchanged on any folk **if** every head part lists its openings and the Husk code uses that list instead of a fixed set of human holes.

**Universal tells.** These hold for every folk, so a Husk reads at once:

1. **Worms.** The same pale ringed worms, the same colour on every folk.
2. **Grey-out.** Skin lerps toward grey by 0.3 to 0.45. Alien accent colours lerp the same way, and pattern contrast drops to 40%. Glow dots go out. Eye shine goes out. Fur and feathers darken and mat.
3. **Gait.** The puppet stride, the jerk, the lagging arms, the dragged foot.
4. **A wrong shape.** `Wrong.Jaw`, `Arm`, `Hunch` or `Head`. For folk with long arms or a baseline hunch, the head tilt and the jaw carry it.
5. **Torn clothes.** Rents and hanging strips, already built.
6. **Nothing over the face.** `OverFace` gains the new face garments: muzzle masks, visors, beak covers.

**Worm count.** Each opening gets at least one. Mouth gets 5 to 8. The total is capped at 16, as the human count is now (12 to 17). A face with many openings does not cost more.

### Head by head

| Head kind | Openings the worms use | What `Wrong.Jaw` does | Extra |
|---|---|---|---|
| **Human-like, ears out** | mouth, nostrils, eye sockets, ear holes | the jaw sphere hangs low | as now |
| **Snout or muzzle** | gape along the whole muzzle (worms spill from the mouth line), nostrils at the tip, eye sockets, ear holes | the lower-jaw ellipsoid drops on its hinge; the gape is wide | a long jaw makes the worst mouth. Worms hang from the teeth line |
| **Beak** | the beak seam (upper and lower parted), nostril holes on the beak's top, eye sockets | the lower beak swings down, 30 to 50 degrees | beak chipped (one point shorter); worms between the halves |
| **Mandibles** | between the mandibles and from the mouth palps | mandibles splay apart | compound eyes go dull and dark; a worm or two between facets, none through the whole eye |
| **Tusks** | as the muzzle or short face | the jaw hangs and the tusks tilt out | one tusk broken |
| **Face plate** | a dark seam opens down the plate's middle and along the eye edges | the plate cracks along the jaw | worms come from the crack, not from smooth carapace |
| **Smooth dome** | the mouth slit and the eye band; if it has no real openings, the worms split a vertical seam | the slit gapes | the one head with no natural opening. Keep it rare |
| **Stalk eyes** | the stalk tips: the ball is replaced by a dark socket bulb and a worm | jaw hangs as usual | stalks droop, one bent |
| **Compound eyes** | between facets; the eye goes dark | as usual | no pupil |
| **Gills and frills** | gill slits (1 or 2 worms each, up to 4 total), plus the mouth | jaw hangs | the frill droops, torn at one edge; gill fronds go limp and dark |
| **Antennae, horns, crest** | none of their own; they carry the tell | none | crest flat and matted, antennae limp and tangled, one broken. A big head tilt shows from far away |
| **Wide slit mouth** | the whole slit | the slit gapes | this is the largest mouth; keep its worms to the cap |

### Body parts

- **Tail:** slack and low, dragging when it walks; it jerks with the same `jerk` as the shoulders. Optional: the tip frays into worm strands (three prims).
- **Wings, shell, sail:** no worms. Wings droop and one is torn. A shell is cracked along a seam with a pale line.
- **Ears, frills:** droop and tear. Ear holes get worms.
- **Webbed hands, claws:** unchanged.
- **Colour:** a luminous folk goes dark. A bright folk goes grey. A pale folk goes yellow-grey.

### The seize, on alien bodies

`Seizing.CanReach` compares the Husk's `Figure.Height` plus 0.15 m with the victim's head height. A player's head is 1.7 m standing and 1.05 m crouched. Two effects follow:

- **Small folk make weak seizers.** A Husk of a folk under about 1.55 m can't take a standing player's head. By the code's own rule it strikes or retches instead. Tall folk Husks reach more. Accept it as variety, or lift the reach for small folk. Owner's call.
- **Faces that stick out.** `FaceToFace` is a fixed 0.5 m, which assumes a flat human face. A long muzzle or beak should add its length to the gap, or the snout goes into the victim's head.

### Silhouette traps

- A folk with a **baseline hunch** looks like a `Wrong.Hunch` Husk, and one with **long arms** like a `Wrong.Arm` Husk. For them, Husk selection should prefer `Head` or `Jaw`.
- A **tall crest with a head tilt** is an excellent cue at range. Keep it.
- **Husk crowds mix hosts** (`Species.Hosts`). With mixed folk the crowd is a mix of silhouettes with the same horror tells. That is a strong effect and it costs nothing extra.

## Open questions

1. **Door heights. Decided (owner, 8 Nov 2026):** cap height. Every people stays under a height cap, and every generated doorway fits that cap. Pick the cap from the actual one-block doorway height once it is measured (on the 2 m grid a doorway is about 2 m, so adults top out a little under it, about 1.9 m), and check wide shells and thick tails against door width and the nav radius the same way.
2. **Where tall and wide folk path.** `NavGrid` uses a radius, and `PeopleGenerator` sets 0.35 for all. Shells and thick tails may need up to 0.5.
3. **Vharn Dominion. Decided (owner, 8 Nov 2026):** the Vharn are insect-like, built from this system with a strong bias to carapace, mandibles, antennae and chitin coverings, still on the human shape so their gestures read.
4. **Hit zones.** Should parts that look like armour act as armour? It would make the visual honest in combat. A later hook.
5. **Gravity.** Not in `WorldConditions` today. Climate fit for height and bulk could use it.
6. **The Husk branch** (`proto/husks`) is not merged. Section 9 builds on its worms, jaw drop and tilt. If it lands differently, section 9 changes with it.
