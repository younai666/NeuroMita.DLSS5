// NeuroMita.DLSS5 -- built-in DLSS 5 neural rendering settings for NeuroMita.
// SPDX-License-Identifier: MIT
//
// Part of the partial Plugin class (see Plugin.cs for the entry point).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using NeuroMita.Menu;
using UnityEngine;

namespace NeuroMita.DLSS5
{
    public partial class Plugin
    {
        // ============================================================= cfg store

        internal static string FeedConfigPath
        {
            get { return Path.Combine(Paths.GameRootPath, "dlss5-feed.cfg"); }
        }

        internal static string ReadFeedKey(string key, string fallback)
        {
            try
            {
                var map = GetFeedMap();
                string v;
                if (map != null && map.TryGetValue(key, out v)) return v;
            }
            catch (Exception e) { PLog.LogWarning("ReadFeedKey(" + key + "): " + e.Message); }
            return fallback;
        }

        // Parsed cache of dlss5-feed.cfg, invalidated by the file's write time. Get* interception
        // calls this on the game's UI hot path, so re-reading the file every time is wasteful.
        private static Dictionary<string, string> _feedMap;
        private static long _feedMapStamp = -1;

        private static Dictionary<string, string> GetFeedMap()
        {
            string path = FeedConfigPath;
            if (!File.Exists(path)) return null;
            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            if (_feedMap != null && _feedMapStamp == stamp) return _feedMap;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(path))
            {
                var t = raw.TrimStart();
                if (t.Length == 0 || t[0] == '#' || t[0] == ';') continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                map[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
            }
            _feedMap = map;
            _feedMapStamp = stamp;
            return map;
        }

        internal static void InvalidateFeedCache()
        {
            _feedMap = null;
            _feedMapStamp = -1;
        }

        /// <summary>Write one key (UTF-8, no BOM -- the feeder ignores a BOM, measured).</summary>
        internal static void WriteFeedKey(string key, string value)
        {
            try
            {
                string path = FeedConfigPath;
                if (!File.Exists(path)) { PLog.LogWarning("dlss5-feed.cfg missing: " + path); return; }
                var lines = File.ReadAllLines(path);
                bool found = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (lines[i] != key + "=" + value) lines[i] = key + "=" + value;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    var tmp = new List<string>(lines) { key + "=" + value };
                    lines = tmp.ToArray();
                }
                File.WriteAllLines(path, lines, new UTF8Encoding(false));
                InvalidateFeedCache();
            }
            catch (Exception e) { PLog.LogError("WriteFeedKey(" + key + ") failed: " + e); }
        }

        // ---- ReShade.ini [RenoDX.DLSS5] (the neural-rendering look controls) ----
        // Same knobs Magpie exposes for DLSSNR (NR style/intensity/local tone/local structure/
        // skin structure/auto mask/UI correction/multi pass/anti-flicker/follow input res).

        internal static string ReShadeIniPath
        {
            get { return Path.Combine(Paths.GameRootPath, "ReShade.ini"); }
        }

        private const string RenoDxSection = "RenoDX.DLSS5";

        internal static string ReadIniKey(string key, string fallback)
        {
            try
            {
                var map = GetIniMap();
                string v;
                if (map != null && map.TryGetValue(key, out v)) return v;
            }
            catch (Exception e) { PLog.LogWarning("ReadIniKey(" + key + "): " + e.Message); }
            return fallback;
        }

        private static Dictionary<string, string> _iniMap;
        private static long _iniMapStamp = -1;

        internal static void InvalidateIniCache()
        {
            _iniMap = null;
            _iniMapStamp = -1;
        }

