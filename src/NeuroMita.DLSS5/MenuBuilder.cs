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
        // ============================================================= visuals

        internal static void SetLabel(GameObject go, string label)
        {
            // A row holds several text objects: the label AND the slider's value readout.
            // Writing to all of them duplicated the label into the value slot (looked a mess),
            // so prefer the dedicated label object and never touch value/case visuals.
            try
            {
                var t = go.transform.Find("Text");
                if (t != null && WriteTexts(t.gameObject, label)) return;

                foreach (var tmp in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                {
                    if (tmp == null || IsValueText(tmp.transform, go.transform)) continue;
                    if (WriteTexts(tmp.gameObject, label)) return;
                }
                foreach (var ut in go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    if (ut == null || IsValueText(ut.transform, go.transform)) continue;
                    if (WriteTexts(ut.gameObject, label)) return;
                }
                PLog.LogWarning("SetLabel: no label text under '" + go.name + "'");
            }
            catch (Exception e) { PLog.LogWarning("SetLabel: " + e.Message); }
        }

        private static void ApplyLabelStyle(GameObject row)
        {
            // NOTE: deliberately does NOT touch font/fontSize. The game swaps the page font when
            // the page is shown; overriding it here with a font captured from a not-yet-localized
            // row produced text with missing Chinese glyphs.
            //
            // Best-fit IS enabled: a row label sits in a fixed-width slot left of the value and the
            // slider, and our captions differ a lot in length between languages (Russian and German
            // are the long ones). With best-fit Unity shrinks the font until the label fits instead
            // of letting the text run underneath the value/slider.
            try
            {
                foreach (var ut in row.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    if (ut == null || IsValueText(ut.transform, row.transform)) continue;
                    ut.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;
                    ut.verticalOverflow = UnityEngine.VerticalWrapMode.Overflow;
                    int max = ut.fontSize > 0 ? ut.fontSize : 32;
                    ut.resizeTextForBestFit = true;
                    ut.resizeTextMaxSize = max;
                    ut.resizeTextMinSize = Mathf.Max(8, max / 2);
                }
            }
            catch (Exception e) { PLog.LogWarning("ApplyLabelStyle: " + e.Message); }
        }

        private static bool _fontsSynced;

        /// <summary>
        /// Our rows are cloned from game rows, so they inherit whatever font that row happened to
        /// carry (rows cloned from the graphics page used a Russian-subset font and rendered blank
        /// CJK text). Use the font the game itself picked for the current language
        /// (LocalizationManager.CurrentFont) and fall back to copying a working row's font.
        /// </summary>
        internal static void SyncFontsWhenVisible(bool force = false)
        {
            if (_fontsSynced && !force) return;
            try
            {
                var page = FindByName(L2PageName);
                if (page == null || !page.gameObject.activeInHierarchy) return;

                UnityEngine.Font font = null;
                int size = 0;

                try
                {
                    font = LocalizationManager.CurrentFont;
                    if (font != null) PLog.LogInfo("font from LocalizationManager: '" + font.name + "'");
                }
                catch (Exception e) { PLog.LogWarning("CurrentFont: " + e.Message); }

                if (font == null)
                {
                    foreach (var p in Params)
                    {
                        var row = FindByName(p.Row);
                        if (row == null) continue;
                        var t = row.transform.Find("Text");
                        var ut = t != null ? t.GetComponent<UnityEngine.UI.Text>() : null;
                        if (ut == null || ut.font == null || string.IsNullOrEmpty(ut.text)) continue;
                        font = ut.font; size = ut.fontSize;
                        break;
                    }
                }
                if (font == null) return;

                if (size <= 0)
                {
                    // keep the size of a row that is already rendered
                    foreach (var p in Params)
                    {
                        var row = FindByName(p.Row);
                        if (row == null) continue;
                        var t = row.transform.Find("Text");
                        var ut = t != null ? t.GetComponent<UnityEngine.UI.Text>() : null;
                        if (ut != null && ut.fontSize > 0) { size = ut.fontSize; break; }
                    }
                }

                int applied = 0;
                foreach (var p in Params)
                {
                    var row = FindByName(p.Row);
                    if (row == null) continue;
                    var t = row.transform.Find("Text");
                    var ut = t != null ? t.GetComponent<UnityEngine.UI.Text>() : null;
                    if (ut == null) continue;
                    ut.font = font;
                    if (size > 0) ut.fontSize = size;
                    applied++;
                }
                var back = FindByName(RowBack);
                if (back != null)
                {
                    var t = back.transform.Find("Text");
                    var ut = t != null ? t.GetComponent<UnityEngine.UI.Text>() : null;
                    if (ut != null) { ut.font = font; if (size > 0) { ut.fontSize = size; ut.resizeTextMaxSize = size; } applied++; }
                }
                var title = FindByName(L2PageName + "Title");
                if (title != null)
                {
                    var ut = title.GetComponentInChildren<UnityEngine.UI.Text>(true);
                    if (ut != null) { ut.font = font; if (size > 0) { ut.fontSize = size; ut.resizeTextMaxSize = size; } applied++; }
                }
                _fontsSynced = true;
                PLog.LogInfo("font sync: '" + font.name + "' size " + size + " applied to " + applied + " rows");
            }
            catch (Exception e) { PLog.LogWarning("SyncFonts: " + e.Message); }
        }

        /// <summary>Diagnostic: rendering properties of a row's text (font / alpha / enabled).</summary>
        private static void LogTextProps(string tag, GameObject go)
        {
            try
            {
                foreach (var ut in go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    if (ut == null) continue;
                    PLog.LogInfo("TEXTPROPS " + tag + " '" + ut.gameObject.name + "' text='" + ut.text +
                                 "' font=" + (ut.font != null ? ut.font.name : "null") +
                                 " size=" + ut.fontSize + " enabled=" + ut.enabled +
                                 " colorA=" + ut.color.a.ToString("0.##", CultureInfo.InvariantCulture) +
                                 " active=" + ut.gameObject.activeInHierarchy);
                }
                foreach (var tmp in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                {
                    if (tmp == null) continue;
                    PLog.LogInfo("TEXTPROPS " + tag + " (TMP) '" + tmp.gameObject.name + "' text='" + tmp.text +
                                 "' font=" + (tmp.font != null ? tmp.font.name : "null") +
                                 " size=" + tmp.fontSize + " enabled=" + tmp.enabled);
                }
            }
            catch { }
        }

        private static bool IsValueText(Transform t, Transform row)
        {
            var p = t;
            while (p != null && p != row)
            {
                if (p.name == "Slider" || p.name == "Case" || p.name == "CaseCheck" || p.name == "Value")
                    return true;
                p = p.parent;
            }
            return false;
        }

        private static bool WriteTexts(GameObject go, string label)
        {
            bool any = false;
            foreach (var tmp in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
            {
                if (tmp == null) continue;
                any = true;
                if (tmp.text != label) tmp.text = label;
                // the title's typewriter limit can freeze the string mid-way ("DLSS5 设")
                try { if (tmp.maxVisibleCharacters < label.Length) tmp.maxVisibleCharacters = 99999; } catch { }
                try { tmp.ForceMeshUpdate(); } catch { }
            }
            foreach (var t3 in go.GetComponentsInChildren<TMPro.TextMeshPro>(true))
            {
                if (t3 == null) continue;
                any = true;
                if (t3.text != label) t3.text = label;
                try { if (t3.maxVisibleCharacters < label.Length) t3.maxVisibleCharacters = 99999; } catch { }
            }
            foreach (var ut in go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if (ut == null) continue;
                any = true;
                if (ut.text != label) ut.text = label;
            }
            return any;
        }

        /// <summary>Diagnostic: dump a row's text objects so label and value can be told apart.</summary>
        private static void DumpRowTexts(Transform row)
        {
            try
            {
                PLog.LogInfo("  TEXTS of '" + row.name + "':");
                DumpTextsRecursive(row, 0);
            }
            catch { }
        }

        private static void DumpTextsRecursive(Transform t, int depth)
        {
            if (t == null || depth > 4) return;
            string info = " active=" + t.gameObject.activeSelf;
            try
            {
                var tmp = t.GetComponent<TMPro.TextMeshProUGUI>();
                if (tmp != null) info += " TMP='" + tmp.text + "'(maxVis=" + tmp.maxVisibleCharacters + ")";
                var ut = t.GetComponent<UnityEngine.UI.Text>();
                if (ut != null) info += " UIText='" + ut.text + "'";
                var cg = t.GetComponent<UnityEngine.CanvasGroup>();
                if (cg != null) info += " alpha=" + cg.alpha.ToString("0.##", CultureInfo.InvariantCulture);
                if (t.GetComponent<MenuToggleSettingButton>() != null) info += " [Toggle]";
                if (t.GetComponent<MenuSliderSettingButton>() != null) info += " [Slider]";
                if (t.GetComponent<MenuGoToPanelButton>() != null) info += " [GoTo]";
                if (t.GetComponent<UnityEngine.UI.Image>() != null) info += " [Image]";
            }
            catch { }
            PLog.LogInfo("    " + new string(' ', depth * 2) + t.name + info);
            for (int i = 0; i < t.childCount; i++) DumpTextsRecursive(t.GetChild(i), depth + 1);
        }

        internal static string GetRowLabel(GameObject go)
        {
            try
            {
                var tmp = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (tmp != null) return tmp.text;
            }
            catch { }
            try
            {
                var ut = go.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (ut != null) return ut.text;
            }
            catch { }
            return "<none>";
        }

        /// <summary>Label actually expected on a row (live status for the master switch).</summary>
        private static string ExpectedLabel(Param p)
        {
            if (p.Key == "mode") return T(p.Label) + FeedStatusSuffix();
            if (p.Key == "work_resolution" && ReadValue(p) < 100f) return T(p.Label) + T("suffix.softer");
            if (p.RenoDx) return T(p.Label) + T("suffix.restart");
            return T(p.Label);
        }

        /// <summary>
        /// What the feeder is really doing, derived from dlss5-feed.log activity:
        /// " [运行中]" while frames keep being delivered, " [已停止]" once it has been quiet.
        /// </summary>
        private static string FeedStatusSuffix()
        {
            if (_feedCount < 0) return "";
            return _feedRunning ? T("state.running") : T("state.stopped");
        }

        /// <summary>Sample the feed log; called from the probe every couple of seconds.</summary>
        internal static void TrackFeedState()
        {
            try
            {
                long now = Environment.TickCount64;
                int count = CountFeedActivity();
                if (count < 0) return;
                if (_feedCount < 0) { _feedCount = count; _feedChangedMs = now; return; }
                if (count > _feedCount)
                {
                    _feedCount = count;
                    _feedChangedMs = now;
                    if (!_feedRunning)
                    {
                        _feedRunning = true;
                        PLog.LogInfo("feed state -> RUNNING (activity=" + count + ")");
                        RefreshRow(Params[0]);
                    }
                }
                else if (_feedRunning && now - _feedChangedMs > 15000)
                {
                    _feedRunning = false;
                    PLog.LogInfo("feed state -> STOPPED (no activity for 15s)");
                    RefreshRow(Params[0]);
                }
                _feedCount = count;
            }
            catch { }
        }

        /// <summary>After switching off, confirm the feeder really stopped; re-write the flag if not.</summary>
        internal static void OffVerifyTick()
        {
            if (_offVerifyAt == 0) return;
            long now = Environment.TickCount64;
            if (now < _offVerifyAt) return;

            // the user may have switched it back on while this check was pending -- never fight them
            if (Params[0] != null && ReadToggle(Params[0]))
            {
                PLog.LogInfo("switch-off check cancelled (switch is on again)");
                _offVerifyAt = 0;
                _offRetries = 0;
                return;
            }

            int count = CountFeedActivity();
            if (count > _offSnap)
            {
                if (_offRetries < 2)
                {
                    _offRetries++;
                    PLog.LogWarning("switch-off not effective yet (activity " + _offSnap + "->" + count +
                                    ") -> re-writing mode=0 (retry " + _offRetries + ")");
                    WriteFeedKey("mode", "0");
                    _offSnap = count;
                    _offVerifyAt = now + 8000;
                }
                else
                {
                    PLog.LogError("switch-off FAILED after retries (activity keeps growing)");
                    _offVerifyAt = 0;
                }
                return;
            }
            PLog.LogInfo("switch-off verified: feeding stopped (activity " + _offSnap + "->" + count + ")");
            _feedRunning = false;
            _offVerifyAt = 0;
            RefreshRow(Params[0]);
        }

        private static void RefreshRow(Param p)
        {
            try
            {
                var t = FindByName(p.Row);
                if (t == null) return;
                if (p.Kind == 't')
                {
                    var tg = t.GetComponent<MenuToggleSettingButton>();
                    if (tg != null) ApplyToggleVisual(tg, ReadToggle(p));
                }
                else
                {
                    var sl = t.GetComponent<MenuSliderSettingButton>();
                    if (sl != null) { try { sl.currentValue = ReadValue(p); } catch { } }
                    SetLabel(t.gameObject, ExpectedLabel(p));
                }
            }
            catch (Exception e) { PLog.LogWarning("RefreshRow(" + p.Row + "): " + e.Message); }
        }

        internal static void ApplyToggleVisual(MenuToggleSettingButton comp, bool on)
        {
            if (comp == null) return;
            try
            {
                try
                {
                    var pr = typeof(MenuToggleSettingButton).GetProperty("currentValue",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (pr != null && pr.CanWrite) pr.SetValue(comp, on);
                }
                catch { }

                // Let the game's own widget paint its marker from currentValue. Doing it by hand
                // got the tick/cross the wrong way round (measured: ON showed the X).
                if (!_paintingVisuals)
                {
                    _paintingVisuals = true;
                    try { comp.RefreshVisuals(); } catch { }
                    finally { _paintingVisuals = false; }
                }

                var p = FindByRow(comp.gameObject.name);
                SetLabel(comp.gameObject, p != null ? ExpectedLabel(p) : comp.gameObject.name);
            }
            catch (Exception e) { PLog.LogWarning("ApplyToggleVisual: " + e.Message); }
        }

        // ============================================================= lookup

        // Scene lookups used to call FindObjectsOfType<Transform>(true) every single time, i.e.
        // dozens of full-scene scans and thousands of allocations per tick. Cache both a name map
        // and the flat list, refreshed at most every NameCacheMs.
        private const int NameCacheMs = 400;
        private static Dictionary<string, Transform> _nameMap;
        private static List<Transform> _allTransforms;
        private static long _nameMapAt = -1;

        internal static void InvalidateTransformCache()
        {
            _nameMap = null;
            _allTransforms = null;
            _nameMapAt = -1;
        }

        private static void EnsureTransformCache()
        {
            long now = Environment.TickCount64;
            if (_nameMap != null && _allTransforms != null && now - _nameMapAt < NameCacheMs) return;

            var all = UnityEngine.Object.FindObjectsOfType<Transform>(true);
            var list = new List<Transform>(all != null ? all.Length : 0);
            var map = new Dictionary<string, Transform>(all != null ? all.Length : 0, StringComparer.Ordinal);
            if (all != null)
            {
                foreach (var t in all)
                {
                    if (t == null) continue;
                    list.Add(t);
                    if (!map.ContainsKey(t.name)) map[t.name] = t;   // first match wins, like the old scan
                }
            }
            _allTransforms = list;
            _nameMap = map;
            _nameMapAt = now;
        }

        internal static List<Transform> FindAllTransforms()
        {
            long now = Environment.TickCount64;
            if (_allTransforms == null || (_nameMap != null && now - _nameMapAt >= NameCacheMs))
                EnsureTransformCache();
            return _allTransforms;
        }

        internal static Transform FindByName(string n)
        {
            if (string.IsNullOrEmpty(n)) return null;
            EnsureTransformCache();
            Transform t;
            return _nameMap.TryGetValue(n, out t) ? t : null;
        }

        // ============================================================= menu construction

        private static void InjectMenu()
        {
            if (!_presetsRegistered) return;
            _constructing = true;
            try { InjectMenuCore(); }
            catch (Exception e) { PLog.LogError("InjectMenuCore: " + e); }
            finally { _constructing = false; }
        }

        private static void InjectMenuCore()
        {
            var gfx = FindByName("Location Options Graphics");
            if (gfx == null) { PLog.LogInfo("graphics page not found yet"); return; }
            var gfxPanel = gfx.GetComponent<MenuPanel>();
            if (gfxPanel == null) { PLog.LogError("graphics page has no MenuPanel"); return; }

            var fat = FindByName("Location Settings SleepFatigue");
            if (fat == null) { PLog.LogError("template page SleepFatigue missing"); return; }

            Transform l2 = FindByName(L2PageName);
            if (l2 == null)
            {
                l2 = BuildPage(fat, gfx, L2PageName, TitleCaption(), Params, gfxPanel, RowBack);
                if (l2 == null) return;
            }

            var nav = FindByName(NavRowName);
            if (nav == null)
            {
                var gfxBack = FindGoToRow(gfx);
                if (gfxBack == null) { PLog.LogError("no GoToPanel template row in graphics page"); return; }
                var navGo = UnityEngine.Object.Instantiate(gfxBack.gameObject, gfx.transform, false);
                navGo.name = NavRowName;

                // Put it right under "WindowMode" (the row it follows) using the game's own
                // placement helper: the clone still carries the back row's coordinates otherwise.
                var after = gfx.transform.Find("Button WindowMode");
                if (after != null) PlaceRow(after, navGo.transform);
                else navGo.transform.SetSiblingIndex(gfxBack.GetSiblingIndex());

                var goBtn = navGo.GetComponent<MenuGoToPanelButton>();
                var srcBtn = gfxBack.GetComponent<MenuGoToPanelButton>();
                bool clearStack = srcBtn != null && srcBtn.clearStack;
                goBtn.ConfigureTarget(l2.GetComponent<MenuPanel>(), clearStack);
                StripLocalization(navGo);
                SetLabel(navGo, T("nav"));

                RebuildPanelOrder(gfxPanel, gfx.transform);
                PLog.LogInfo("L1 nav row injected: '" + NavRowName + "' -> '" + L2PageName +
                             "' (clearStack=" + clearStack + ")");
            }

            _menuInjected = true;
            InvalidateTransformCache();
            foreach (var p in Params) RefreshRow(p);
        }

        /// <summary>
        /// Clone the "sleep and fatigue" page into one of our pages and fill it with rows.
        /// Rows on these pages are individually positioned, so every row after the first is placed
        /// with the game's own InterfaceFastMenu.CopyRowPositionAndMakeRoom helper.
        /// </summary>
        private static Transform BuildPage(Transform fat, Transform gfx, string pageName, string titleText,
                                           List<Param> items, MenuPanel backTarget, string backRowName)
        {
            var pageGo = UnityEngine.Object.Instantiate(fat.gameObject, fat.parent, false);
            pageGo.name = pageName;
            pageGo.transform.SetSiblingIndex(gfx.GetSiblingIndex() + 1);
            // Visibility belongs to the menu system (it activates exactly one page at a time).
            // Forcing the clone active made it draw on top of every other page -- never do that.
            var pageT = pageGo.transform;

            var title = pageT.GetChild(0);
            title.name = pageName + "Title";
            // capture the label style from a TEMPLATE ROW (slider) -- those labels render the game's
            // Chinese text correctly, while the page title still carries the Russian font.
            StripLocalization(title.gameObject);
            SetLabel(title.gameObject, titleText);

            // ---- template rows: capture geometry/prototypes, then remove every one of them ----
            Transform sliderProtoSrc = null, toggleProtoSrc = null;
            RectTransform slotRt = null;
            var templateRows = new List<GameObject>();
            for (int i = 0; i < pageT.childCount; i++)
            {
                var c = pageT.GetChild(i);
                bool isSlider = c.GetComponent<MenuSliderSettingButton>() != null;
                bool isToggle = c.GetComponent<MenuToggleSettingButton>() != null;
                if (!isSlider && !isToggle)
                {
                    if (c.GetComponent<MenuBackButton>() != null) templateRows.Add(c.gameObject);
                    continue;
                }
                if (isSlider && sliderProtoSrc == null) sliderProtoSrc = c;
                else if (isToggle && toggleProtoSrc == null) toggleProtoSrc = c;
                else if (slotRt == null) slotRt = c.GetComponent<RectTransform>();
                templateRows.Add(c.gameObject);
            }
            if (sliderProtoSrc == null || toggleProtoSrc == null || slotRt == null)
            {
                PLog.LogError("template shape unexpected (slider=" + (sliderProtoSrc != null) +
                              " toggle=" + (toggleProtoSrc != null) + " slot=" + (slotRt != null) + ")");
                UnityEngine.Object.Destroy(pageGo);
                return null;
            }

            // Toggle prototype: use the TEMPLATE page's own toggle. The graphics page's toggle row
            // relies on the localization system for its font and rendered no text once cloned.
            // (Its tick/cross is painted by the widget itself, see ApplyToggleVisual.)

            var protoSlider = UnityEngine.Object.Instantiate(sliderProtoSrc.gameObject);
            var protoToggle = UnityEngine.Object.Instantiate(toggleProtoSrc.gameObject);
            protoSlider.SetActive(false);
            protoToggle.SetActive(false);
            protoSlider.transform.SetParent(pageT, false);
            protoToggle.transform.SetParent(pageT, false);

            var slotAnchorMin = slotRt.anchorMin;
            var slotAnchorMax = slotRt.anchorMax;
            var slotPivot = slotRt.pivot;
            var slotSize = slotRt.sizeDelta;
            var slotPos = slotRt.anchoredPosition;
            int slotIndex = slotRt.GetSiblingIndex();

            foreach (var g in templateRows) UnityEngine.Object.DestroyImmediate(g);

            // Rows are placed strictly top-down starting at the template's first row slot.
            // The BACK row goes first (right under the title): the bottom of these pages is
            // clipped by the panel, so a bottom back row cannot be reached with the mouse.
            var last = (Transform)null;
            bool first = true;
            var gfxBack = FindGoToRow(gfx);
            if (gfxBack != null && backTarget != null)
            {
                var backClone = InsertNavRow(pageT, backRowName, BackCaption(), backTarget, null, gfxBack);
                if (backClone != null)
                {
                    var brt = backClone.GetComponent<RectTransform>();
                    if (brt != null)
                    {
                        brt.anchorMin = slotAnchorMin;
                        brt.anchorMax = slotAnchorMax;
                        brt.pivot = slotPivot;
                        brt.sizeDelta = slotSize;
                        brt.anchoredPosition = slotPos;
                    }
                    backClone.transform.SetSiblingIndex(slotIndex);
                    last = backClone.transform;
                    first = false;
                }
            }

            foreach (var p in items)
            {
                var proto = p.Kind == 't' ? protoToggle : protoSlider;
                var rowGo = UnityEngine.Object.Instantiate(proto.gameObject, pageT, false);
                rowGo.name = p.Row;
                rowGo.SetActive(true);
                var rowT = rowGo.transform;

                if (first)
                {
                    first = false;
                    var dstRt = rowT.GetComponent<RectTransform>();
                    if (dstRt != null)
                    {
                        dstRt.anchorMin = slotAnchorMin;
                        dstRt.anchorMax = slotAnchorMax;
                        dstRt.pivot = slotPivot;
                        dstRt.sizeDelta = slotSize;
                        dstRt.anchoredPosition = slotPos;
                    }
                    rowT.SetSiblingIndex(slotIndex);
                }
                else
                {
                    PlaceRow(last, rowT);
                    // keep the row pitch uniform even though the prototypes come from two pages
                    var prt = rowT.GetComponent<RectTransform>();
                    if (prt != null) prt.sizeDelta = new Vector2(prt.sizeDelta.x, slotSize.y);
                }
                last = rowT;

                if (p.Kind == 't')
                {
                    var tg = rowGo.GetComponent<MenuToggleSettingButton>();
                    if (tg != null) { tg.ConfigureSetting(p.Id); ApplyToggleVisual(tg, ReadToggle(p)); }
                }
                else
                {
                    var sl = rowGo.GetComponent<MenuSliderSettingButton>();
                    if (sl != null) { sl.ConfigureSetting(p.Id); try { sl.currentValue = ReadValue(p); } catch { } }
                }
                StripLocalization(rowGo);
                SetLabel(rowGo, ExpectedLabel(p));
                ApplyLabelStyle(rowGo);
                if (DebugEnabled()) LogTextProps("row:" + p.Key, rowGo);
                PLog.LogInfo("  row '" + p.Row + "' (" + p.Key + ") = " + ReadRaw(p));
            }

            if (protoSlider != null) UnityEngine.Object.DestroyImmediate(protoSlider);
            if (protoToggle != null) UnityEngine.Object.DestroyImmediate(protoToggle);

            var panel = pageT.GetComponent<MenuPanel>() ?? pageT.GetComponentInParent<MenuPanel>(true);
            RebuildPanelOrder(panel, pageT);
            ForceRowsVisible(pageT);
            PLog.LogInfo("page built: '" + pageName + "' rows=" + items.Count + " children=" + pageT.childCount);
            return pageT;
        }

        /// <summary>
        /// The page's chain transition caches one CanvasGroup per row and only animates the rows it
        /// knew about at show time, so rows we append afterwards can stay at alpha 0 (invisible and
        /// unclickable). Force every row of ours visible/interactable and finish the show animation.
        /// </summary>
        private static void ForceRowsVisible(Transform pageT)
        {
            try
            {
                var transition = pageT.GetComponent<MenuPanelChainTransition>();
                if (transition == null) transition = pageT.GetComponentInParent<MenuPanelChainTransition>(true);
                if (transition != null)
                {
                    try { transition.CompleteShowImmediately(); } catch { }
                }

                for (int i = 0; i < pageT.childCount; i++)
                {
                    var c = pageT.GetChild(i);
                    if (c == null) continue;
                    var cg = c.GetComponent<UnityEngine.CanvasGroup>();
                    if (cg == null) cg = c.gameObject.AddComponent<UnityEngine.CanvasGroup>();
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
            }
            catch (Exception e) { PLog.LogWarning("ForceRowsVisible: " + e.Message); }
        }

        /// <summary>
        /// Insert a GoToPanel navigation row (cloned from the graphics page's own back row) right
        /// below <paramref name="source"/>. Pass name == null to auto-name it.
        /// </summary>
        private static GameObject InsertNavRow(Transform page, string name, string label,
                                               MenuPanel target, Transform source, Transform navTemplate)
        {
            try
            {
                var clone = UnityEngine.Object.Instantiate(navTemplate.gameObject, page, false);
                clone.name = name ?? ("Button DLSS5 nav " + label);
                var t = clone.transform;
                if (source != null) PlaceRow(source, t);
                var btn = clone.GetComponent<MenuGoToPanelButton>();
                var srcBtn = navTemplate.GetComponent<MenuGoToPanelButton>();
                btn.ConfigureTarget(target, srcBtn != null && srcBtn.clearStack);
                StripLocalization(clone);
                SetLabel(clone, label);
                ApplyLabelStyle(clone);
                if (DebugEnabled()) LogTextProps("nav:" + clone.name, clone);
                ForceRowsVisible(page);
                PLog.LogInfo("  nav row '" + clone.name + "' -> '" + target.gameObject.name + "'");
                return clone;
            }
            catch (Exception e) { PLog.LogError("InsertNavRow: " + e); return null; }
        }

        /// <summary>
        /// Remove the game's LocalizedText/TableText components from a cloned row. They re-apply
        /// table text on every relocalize and would overwrite our labels with unrelated strings
        /// (measured: rows showed "音乐音量"/"语音音量" from other pages).
        /// </summary>
        private static void StripLocalization(GameObject go)
        {
            try
            {
                foreach (var c in go.GetComponentsInChildren<NeuroMita.Menu.LocalizedText>(true))
                    if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            catch (Exception e) { PLog.LogWarning("StripLocalization(LocalizedText): " + e.Message); }
        }

        /// <summary>
        /// Put <paramref name="row"/> right below <paramref name="source"/> using the game's own
        /// InterfaceFastMenu.CopyRowPositionAndMakeRoom (it also shifts the rows underneath).
        /// Falls back to copying the anchored position if the helper is unavailable.
        /// </summary>
        private static void PlaceRow(Transform source, Transform row)
        {
            try
            {
                if (source != null) row.SetSiblingIndex(source.GetSiblingIndex() + 1);
                var srcRt = source != null ? source.GetComponent<RectTransform>() : null;
                var dstRt = row.GetComponent<RectTransform>();
                if (srcRt == null || dstRt == null) return;

                var helper = typeof(InterfaceFastMenu).GetMethod("CopyRowPositionAndMakeRoom",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (helper != null)
                {
                    helper.Invoke(null, new object[] { srcRt, dstRt });
                    return;
                }
                dstRt.anchorMin = srcRt.anchorMin;
                dstRt.anchorMax = srcRt.anchorMax;
                dstRt.pivot = srcRt.pivot;
                dstRt.sizeDelta = srcRt.sizeDelta;
                float dy = srcRt.rect.height + 6f;
                dstRt.anchoredPosition = srcRt.anchoredPosition - new Vector2(0f, dy);
            }
            catch (Exception e) { PLog.LogWarning("PlaceRow: " + e.Message); }
        }

        private static Transform FindGoToRow(Transform page)
        {
            for (int i = 0; i < page.childCount; i++)
            {
                var c = page.GetChild(i);
                if (c.GetComponent<MenuGoToPanelButton>() != null) return c;
            }
            return null;
        }

        private static void RebuildPanelOrder(MenuPanel panel, Transform pageRoot)
        {
            try
            {
                if (panel == null || pageRoot == null) return;
                var buttons = new Il2CppSystem.Collections.Generic.List<MenuButtonBase>();
                foreach (var b in pageRoot.GetComponentsInChildren<MenuButtonBase>(true))
                    if (b != null) buttons.Add(b);
                if (buttons.Count == 0) return;
                var rebuild = typeof(InterfaceFastMenu).GetMethod("RebuildPanelOrder",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (rebuild != null) rebuild.Invoke(null, new object[] { panel, buttons });
            }
            catch (Exception e) { PLog.LogWarning("RebuildPanelOrder: " + e.Message); }
        }

        /// <summary>Drift self-heal: the game's localization overwrites our labels; setting ids can be lost.</summary>
        private static void CheckDrift()
        {
            foreach (var p in Params)
            {
                var t = FindByName(p.Row);
                if (t == null) continue;

                string want = ExpectedLabel(p);
                string got = GetRowLabel(t.gameObject);
                bool labelDrift = got != want;
                bool settingDrift = false;

                if (p.Kind == 't')
                {
                    var tg = t.GetComponent<MenuToggleSettingButton>();
                    if (tg != null && tg.setting != p.Id) settingDrift = true;
                }
                else
                {
                    var sl = t.GetComponent<MenuSliderSettingButton>();
                    if (sl != null && sl.setting != p.Id) settingDrift = true;
                }

                if (!labelDrift && !settingDrift) continue;
                PLog.LogInfo("DRIFT '" + p.Row + "': label='" + got + "' settingDrift=" + settingDrift + " -> re-assert");
                try
                {
                    if (p.Kind == 't')
                    {
                        var tg = t.GetComponent<MenuToggleSettingButton>();
                        if (tg != null) { tg.ConfigureSetting(p.Id); ApplyToggleVisual(tg, ReadToggle(p)); }
                    }
                    else
                    {
                        var sl = t.GetComponent<MenuSliderSettingButton>();
                        if (sl != null)
                        {
                            sl.ConfigureSetting(p.Id);
                            try { sl.currentValue = ReadValue(p); } catch { }
                            SetLabel(t.gameObject, want);
                        }
                    }
                }
                catch (Exception e) { PLog.LogWarning("re-assert " + p.Row + ": " + e.Message); }
            }

            foreach (var name in new[] { NavRowName, L2TitleName, L2PageName + "Title", RowBack })
            {
                var t = FindByName(name);
                if (t == null) continue;
                string want = name == NavRowName ? T("nav") : (name.EndsWith("Title") ? TitleCaption() : BackCaption());
                if (GetRowLabel(t.gameObject) != want)
                {
                    PLog.LogInfo("DRIFT label '" + name + "' -> '" + want + "'");
                    SetLabel(t.gameObject, want);
                }
            }
        }

    }
}