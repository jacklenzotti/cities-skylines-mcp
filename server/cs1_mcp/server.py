"""FastMCP server exposing Cities: Skylines (CS1) as MCP tools.

Each tool is a thin wrapper over a bridge command (see PROTOCOL.md). The mod
must be running inside a loaded city for tools to succeed.

Config via env:
    CS1MCP_HOST  (default 127.0.0.1)
    CS1MCP_PORT  (default 50545)
"""

from __future__ import annotations

import os

from fastmcp import FastMCP
from fastmcp.exceptions import ToolError

from .bridge import BridgeClient, BridgeError

HOST = os.environ.get("CS1MCP_HOST", "127.0.0.1")
PORT = int(os.environ.get("CS1MCP_PORT", "50545"))

bridge = BridgeClient(HOST, PORT)

mcp = FastMCP(
    "cs1-mcp",
    instructions=(
        "Controls a running Cities: Skylines (CS1) game for content creation. "
        "Requires the CS1 MCP Bridge mod active in a loaded city. Call `ping` first "
        "to confirm the game is reachable. Map coordinates are world units; the "
        "playable area is roughly -8000..8000 on both x and z, centered at 0,0. "
        "Typical content loop: frame a shot with set_camera, set conditions "
        "(weather/time/sim speed), trigger an event (spawn_disaster), then screenshot."
    ),
)


def _call(cmd: str, **args):
    """Run a bridge command, translating transport errors into clean tool errors."""
    try:
        return bridge.call(cmd, **args)
    except BridgeError as exc:
        raise ToolError(str(exc)) from exc


# ============================ liveness ====================================
@mcp.tool
def ping() -> str:
    """Check the in-game bridge is reachable. Returns 'pong' when the game is ready."""
    return _call("ping")


# ======================= simulation control ===============================
@mcp.tool
def set_sim_speed(speed: int = 1, paused: bool = False) -> dict:
    """Set simulation speed and pause state.

    speed: 0..3 (0 slowest, 3 fastest). paused: freeze the simulation.
    Use for timelapses (speed=3) or holding a frame for a clean shot (paused=True).
    """
    return _call("set_sim_speed", speed=speed, paused=paused)


@mcp.tool
def set_time_of_day(hour: int, minute: int = 0) -> dict:
    """Move the in-game clock to set the sun position / lighting.

    hour: 0..23, minute: 0..59. Golden-hour shots ~6-8 or ~18-20; night ~22.
    """
    return _call("set_time_of_day", hour=hour, minute=minute)


# ============================== economy ===================================
@mcp.tool
def add_money(amount: int) -> dict:
    """Add (or, if negative, remove) city cash in whole currency units.

    Use a large positive amount for unconstrained building. Note: the displayed
    cash (get_city_stats) only updates on the next economy tick, so let the sim
    run a moment before reading it back — the credit is applied immediately.
    """
    return _call("add_money", amount=amount)


@mcp.tool
def get_city_stats() -> dict:
    """Read live city KPIs (population, money). Useful for narration and pacing."""
    return _call("get_city_stats")


# ============================== weather ===================================
@mcp.tool
def set_weather(rain: float | None = None, fog: float | None = None) -> dict:
    """Set weather targets. rain and fog are 0.0..1.0; omit one to leave it unchanged.

    rain=1 storms, fog=1 heavy fog — both strong mood/visual-variety levers.
    """
    args: dict[str, float] = {}
    if rain is not None:
        args["rain"] = rain
    if fog is not None:
        args["fog"] = fog
    return _call("set_weather", **args)


# ====================== the money genre: disasters ========================
@mcp.tool
def spawn_disaster(type: str, x: float, z: float, intensity: float = 50.0) -> dict:
    """Spawn a disaster at map coordinates for dramatic content.

    type: substring of a disaster name — Tornado, Earthquake, MeteorStrike,
          ForestFire, StructureFire, Sinkhole, Tsunami, ThunderStorm
          (exact set depends on installed DLC).
    x, z: world coordinates. intensity: 0..100.

    NOTE: this binding is best-effort and unverified — the DisasterManager
    activation call must be confirmed in Mod Tools; it may error until then.
    """
    return _call("spawn_disaster", type=type, x=x, z=z, intensity=intensity)


@mcp.tool
def list_disasters() -> dict:
    """List the disaster prefab names available in this game (for spawn_disaster `type`).

    Returns count 0 if the Natural Disasters DLC isn't installed — disasters need it.
    """
    return _call("list_disasters")


# ============================= cinematics =================================
@mcp.tool
def set_camera(
    x: float,
    z: float,
    angle_x: float = 0.0,
    angle_y: float = 30.0,
    zoom: float = 200.0,
) -> dict:
    """Position the game camera for a shot.

    x, z: world coordinates to look at. angle_x: compass rotation (deg).
    angle_y: tilt above horizon (deg, ~5 low/cinematic .. ~90 top-down).
    zoom: smaller = closer. Pair with `screenshot` to capture the framed shot.
    """
    return _call("set_camera", x=x, z=z, angle_x=angle_x, angle_y=angle_y, zoom=zoom)


@mcp.tool
def get_camera() -> dict:
    """Read the current camera position, angle, and zoom (for planning shots)."""
    return _call("get_camera")


@mcp.tool
def follow_instance(id: int, kind: str = "citizen") -> dict:
    """Make the camera follow a moving instance (the 'day in the life' shot).

    id: instance id (e.g. from find_buildings). kind: citizen | vehicle | building.

    NOTE: not yet bound in the bridge mod — will error until implemented.
    """
    return _call("follow_instance", id=id, kind=kind)


# ============================== capture ===================================
@mcp.tool
def screenshot(path: str | None = None) -> dict:
    """Capture a PNG of the current view. Returns the absolute path written.

    path: optional absolute output path; defaults to the game's persistent data
    folder with a timestamped name. The file is written at end of frame.
    """
    args = {} if path is None else {"path": path}
    return _call("screenshot", **args)


# ============================ info overlays ================================
@mcp.tool
def set_info_view(mode: str = "None") -> dict:
    """Switch the info-view overlay for visually distinct footage.

    mode: "None" clears; otherwise an InfoManager mode name — Traffic, Pollution,
    NoisePollution, LandValue, Health, Density, Heating, etc.
    """
    return _call("set_info_view", mode=mode)


# ============================== buildings =================================
@mcp.tool
def find_buildings(filter: str | None = None, limit: int = 50) -> dict:
    """List buildings (id, name, x, z), optionally filtered by name substring.

    Use the returned ids with bulldoze_building or follow_instance.
    """
    args: dict = {"limit": limit}
    if filter is not None:
        args["filter"] = filter
    return _call("find_buildings", **args)


@mcp.tool
def bulldoze_building(id: int) -> dict:
    """Demolish a building by id (e.g. to stage a before/after or clear a shot)."""
    return _call("bulldoze_building", id=id)


# =============================== networks =================================
@mcp.tool
def place_road(
    start_x: float,
    start_z: float,
    end_x: float,
    end_z: float,
    road: str = "Basic Road",
) -> dict:
    """Place a straight road segment between two world coordinates.

    NOTE: not yet bound in the bridge mod (NetManager binding is non-trivial) —
    will error until implemented.
    """
    return _call(
        "place_road",
        start_x=start_x,
        start_z=start_z,
        end_x=end_x,
        end_z=end_z,
        road=road,
    )


def main() -> None:
    mcp.run()  # stdio transport


if __name__ == "__main__":
    main()
