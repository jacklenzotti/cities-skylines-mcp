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
        "Typical content loop: build/modify the city (place_road, place_building), "
        "frame a shot with set_camera or fly_to, set conditions (weather/time/sim "
        "speed), then screenshot. Use list_prefabs to discover road/building names."
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
def follow_instance(id: int, kind: str = "vehicle") -> dict:
    """Make the camera follow a moving instance (the 'day in the life' shot).

    id: instance id (e.g. a vehicle id, or a building id from find_buildings).
    kind: vehicle | building | citizen. Pass id=0 to stop following and free the camera.
    """
    return _call("follow_instance", id=id, kind=kind)


@mcp.tool
def fly_to(
    x: float,
    z: float,
    angle_x: float = 0.0,
    angle_y: float = 30.0,
    zoom: float = 200.0,
    seconds: float = 3.0,
) -> dict:
    """Smoothly glide the camera to a target over `seconds` (cinematic sweep).

    Eased ease-in-out move; blocks until the move finishes, so you can chain
    fly_to → screenshot. Same framing args as set_camera, plus duration.
    """
    return _call(
        "fly_to", x=x, z=z, angle_x=angle_x, angle_y=angle_y, zoom=zoom, seconds=seconds
    )


@mcp.tool
def hide_ui(hidden: bool = True) -> dict:
    """Hide or show the entire game UI/HUD for clean capture.

    hidden=True hides all panels and the HUD; hidden=False restores them.
    Hide before screenshots/recording, show again when done.
    """
    return _call("hide_ui", hidden=hidden)


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


@mcp.tool
def place_building(building: str, x: float, z: float, angle: float = 0.0) -> dict:
    """Place a building / landmark / park / service at a world coordinate.

    building: name (or substring) of a BuildingInfo prefab — use list_prefabs
              (kind="building") to discover names. angle: facing, in degrees.
    Returns the new building id (usable with follow_instance / bulldoze_building).
    """
    return _call("place_building", building=building, x=x, z=z, angle=angle)


# =============================== networks =================================
@mcp.tool
def place_road(
    start_x: float,
    start_z: float,
    end_x: float,
    end_z: float,
    road: str = "Basic Road",
    middle_x: float | None = None,
    middle_z: float | None = None,
) -> dict:
    """Place a road segment between two world coordinates.

    road: name (or substring) of a NetInfo prefab (use list_prefabs kind="road"),
    e.g. "Basic Road", "Highway", "Pedestrian Path".
    middle_x/middle_z: optional control point. If given, the segment curves (bows
    toward it) instead of running straight. Returns the new segment + node ids.
    """
    args: dict = dict(
        start_x=start_x, start_z=start_z, end_x=end_x, end_z=end_z, road=road
    )
    if middle_x is not None and middle_z is not None:
        args["middle_x"] = middle_x
        args["middle_z"] = middle_z
    return _call("place_road", **args)


@mcp.tool
def place_path(points: list[list[float]], road: str = "Basic Road") -> dict:
    """Build a connected road through a list of [x, z] waypoints, smoothly.

    points: list of [x, z] pairs (2..64). Chains nodes and segments through them
    with smooth tangents, so you can build curves, roundabouts (points around a
    circle), grids, or any shape. road: NetInfo name (use list_prefabs kind="road").
    Returns the road name, node count, and the list of created segment ids.
    """
    return _call("place_path", points=points, road=road)


# ============================ prefab discovery ============================
@mcp.tool
def list_prefabs(kind: str = "road", filter: str | None = None, limit: int = 80) -> dict:
    """List loadable prefab names for placement.

    kind: "road" (for place_road) or "building" (for place_building).
    filter: optional name substring. Returns {count, prefabs[]}.
    """
    args: dict = {"kind": kind, "limit": limit}
    if filter is not None:
        args["filter"] = filter
    return _call("list_prefabs", **args)


def main() -> None:
    mcp.run()  # stdio transport


if __name__ == "__main__":
    main()
