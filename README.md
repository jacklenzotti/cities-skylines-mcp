# cs1-mcp

Drive **Cities: Skylines (CS1)** from AI tools over the Model Context Protocol —
for AI-directed city-building, disaster cinematics, timelapses, and other
short-form content.

Two halves, one protocol:

| dir                  | what                                                                 |
|----------------------|----------------------------------------------------------------------|
| [`mod/`](mod)        | the in-game C# bridge mod — opens a loopback socket inside the game   |
| [`server/`](server)  | a FastMCP (Python) server that exposes the bridge as MCP tools       |
| [`PROTOCOL.md`](PROTOCOL.md) | the newline-delimited JSON contract between them — any client can speak it |

```
MCP client (Claude, etc.)
        │  MCP (stdio)
        ▼
   server/  (FastMCP, Python)
        │  newline-delimited JSON over TCP  (PROTOCOL.md)
        ▼
    mod/    (C# bridge, in-game)  ──►  Cities: Skylines managers
```

> **Status:** working. Verified end-to-end against a live base-game CS1
> (macOS, monolithic Unity build). This repo is the open bridge + server only —
> content/orchestration logic lives elsewhere.

## Command status

Verified live unless noted:

| command            | status                                                            |
|--------------------|-------------------------------------------------------------------|
| `ping`             | ✅ verified                                                        |
| `get_city_stats`   | ✅ verified (population is noisy for ~10s after a save loads — let it settle) |
| `set_sim_speed`    | ✅ verified                                                        |
| `set_time_of_day`  | ⚠️ applies but the sim re-drives the clock; lighting effect weak  |
| `add_money`        | ✅ verified — cash reflects after one economy tick (`LastCashAmount` lags a frame) |
| `set_weather`      | ✅ verified                                                        |
| `spawn_disaster`   | ✅ verified (needs **Natural Disasters DLC**). Sets `SelfTrigger` so it actually strikes. ⚠️ **Meteors fly in from far off-map and land at the spawn `x,z`** — they take time to arrive; the `scale` arg widens a meteor's blast |
| `list_disasters`   | ✅ verified (diagnostic — lists loaded disaster prefabs)           |
| `clear_disasters`  | ✅ verified                                                        |
| `find_meteor`      | ✅ verified — locate the in-flight meteor vehicle (`x,z,y`) to follow it down |
| `set_camera`       | ✅ verified                                                        |
| `get_camera`       | ✅ verified                                                        |
| `fly_to`           | ✅ verified — timed eased camera move, exact duration             |
| `follow_instance`  | ✅ verified (building + vehicle)                                   |
| `hide_ui`          | ✅ verified — free-camera mode, full HUD hide for clean capture    |
| `screenshot`       | ✅ verified                                                        |
| `set_info_view`    | ✅ verified (Traffic / Pollution / LandValue / … overlays)        |
| `find_buildings`   | ✅ verified                                                        |
| `bulldoze_building`| ✅ verified                                                        |
| `place_road`       | 🚧 stub (NetManager binding not yet implemented)                  |

> **Disasters note:** they fire and do real damage, but *filming* them cinematically
> is finicky — meteors arrive on a long off-map trajectory and the strike is brief.
> Driving the camera manually around the spawn point works best. City tours,
> timelapses, and info-view montages are the most reliable automated content.

## Quick start

1. **Mod:** build `mod/` against your local game, install, enable it,
   load a city (see [Build the mod](#build-the-mod)).
2. **Server:** `cd server && uv run cs1-mcp`, then point your MCP client at it
   (see [server/README.md](server/README.md)).
3. **Verify:** call the `ping` tool → `"pong"`.

## Build the mod

Requires the .NET SDK and a local CS1 install. No external packages or NuGet
restore — the project compiles fully offline against the game's own framework +
Unity assemblies (game DLLs are referenced, never vendored, per Paradox terms):

```bash
dotnet build mod/CS1McpBridge.csproj -c Release
# override the install path if the default for your OS is wrong:
dotnet build mod/CS1McpBridge.csproj -c Release -p:ManagedPath="/path/to/Cities_Data/Managed"
```

Copy `CS1McpBridge.dll` into the game's `Addons/Mods/CS1McpBridge/` folder and
enable it in Content Manager → Mods. No other mods required.

## License

MIT — see [LICENSE](LICENSE).
