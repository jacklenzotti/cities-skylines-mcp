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
    /// Simulation-state commands run on the sim thread (Sim); camera/screenshot/UI
    /// run on the main thread (Main). See Dispatch.
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
                        sm.SelectedSimulationSpeed = speed;
                        sm.ForcedSimulationPaused = paused;
                        return Obj("speed", speed, "paused", paused);
                    });
                }

                case "set_time_of_day":
                {
                    int hour = Mathf.Clamp(a["hour"].AsInt, 0, 23);
                    int minute = a.HasKey("minute") ? Mathf.Clamp(a["minute"].AsInt, 0, 59) : 0;
                    return Sim(() =>
                    {
                        var sm = Singleton<SimulationManager>.instance;
                        var now = sm.m_currentGameTime;
                        sm.m_currentGameTime = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);
                        return Obj("hour", hour, "minute", minute);
                    });
                }

                // ===== economy =======================================================
                case "add_money":
                {
                    long amount = a["amount"].AsLong;   // whole currency units; negative removes
                    return Sim(() =>
                    {
                        var em = Singleton<EconomyManager>.instance;
                        long cents = amount * 100L;      // cash is stored in cents
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
                        var dm = Singleton<DistrictManager>.instance;
                        return Obj(
                            "population", dm.m_districts.m_buffer[0].m_populationData.m_finalCount,
                            "money", Singleton<EconomyManager>.instance.LastCashAmount);
                    });
                }

                // ===== weather =======================================================
                case "set_weather":
                {
                    bool hasRain = a.HasKey("rain");
                    bool hasFog = a.HasKey("fog");
                    float rain = a["rain"].AsFloat;
                    float fog = a["fog"].AsFloat;
                    return Sim(() =>
                    {
                        var wm = Singleton<WeatherManager>.instance;
                        if (hasRain) wm.m_targetRain = Mathf.Clamp01(rain);
                        if (hasFog) wm.m_targetFog = Mathf.Clamp01(fog);
                        return Obj("rain", wm.m_targetRain, "fog", wm.m_targetFog);
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
                        cc.m_targetPosition = new Vector3(x, cc.m_targetPosition.y, z);
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
                        var p = cc.m_currentPosition;
                        var ang = cc.m_currentAngle;
                        return Obj("x", p.x, "z", p.z, "angle_x", ang.x, "angle_y", ang.y, "zoom", cc.m_currentSize);
                    });
                }

                case "follow_instance":
                {
                    // Follow a building / vehicle / citizen by id. id <= 0 clears the follow.
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

                    Dispatch.Run(RunOn.Main, () =>
                    {
                        var cc = Cam();
                        cc.ClearTarget();
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
                    // Free-camera mode hides the whole HUD (toolbar + labels) for clean capture.
                    bool hidden = a.HasKey("hidden") ? a["hidden"].AsBool : true;
                    return Main(() =>
                    {
                        Cam().m_freeCamera = hidden;
                        if (_uiViews == null || _uiViews.Length == 0)
                            _uiViews = UnityEngine.Object.FindObjectsOfType<UIView>();
                        foreach (var v in _uiViews)
                            if (v != null) v.gameObject.SetActive(!hidden);
                        return Obj("hidden", hidden);
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
                        Application.CaptureScreenshot(path); // Unity 5.x/2017 monolithic API
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
                        im.SetCurrentMode(infoMode, InfoManager.SubInfoMode.Default);
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
                            string name = bm.GetBuildingName((ushort)id, InstanceID.Empty);
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
                        Singleton<BuildingManager>.instance.ReleaseBuilding((ushort)id);
                        return Obj("id", id, "released", true);
                    });
                }

                case "place_building":
                {
                    // Place a building/landmark/park at a point. angle is degrees.
                    string name = a["building"].Value;
                    float x = a["x"].AsFloat, z = a["z"].AsFloat;
                    float angle = a.HasKey("angle") ? a["angle"].AsFloat : 0f;
                    return Sim(() =>
                    {
                        BuildingInfo info = FindBuilding(name);
                        if (info == null) throw new Exception("no building prefab matching '" + name + "' (try list_prefabs kind=building)");
                        var bm = Singleton<BuildingManager>.instance;
                        var sm = Singleton<SimulationManager>.instance;
                        Vector3 p = new Vector3(x, 0f, z);
                        p.y = Singleton<TerrainManager>.instance.SampleRawHeightSmoothWithWater(p, false, 0f);
                        ushort b;
                        if (!bm.CreateBuilding(out b, ref sm.m_randomizer, info, p, angle * Mathf.Deg2Rad, 0, sm.m_currentBuildIndex))
                            throw new Exception("CreateBuilding failed (no room / invalid spot?)");
                        sm.m_currentBuildIndex++;
                        return Obj("building", info.name, "id", b, "x", p.x, "z", p.z);
                    });
                }

                // ===== networks =====================================================
                case "place_road":
                {
                    // Place a straight road segment between two world points.
                    string road = a.HasKey("road") ? a["road"].Value : "Basic Road";
                    float sx = a["start_x"].AsFloat, sz = a["start_z"].AsFloat;
                    float ex = a["end_x"].AsFloat, ez = a["end_z"].AsFloat;
                    return Sim(() =>
                    {
                        NetInfo info = FindNet(road);
                        if (info == null) throw new Exception("no road prefab matching '" + road + "' (try list_prefabs kind=road)");
                        var nm = Singleton<NetManager>.instance;
                        var sm = Singleton<SimulationManager>.instance;
                        var tm = Singleton<TerrainManager>.instance;
                        Vector3 s = new Vector3(sx, 0f, sz); s.y = tm.SampleRawHeightSmoothWithWater(s, false, 0f);
                        Vector3 e = new Vector3(ex, 0f, ez); e.y = tm.SampleRawHeightSmoothWithWater(e, false, 0f);
                        Vector3 dir = e - s; dir.y = 0f;
                        if (dir.sqrMagnitude < 1f) throw new Exception("start and end are too close");
                        dir.Normalize();

                        ushort sn, en, seg;
                        if (!nm.CreateNode(out sn, ref sm.m_randomizer, info, s, sm.m_currentBuildIndex))
                            throw new Exception("CreateNode (start) failed");
                        sm.m_currentBuildIndex++;
                        if (!nm.CreateNode(out en, ref sm.m_randomizer, info, e, sm.m_currentBuildIndex))
                            throw new Exception("CreateNode (end) failed");
                        sm.m_currentBuildIndex++;
                        if (!nm.CreateSegment(out seg, ref sm.m_randomizer, info, sn, en, dir, -dir,
                                sm.m_currentBuildIndex, sm.m_currentBuildIndex, false))
                            throw new Exception("CreateSegment failed");
                        sm.m_currentBuildIndex++;
                        return Obj("road", info.name, "segment", seg, "start_node", sn, "end_node", en);
                    });
                }

                // ===== prefab discovery =============================================
                case "list_prefabs":
                {
                    // kind: "road" (NetInfo) or "building" (BuildingInfo). Optional name filter.
                    string kind = a.HasKey("kind") ? a["kind"].Value : "road";
                    string filter = a.HasKey("filter") ? a["filter"].Value : null;
                    int limit = a.HasKey("limit") ? a["limit"].AsInt : 80;
                    return Sim(() =>
                    {
                        var arr = new JSONArray();
                        if (kind.ToLower() == "building")
                        {
                            int n = PrefabCollection<BuildingInfo>.LoadedCount();
                            for (int i = 0; i < n && arr.Count < limit; i++)
                                AddName(arr, GetName(PrefabCollection<BuildingInfo>.GetLoaded((uint)i)), filter);
                        }
                        else
                        {
                            int n = PrefabCollection<NetInfo>.LoadedCount();
                            for (int i = 0; i < n && arr.Count < limit; i++)
                                AddName(arr, GetName(PrefabCollection<NetInfo>.GetLoaded((uint)i)), filter);
                        }
                        var r = new JSONObject();
                        r["count"] = arr.Count;
                        r["prefabs"] = arr;
                        return (JSONNode)r;
                    });
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

        static CameraController Cam() => ToolsModifierControl.cameraController;

        // -- prefab lookup ---------------------------------------------------------
        static NetInfo FindNet(string q)
        {
            int n = PrefabCollection<NetInfo>.LoadedCount();
            NetInfo partial = null;
            for (int i = 0; i < n; i++)
            {
                var info = PrefabCollection<NetInfo>.GetLoaded((uint)i);
                if (info == null || info.name == null) continue;
                if (string.Equals(info.name, q, StringComparison.OrdinalIgnoreCase)) return info;
                if (partial == null && info.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) partial = info;
            }
            return partial;
        }

        static BuildingInfo FindBuilding(string q)
        {
            int n = PrefabCollection<BuildingInfo>.LoadedCount();
            BuildingInfo partial = null;
            for (int i = 0; i < n; i++)
            {
                var info = PrefabCollection<BuildingInfo>.GetLoaded((uint)i);
                if (info == null || info.name == null) continue;
                if (string.Equals(info.name, q, StringComparison.OrdinalIgnoreCase)) return info;
                if (partial == null && info.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) partial = info;
            }
            return partial;
        }

        static string GetName(PrefabInfo p) => p == null ? null : p.name;

        static void AddName(JSONArray arr, string name, string filter)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;
            arr.Add(name);
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
