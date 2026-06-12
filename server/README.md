# cs1-mcp (server)

FastMCP server that exposes a running **Cities: Skylines (CS1)** game as MCP
tools. It's a thin client over the [CS1 MCP Bridge](../mod) mod — the mod must be
active in a loaded city for tools to work.

## Tools

| tool             | what it does                                            |
|------------------|---------------------------------------------------------|
| `ping`           | confirm the game/bridge is reachable                    |
| `set_sim_speed`  | speed 0–3 + pause (timelapses, holding a frame)         |
| `get_city_stats` | population + money                                       |
| `spawn_disaster` | disaster at coords *(pending bridge binding)*           |
| `set_camera`     | frame a shot (position, angle, zoom)                    |
| `screenshot`     | capture a PNG, returns the path                         |

## Run

```bash
cd server
uv run cs1-mcp          # or: pip install -e . && cs1-mcp
```

Speaks stdio. Point an MCP client at it. For Claude Code:

```bash
claude mcp add cs1 -- uv run --directory /path/to/cs1-mcp/server cs1-mcp
```

Or a raw client config entry:

```json
{
  "mcpServers": {
    "cs1": {
      "command": "uv",
      "args": ["run", "--directory", "/path/to/cs1-mcp/server", "cs1-mcp"]
    }
  }
}
```

## Config

| env var       | default     | meaning                          |
|---------------|-------------|----------------------------------|
| `CS1MCP_HOST` | `127.0.0.1` | bridge host                      |
| `CS1MCP_PORT` | `50545`     | bridge port (match the mod)      |

## Smoke test

With CS1 running, the mod enabled, and a city loaded, call `ping` → `"pong"`.
If you get a connection error, the game isn't reachable — check the mod is
enabled and a city (not the main menu) is loaded.
