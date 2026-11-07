# Credits

GalHack credits every asset it uses, including CC0 ones. Add a row for each asset when it's added to the project.

| Asset | Author | Source | Licence |
| --- | --- | --- | --- |
| Michroma font (prototypes/diegetic-ui/fonts/Michroma; also read in place by prototypes/manufacturing) | Vernon Adams, The Michroma Project Authors | https://github.com/google/fonts/tree/main/ofl/michroma | SIL Open Font License 1.1 |
| Oxanium font (prototypes/diegetic-ui/fonts/Oxanium; also read in place by prototypes/manufacturing) | Severin Meyer, The Oxanium Project Authors | https://github.com/google/fonts/tree/main/ofl/oxanium | SIL Open Font License 1.1 |
| Space Mono Bold font (prototypes/diegetic-ui/fonts/SpaceMono; also read in place by prototypes/manufacturing) | Colophon Foundry, The Space Mono Project Authors | https://github.com/google/fonts/tree/main/ofl/spacemono | SIL Open Font License 1.1 |

## Music and sound

| Work | Author | Source | Licence |
| --- | --- | --- | --- |
| Generated ambient drones (shared/Music), written for this game, no samples | The GalHack project | https://github.com/pipspcrepair-ops/GalHack | PolyForm Noncommercial, as the rest of this repository |
| All sound effects, synthesised in code | The GalHack project | https://github.com/pipspcrepair-ops/GalHack | PolyForm Noncommercial, as the rest of this repository |

Chosen tracks are listed here, one row each, as they come in. Each also has a row in `prototypes/slice/data/music.json`, and shows in the game under Stats > Credits. CC0 tracks are credited too.

| Track | Author | Source | Licence | Attribution |
| --- | --- | --- | --- | --- |

## Libraries

| Library | Author | Source | Licence | Used by |
| --- | --- | --- | --- | --- |
| Godot Engine 4.7 (.NET build) | Juan Linietsky, Ariel Manzur and the Godot contributors | https://godotengine.org | MIT | Everything |
| ENet (bundled with Godot) | Lee Salzman | http://enet.bespin.org | MIT | `prototypes/coop`: LAN, direct-IP and dedicated-server transport |
| Jolt Physics (bundled with Godot) | Jorrit Rouwe | https://github.com/jrouwe/JoltPhysics | MIT | Physics in every prototype |
| Steamworks.NET 2024.8.0 | Riley Labrecque and contributors | https://github.com/rlabrecque/Steamworks.NET (NuGet `Steamworks.NET`) | MIT | `prototypes/coop`: Steam transport |
| Steam API redistributable, `steam_api64.dll` (Steamworks SDK 1.60) | Valve Corporation | Taken from the Steamworks.NET 2024.8.0 tag by `tools/steam-coop-test.ps1 -FetchSteamApi`, which checks the exact file and Valve's signature. Not stored in the repository. | Steamworks SDK Access Agreement: Valve's redistributable, shipped alongside a Steam game | `prototypes/coop`: Steam transport |
