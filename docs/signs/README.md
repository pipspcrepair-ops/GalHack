# GalHack signs

Draft 3, for review. Nothing here is in the game yet.

All of this art is original to GalHack. Every symbol was worked out from what it has to say and drawn as our own vector shapes in the scripts in `tools/`. It takes its family feel from the industrial signage of 1970s science fiction film, but it contains no traced, redrawn or adapted icon from any other set. It uses no existing font, clip art or icon pack, and no Kenney assets. The only font involved is a system font for the labels on the review sheets, and those labels are not part of the art.

There are four families.

| Family | Who made it | Look | Files |
|---|---|---|---|
| **Signs** | The old world, and its authorities | Framed, colour-coded, machine-made, confident | `svg/`, `svg-stencil/`, `png/`, `sign-sheet.png`, `sign-sheet-small.png` |
| **Traveller marks** | Wanderers, scavengers and survivors | Unframed, one colour, rough, scratched by hand | `marks/`, `marks-sheet.png` |
| **Faction sigils** | The seven factions | Bold, unframed, each in its own faction's character | `sigils/`, `sigils-sheet.png` |
| **Deck and zone markers** | Whoever built the place | Colour bands and bars that run along walls | `zones/`, `zones-sheet.png` |

`restyle/` and `signs-restyle.png` hold a proposal for the owner, explained below.

## Signs

81 signs. A sign is read in three steps: the colour says what kind of thing it is, the symbol says what it is, and the pips say how bad it is.

### Colour system

The field colour is the category. Each category has a one-word code. The frame stays red on every sign.

| Code | Field | Symbol | Covers |
|---|---|---|---|
| **Void** | Black | White | Air, pressure and gravity |
| **Danger** | Red | White | Hazards, mostly indoors |
| **Land** | Brown | White | Ground, weather and wildlife outdoors |
| **Order** | Violet | White | Old-world authority: locked, sealed, forbidden |
| **Energy** | Yellow | Black | Power and radiation |
| **Cold** | Blue | White | Cold and ice |
| **Life** | Green | White | Medical, food and care |
| **Systems** | Grey | White | Machines and general stores |
| **Route** | White | Black | Ways to move |

### Level pips

The heavy bottom bar of the frame holds the level, so any hazard can show how bad it is without redrawing its symbol.

| Pips | Meaning |
|---|---|
| None | A place or a thing, not a warning |
| One | Take care |
| Two | Dangerous |
| Three | Lethal without protection |

### Frame

Draft 1 and 2 signs use a tile 256 units square with a corner radius of 30. The band is 18 wide at the top and sides and 42 at the bottom, with a 7-wide keyline and a field with cut corners.

The 25 signs new in draft 3, and every sign in `restyle/`, use a firmer frame: a 24-wide band, a 44-wide bottom bar, an 8-wide keyline and a plain field with small round corners. Their symbols can run off the edge of the field. The heavy bottom bar with its pips stays, as that is this set's own way of combining a symbol with a status.

### The geometric language

New signs are built from flat shapes that stand for concepts rather than small pictures of things:

- **Square:** a body or a thing
- **Disc:** something alive
- **Triangle:** force or direction
- **Bar:** a wall, floor or boundary
- **Colour swap at a line:** half inside something

There are deliberately no figures in this language, because a head on a bar would sit too close to the reference sheet's figure. Field colour must stay visible, since it carries the category.

### The restyle proposal

`signs-restyle.png` shows 22 approved signs beside proposed versions in this language. The proposed files are in `restyle/`, and the approved signs are untouched in `svg/`, `svg-stencil/` and `png/`. Nothing is replaced until the owner decides. If the proposal is approved, the other approved signs can take the firmer frame at the same time.

### Shared motifs

Repeated parts make the set easier to learn:

- **Moving air:** a heavy S-wave. Sealed in a hull it means pressurised, and struck out it means no air. Stood on end it means heat.
- **The block figure:** a square head on a block body, matching the world.
- **The stack:** two crates on the floor, with the third place showing the contents: a crate, an apple or a snow mark.
- **The strike:** one heavy slash with a gap either side means "not this".

### Stencils

`svg-stencil/` has every symbol as one flat colour on transparent, with no frame or pips, for spraying on block faces. Holes are real transparent holes, made with an SVG mask. They render correctly through Godot's SVG importer.

## Traveller marks

