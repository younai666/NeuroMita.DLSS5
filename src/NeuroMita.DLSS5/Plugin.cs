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
    /// <summary>
    /// Built-in DLSS5 settings menu (v3.0): L1 row on the graphics page opens an L2 page with
    /// every useful dlss5-feed.cfg parameter.
    ///
    ///   Graphics page -- L1 "DLSS5 Neural Rendering" --> L2 "DLSS5 Settings"
    ///
    /// Everything is persisted straight into dlss5-feed.cfg, which DLSS5-Feeder hot-reloads
    /// while the game runs, so every row takes effect immediately.
    ///
    /// Hard-won facts baked in here:
    ///   - rows in these settings pages are positioned individually (not by a layout group),
    ///     so new rows must be placed with the game's own helper
    ///     InterfaceFastMenu.CopyRowPositionAndMakeRoom(source, clone); reordering by sibling
    ///     index does nothing and makes rows overlap.
    ///   - the native MenuSettings setters silently drop unknown ids (measured), so all
    ///     reads/writes are intercepted and served from the cfg file.
    ///   - Harmony must match the game's parameter names exactly (settingId); where the
    ///     generated interop method has no parameter names, take the argument positionally
    ///     via object[] __args.
    ///   - widget bootstrap writes during page construction must be swallowed
    ///     (_constructing), otherwise template values leak into the cfg (sharpness became 1).
    ///   - WriteFeedKey must write UTF-8 WITHOUT BOM; a BOM makes the feeder ignore the file.
    ///
    /// NOTE: this file contains non-ASCII text. Only use UTF-8 aware read/write helpers on it;
    /// PowerShell Get-Content/Set-Content (ANSI default) destroys it.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public partial class Plugin : BasePlugin
    {
        public const string PluginGuid = "nm.dlss5menu";
        public const string PluginName = "NeuroMita.DLSS5";
        public const string PluginVersion = "1.0.0";

        public const string NavRowName = "Button DLSS5";
        public const string L2PageName = "Location Options DLSS5";
        public const string L2TitleName = "DLSS5Title";
        public const string RowBack = "Button DLSS5 back";

        /// <summary>One configurable parameter = one row on the L2 page.</summary>
        private sealed class Param
        {
            public MenuSettingId Id;
            public string Row;          // GameObject name of the row
            public string Label;        // translation key (see Localization.cs)
            public string Key;          // cfg key (dlss5-feed.cfg) or ini key (ReShade.ini)
            public char Kind;           // 't' toggle, 's' slider
            public MenuSettingValueType VType;
            public float Min, Max, Step;
            public string Suffix = "";
            public string Def = "0";
            public bool RenoDx;         // true -> lives in ReShade.ini [RenoDX.DLSS5]
            public string OnValue = "1";
            public string OffValue = "0";
        }

        /// <summary>
        /// Parameter set. Feeder keys are hot-reloaded by DLSS5-Feeder; the RenoDX keys are the
        /// neural-rendering look controls (same ones Magpie exposes: NR style / intensity / local
        /// tone / local structure / skin structure / auto mask / UI correction / multi pass /
        /// anti-flicker / follow input resolution).
        /// </summary>
        private static readonly List<Param> Params = new List<Param>
        {
            // ---- DLSS5-Feeder (dlss5-feed.cfg, hot reload) ----
            // The master switch drives `mode` (2 = full DLSS path, 0 = inert), NOT `enabled`:
            // once the feeder reads enabled=0 it tears the session down and stops watching the
            // config file altogether (measured: dlss5-feed.log ends there and enabled=1 is never
            // seen again), so `enabled` stays 1 for the whole session.
            new Param { Id = (MenuSettingId)50, Row = "Button DLSS5 enabled",   Label = "param.enabled", Key = "mode",            Kind = 't', VType = MenuSettingValueType.Bool,  Def = "2", OnValue = "2", OffValue = "0" },
            new Param { Id = (MenuSettingId)51, Row = "Button DLSS5 workres",   Label = "param.workres",     Key = "work_resolution", Kind = 's', VType = MenuSettingValueType.Int,   Min = 50f,  Max = 100f, Step = 5f, Suffix = "%", Def = "100" },
            new Param { Id = (MenuSettingId)52, Row = "Button DLSS5 sharp",     Label = "param.sharp",           Key = "work_sharpness",  Kind = 's', VType = MenuSettingValueType.Float, Min = 0f,   Max = 1f,   Step = 0.05f, Def = "0.30" },
            new Param { Id = (MenuSettingId)54, Row = "Button DLSS5 preset",    Label = "param.preset", Key = "preset", Kind = 's', VType = MenuSettingValueType.Int, Min = 0f, Max = 11f, Step = 1f, Def = "0" },

            // ---- RenoDX NR look (ReShade.ini [RenoDX.DLSS5]). Editable only while the master
            // switch is OFF; turning the switch back ON re-creates the NR feature, which is what
            // re-reads these values (no game restart needed).
            new Param { Id = (MenuSettingId)56, Row = "Button DLSS5 style",     Label = "param.style", Key = "NRLookMode",      Kind = 's', VType = MenuSettingValueType.Int,   Min = 0f,  Max = 2f,  Step = 1f,   Def = "0", RenoDx = true },
            new Param { Id = (MenuSettingId)57, Row = "Button DLSS5 intensity", Label = "param.intensity",        Key = "NRIntensity",     Kind = 's', VType = MenuSettingValueType.Float, Min = 0f,  Max = 2f,  Step = 0.05f, Def = "1", RenoDx = true },
            new Param { Id = (MenuSettingId)58, Row = "Button DLSS5 localtone", Label = "param.localtone",   Key = "NRLocalTone",     Kind = 's', VType = MenuSettingValueType.Float, Min = 0f,  Max = 2f,  Step = 0.05f, Def = "1", RenoDx = true },
            new Param { Id = (MenuSettingId)59, Row = "Button DLSS5 localstruct",Label = "param.localstruct",  Key = "NRLocalStructure",Kind = 's', VType = MenuSettingValueType.Float, Min = 0f,  Max = 2f,  Step = 0.05f, Def = "1", RenoDx = true },
            new Param { Id = (MenuSettingId)60, Row = "Button DLSS5 skin",      Label = "param.skin", Key = "NRSkinStructure", Kind = 's', VType = MenuSettingValueType.Float, Min = -1f, Max = 2f, Step = 0.05f, Def = "0", RenoDx = true },
            new Param { Id = (MenuSettingId)61, Row = "Button DLSS5 automask",  Label = "param.automask",       Key = "NRAutoMask",      Kind = 't', VType = MenuSettingValueType.Bool,  Def = "0", RenoDx = true },
            new Param { Id = (MenuSettingId)63, Row = "Button DLSS5 passes",    Label = "param.passes", Key = "NRPasses",      Kind = 's', VType = MenuSettingValueType.Int,   Min = 1f,  Max = 10f, Step = 1f,   Def = "1", RenoDx = true },
        };

        internal static ManualLogSource PLog;
        internal static Plugin Instance;

        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

        private static Harmony _harmony;

        private static bool _menuInjected;
        private static bool _structureDumped;
        private static bool _presetsRegistered;
        private static bool _constructing;
        private static bool _paintingVisuals;   // guards the recursive RefreshVisuals call

        // per-row click debounce: a single shared timer let one row swallow another row's click
        private static readonly Dictionary<MenuSettingId, long> _lastToggleMs = new Dictionary<MenuSettingId, long>();
        private const int DebounceMs = 800;

        private static int _wdPhase;
        private static long _wdDeadlineMs;
        private static int _wdSnapshot;
        private static int _wdRebuild;

        // real feed state (measured from dlss5-feed.log activity, not from the cfg flag)
        private static int _feedCount = -1;
        private static long _feedChangedMs;
        private static bool _feedRunning;

        // switch-off verification
        private static long _offVerifyAt;
        private static int _offSnap;
        private static int _offRetries;

        private static int _testStep;
        private static bool _testDone;
        private static bool _testOriginalOn;
        private static long _testDeadline;
        private static int _testSnap0;
        private static int _testSnap1;
        private static int _navAttempts;

        public override void Load()
        {
            Instance = this;
            PLog = base.Log;
            PLog.LogInfo(PluginName + " v" + PluginVersion + " loading (" + Params.Count + " params)");

            try { WriteFeedKey("enabled", "1"); }   // the feeder must keep watching its config file
            catch { }
            try { RegisterPresets(); }
            catch (Exception e) { PLog.LogError("RegisterPresets: " + e); }

            try { InstallPatch(); }
            catch (Exception e) { PLog.LogError("InstallPatch: " + e); }

            try { HookLanguageChanges(); }
            catch (Exception e) { PLog.LogWarning("HookLanguageChanges: " + e); }

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<Probe>();
                var go = new GameObject("NeuroMita.DLSS5.Probe");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<Probe>();
                PLog.LogInfo("Probe registered");
            }
            catch (Exception e) { PLog.LogError("Probe: " + e); }
        }

        public override bool Unload()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { }
            try { LocalizationManager.LanguageChanged -= (Action)OnGameLanguageChanged; } catch { }
            return true;
        }

        // ============================================================= param table helpers

        // Lookup tables: Find() sits on the hot path (every MenuSettings.Get/Set interception),
        // so a linear scan over the parameter list is not acceptable there.
        private static readonly Dictionary<MenuSettingId, Param> _byId = new Dictionary<MenuSettingId, Param>();
        private static readonly Dictionary<string, Param> _byRow = new Dictionary<string, Param>();

        private static void BuildParamIndex()
        {
            _byId.Clear();
            _byRow.Clear();
            foreach (var p in Params)
            {
                _byId[p.Id] = p;
                if (!string.IsNullOrEmpty(p.Row)) _byRow[p.Row] = p;
            }
        }

        private static Param Find(MenuSettingId id)
        {
            if (_byId.Count == 0) BuildParamIndex();
            Param p;
            return _byId.TryGetValue(id, out p) ? p : null;
        }

        private static Param FindByRow(string row)
        {
            if (_byRow.Count == 0) BuildParamIndex();
            Param p;
            return (row != null && _byRow.TryGetValue(row, out p)) ? p : null;
        }

        private static bool IsOurs(MenuSettingId id) { return Find(id) != null; }

        // ============================================================= preset registration

        private static void RegisterPresets()
        {
            var mapProp = typeof(MenuSettingRegistry).GetProperty("_map",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (mapProp == null) throw new Exception("_map property not found");
            object map = mapProp.GetValue(null);
            if (map == null)
            {
                try { MenuSettingRegistry.TryGet(MenuSettingId.VSync, out _); } catch { }
                map = mapProp.GetValue(null);
            }
            if (map == null) throw new Exception("_map is null");
            var set = map.GetType().GetMethod("set_Item");
            if (set == null) throw new Exception("dictionary set_Item not found");

            int ready = 0;
            foreach (var p in Params)
            {
                MenuSettingPreset existing;
                bool has;
                try { has = MenuSettingRegistry.TryGet(p.Id, out existing); }
                catch { has = false; existing = null; }
                if (has && existing != null) { ready++; continue; }

                int defInt = (int)Math.Round(ParseFloat(p.Def, 0f));
                var preset = new MenuSettingPreset(
                    p.Id, MenuSettingDomain.Graphics, p.VType,
                    "NM_DLSS5_" + p.Key.ToUpperInvariant().Replace('.', '_'),
                    p.Def == "1", ParseFloat(p.Def, 0f), defInt, null,
                    p.Min, p.Max, p.Step, p.Suffix);
                set.Invoke(map, new object[] { p.Id, preset });
                ready++;
            }
            _presetsRegistered = true;
            PLog.LogInfo("presets ready in registry: " + ready);
        }

        private static float ParseFloat(string s, float fallback)
        {
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }
    }
}
