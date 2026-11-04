# Credits

GalHack credits every asset it uses, including CC0 ones. Add a row for each asset when it's added to the project.

| Asset | Author | Source | Licence |
| --- | --- | --- | --- |

## Libraries

| Library | Author | Source | Licence | Used by |
| --- | --- | --- | --- | --- |
| Godot Engine 4.7 (.NET build) | Juan Linietsky, Ariel Manzur and the Godot contributors | https://godotengine.org | MIT | Everything |
| ENet (bundled with Godot) | Lee Salzman | http://enet.bespin.org | MIT | `prototypes/coop`: LAN, direct-IP and dedicated-server transport |
| Jolt Physics (bundled with Godot) | Jorrit Rouwe | https://github.com/jrouwe/JoltPhysics | MIT | Physics in every prototype |
| Steamworks.NET 2024.8.0 | Riley Labrecque and contributors | https://github.com/rlabrecque/Steamworks.NET (NuGet `Steamworks.NET`) | MIT | `prototypes/coop`: Steam transport |
| Steam API redistributable, `steam_api64.dll` (Steamworks SDK 1.60) | Valve Corporation | Taken from the Steamworks.NET 2024.8.0 tag by `tools/steam-coop-test.ps1 -FetchSteamApi`, which checks the exact file and Valve's signature. Not stored in the repository. | Steamworks SDK Access Agreement: Valve's redistributable, shipped alongside a Steam game | `prototypes/coop`: Steam transport |