27 marks. These are what people left behind for whoever comes next. They are meant to be drawable with a stick, so they use only lines, loops and dots. Each is one colour on transparent, chalk white by default, and at 256 pixels in `marks/`.

The marks have two small rules of their own:

- **Bar under:** someone vouches for this. Safe camp, clean water, shelter and friendly people have it.
- **Slash through:** this is bad.

They were invented for GalHack, not taken from the historic hobo signs. Where a mark came close to a historic sign as remembered, it was changed: way out began as a ring and arrow and became a gap in a wall, and the nest's eggs were pulled apart so they don't interlock.

The warding mark is deliberately left unexplained here. Keep it that way in game: no label, no tooltip, no codex entry.

## Faction sigils

One sigil for each of the seven factions in section 17 of `docs/DESIGN.md`, drawn from each faction's look in the ships and structures table. They go on doors, hulls and territory, and are stencilled over older signs when a faction takes a ruin. Each has a clean version in `sigils/svg/` and `sigils/png/`, and a one-colour stencil in `sigils/svg-stencil/`.

| Sigil | Idea |
|---|---|
| Vharn Dominion | Spread wing-cases, one long and one short, with a glowing point where they meet. Final |
| Kessari Clans | A lattice of identical modules, mirrored both ways, with one status light. Final |
| Meridian Combine | One massive square with its corner taken and a planet in the corner. Final |
| Ledger Exchange | A coin split on a step: one side given, one side got. Final |
| Carrion Reavers | A kill tally: four bars and the strike that closes the gate. Final |
| Freeholders | A lantern: somebody left the light on, and that is all a safe place is. Final |
| Hollow Bloom | Grown, not built. Left out of the rounds so far: its files are still in `sigils/`, but it isn't on the sheet |

Two sheets record the rounds. `sigils-attempts.png` shows three ideas per faction beside the sigils of that time, with files in `sigils/attempts/`. `sigils-freeholders.png` shows the Freeholders reroll in line with the chosen five, with files in `sigils/attempts-freeholders/`.

Sigils were checked by eye against real-world and fictional emblems, flags, and religious and political symbols, as far as memory allows. Kessari uses mirror symmetry, not rotation, on purpose. A trademark or heraldry search has not been done.

## Deck and zone markers

These show a level and wing in a big place with no numerals or words. Each tile is a dark plate with a band running edge to edge, so tiles join into one stripe along a wall.

How to read one:

1. **The band says which wing.** Each wing has a colour and a pattern. The pattern matters: on a stencil the colour is gone and it is all that's left.
2. **The side says which way.** Marks above the band are decks up from the main deck. Marks below it are decks down. A bare band is the main deck.
3. **The marks say how far.** A thin bar is 1 and a wide block is 5, and you add them up. A block and two bars is 7.

Blocks come first, then bars. A tile has room for two blocks and four bars, so one wing counts to 14 each way. Beyond that, start a new wing.

The six wings are cobalt (solid), orange (centre stripe), yellow (checker), green (chevrons), magenta (diagonals) and white (notched edge). Red and black are left out so a marker is never mistaken for a warning or for Void. Patterns repeat every 64 units, so a run of tiles has no seam. `zones/` holds a sample of 14. `tools/build_zones.py` can make any other wing and level.

## Rebuilding

Run these from `tools/`, signs first, as the others import from it and the sigil sheet uses its PNGs:

```
python build_signs.py
python build_marks.py
python build_sigils.py
python build_freeholders_attempts.py
python build_zones.py
```

Each clears its own output folders, writes SVGs, renders them with Godot's built-in SVG rasteriser and composes its sheets with PIL. Set `GALHACK_GODOT` if Godot isn't at the default path. Nothing else needs installing.

## Originality check

Each finished sign was compared by eye, one at a time, with the reference sheet the owner shared. These were changed because of that check:

- **Artificial gravity on:** its first version, a chevron over a block over a bar, stacked too much like the sheet's Y-and-bar gravity icon.
- **Venomous plants, proposed version:** an early draft was a Y over a bar and a circle, so it became a barbed diagonal.
- **Healing booth, proposed version:** a bracket around a box with a medical mark was too close to the sheet's medical life support.

Other redraws came from shapes that read as something else. Explosives was a six-pointed star, which reads as a religious symbol, and it is now an uneven nine-point burst. Sealed by order read as the letter phi, and bulkhead door as a sigma.

The traveller marks were compared with the historic hobo signs from memory, not against a source. The Traveller T5 symbol font wasn't available, so it hasn't been compared or consulted.
