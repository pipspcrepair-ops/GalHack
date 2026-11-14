# Asset viewer

A browser for everything GalHack generates: creatures, people, places, blocks, items, decorations, signs, vehicles, weather, plants, terrain, skies, worlds. Pick an asset in the left list and it turns slowly on a stage. Orbit it, zoom, pan, or fly a free camera round it. Anything that can be rolled again has a **Randomise** button and a seed box; anything with states (age, theme, damage) has drop-downs; anything that moves lists its animations.

Every asset gets a comment and two tick boxes, **Really like** and **Problem**. Creatures and anything else with animations get the same three controls for each animation. Notes are written to a JSON file as you type and whenever you press Save (Ctrl+S), so you can hand the file to an agent.

## Running it

```
.\tools\godot.ps1 -Project prototypes/asset-viewer -Play
```

Or double-click `Asset Viewer.cmd` in the repo root.

Command line, after `--`:

| Argument | What it does |
| --- | --- |
| `--select=<name, id or key>` | Open on that asset |
| `--seed=N`, `--option=Name=Value`, `--anim=Name` | Its seed, options and animation |
| `--shot=file.png` | Render and quit (used for screenshots) |
| `--list` | Print every asset and quit |
| `--notes=<folder>` | Where the JSON files go |
| `--free`, `--hide-panels`, `--fresh`, `--demo` | Free camera; hide panels; ignore the saved position; fill in sample notes |

## Controls

**Orbit camera** (default): left drag turns, right or middle drag pans, wheel zooms. W and S zoom, A and D pan sideways, Q and E pan down and up, arrows turn, Shift is faster.

**Free camera** (C): hold left or right mouse to look; W A S D move; E or Space up; Q or Ctrl down; wheel sets the speed; Shift is faster.

R randomise, `[` `]` or Page Up/Down previous and next asset, `,` `.` previous and next animation, Space turntable, C orbit/free, F frame, G ground, Z wireframe, H hide panels, L really like, P problem, F5 save position, F9 load the newest saved position, Ctrl+S save notes, F1 help.

## The files

Notes live in `asset-notes/` in the **main checkout** (`G:\GalHack\asset-notes\`), found from whichever worktree you run it in, so removing a worktree never takes your notes with it. Override with `--notes=<folder>` or the `GALHACK_ASSET_NOTES` environment variable.

- `asset-notes.json`: one entry per asset that has a comment or a tick: category, group, name, id, `comment`, `really_like`, `problem`, the `seed` and `options` on screen when you last edited it (so an agent can rebuild exactly what you saw), and an `animations` map with the same three fields per animation. Empty notes are left out. A `counts` block at the top totals them.
- `viewer-positions.json`: the position you left off at (restored on start) and your saved positions: asset, seed, options, animation and camera.

The **Copy report** button puts the problems, likes and comments on the clipboard as plain text.

## How it is built

`Viewer` (the scene), `Stage` (turntable, sky, sun, ground), `ViewCamera` (orbit and free), `NoteStore` (the two JSON files), `AssetRegistry` (finds the providers). Assets come from **providers**.

### Writing a provider

A provider is a class in `src/Providers/<Group>/` that implements `IAssetProvider`. The viewer finds it by reflection; nothing else needs editing.

```csharp
public sealed partial class MyProvider : IAssetProvider   // partial if it nests Godot nodes
{
    public string Category => "Bestiary";
    public IEnumerable<AssetEntry> Entries() { ... }      // cheap: names and builders only
}
```

- **`AssetEntry`**: `Id` (unique in the category, stable between runs: notes are filed under it, so never put a seed or a list index in it), `Group` ("A/B" folders in the tree), `Name`, `Randomisable`, `Options`, `Build`.
- **`Build(BuildRequest)`** returns an `AssetInstance` (a `Node3D`). It gets `Seed` and the chosen `Options`. Everything that is rolled must come from `Seed`, so the same seed gives the same asset and a note can be reproduced.
- **`AssetOption`**: a drop-down (state, age, theme, family...). `Rollable = true` lets Randomise pick it too.
- **`AssetInstance`**: add meshes, lights and sub-nodes as children. Override `Animations` and `PlayAnimation` to list and play animations, `SetSpeed` for the speed slider, `Facts()` for short label and value lines (size, counts, traits), `FrameBounds` if the meshes are not all there on the first frame, `Spins` (false for anything driven in world space, such as weather; the camera drifts instead), `Stage` (ground, own environment, background, minimum zoom, start inside).
- **Mounts**: shaders and fonts that the game code loads from `res://shaders/...` are mounted from the owning prototype (`IAssetProvider.Mounts`).
- **Sources**: the viewer compiles the game's own code in place. A provider lists what it needs in `src/Providers/<Group>/<Group>.props` (every `*.props` under `src/Providers` is imported), with paths such as `$(ProtoRoot)wildlife\src\Core\**\*.cs` and `$(SharedRoot)Items\Core\**\*.cs`. Two groups listing the same file is fine; repeats are dropped.
- **Derive entries from the game's data**, never from a hand-kept list, so new content appears by itself. Each group's test checks that every id in the game's data has an entry.

`src/Providers/Reference` holds the viewer's own scale pieces and a template.

## Tests

`tests/ViewerTest.tscn` checks the registry, builds every asset, round-trips the notes file and drives the viewer.
