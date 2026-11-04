# Prototype: diegetic UI

Proves that GalHack's interface can live entirely on things in the world, stay quick to use and cost almost nothing on a low-end PC: screens on consoles you walk up to, a holo-table, a wrist computer you raise, and a helmet visor. There are no flat menus or floating windows, and the hidden numbers (power, CPU, mass, oxygen, temperature) show up at the moment you make a choice.

![Ship systems console, a role mid-drag](screenshots/console.png)
![The holo-table star map](screenshots/holo.png)
![The wrist computer](screenshots/wrist.png)
![The temperature vital flashing in the cold store](screenshots/cold.png)
![The invasion countdown on the visor and the console](screenshots/invasion.png)

## What it proves

- **A reusable in-world screen** (`src/Screens/WorldScreen.cs`). A 2D page is drawn into a `SubViewport` and shown on a panel through a scanline-and-glow surface shader. Look at it and press E: the view eases in to frame it. While it's in use, every mouse event becomes a ray from the camera through a virtual cursor. The ray is intersected with the panel's plane, converted to UV and then to viewport pixels, and pushed into the SubViewport as an ordinary mouse event, so stock Controls and our widgets both work. Esc eases back out. The world keeps running throughout.
- **Screens cost nothing while idle.** A SubViewport is switched off (`UpdateMode.Disabled`) until its page changes. It then redraws once, and only if the camera could read it: it must be close enough, facing you and inside the frustum. `ScreenManager` runs these redraws within a small per-frame budget, with the screen in use going first. Background animation is capped at 30 Hz. The cursor, scanlines, flicker and alert pulse all live in the surface shader, so moving the mouse costs no redraw.
- **Gameplay logic is pure C#** (`src/Logic`, no engine types), so it can be tested headless:
  - vitals with a warning line well ahead of the harm line
  - a warning timeline that announces threats at 10, 5 and 1 minutes
  - inventory with mass and a hard volume cap
  - a constructor queue that plans its own sub-parts
  - a station roster with exactly one pilot and shared gunnery
  - trade with sell-all-wanted
  - ship power, CPU and mass
  - the star map
  - the focus state machine (Free, Entering, Using, Leaving), which the camera, the input routing and the cursor all read from, so they can never disagree
- **A shared theme** (`src/Theme`):
  - the faction palette (`Palette.Helios`)
  - three OFL fonts:
    - Michroma for titles, a wide face like those on 70s paperbacks
    - Oxanium for labels
    - Space Mono for numbers
  - chunky vector widgets:
    - segmented bars that preview what a switch would cost before you flip it
    - a rocker switch
    - a lever you can drag or click
    - block buttons
    - row lists with "wanted" rows lit
  - a page style: a colour-block header with racing stripes, and a status strip that every console turns into the red countdown during a threat

## What's on the bridge

| Thing | What it shows |
| --- | --- |
| Ship systems console | Live power, CPU and mass bars. The shields rocker and the core-gravity lever preview their cost on the bars while the cursor is on them. A real lever on the desk follows the on-screen one. Drag station roles (PILOT, NAV, GUN, ENG, SCAN) onto three crew cards: the pilot can't be dropped, and gunnery splits the turrets |
| Holo-table | A glowing hologram of the system. Discovered worlds are named; unsurveyed contacts are dim violet with a "?". Click a world to set a waypoint: the rim screen describes it, and the visor's compass and mini-map mark it |
| Constructor | Order on the left. Two queues on the right: your orders above, and the sub-parts it added for you below, each saying which order it feeds. Missing raw materials show in red |
| Trade link | Depot buys (wanted items in solid yellow) and sells, beside your suit and the ship's hold. Each sell-all button shows what it pays before you press it. Click a row to buy or sell one |
| Wrist computer (Tab) | A forearm comes into view. GEAR is the inventory grid with mass and volume totals, the armor and booster slots, and hazard lamps showing what the worn gear covers. CODEX is one entry, read out by VESTA, the suit's assistant (text for now). You can keep walking while it's up |
| Visor | Vitals along the bottom curve of the glass, a compass arc with contacts, and a mini-map with unsurveyed space left dark. It fades out when all is well. A vital flashes while it is still only a warning, before it costs health. The readouts lag slightly behind fast head turns. The glass frosts in the cold, pulses red when you're hurt, and flashes on the side a warning shot came from |
| Sirens | Rotating red beacons that wake with the invasion warning and spin faster as it gets closer |

