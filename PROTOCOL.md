# CS1 MCP Bridge — wire protocol

The in-game mod listens on `127.0.0.1:50545` (override with the `CS1MCP_PORT`
env var). It speaks **newline-delimited JSON**: one request object per line in,
one response object per line out. This is the contract — any client in any
language can drive the game by speaking it; the MCP server is just one such client.

## Request

```json
{ "id": 1, "cmd": "set_sim_speed", "args": { "speed": 3, "paused": false } }
```

- `id` — optional; echoed back on the matching response so clients can correlate.
- `cmd` — command name (see below).
- `args` — command-specific object; omitted args fall back to documented defaults.

## Response

Success:
```json
{ "id": 1, "ok": true, "result": { "speed": 3, "paused": false } }
```

Failure:
```json
{ "id": 1, "ok": false, "error": "unknown command: foo" }
```

## Commands (v0)

| cmd                | thread | args                                              | result                              |
|--------------------|--------|---------------------------------------------------|-------------------------------------|
| `ping`             | —      | —                                                 | `"pong"`                            |
| `set_sim_speed`    | sim    | `speed` 0–3, `paused` bool                        | `{ speed, paused }`                 |
| `set_time_of_day`  | sim    | `hour` 0–23, `minute` 0–59                        | `{ hour, minute }`                  |
| `add_money`        | sim    | `amount` (whole units; negative removes)          | `{ added }`                         |
| `get_city_stats`   | sim    | —                                                 | `{ population, money }`             |
| `set_weather`      | sim    | `rain` 0–1, `fog` 0–1 (both optional)             | `{ rain, fog }`                     |
| `spawn_disaster`   | sim    | `type`, `x`, `z`, `intensity` 10–100, `scale` (meteor blast ×) | `{ id, type, intensity, scale }` †  |
| `list_disasters`   | sim    | —                                                 | `{ count, disasters[] }`            |
| `clear_disasters`  | sim    | —                                                 | `{ cleared }`                       |
| `set_camera`       | main   | `x`, `z`, `angle_x`, `angle_y`, `zoom`            | `{ x, z, zoom }`                    |
| `get_camera`       | main   | —                                                 | `{ x, z, angle_x, angle_y, zoom }`  |
| `fly_to`           | main   | `x`, `z`, `angle_x`, `angle_y`, `zoom`, `seconds` | `{ x, z, zoom, seconds }`           |
| `follow_instance`  | main   | `id` (0 clears), `kind` (vehicle/building/citizen)| `{ following, id, kind }`           |
| `hide_ui`          | main   | `hidden` (bool, default true)                     | `{ hidden }`                        |
| `screenshot`       | main   | `path` (optional)                                 | `{ path }`                          |
| `set_info_view`    | main   | `mode` (`None`/`Traffic`/`Pollution`/…)           | `{ mode }`                          |
| `find_buildings`   | sim    | `filter` (optional), `limit` (default 50)         | `{ count, buildings[] }`            |
| `bulldoze_building`| sim    | `id`                                              | `{ id, released }`                  |
| `place_road`       | sim    | `start_x`, `start_z`, `end_x`, `end_z`, `road`    | — ‡ *(not yet bound)*               |

**Thread** indicates which game thread the command runs on internally (sim state
vs. render/camera). Clients don't need to care — it's handled by the bridge — but
it explains why a few commands (camera, screenshot) only take effect on frame
boundaries.

† `spawn_disaster` requires the Natural Disasters DLC (use `list_disasters`;
count 0 = no DLC). It mirrors the game's own trigger (SelfTrigger flag + StartNow).
`scale` boosts a meteor's blast radius (meteors only) for one giant impact.
‡ `place_road` is stubbed — it returns an error until the `NetManager`
CreateNode/CreateSegment binding is implemented.

Every other command's game-API calls are also marked `// TODO(verify)` in the mod:
manager names are stable across game versions, but field names and method overloads
are not, so confirm them against your build before trusting a command.

## Quick manual test

```bash
# with a city loaded:
printf '{"id":1,"cmd":"ping"}\n' | nc 127.0.0.1 50545
# -> {"id":1,"ok":true,"result":"pong"}
```
