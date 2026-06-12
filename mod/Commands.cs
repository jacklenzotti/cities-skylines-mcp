using System;
using System.IO;
using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;

namespace CS1McpBridge
{
    /// <summary>
    /// Parses one request line, runs the named command on the correct game thread,
    /// and serialises the response. This is the file you extend to add new tools —
    /// each case is a self-contained binding to a Cities: Skylines manager.
    ///
    /// NOTE: bindings marked TODO(verify) reference game fields/methods whose exact
    /// names depend on your installed game version. Confirm them in the Mod Tools
    /// console (or ILSpy on Assembly-CSharp.dll) before relying on them. Manager
    /// *names* are stable across versions; field names and overloads are not.
    /// </summary>
    public static class Commands
    {
        public static string Handle(string requestLine)
        {
            JSONNode req;
            try { req = JSON.Parse(requestLine); }
            catch (Exception e) { return Error(null, "bad json: " + e.Message); }

            JSONNode id = req.HasKey("id") ? req["id"] : null;
            string cmd = req["cmd"].Value;
            JSONNode args = req.HasKey("args") ? req["args"] : new JSONObject();

            try
            {
                JSONNode result = Run(cmd, args);
                var res = new JSONObject();
                if (id != null) res["id"] = id;
                res["ok"] = true;
                res["result"] = result ?? JSONNull.CreateOrGet();
                return res.ToString();
            }
            catch (Exception e)
            {
                return Error(id, e.Message);
            }
        }

