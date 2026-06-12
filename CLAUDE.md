# CLAUDE.md — cs1-mcp

Monorepo for driving Cities: Skylines (CS1) over MCP. Two halves talking the
newline-delimited JSON protocol in `PROTOCOL.md`:
- `mod/` — in-game C# bridge mod (opens a loopback TCP socket).
- `server/` — FastMCP (Python) server exposing the bridge as MCP tools.

## What this repo is / isn't
- **Is:** the open bridge mod + MCP server + wire protocol.
- **Isn't:** content/orchestration logic (prompts, shot direction, editing,
  publishing) — that stays private. Only the bridge primitives are open source.

## Layout
- `mod/Mod.cs` — IUserMod entry; Loader (start/stop server on city load);
  Threading (main-thread pump via OnUpdate).
- `mod/BridgeServer.cs` — loopback TCP, newline-delimited JSON, one thread/client.
- `mod/Dispatch.cs` — marshals socket calls onto Sim or Main thread, blocks for result.
- `mod/Commands.cs` — the command surface. **Extend here** to add tools.
- `mod/Vendor/` — drop-in third-party single files (SimpleJSON.cs, not committed).
- `server/cs1_mcp/bridge.py` — TCP client for the protocol (reused connection + lock).
- `server/cs1_mcp/server.py` — FastMCP tools, one per bridge command. **Extend here too.**

## Adding a command (touches both halves + the contract)
1. `mod/Commands.cs` — add a `case` in `Run`, routed via `Dispatch.Run(RunOn.Sim|Main, …)`.
2. `server/cs1_mcp/server.py` — add a `@mcp.tool` wrapper calling `_call("<cmd>", …)`.
3. `PROTOCOL.md` — add a row. The protocol is the contract; keep it in sync.

## Threading model (mod side, important)
- `RunOn.Sim` → `SimulationManager.AddAction`. For sim state: economy, disasters,
  buildings, sim speed.
- `RunOn.Main` → queue drained in `Threading.OnUpdate`. For camera + screenshots.
- Never call game managers from a socket worker thread directly — always via `Dispatch.Run`.

## Conventions
- Game-API calls whose signatures depend on the installed build are marked
  `// TODO(verify)`. Don't present these as working until confirmed in Mod Tools
  or ILSpy against `Assembly-CSharp.dll`. Manager names are stable; field names
  and overloads are not.

## Build / run
- Mod: `dotnet build mod/CS1McpBridge.csproj -c Release` — links against local
  game DLLs via `ManagedPath` (`Directory.Build.props`). Assemblies never committed.
- Server: `cd server && uv run cs1-mcp` (stdio). Env: `CS1MCP_HOST`, `CS1MCP_PORT`.

## Can't be fully verified headless
The mod build needs the user's CS1 Managed assemblies and binding verification is
manual (in a loaded city). The server's Python compiles/tests independently, but
end-to-end requires the running game.
