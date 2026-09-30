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
        // ============================================================= self test

        /// <summary>Verbose diagnostics (row/structure dumps). Off by default; needed only when reporting issues.</summary>
        internal static bool DebugEnabled()
        {
            try
            {
                var v = Instance?.Config.Bind("Diagnostics", "DebugDump", false,
                    "Dump menu structure and row text objects to the BepInEx log (verbose)");
                if (v != null) return v.Value;
            }
            catch { }
            return false;
        }

        internal static bool SelfTestEnabled()
        {
            try
            {
                var v = Instance?.Config.Bind("Automation", "SelfTest", false,
                    "Run the automated verification on start (debug only; it takes over the menu)");
                if (v != null) return v.Value;
            }
            catch { }
            return false;
        }

        internal static void RunStoreTest()
        {
            try
            {
                bool ok = true;
                foreach (var p in Params)
                {
                    if (p.Key == "enabled") continue;       // would stop feeding
                    if (p.Key == "rebuild") continue;       // would trigger a rebuild
                    string orig = ReadRaw(p);
                    float probe = p.Min == p.Max ? p.Max : (p.Min + p.Max) / 2f;
                    string expect = Format(p, probe);
                    if (expect == orig) probe = p.Max;
                    expect = Format(p, probe);
                    if (expect == orig) { continue; }
                    MenuSettings.SetFloat(p.Id, probe);
                    bool a = ReadRaw(p) == expect;
                    MenuSettings.SetFloat(p.Id, ParseFloat(orig, 0f));
                    bool b = ReadRaw(p) == orig;
                    if (!a || !b) ok = false;
                    PLog.LogInfo("STORETEST " + p.Key + ": write=" + (a ? "OK" : "FAIL") +
                                 " restore=" + (b ? "OK" : "FAIL"));
                }
                PLog.LogInfo("STORETEST " + (ok ? "[ALL OK]" : "[FAILED]"));
            }
            catch (Exception e) { PLog.LogError("STORETEST: " + e); }
        }

        internal static bool InvokeRow(MenuButtonBase b)
        {
            if (b == null) return false;
            try
            {
                var m = typeof(MenuButtonBase).GetMethod("OnSubmit",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (m == null)
                {
                    foreach (var ty in new[] { typeof(MenuGoToPanelButton), typeof(MenuToggleSettingButton) })
                    {
                        m = ty.GetMethod("OnSubmit", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (m != null) break;
                    }
                }
                if (m == null) { PLog.LogWarning("OnSubmit not found"); return false; }
                m.Invoke(b, null);
                return true;
            }
            catch (Exception e) { PLog.LogError("InvokeRow failed: " + e.Message); return false; }
        }

        // ============================================================= probe

        public class Probe : MonoBehaviour
        {
            private int _tick;

            private void Update()
            {
                _tick++;

                // Fast caption guard (~0.25 s at 60 fps). When the game relocalizes the menu it
                // writes its own template text (e.g. "Fatigue reserve") into our cloned rows; the
                // 2 s drift sweep alone left those visible for up to two seconds.
                if (_tick % 15 == 0) { try { LabelGuardTick(); } catch { } }

                if (_tick % 120 != 0) return;
                try { Step(); }
                catch (Exception e) { PLog.LogError("probe: " + e); }
            }

            private void Step()
            {
                if (!_menuInjected) InjectMenu();
                if (!_menuInjected) return;

                if (DebugEnabled() && !_structureDumped) { _structureDumped = true; DumpStructure(); }

                try { CheckDrift(); } catch { }
                WatchdogTick();
                TrackFeedState();
                OffVerifyTick();
                SyncFontsWhenVisible();

                if (!_testDone && SelfTestEnabled()) RunTest();
            }

            private void RunTest()
            {
                var enabledParam = Params[0];
                switch (_testStep)
                {
                    case 0:
                    {
                        RunStoreTest();
                        var l2chk = FindByName(L2PageName);
                        bool l2visible = l2chk != null && l2chk.gameObject.activeInHierarchy;
                        PLog.LogInfo("TEST: L2 page visible while on main menu = " + l2visible +
                                     (l2visible ? "  [FAIL: page must stay hidden until opened]" : "  [OK: hidden]"));
                        _testOriginalOn = ReadToggle(Params[0]);
                        _testSnap0 = CountFeedActivity();
                        PLog.LogInfo("TEST: baseline activity=" + _testSnap0 + " enabled=" + (_testOriginalOn ? 1 : 0));
                        var r = FindByName(enabledParam.Row);
                        var tg = r != null ? r.GetComponent<MenuToggleSettingButton>() : null;
                        if (tg != null) InvokeRow(tg);
                        _testDeadline = Environment.TickCount64 + 4000;   // let a pending summary flush
                        _testStep = 1;
                        break;
                    }
                    case 1:
                    {
                        if (Environment.TickCount64 < _testDeadline) break;
                        _testSnap0 = CountFeedActivity();
                        _testDeadline = Environment.TickCount64 + 12000;
                        _testStep = 2;
                        break;
                    }
                    case 2:
                    {
                        if (Environment.TickCount64 < _testDeadline) break;
                        int a = CountFeedActivity();
                        PLog.LogInfo("TEST after OFF: activity " + _testSnap0 + "->" + a +
                                     (a == _testSnap0 ? "  [OK: feeding stopped]" : "  [FAIL: still feeding]"));
                        var r = FindByName(enabledParam.Row);
                        var tg = r != null ? r.GetComponent<MenuToggleSettingButton>() : null;
                        if (tg != null) InvokeRow(tg);
                        _testSnap1 = a;
                        _testDeadline = Environment.TickCount64 + 15000;
                        _testStep = 3;
                        break;
                    }
                    case 3:
                    {
                        if (Environment.TickCount64 < _testDeadline) break;
                        int b = CountFeedActivity();
                        PLog.LogInfo("TEST after ON: activity " + _testSnap1 + "->" + b +
                                     (b > _testSnap1 ? "  [OK: feeding resumed]" : "  [FAIL: watchdog will recover]"));
                        if (_testOriginalOn != ReadToggle(Params[0]))
                        {
                            var r = FindByName(enabledParam.Row);
                            var tg = r != null ? r.GetComponent<MenuToggleSettingButton>() : null;
                            if (tg != null) { InvokeRow(tg); PLog.LogInfo("TEST: restored original"); }
                        }
                        _testStep = 4;
                        break;
                    }
                    case 4:
                    {
                        bool ok = NavClick("Button Settings");
                        if (ok) { PLog.LogInfo("NAV settings -> invoked"); _testStep = 5; }
                        else if (++_navAttempts > 10) { PLog.LogWarning("NAV: settings never active"); _testStep = 5; }
                        break;
                    }
                    case 5:
                    {
                        bool ok = NavClick("Button Option Graphics");
                        if (ok) { PLog.LogInfo("NAV graphics -> invoked"); _testStep = 6; _navAttempts = 0; }
                        else if (++_navAttempts > 10) { PLog.LogWarning("NAV: graphics never active"); _testStep = 9; }
                        break;
                    }
                    case 6:
                    {
                        var nav = FindByName(NavRowName);
                        if (nav != null && nav.gameObject.activeInHierarchy)
                        {
                            var gb = nav.GetComponent<MenuGoToPanelButton>();
                            if (gb != null) InvokeRow(gb);
                            PLog.LogInfo("NAV L1 row -> invoked");
                            _testStep = 7;
                            _navAttempts = 0;
                        }
                        else if (++_navAttempts > 15) { PLog.LogWarning("NAV: L1 row never active"); _testStep = 9; }
                        break;
                    }
                    case 7:
                    {
                        var probe = Params[0];
                        var en = FindByName(probe.Row);
                        if (en != null && en.gameObject.activeInHierarchy)
                        {
                            PLog.LogInfo("L2 ACTIVE -- rows:");
                            foreach (var p in Params)
                            {
                                var t = FindByName(p.Row);
                                if (t == null) { PLog.LogInfo("  '" + p.Row + "': MISSING"); continue; }
                                var rt = t.GetComponent<RectTransform>();
                                string pos = rt != null ? (" y=" + rt.anchoredPosition.y.ToString("0.#", CultureInfo.InvariantCulture)) : "";
                                PLog.LogInfo("  '" + p.Label + "'" + pos + " key=" + p.Key +
                                             " value=" + ReadRaw(p) +
                                             " label='" + GetRowLabel(t.gameObject) + "'");
                            }
                            var backT = FindByName(RowBack);
                            var backBtn = backT != null ? backT.GetComponent<MenuGoToPanelButton>() : null;
                            if (backBtn != null)
                            {
                                InvokeRow(backBtn);
                                PLog.LogInfo("TEST: clicked back -> expect graphics page");
                                _testDeadline = Environment.TickCount64 + 5000;
                                _testStep = 8;
                            }
                            else { _testDone = true; PLog.LogInfo("AUTOTEST COMPLETE (no back row)"); }
                        }
                        else if (++_navAttempts > 15)
                        {
                            PLog.LogWarning("L2 page never became active");
                            _testDone = true;
                        }
                        break;
                    }
                    case 8:
                    {
                        if (Environment.TickCount64 < _testDeadline) break;
                        var gfx = FindByName("Location Options Graphics");
                        bool onGfx = gfx != null && gfx.gameObject.activeInHierarchy;
                        PLog.LogInfo("TEST back navigation: " + (onGfx ? "[OK: back to graphics]" : "[FAIL: not on graphics]"));
                        _testDone = true;
                        PLog.LogInfo("AUTOTEST COMPLETE");
                        break;
                    }
                }
            }

            private static bool NavClick(string goName)
            {
                foreach (var t in FindAllTransforms())
                {
                    if (t == null || t.name != goName || !t.gameObject.activeInHierarchy) continue;
                    var comp = t.GetComponent<MenuButtonBase>();
                    if (comp == null) continue;
                    return InvokeRow(comp);
                }
                return false;
            }
        }
    }
}