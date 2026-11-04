# Prototype: walkable moving ship

Proves the riskiest feature in GalHack: walking around inside a ship while it flies, rolls and stunts, then stepping off it (or back on) without anything glitching.

![Outside](screenshots/outside.png)
![Cockpit](screenshots/cockpit.png)

## How it works

The ship you see flies through the world. Anyone aboard actually walks on a **ghost cabin**: a motionless copy of the interior, far below the map, on its own physics layer. The player's camera is drawn at `ship transform × position on the ghost`. However hard the ship turns, the character controller only ever sees a still room with gravity pointing at the floor. Loose cargo works the same way.

Stepping off the back converts your position, velocity and view direction into the world frame, adding the ship's velocity at the door. Walking back in does the reverse.

## Play it

From the repo root, in PowerShell:

```powershell
.\tools\godot.ps1 -Play
```

| Key | Action |
| --- | --- |
| WASD, mouse | Walk and look; fly and steer when seated |
| Space / Ctrl | Jump, or up/down (zero-g, flying, jetpack outside) |
| Shift | Sprint, or boost when flying |
| F | Sit in or leave the pilot seat (front of the cabin) |
| Q / E | Roll |
| G | Toggle core gravity: cargo and you start to float |
| T | Stunt autopilot: barrel rolls while you walk around |
| V | Third person |
| R | Back aboard |
| Backspace | Restart the ring course |
| Esc | Free the mouse |

Fly through the 10 rings in order for a lap time. Try jumping out of the back during a stunt and catching up with the jetpack.

## Test it

```powershell
.\tools\test.ps1
```

Runs `tests/FrameTest.tscn` headless and checks that the player:
- stays on the deck through 15 s of barrel rolls
- stays inside the cabin in zero-g
- keeps the ship's velocity when stepping off at 40 m/s
- boards when walking back in

`tests/Screenshot.tscn -- --view=cabin|stunt|cockpit|outside --out=file.png` renders a view to a PNG.

## Known limits

- The ship ignores terrain apart from a ground clamp; collisions belong to the physics module.
- One ship, one ghost slot. More ships need a ghost slot each.
- The on-screen text is a debug overlay, not the diegetic UI.
- Single player only; positions inside ships are already ship-relative, ready for networking.