        static JSONNode Run(string cmd, JSONNode a)
        {
            switch (cmd)
            {
                // ===== liveness ======================================================
                case "ping":
                    return "pong";

                // ===== simulation control ============================================
                case "set_sim_speed":
                {
                    int speed = Mathf.Clamp(a["speed"].AsInt, 0, 3);
                    bool paused = a["paused"].AsBool;
                    return Sim(() =>
                    {
                        var sm = Singleton<SimulationManager>.instance;
                        sm.SelectedSimulationSpeed = speed;   // TODO(verify) field name
                        sm.ForcedSimulationPaused = paused;   // TODO(verify) field name
                        return Obj("speed", speed, "paused", paused);
                    });
                }

                case "set_time_of_day":
                {
                    // Move the in-game clock to a given hour; drives sun position / lighting.
                    int hour = Mathf.Clamp(a["hour"].AsInt, 0, 23);
                    int minute = a.HasKey("minute") ? Mathf.Clamp(a["minute"].AsInt, 0, 59) : 0;
                    return Sim(() =>
                    {
                        var sm = Singleton<SimulationManager>.instance;
                        // TODO(verify): m_currentGameTime is the DateTime the day/night cycle reads.
                        var now = sm.m_currentGameTime;
                        sm.m_currentGameTime = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
                        return Obj("hour", hour, "minute", minute);
                    });
                }

                // ===== economy =======================================================
                case "add_money":
                {
                    // Positive adds cash, negative removes. Whole currency units.
                    long amount = a["amount"].AsLong;
                    return Sim(() =>
                    {
                        var em = Singleton<EconomyManager>.instance;
                        // Cash is stored in cents. AddResource(PublicIncome,…) is how the sim
                        // itself credits cash, so the change isn't overwritten next frame.
                        long cents = amount * 100L;
                        if (cents > int.MaxValue) cents = int.MaxValue;
                        if (cents < int.MinValue) cents = int.MinValue;
                        em.AddResource(EconomyManager.Resource.PublicIncome, (int)cents,
                            ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.None);
                        return Obj("added", amount, "money", em.LastCashAmount);
                    });
                }

                // ===== read city KPIs ================================================
                case "get_city_stats":
                {
                    return Sim(() =>
                    {
                        // TODO(verify): district[0] holds citywide aggregates; confirm field paths.
                        var dm = Singleton<DistrictManager>.instance;
                        return Obj(
                            "population", dm.m_districts.m_buffer[0].m_populationData.m_finalCount,
                            "money", Singleton<EconomyManager>.instance.LastCashAmount); // TODO(verify)
                    });
                }

                // ===== weather =======================================================
                case "set_weather":
                {
                    // rain/fog as 0..1. Both optional; omitted leaves the current target.
                    bool hasRain = a.HasKey("rain");
                    bool hasFog = a.HasKey("fog");
                    float rain = a["rain"].AsFloat;
                    float fog = a["fog"].AsFloat;
                    return Sim(() =>
                    {
                        var wm = Singleton<WeatherManager>.instance;
                        // TODO(verify): target fields drive the smooth transition.
                        if (hasRain) wm.m_targetRain = Mathf.Clamp01(rain);
                        if (hasFog) wm.m_targetFog = Mathf.Clamp01(fog);
                        return Obj("rain", wm.m_targetRain, "fog", wm.m_targetFog);
                    });
                }

                // ===== the money genre: spawn a disaster =============================
                case "spawn_disaster":
                {
                    string type = a["type"].Value;          // e.g. "Tornado", "Earthquake", "MeteorStrike"
                    float x = a["x"].AsFloat;
                    float z = a["z"].AsFloat;
                    float intensity = a.HasKey("intensity") ? a["intensity"].AsFloat : 50f;
                    return Sim(() =>
                    {
                        // Best-effort binding — confirm the activation call in Mod Tools.
                        DisasterInfo info = FindDisasterInfo(type);
                        if (info == null) throw new Exception("no DisasterInfo matching '" + type + "'");

                        var dm = Singleton<DisasterManager>.instance;
                        ushort dId;
                        if (!dm.CreateDisaster(out dId, info))   // TODO(verify) signature
                            throw new Exception("CreateDisaster failed (disaster limit reached?)");

                        byte i255 = (byte)Mathf.Clamp(intensity * 2.55f, 1f, 255f);
                        dm.m_disasters.m_buffer[dId].m_intensity = i255;                       // TODO(verify) field
                        dm.m_disasters.m_buffer[dId].m_targetPosition = new Vector3(x, 0f, z); // TODO(verify) field
                        // TODO(verify): activation may be StartNow / ActivateNow / StartDisaster.
                        info.m_disasterAI.StartNow(dId, ref dm.m_disasters.m_buffer[dId]);
                        return Obj("id", dId, "type", info.name, "intensity", (int)i255);
                    });
                }

                case "list_disasters":
                {
                    // Diagnostic: which disaster prefabs are loaded (needs Natural Disasters DLC).
                    return Sim(() =>
                    {
                        var arr = new JSONArray();
                        int n = PrefabCollection<DisasterInfo>.LoadedCount();
                        for (int i = 0; i < n; i++)
                        {
                            var info = PrefabCollection<DisasterInfo>.GetLoaded((uint)i);
                            if (info != null) arr.Add(info.name);
                        }
                        var r = new JSONObject();
                        r["count"] = arr.Count;
                        r["disasters"] = arr;
                        return (JSONNode)r;
                    });
                }

                // ===== cinematics: camera (MAIN thread) =============================
                case "set_camera":
                {
                    float x = a["x"].AsFloat;
                    float z = a["z"].AsFloat;
                    float angleX = a.HasKey("angle_x") ? a["angle_x"].AsFloat : 0f;
                    float angleY = a.HasKey("angle_y") ? a["angle_y"].AsFloat : 30f;
                    float zoom = a.HasKey("zoom") ? a["zoom"].AsFloat : 200f;
                    return Main(() =>
                    {
                        var cc = Cam();
                        cc.m_targetPosition = new Vector3(x, cc.m_targetPosition.y, z); // TODO(verify) fields
                        cc.m_targetAngle = new Vector2(angleX, angleY);
                        cc.m_targetSize = zoom;
                        return Obj("x", x, "z", z, "zoom", zoom);
                    });
                }

                case "get_camera":
                {
                    return Main(() =>
                    {
                        var cc = Cam();
                        var p = cc.m_currentPosition;       // TODO(verify) fields
                        var ang = cc.m_currentAngle;
                        return Obj("x", p.x, "z", p.z, "angle_x", ang.x, "angle_y", ang.y, "zoom", cc.m_currentSize);
                    });
                }

                case "follow_instance":
                {
                    // Follow a building / vehicle / citizen by id (the "day in the life" shot).
                    // id <= 0 clears the follow and frees the camera.
                    int fid = a["id"].AsInt;
                    string kind = a.HasKey("kind") ? a["kind"].Value : "vehicle";
                    return Main(() =>
                    {
                        var cc = Cam();
                        if (fid <= 0) { cc.ClearTarget(); return Obj("following", false); }

                        InstanceID iid = default(InstanceID);
                        Vector3 pos = cc.m_currentPosition;
                        switch (kind.ToLower())
                        {
                            case "building":
                                iid.Building = (ushort)fid;
                                pos = Singleton<BuildingManager>.instance.m_buildings.m_buffer[fid].m_position;
                                break;
                            case "vehicle":
                                iid.Vehicle = (ushort)fid;
                                pos = Singleton<VehicleManager>.instance.m_vehicles.m_buffer[fid].GetLastFramePosition();
                                break;
                            case "citizen":
                                iid.Citizen = (uint)fid;
                                break;
                            default:
                                throw new Exception("kind must be building, vehicle, or citizen");
                        }
                        cc.SetTarget(iid, pos, true);
                        return Obj("following", true, "id", fid, "kind", kind);
                    });
                }

                case "fly_to":
                {
                    // Smooth, timed camera move to a target — for cinematic sweeps.
                    float fx = a["x"].AsFloat;
                    float fz = a["z"].AsFloat;
                    float fangleX = a.HasKey("angle_x") ? a["angle_x"].AsFloat : 0f;
                    float fangleY = a.HasKey("angle_y") ? a["angle_y"].AsFloat : 30f;
                    float fzoom = a.HasKey("zoom") ? a["zoom"].AsFloat : 200f;
                    float seconds = a.HasKey("seconds") ? a["seconds"].AsFloat : 3f;

                    // Register the animation on the main thread (quick), then block this
                    // socket thread until the per-frame Tick finishes it.
                    Dispatch.Run(RunOn.Main, () =>
                    {
                        var cc = Cam();
                        cc.ClearTarget(); // break any active follow first
                        CameraAnim.Begin(cc, new Vector3(fx, cc.m_currentPosition.y, fz),
                            new Vector2(fangleX, fangleY), fzoom, seconds);
                        return (JSONNode)new JSONObject();
                    });
                    if (!CameraAnim.WaitFor((int)(seconds * 1000f) + 3000))
                        throw new TimeoutException("fly_to did not complete in time");
                    return Obj("x", fx, "z", fz, "zoom", fzoom, "seconds", seconds);
                }

                case "hide_ui":
                {
                    // Toggle the whole game UI for clean capture. hidden=true hides it.
                    // Deactivate the UIView GameObject subtree. This hides floating panels,
                    // overlays and tutorial popups, but the game re-asserts the docked bottom
                    // toolbar each frame, so it isn't a full hide.
                    // TODO(full-hide): switch to free-camera mode (CameraController.m_freeCamera)
                    //   which hides the entire HUD; verify it doesn't fight fly_to/SetTarget.
                    // We cache the views (FindObjectsOfType can't see them once inactive).
                    bool hidden = a.HasKey("hidden") ? a["hidden"].AsBool : true;
                    return Main(() =>
                    {
                        if (_uiViews == null || _uiViews.Length == 0)
                            _uiViews = UnityEngine.Object.FindObjectsOfType<UIView>();
                        foreach (var v in _uiViews)
                            if (v != null) v.gameObject.SetActive(!hidden);
                        return Obj("hidden", hidden, "views", _uiViews.Length);
                    });
                }

                // ===== capture (MAIN thread) ========================================
                case "screenshot":
                {
                    string path = a.HasKey("path")
                        ? a["path"].Value
                        : Path.Combine(Application.persistentDataPath,
                            "cs1mcp_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                    return Main(() =>
                    {
                        Application.CaptureScreenshot(path); // Unity 5.x/2017 monolithic API; writes at end of frame
                        return Obj("path", path);
                    });
                }

                // ===== info overlays (MAIN thread) ==================================
                case "set_info_view":
                {
                    // mode: "None" clears; otherwise an InfoManager.InfoMode name
                    // (Traffic, Pollution, NoisePollution, LandValue, Health, ...).
                    string mode = a.HasKey("mode") ? a["mode"].Value : "None";
                    return Main(() =>
                    {
                        var im = Singleton<InfoManager>.instance;
                        var infoMode = (InfoManager.InfoMode)Enum.Parse(typeof(InfoManager.InfoMode), mode, true);
                        im.SetCurrentMode(infoMode, InfoManager.SubInfoMode.Default); // TODO(verify) overload
                        return Obj("mode", mode);
                    });
                }

                // ===== buildings ====================================================
                case "find_buildings":
                {
                    string filter = a.HasKey("filter") ? a["filter"].Value : null;
                    int limit = a.HasKey("limit") ? a["limit"].AsInt : 50;
                    return Sim(() =>
                    {
                        var bm = Singleton<BuildingManager>.instance;
                        var arr = new JSONArray();
                        var buf = bm.m_buildings.m_buffer;
                        for (int id = 1; id < buf.Length && arr.Count < limit; id++)
                        {
                            if ((buf[id].m_flags & Building.Flags.Created) == 0) continue;
                            string name = bm.GetBuildingName((ushort)id, InstanceID.Empty); // TODO(verify)
                            if (!string.IsNullOrEmpty(filter) &&
                                (name == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0))
                                continue;
                            var pos = buf[id].m_position;
                            arr.Add(Obj("id", id, "name", name ?? "", "x", pos.x, "z", pos.z));
                        }
                        var r = new JSONObject();
                        r["count"] = arr.Count;
                        r["buildings"] = arr;
                        return (JSONNode)r;
                    });
                }

                case "bulldoze_building":
                {
                    int id = a["id"].AsInt;
                    return Sim(() =>
                    {
                        Singleton<BuildingManager>.instance.ReleaseBuilding((ushort)id); // TODO(verify)
                        return Obj("id", id, "released", true);
                    });
                }

                // ===== networks =====================================================
                case "place_road":
                {
                    // STUB: placing a road segment needs a NetInfo prefab + two nodes via
                    // NetManager.CreateNode / CreateSegment (or NetTool). Non-trivial —
                    // sketch the binding against your build in Mod Tools before implementing:
                    //   NetInfo info = PrefabCollection<NetInfo>.FindLoaded("Basic Road");
                    //   nm.CreateNode(out a, ref rng, info, startPos, frame);
                    //   nm.CreateNode(out b, ref rng, info, endPos, frame);
                    //   nm.CreateSegment(out seg, ref rng, info, a, b, dir, dir2, frame, frame, invert);
                    throw new NotImplementedException(
                        "place_road not yet bound — confirm NetManager.CreateNode/CreateSegment signatures in Mod Tools.");
                }

                default:
                    throw new Exception("unknown command: " + cmd);
            }
        }