## Play it

From the repo root, in PowerShell:

```powershell
.\tools\godot.ps1 -Project prototypes/diegetic-ui -Play
```

| Key | Action |
| --- | --- |
| WASD, mouse | Walk and look |
| Shift | Sprint |
| Space | Jump |
| Ctrl (hold) | Crouch |
| E | Use the screen or holo-table you're looking at |
| Mouse, left button | Use the cursor on the screen; drag roles and the lever |
| Esc | Step back from a screen (or free the mouse when walking) |
| Tab | Raise or lower the wrist computer |
| C | Debug: shove into the cold store (-60 °C); the temperature vital flashes, then hurts |
| I | Debug: invasion warning in fast time (a minute a second): 10, 5 and 1 minute calls, sirens, console countdowns |
| F | Debug: a hostile's warning shot misses the ship; the visor flashes toward it and the cockpit chirps |

The siren whoop and the warning chirp are synthesised in code (`src/World/Sfx.cs`). No audio files are used.

## Test it

```powershell
.\tools\test.ps1 -Project prototypes/diegetic-ui -Scene res://tests/UiTest.tscn
```

`tests/UiTest.tscn` runs headless and exits 0 if every check passes, or 1 otherwise. It checks:

- the temperature vital flashes 6.7 s before damage starts, at full health; thermal gear stops it altogether
- warnings fire once each at 10, 5 and 1 minutes and on arrival, on a simulated clock. A big step that crosses several marks announces them in order, and a late-scheduled threat skips stale marks
- inventory mass and volume totals are exact, and the volume cap holds for both all-or-nothing and partial adds
- ordering 2 rover wheels auto-queues 3 plates, 6 wires and 2 motors in build order, and building them uses exactly those parts. Raw materials that no recipe makes are reported as missing
- the pilot role moves and can't be dropped; gunnery splits 4 turrets 2/2, then 2/1/1
- sell-all removes exactly the wanted items and credits the amount shown on the button
- the ray-to-pixel mapping is within 0.003 px over 25 rays at five angles, up to 80° off-axis, and every ray picks the right collider
- the focus state machine, on its own and in the scene:
  - E eases in with no cursor
  - the cursor appears only once the screen is in use, and only on that screen
  - walking is locked
  - a click flips the shields
  - dragging GUN onto a crew card shares gunnery; dragging the pilot off is refused
  - Esc removes the cursor at once and eases back to the head
- the wrist raises and lowers with Tab, and you can walk while it's up
- the cold store flashes TEMP at full health, before any damage
- a warning shot's visor flash points toward the shooter
- performance, with all 5 screens in view:
  - a full refresh is spread across frames within the budget
  - nothing redraws while idle (0 redraws over ~2 s)
  - one change redraws only the screen that shows it
  - a busy constructor redraws at its 12 Hz refresh, not every frame

Run it windowed (`$GodotConsole --path prototypes/diegetic-ui res://tests/UiTest.tscn`) and it also prints what each SubViewport costs per redraw on your machine.

`tests/Screenshot.tscn -- --view=console|holo|wrist|cold|invasion|invasion-late|fire|constructor|trader|overview --out=file.png` renders a view to a PNG. Run it windowed, not headless.

## Known limits

- Screens are flat quads. Curved glass would need its own ray-to-UV mapping, though the rest of the component wouldn't change.
- `ViewportTexture` has no mipmaps, so a small screen seen from far away is only supersampled a little in the shader. Text stays readable to a few metres, which is how far you'd stand from it anyway.
- Pages are hand-laid at fixed pixel sizes. A data-driven layout would be the next step once there are many consoles.
- Roles are dragged with our own simple drag code rather than Godot's drag-and-drop, so drags behave the same through pushed input events.
- The visor's curve is drawn as geometry (arcs and tilted pills), not as a lens distortion over the whole frame.
- The codex voice is text only. The tech tree, the trader's buy/sell confirmations and ammo on the visor aren't built yet.
- Single player. In co-op, view-models would live on the host, and screens would only read them.
