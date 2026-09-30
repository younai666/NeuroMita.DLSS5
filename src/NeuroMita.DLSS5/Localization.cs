// NeuroMita.DLSS5: built-in DLSS 5 neural rendering settings for NeuroMita.
// SPDX-License-Identifier: MIT
//
// Part of the partial Plugin class (see Plugin.cs for the entry point).
//
// Text generation for our injected rows. Nothing is baked in at build time: every label is
// resolved through T() at the moment it is written, and the language comes from the game itself
// (LocalizationManager.CurrentLanguage, hooked through LocalizationManager.LanguageChanged), so the
// rows follow the in-game language setting like native rows do.
//
// The game ships 11 languages; all of them are covered here. Two rows borrow the game's own
// localized text outright, because those words already exist in its table: the back row reuses the
// text of a real back button, and the page title reuses the game's own settings-page title.

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
        /// <summary>Languages shipped by NeuroMita, in the order of its own language list.</summary>
        private enum Lang
        {
            Chinese = 0, Dutch = 1, English = 2, French = 3, German = 4, Polish = 5,
            Romanian = 6, Russian = 7, Spanish = 8, Turkish = 9, Ukrainian = 10
        }

        private const int LangCount = 11;

        private static Lang _lang = Lang.English;
        private static bool _langKnown;
        private static long _firstProbeAt;

        /// <summary>
        /// Translation table: index order matches the Lang enum
        /// { zh, nl, en, fr, de, pl, ro, ru, es, tr, uk }.
        /// Keys are the Label values of the parameter table plus the fixed row captions.
        /// </summary>
        private static readonly Dictionary<string, string[]> Strings = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            //                                  中文               Nederlands                 English                Français               Deutsch                Polski                    Română                    Русский                   Español                  Türkçe                 Українська
            { "nav", new[] { "DLSS5 神经渲染", "DLSS5 neurale rendering", "DLSS5 Neural Rendering", "DLSS5 rendu neuronal", "DLSS5 neuronales Rendern", "DLSS5 neuronowe", "DLSS5 neuronal", "DLSS5 нейро-рендеринг", "DLSS5 neuronal", "DLSS5 sinirsel", "DLSS5 нейро-рендеринг" } },
            { "title", new[] { "DLSS5 设置", "DLSS5-instellingen", "DLSS5 Settings", "Paramètres DLSS5", "DLSS5-Einstellungen", "Ustawienia DLSS5", "Setări DLSS5", "Настройки DLSS5", "Ajustes de DLSS5", "DLSS5 ayarları", "Налаштування DLSS5" } },
            { "back", new[] { "返回", "Terug", "Back", "Retour", "Zurück", "Wstecz", "Înapoi", "Назад", "Atrás", "Geri", "Назад" } },

            { "param.enabled", new[] { "神经渲染", "Neuraal", "Neural rendering", "Rendu neuronal", "Neuronales", "Neuronowe", "Neuronal", "Нейро-рендеринг", "Neuronal", "Sinirsel", "Нейро-рендеринг" } },
            { "param.workres", new[] { "工作分辨率", "Werkresolutie", "Work resolution", "Résolution", "Auflösung", "Rozdzielczość", "Rezoluție", "Разрешение", "Resolución", "Çözünürlük", "Роздільність" } },
            { "param.sharp", new[] { "锐化", "Verscherping", "Sharpness", "Netteté", "Schärfe", "Wyostrzanie", "Claritate", "Резкость", "Nitidez", "Keskinlik", "Різкість" } },
            { "param.preset", new[] { "DLSS 预设", "DLSS-preset", "DLSS preset", "Preset DLSS", "DLSS-Preset", "Preset DLSS", "Preset DLSS", "Пресет DLSS", "Preajuste DLSS", "DLSS ayarı", "Пресет DLSS" } },
            { "param.style", new[] { "NR 风格", "NR-stijl", "NR style", "Style NR", "NR-Stil", "Styl NR", "Stil NR", "Стиль NR", "Estilo NR", "NR stili", "Стиль NR" } },
            { "param.intensity", new[] { "NR 强度", "NR-intensiteit", "NR intensity", "Intensité NR", "NR-Intensität", "Intensywność", "Intensitate", "Сила NR", "Intensidad", "NR yoğunluğu", "Сила NR" } },
            { "param.localtone", new[] { "局部色调", "Lokale toon", "Local tone", "Ton local", "Lokaler Ton", "Ton lokalny", "Ton local", "Локальный тон", "Tono local", "Yerel ton", "Локальний тон" } },
            { "param.localstruct", new[] { "局部结构", "Lokale structuur", "Local structure", "Structure locale", "Lokale Struktur", "Struktura lokalna", "Structură locală", "Локальная структура", "Estructura local", "Yerel yapı", "Локальна структура" } },
            { "param.skin", new[] { "皮肤结构", "Huidstructuur", "Skin structure", "Structure de peau", "Hautstruktur", "Struktura skóry", "Structură piele", "Структура кожи", "Estructura de piel", "Cilt yapısı", "Структура шкіри" } },
            { "param.automask", new[] { "自动遮罩", "Automasker", "Automatic mask", "Masque auto", "Autom. Maske", "Automaska", "Mască auto", "Автомаска", "Máscara auto", "Otomatik maske", "Автомаска" } },
            { "param.passes", new[] { "Multi Pass", "Multi Pass", "Multi Pass", "Multi Pass", "Multi Pass", "Multi Pass", "Multi Pass", "Multi Pass", "Multi Pass", "Multi Pass", "Multi Pass" } },

            { "state.running", new[] { " [运行中]", " [actief]", " [running]", " [actif]", " [läuft]", " [działa]", " [activ]", " [работает]", " [activo]", " [çalışıyor]", " [працює]" } },
            { "state.stopped", new[] { " [已停止]", " [gestopt]", " [stopped]", " [arrêté]", " [gestoppt]", " [zatrzymane]", " [oprit]", " [остановлено]", " [detenido]", " [durdu]", " [зупинено]" } },
            { "suffix.restart", new[] { " (重启)", " (herstart)", " (restart)", " (redémarrage)", " (Neustart)", " (restart)", " (repornire)", " (перезапуск)", " (reinicio)", " (yeniden)", " (перезапуск)" } },
            { "suffix.softer", new[] { " (降锐度)", " (zachter)", " (softer)", " (plus doux)", " (weicher)", " (miękciej)", " (mai moale)", " (мягче)", " (más suave)", " (yumuşak)", " (м'якше)" } },
        };

        /// <summary>Translate a caption into the language the game is currently using.</summary>
        internal static string T(string key)
        {
            RefreshLanguageIfChanged();
            string[] row;
            if (!Strings.TryGetValue(key, out row)) return key;
            int i = (int)_lang;
            if (i < 0 || i >= row.Length || string.IsNullOrEmpty(row[i])) i = (int)Lang.English;
            return row[i];
        }

        // --------------------------------------------------------------- detection

        private static string CurrentGameLanguage()
        {
            try
            {
                var lang = LocalizationManager.CurrentLanguage;
                if (!string.IsNullOrEmpty(lang)) return lang;
            }
            catch (Exception e) { PLog.LogWarning("CurrentLanguage: " + e.Message); }
            return null;
        }

        private static int GameLanguageRevision()
        {
            try { return LocalizationManager.CurrentRevision; }
            catch { return -1; }
        }

        private static Lang ClassifyLanguage(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Lang.English;
            string s = raw.Trim().ToLowerInvariant().Replace('_', '-');

            // exact names first (the game uses names such as "Chinese" or "Russian")
            foreach (var kv in LanguageNames)
                if (s == kv.Key) return kv.Value;

            // then language tags such as "zh-CN", "ru", "pt-BR" -> compare the primary subtag
            int dash = s.IndexOf('-');
            string primary = dash > 0 ? s.Substring(0, dash) : s;
            foreach (var kv in LanguageNames)
                if (primary == kv.Key) return kv.Value;

            // finally a loose contains check, longest key first so that "ukrainian" wins over "ru"
            string best = null;
            foreach (var kv in LanguageNames)
                if (s.Contains(kv.Key) && (best == null || kv.Key.Length > best.Length)) best = kv.Key;
            if (best != null) return LanguageNames[best];

            return Lang.English;
        }

        private static readonly Dictionary<string, Lang> LanguageNames = new Dictionary<string, Lang>(StringComparer.Ordinal)
        {
            { "chinese", Lang.Chinese }, { "zh", Lang.Chinese }, { "chin", Lang.Chinese }, { "中文", Lang.Chinese },
            { "dutch", Lang.Dutch },     { "nl", Lang.Dutch },
            { "english", Lang.English }, { "en", Lang.English },
            { "french", Lang.French },   { "fr", Lang.French },
            { "german", Lang.German },   { "de", Lang.German },
            { "polish", Lang.Polish },   { "pl", Lang.Polish },
            { "romanian", Lang.Romanian }, { "ro", Lang.Romanian },
            { "russian", Lang.Russian }, { "ru", Lang.Russian },
            { "spanish", Lang.Spanish }, { "es", Lang.Spanish },
            { "turkish", Lang.Turkish }, { "tr", Lang.Turkish },
            { "ukrainian", Lang.Ukrainian }, { "uk", Lang.Ukrainian },
        };

        /// <summary>
        /// Last-resort detection for builds where the API is not available: read a row the game
        /// localized itself (our own rows are excluded, their text is what we are about to write).
        /// </summary>
        private static Lang DetectLanguageFromScene()
        {
            try
            {
                foreach (var name in new[] { "Button WindowMode", "Button VSync", "Button Back" })
                {
                    var t = FindByName(name);
                    if (t == null) continue;
                    string text = GetRowLabel(t.gameObject);
                    if (string.IsNullOrEmpty(text) || text == "<none>") continue;
                    foreach (var ch in text)
                    {
                        if (ch >= 0x0400 && ch <= 0x04FF) return Lang.Russian;   // Cyrillic
                        if (ch >= 0x4E00 && ch <= 0x9FFF) return Lang.Chinese;   // CJK
                    }
                    return Lang.English;
                }
            }
            catch { }
            return Lang.English;
        }

        private static void RefreshLanguageIfChanged()
        {
            string raw = CurrentGameLanguage();

            if (!_langKnown)
            {
                // At boot the game reports a placeholder language (observed: "Ukrainian") before the
                // saved setting is applied, so wait for the localized revision to be published.
                if (_firstProbeAt == 0) _firstProbeAt = Environment.TickCount64;
                bool revisionReady = GameLanguageRevision() > 0;
                bool waited = Environment.TickCount64 - _firstProbeAt > 15000;
                if (raw == null && !waited) return;
                if (!revisionReady && !waited) return;

                Lang byRaw = ClassifyLanguage(raw);
                if (byRaw == Lang.English)
                {
                    Lang byScene = DetectLanguageFromScene();
                    if (byScene != Lang.English) byRaw = byScene;
                }
                _langKnown = true;
                _lang = byRaw;
                PLog.LogInfo("language detected: " + byRaw + (raw != null ? " ('" + raw + "')" : " (from scene)"));
                return;
            }

            Lang detected = ClassifyLanguage(raw);
            if (detected == Lang.English && raw == null)
            {
                Lang byScene = DetectLanguageFromScene();
                if (byScene != Lang.English) detected = byScene;
            }
            if (detected != _lang)
            {
                _lang = detected;
                PLog.LogInfo("language changed -> " + detected);
                ApplyLocalization(true);
            }
        }

        // --------------------------------------------------------------- application

        /// <summary>
        /// Row captions come from our own table. Borrowing the game's own text was tried and
        /// reverted: during a language switch the game's rows still show the previous language, so
        /// copying them produced mixed states (e.g. a Russian "НАЗАД" above Chinese rows).
        /// </summary>
        internal static string BackCaption()
        {
            return T("back");
        }

        internal static string TitleCaption()
        {
            return T("title");
        }

        /// <summary>Re-label every row we own (build time, language change, and drift healing).</summary>
        internal static void ApplyLocalization(bool relabel)
        {
            if (!relabel) return;
            _fontsSynced = false;          // the game swaps fonts per language; re-sync ours
            InvalidateTransformCache();
            try
            {
                foreach (var p in Params)
                {
                    var row = FindByName(p.Row);
                    if (row == null) continue;
                    SetLabel(row.gameObject, ExpectedLabel(p));
                }
                var nav = FindByName(NavRowName);
                if (nav != null) SetLabel(nav.gameObject, T("nav"));
                var title = FindByName(L2PageName + "Title");
                if (title != null) SetLabel(title.gameObject, TitleCaption());
                var back = FindByName(RowBack);
                if (back != null) SetLabel(back.gameObject, BackCaption());
                PLog.LogInfo("labels applied for " + _lang);
            }
            catch (Exception e) { PLog.LogWarning("ApplyLocalization: " + e.Message); }
        }

        /// <summary>
        /// Cheap caption pass, called several times per second while our page is open: anything the
        /// game overwrote (its localizer writes the cloned template's own text) is put back at once.
        /// </summary>
        internal static void LabelGuardTick()
        {
            if (!_menuInjected) return;
            var page = FindByName(L2PageName);
            if (page == null || !page.gameObject.activeInHierarchy) return;

            foreach (var p in Params)
            {
                var row = FindByName(p.Row);
                if (row == null) continue;
                string want = ExpectedLabel(p);
                if (GetRowLabel(row.gameObject) != want) SetLabel(row.gameObject, want);
            }

            var nav = FindByName(NavRowName);
            if (nav != null && GetRowLabel(nav.gameObject) != T("nav")) SetLabel(nav.gameObject, T("nav"));

            var back = FindByName(RowBack);
            if (back != null && GetRowLabel(back.gameObject) != BackCaption()) SetLabel(back.gameObject, BackCaption());

            var title = FindByName(L2PageName + "Title");
            if (title != null && GetRowLabel(title.gameObject) != TitleCaption()) SetLabel(title.gameObject, TitleCaption());
        }

        /// <summary>Hook the game's own language change so our rows follow immediately.</summary>
        private static void HookLanguageChanges()        {
            try
            {
                LocalizationManager.LanguageChanged += (Action)OnGameLanguageChanged;
                PLog.LogInfo("hook LocalizationManager.LanguageChanged: OK");
            }
            catch (Exception e)
            {
                // not fatal: the RefreshLocalizedTexts postfix and the 2 s drift sweep still relabel
                PLog.LogWarning("hook LocalizationManager.LanguageChanged failed: " + e.Message);
            }
        }

        private static void OnGameLanguageChanged()
        {
            try
            {
                _langKnown = false;
                _firstProbeAt = 0;
                RefreshLanguageIfChanged();
                ApplyLocalization(true);
                SyncFontsWhenVisible(true);
            }
            catch (Exception e) { PLog.LogWarning("OnGameLanguageChanged: " + e.Message); }
        }
    }
}
