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

> **Status:** scaffold. Socket/threading/protocol layers are complete on both
> sides. The game-API bindings in the mod are marked `// TODO(verify)` and must
> be confirmed in **Mod Tools** against your installed game version. `ping` works
> end-to-end today; the rest are wired but unverified. `spawn_disaster` is not yet
> bound. This repo is the open bridge + server only — content/orchestration logic
> lives elsewhere.

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
