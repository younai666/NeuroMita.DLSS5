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
        // ============================================================= diagnostics

        private static void DumpStructure()
        {
            try
            {
                PLog.LogInfo("==== MENU STRUCTURE DUMP ====");
                foreach (var n in new[] { "Location Options Graphics", L2PageName })
                {
                    var t = FindByName(n);
                    if (t == null) { PLog.LogInfo("  PAGE '" + n + "': (absent)"); continue; }
                    PLog.LogInfo("  PAGE '" + n + "' children=" + t.childCount);
                    for (int i = 0; i < t.childCount; i++)
                    {
                        var c = t.GetChild(i);
                        if (c == null) continue;
                        var p = FindByRow(c.name);
                        var rt = c.GetComponent<RectTransform>();
                        string pos = rt != null ? ("y=" + rt.anchoredPosition.y.ToString("0.#", CultureInfo.InvariantCulture)) : "";
                        PLog.LogInfo("    [" + i + "] '" + c.name + "' type=[" + ProbeType(c) + "] " + pos +
                                     " label='" + GetRowLabel(c.gameObject) + "'" +
                                     (p != null ? " key=" + p.Key + " value=" + ReadRaw(p) : ""));
                    }
                }
                PLog.LogInfo("==== END STRUCTURE DUMP ====");
                // label vs value text objects, so the two can be told apart reliably
                var firstRow = Params.Count > 0 ? FindByName(Params[0].Row) : null;
                if (firstRow != null) DumpRowTexts(firstRow);
                var sliderRow = Params.Count > 1 ? FindByName(Params[1].Row) : null;
                if (sliderRow != null) DumpRowTexts(sliderRow);
                var back = FindByName(RowBack);
                if (back != null) DumpRowTexts(back);
            }
            catch (Exception e) { PLog.LogError("DumpStructure: " + e); }
        }

        private static string ProbeType(Transform t)
        {
            var hits = new List<string>();
            try { if (t.GetComponent<MenuGoToPanelButton>() != null) hits.Add("GoToPanel"); } catch { }
            try { if (t.GetComponent<MenuSliderSettingButton>() != null) hits.Add("Slider"); } catch { }
            try { if (t.GetComponent<MenuToggleSettingButton>() != null) hits.Add("Toggle"); } catch { }
            try { if (t.GetComponent<MenuBackButton>() != null) hits.Add("Back"); } catch { }
            try { if (t.GetComponent<MenuPanel>() != null) hits.Add("Panel"); } catch { }
            return hits.Count > 0 ? string.Join("+", hits.ToArray()) : "-";
        }

    }
}