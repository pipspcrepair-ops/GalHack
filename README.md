# GalHack

A first-person space survival sandbox for one group of friends: crash on a hostile world, build your way up from a hover rover to a capital ship, and explore a procedurally generated galaxy full of strange worlds, ruins and wrecks.

GalHack is free and never sold. Every asset is credited in [CREDITS.md](CREDITS.md).

## Design

The full game design document lives here: https://claude.ai/artifact/NayZQcRHckbPwmMSyygC97

Guiding rule 1: **stable and smooth first.** Nothing a player builds, drops or stores ever disappears.

## Stack

- Godot 4.7 (.NET build), C# for all game code
- Steam co-op for a small group of friends
- Everything that can be data is data (text files with schemas)

## Layout

| Folder | What's in it |
| --- | --- |
| `prototypes/` | Throwaway prototypes that prove one risky idea each |
| `prototypes/walkable-ship/` | Walking around inside a ship while it flies, rolls and warps |
| `prototypes/planet-generator/` | Seeded planets in five archetypes, from orbit down to streamed terrain |
| `prototypes/terrain-digging/` | Remove-only grid digging: smooth Surface Nets terrain, anchors, bedrock, atomic audited saves |
| `prototypes/encounters/` | Encounter director: seeded, territory-aware spawns out of sight, fuzzy signal leads, nothing a player touched is ever cleaned up |
| `prototypes/building/` | Block building: the structure model every ship, base, POI and wreck is made of |
| `prototypes/boarding/` | Boarding a Vharn frigate: breach, fight to the core, hijack it in three stages or shoot it derelict |

## Requirements

- [Godot 4.7.2 .NET](https://godotengine.org/download) (the "mono" / .NET build)
- [.NET SDK](https://dotnet.microsoft.com/download) 8 or newer

## Testing

Every prototype has headless test scenes (`tests/*Test.tscn`, `tests/*Probe.tscn`) that exit 0 on success. Run them all before any merge to main:

```powershell
.\tools\test-all.ps1
```

Run one with `.\tools\test.ps1 -Project prototypes/<name> -Scene res://tests/<Scene>.tscn`.