        // Cached UI roots so hide_ui can restore views it deactivated.
        static UIView[] _uiViews;

        // -- thread helpers --------------------------------------------------------
        static JSONNode Sim(Func<JSONNode> work) => Dispatch.Run(RunOn.Sim, work);
        static JSONNode Main(Func<JSONNode> work) => Dispatch.Run(RunOn.Main, work);

        static CameraController Cam() => ToolsModifierControl.cameraController; // TODO(verify) accessor

        // -- prefab lookup ---------------------------------------------------------
        static DisasterInfo FindDisasterInfo(string type)
        {
            int count = PrefabCollection<DisasterInfo>.LoadedCount();
            for (int i = 0; i < count; i++)
            {
                var info = PrefabCollection<DisasterInfo>.GetLoaded((uint)i);
                if (info != null && info.name != null &&
                    info.name.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0)
                    return info;
            }
            return null;
        }

        // -- JSON helpers ----------------------------------------------------------
        /// <summary>Builds a JSONObject from alternating key/value pairs.</summary>
        static JSONNode Obj(params object[] kv)
        {
            var o = new JSONObject();
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                string k = (string)kv[i];
                object v = kv[i + 1];
                if (v is int iv) o[k] = iv;
                else if (v is long lv) o[k] = lv.ToString();   // JS-safe: longs as strings
                else if (v is float fv) o[k] = fv;
                else if (v is double dv) o[k] = (float)dv;
                else if (v is bool bv) o[k] = bv;
                else if (v is JSONNode nv) o[k] = nv;
                else o[k] = v == null ? "" : v.ToString();
            }
            return o;
        }

        static string Error(JSONNode id, string message)
        {
            var res = new JSONObject();
            if (id != null) res["id"] = id;
            res["ok"] = false;
            res["error"] = message;
            return res.ToString();
        }
    }
}