        private static Dictionary<string, string> GetIniMap()
        {
            string path = ReShadeIniPath;
            if (!File.Exists(path)) return null;
            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            if (_iniMap != null && _iniMapStamp == stamp) return _iniMap;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length > 2 && line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }
                if (section != RenoDxSection) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            _iniMap = map;
            _iniMapStamp = stamp;
            return map;
        }

        private static string ReadIniKeyUncached(string key, string fallback)
        {
            try
            {
                if (!File.Exists(ReShadeIniPath)) return fallback;
                string section = null;
                foreach (var raw in File.ReadAllLines(ReShadeIniPath))
                {
                    var line = raw.Trim();
                    if (line.Length > 2 && line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        section = line.Substring(1, line.Length - 2);
                        continue;
                    }
                    if (section != RenoDxSection) continue;
                    if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(key.Length + 1).Trim();
                }
            }
            catch (Exception e) { PLog.LogWarning("ReadIniKey(" + key + "): " + e.Message); }
            return fallback;
        }

        internal static void WriteIniKey(string key, string value)
        {
            try
            {
                if (!File.Exists(ReShadeIniPath)) { PLog.LogWarning("ReShade.ini missing"); return; }
                var lines = new List<string>(File.ReadAllLines(ReShadeIniPath));
                string section = null;
                int sectionAt = -1;
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i].Trim();
                    if (line.Length > 2 && line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        section = line.Substring(1, line.Length - 2);
                        if (section == RenoDxSection) sectionAt = i;
                        continue;
                    }
                    if (section != RenoDxSection) continue;
                    if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (lines[i] != key + "=" + value) lines[i] = key + "=" + value;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    if (sectionAt >= 0) lines.Insert(sectionAt + 1, key + "=" + value);
                    else { lines.Add("[" + RenoDxSection + "]"); lines.Add(key + "=" + value); }
                }
                File.WriteAllLines(ReShadeIniPath, lines.ToArray(), new UTF8Encoding(false));
                InvalidateIniCache();
            }
            catch (Exception e) { PLog.LogError("WriteIniKey(" + key + ") failed: " + e); }
        }

        private static string ReadRaw(Param p)
        {
            return p.RenoDx ? ReadIniKey(p.Key, p.Def) : ReadFeedKey(p.Key, p.Def);
        }

        private static void WriteRaw(Param p, string value)
        {
            if (p.RenoDx) WriteIniKey(p.Key, value);
            else WriteFeedKey(p.Key, value);
        }

        private static float ReadValue(Param p) { return ParseFloat(ReadRaw(p), ParseFloat(p.Def, 0f)); }

        private static bool ReadToggle(Param p)
        {
            return string.Equals(ReadRaw(p), p.OnValue, StringComparison.OrdinalIgnoreCase);
        }

        private static string Format(Param p, float v)
        {
            if (p.VType == MenuSettingValueType.Int) return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Persist one parameter and refresh its row visuals.</summary>
        private static void StoreValue(Param p, float raw)
        {
            if (p == null) return;
            string value;
            if (p.Kind == 't')
            {
                value = raw >= 0.5f ? p.OnValue : p.OffValue;
            }
            else
            {
                float v = raw;
                if (v < p.Min) v = p.Min;
                if (v > p.Max) v = p.Max;
                value = Format(p, v);
            }

            string before = ReadRaw(p);
            if (before == value) { RefreshRow(p); return; }

            // the feeder must never see enabled=0: that kills its config watcher for good
            if (p.Key == "mode") WriteFeedKey("enabled", "1");

            WriteRaw(p, value);
            PLog.LogInfo("STORE " + p.Key + "=" + value);

            RefreshRow(p);
            if (p.Key == "mode")
            {
                if (value != p.OffValue)
                {
                    // switching ON must cancel any pending "did it really stop?" retry, otherwise
                    // that retry writes the off value again and the switch looks dead (measured).
                    _offVerifyAt = 0;
                    _offRetries = 0;
                    // and force the NR feature to be re-created so the look values just edited in
                    // ReShade.ini are (re)read by the add-on.
                    _wdRebuild++;
                    WriteFeedKey("rebuild", _wdRebuild.ToString(CultureInfo.InvariantCulture));
                    PLog.LogInfo("switch ON -> rebuild=" + _wdRebuild + " (re-create NR feature)");
                    ArmWatchdog();
                }
                else DisarmWatchdog();
            }
        }

        // ============================================================= watchdog

        private static string FeedLogPath
        {
            get { return Path.Combine(Paths.GameRootPath, "dlss5-feed.log"); }
        }

        internal static int CountFeedActivity()
        {
            try
            {
                if (!File.Exists(FeedLogPath)) return 0;
                int n = 0;
                foreach (var raw in File.ReadLines(FeedLogPath))
                {
                    if (raw.Contains("600 frames") || raw.Contains("first frame fed") ||
                        raw.Contains("delivered (")) n++;
                }
                return n;
            }
            catch { return -1; }
        }

        internal static void ArmWatchdog()
        {
            _wdSnapshot = CountFeedActivity();
            _wdDeadlineMs = Environment.TickCount64 + 13000;
            _wdPhase = 1;
            PLog.LogInfo("watchdog armed (baseline=" + _wdSnapshot + ")");
        }

        internal static void DisarmWatchdog()
        {
            if (_wdPhase != 0) PLog.LogInfo("watchdog disarmed");
            _wdPhase = 0;
        }

        internal static void WatchdogTick()
        {
            if (_wdPhase == 0) return;
            long now = Environment.TickCount64;

            if (_wdPhase == 3)
            {
                if (now >= _wdDeadlineMs)
                {
                    WriteFeedKey("mode", "2");
                    _wdSnapshot = CountFeedActivity();
                    _wdDeadlineMs = now + 10000;
                    _wdPhase = 4;
                    PLog.LogInfo("watchdog: recovery A done (mode 0->2), watching");
                }
                return;
            }
            if (now < _wdDeadlineMs) return;
            int act = CountFeedActivity();

            switch (_wdPhase)
            {
                case 1:
                    if (act > _wdSnapshot)
                    {
                        PLog.LogInfo("watchdog: feed resumed normally (" + _wdSnapshot + "->" + act + ")");
                        _wdPhase = 0; _feedRunning = true; _feedChangedMs = now; RefreshRow(Params[0]);
                        return;
                    }
                    PLog.LogInfo("watchdog: NOT resumed in 8s -> recovery A (mode 0->2 cycle)");
                    WriteFeedKey("mode", "0");
                    _wdDeadlineMs = now + 2500;
                    _wdPhase = 3;
                    return;
                case 4:
                    if (act > _wdSnapshot)
                    {
                        PLog.LogInfo("watchdog: resumed after recovery A (" + _wdSnapshot + "->" + act + ")");
                        _wdPhase = 0; _feedRunning = true; _feedChangedMs = now; RefreshRow(Params[0]);
                        return;
                    }
                    PLog.LogInfo("watchdog: still dead -> recovery B (rebuild++)");
                    _wdRebuild++;
                    WriteFeedKey("rebuild", _wdRebuild.ToString(CultureInfo.InvariantCulture));
                    _wdSnapshot = CountFeedActivity();
                    _wdDeadlineMs = now + 10000;
                    _wdPhase = 5;
                    return;
                case 5:
                    if (act > _wdSnapshot)
                    {
                        PLog.LogInfo("watchdog: resumed after recovery B");
                        _feedRunning = true; _feedChangedMs = now;
                    }
                    else
                    {
                        PLog.LogError("watchdog: FEED NOT RESUMED after A+B -- check dlss5-feed.log");
                        _feedRunning = false;
                    }
                    _wdPhase = 0;
                    RefreshRow(Params[0]);
                    return;
            }
        }

    }
}