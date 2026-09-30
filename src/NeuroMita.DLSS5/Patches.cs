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
        // ============================================================= Harmony

        private static void InstallPatch()
        {
            _harmony = new Harmony("nm.dlss5menu");

            Patch(typeof(InterfaceFastMenu), "EnsureGameSettingsMenu", null,
                typeof(Plugin).GetMethod("Postfix_Ensure", NonPublicStatic), "EnsureGameSettingsMenu");
            Patch(typeof(InterfaceFastMenu), "RefreshLocalizedTexts", null,
                typeof(Plugin).GetMethod("Postfix_RefreshLocalized", NonPublicStatic), "RefreshLocalizedTexts");

            Patch(typeof(MenuToggleSettingButton), "OnSubmit",
                typeof(Plugin).GetMethod("Prefix_OnSubmit", NonPublicStatic), null, "Toggle.OnSubmit");
            Patch(typeof(MenuToggleSettingButton), "RefreshVisuals", null,
                typeof(Plugin).GetMethod("Postfix_ToggleVisuals", NonPublicStatic), "Toggle.RefreshVisuals");
            Patch(typeof(MenuToggleSettingButton), "OnEnable", null,
                typeof(Plugin).GetMethod("Postfix_ToggleOnEnable", NonPublicStatic), "Toggle.OnEnable");

            Patch(typeof(MenuSliderSettingButton), "RefreshVisuals",
                typeof(Plugin).GetMethod("Prefix_SliderVisuals", NonPublicStatic), null, "Slider.RefreshVisuals");
            Patch(typeof(MenuSliderSettingButton), "OnSliderChanged",
                typeof(Plugin).GetMethod("Prefix_SliderChanged", NonPublicStatic), null, "Slider.OnSliderChanged");

            // diagnostics for navigation rows (our L1 entry and our back row)
            Patch(typeof(MenuGoToPanelButton), "OnSubmit",
                typeof(Plugin).GetMethod("Prefix_GoTo", NonPublicStatic), null, "GoToPanel.OnSubmit");

            Patch(typeof(MenuSettings), "GetBool", typeof(Plugin).GetMethod("Prefix_GetBool", NonPublicStatic), null, "MenuSettings.GetBool");
            Patch(typeof(MenuSettings), "GetFloat", typeof(Plugin).GetMethod("Prefix_GetFloat", NonPublicStatic), null, "MenuSettings.GetFloat");
            Patch(typeof(MenuSettings), "GetInt", typeof(Plugin).GetMethod("Prefix_GetInt", NonPublicStatic), null, "MenuSettings.GetInt");
            Patch(typeof(MenuSettings), "SetBool", typeof(Plugin).GetMethod("Prefix_SetBool", NonPublicStatic), null, "MenuSettings.SetBool");
            Patch(typeof(MenuSettings), "SetFloat", typeof(Plugin).GetMethod("Prefix_SetFloat", NonPublicStatic), null, "MenuSettings.SetFloat");
            Patch(typeof(MenuSettings), "SetInt", typeof(Plugin).GetMethod("Prefix_SetInt", NonPublicStatic), null, "MenuSettings.SetInt");
        }

        private static void Patch(Type type, string method, MethodInfo prefix, MethodInfo postfix, string label)
        {
            try
            {
                var m = type.GetMethod(method, BindingFlags.Static | BindingFlags.Instance |
                                                BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) { PLog.LogError("patch " + label + ": method not found"); return; }
                _harmony.Patch(m,
                    prefix == null ? null : new HarmonyMethod(prefix),
                    postfix == null ? null : new HarmonyMethod(postfix));
                var info = Harmony.GetPatchInfo(m);
                bool ok = info != null &&
                          ((prefix != null && info.Prefixes != null && info.Prefixes.Count > 0) ||
                           (postfix != null && info.Postfixes != null && info.Postfixes.Count > 0));
                PLog.LogInfo("patch " + label + ": " + (ok ? "OK" : "FAILED"));
            }
            catch (Exception e)
            {
                var inner = e.InnerException != null ? " | inner: " + e.InnerException.Message : "";
                PLog.LogError("patch " + label + " threw: " + e.Message + inner);
            }
        }

        private static void Postfix_Ensure()
        {
            PLog.LogInfo("EnsureGameSettingsMenu -> (re)build our menu");
            _menuInjected = false;
            try { InjectMenu(); } catch (Exception e) { PLog.LogError("InjectMenu: " + e); }
        }

        private static void Postfix_RefreshLocalized()
        {
            // the game has just (re)localized every row, ours included, with the cloned template's
            // text -> put our captions back immediately, then run the normal drift pass
            try { ApplyLocalization(true); } catch (Exception e) { PLog.LogWarning("ApplyLocalization: " + e.Message); }
            try { CheckDrift(); } catch (Exception e) { PLog.LogWarning("CheckDrift: " + e.Message); }
        }

        private static void Postfix_ToggleOnEnable(MenuToggleSettingButton __instance)
        {
            try
            {
                var p = Find(__instance.setting);
                if (p == null) return;
                __instance.ConfigureSetting(p.Id);
                ApplyToggleVisual(__instance, ReadToggle(p));
            }
            catch (Exception e) { PLog.LogWarning("OnEnable: " + e.Message); }
        }

        private static void Postfix_ToggleVisuals(MenuToggleSettingButton __instance)
        {
            try
            {
                var p = Find(__instance.setting);
                if (p == null) return;
                ApplyToggleVisual(__instance, ReadToggle(p));
            }
            catch (Exception e) { PLog.LogWarning("ToggleVisuals: " + e.Message); }
        }

        private static bool Prefix_SliderVisuals(MenuSliderSettingButton __instance)
        {
            try
            {
                var p = Find(__instance.setting);
                if (p == null) return true;
                __instance.currentValue = ReadValue(p);
                SetLabel(__instance.gameObject, ExpectedLabel(p));
            }
            catch (Exception e) { PLog.LogWarning("SliderVisuals: " + e.Message); }
            return true;
        }

        /// <summary>
        /// Slider value change. The generated interop method may carry no parameter names at all
        /// (Harmony then fails with "Parameter ... not found"), so take the argument positionally.
        /// </summary>
        private static void Prefix_SliderChanged(MenuSliderSettingButton __instance, object[] __args)
        {
            try
            {
                var p = Find(__instance.setting);
                if (p == null) return;
                if (_constructing) return;
                    if (__args == null || __args.Length == 0) return;
                StoreValue(p, Convert.ToSingle(__args[0], CultureInfo.InvariantCulture));
            }
            catch (Exception e) { PLog.LogWarning("SliderChanged: " + e.Message); }
        }

        /// <summary>Logs every GoTo navigation click: which row, which target panel, stack flag.</summary>
        private static bool Prefix_GoTo(MenuGoToPanelButton __instance)
        {
            try
            {
                string target = "?";
                try
                {
                    var tp = __instance.target;
                    target = tp != null ? tp.gameObject.name : "null";
                }
                catch (Exception ie) { target = "err:" + ie.Message; }
                PLog.LogInfo("GOTO row='" + __instance.gameObject.name + "' target='" + target +
                             "' clearStack=" + __instance.clearStack);
            }
            catch { }
            return true;
        }

        private static bool Prefix_OnSubmit(MenuToggleSettingButton __instance)
        {
            try
            {
                var p = Find(__instance.setting);
                PLog.LogInfo("CLICK row='" + __instance.gameObject.name + "' id=" + (int)__instance.setting +
                             (p == null ? " (not ours)" : " (" + p.Key + ")"));
                if (p == null) return true;

                long now = Environment.TickCount64;
                long last;
                _lastToggleMs.TryGetValue(__instance.setting, out last);
                if (now - last < DebounceMs)
                {
                    PLog.LogInfo("click debounced (" + (now - last) + "ms)");
                    return false;
                }
                _lastToggleMs[__instance.setting] = now;

                bool next = !ReadToggle(p);
                StoreValue(p, next ? 1f : 0f);
                ApplyToggleVisual(__instance, next);

                // real-effect check: the cfg flag alone proves nothing
                if (!next)
                {
                    DisarmWatchdog();
                    _offSnap = CountFeedActivity();
                    _offRetries = 0;
                    _offVerifyAt = Environment.TickCount64 + 10000;
                }
                RefreshRow(p);
                return false;
            }
            catch (Exception e)
            {
                PLog.LogError("Prefix_OnSubmit: " + e);
                return true;
            }
        }

        // ---- MenuSettings interception (parameter name must match the game side exactly) ----

        private static bool Prefix_GetBool(MenuSettingId settingId, ref bool __result)
        {
            var p = Find(settingId);
            if (p == null) return true;
            __result = ReadToggle(p);
            return false;
        }

        private static bool Prefix_GetFloat(MenuSettingId settingId, ref float __result)
        {
            var p = Find(settingId);
            if (p == null) return true;
            __result = ReadValue(p);
            return false;
        }

        private static bool Prefix_GetInt(MenuSettingId settingId, ref int __result)
        {
            var p = Find(settingId);
            if (p == null) return true;
            __result = (int)Math.Round(ReadValue(p));
            return false;
        }

        private static bool Prefix_SetBool(MenuSettingId settingId, bool value)
        {
            var p = Find(settingId);
            if (p == null) return true;
            if (_constructing) return false;

            StoreValue(p, value ? 1f : 0f);
            return false;
        }

        private static bool Prefix_SetFloat(MenuSettingId settingId, float value)
        {
            var p = Find(settingId);
            if (p == null) return true;
            if (_constructing) return false;

            StoreValue(p, value);
            return false;
        }

        private static bool Prefix_SetInt(MenuSettingId settingId, int value)
        {
            var p = Find(settingId);
            if (p == null) return true;
            if (_constructing) return false;

            StoreValue(p, value);
            return false;
        }

    }
}