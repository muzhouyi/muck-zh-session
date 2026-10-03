using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UU9.Muck.Translater
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "UU9.Muck.Translater";
        public const string PluginName = "UU9 Muck Translater";
        public const string PluginVersion = "1.0.2";

        public static ManualLogSource Log { get; private set; }
        private Harmony _harmony;
        private static Plugin _instance;
        private int _pendingTextRefreshFrames;
        private static ConfigEntry<string> _fontFile;
        private static ConfigEntry<bool> _enableF5Reload;
        private static ConfigEntry<bool> _enableF6Toggle;

        public static string FontFile => _fontFile?.Value ?? "msyh.ttc";
        public static bool IsF5ReloadEnabled => _enableF5Reload?.Value ?? true;
        public static bool IsF6ToggleEnabled => _enableF6Toggle?.Value ?? true;

        private void Awake()
        {
            Log = Logger;
            _instance = this;

            try
            {
                InitializeSettings();
                FontManager.Initialize(Log);
                TranslationManager.Initialize(Log);

                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());

                // 监听场景加载完成事件，自动汉化新场景 UI
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

                // 插件初始化完成后立即触发一次当前场景文本扫描
                RefreshSceneTexts();

                Log.LogInfo($"[UU9 Translater] {PluginName} v{PluginVersion} 初始化成功！按下 F5 可热重载词典。");
            }
            catch (Exception ex)
            {
                Log.LogError($"[UU9 Translater] 初始化阶段抛出异常: {ex.Message}");
            }
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Log.LogInfo($"[UU9 Translater] 检测到场景加载: {scene.name}，正在自动刷新 UI 汉化文本...");
            RefreshSceneTexts();
        }

        public static bool IsTranslationEnabled { get; private set; } = true;

        private static void InitializeSettings()
        {
            string modConfigDirectory = System.IO.Path.Combine(Paths.ConfigPath, "UU9.Muck.Translater");
            System.IO.Directory.CreateDirectory(modConfigDirectory);
            string configPath = System.IO.Path.Combine(modConfigDirectory, "UU9.Muck.Translater.cfg");
            var settings = new ConfigFile(configPath, true);

            _fontFile = settings.Bind("General", "FontFile", "msyh.ttc",
                "Font filename to use. Searches C:\\Windows\\Fonts first, then BepInEx\\config\\UU9.Muck.Translater\\Font.");
            _enableF5Reload = settings.Bind("Hotkeys", "EnableF5Reload", true,
                "Enable F5 dictionary reload.");
            _enableF6Toggle = settings.Bind("Hotkeys", "EnableF6Toggle", true,
                "Enable F6 translation toggle.");
        }

        private void Update()
        {
            if (_pendingTextRefreshFrames > 0)
            {
                _pendingTextRefreshFrames--;
                if (_pendingTextRefreshFrames == 0 && IsTranslationEnabled)
                {
                    RefreshSceneTexts(false);
                }
            }

            // 按下 F5 热重载词典并更新当前场景中的 UI 文本
            if (IsF5ReloadEnabled && UnityEngine.Input.GetKeyDown(KeyCode.F5))
            {
                Log.LogInfo("[UU9 Translater] 触发 F5 热重载，正在重新读取汉化词典并更新界面...");
                TranslationManager.LoadTranslations();
                if (IsTranslationEnabled)
                {
                    RefreshSceneTexts();
                }
            }

            // 按下 F6 切换开启/还原翻译
            if (IsF6ToggleEnabled && UnityEngine.Input.GetKeyDown(KeyCode.F6))
            {
                IsTranslationEnabled = !IsTranslationEnabled;
                if (IsTranslationEnabled)
                {
                    Log.LogInfo("[UU9 Translater] 触发 F6：开启汉化，正在还原汉化文本...");
                    RefreshSceneTexts();
                }
                else
                {
                    Log.LogInfo("[UU9 Translater] 触发 F6：暂时关闭汉化，正在还原为英文原文...");
                    RestoreOriginalSceneTexts();
                }
            }
        }

        public static void RequestSceneTextRefresh()
        {
            if (_instance == null) return;

            // 合并连续触发的 UI 回调，并让游戏完成提示框文本的写入。
            if (_instance._pendingTextRefreshFrames == 0)
            {
                _instance._pendingTextRefreshFrames = 2;
            }
        }

        private void RestoreOriginalSceneTexts()
        {
            try
            {
                int restoredCount = 0;

                // 还原 TMP_Text 文本
                TMP_Text[] tmpTexts = Resources.FindObjectsOfTypeAll<TMP_Text>();
                foreach (var tmp in tmpTexts)
                {
                    if (tmp == null || !tmp.gameObject.activeInHierarchy) continue;
                    if (TranslationManager.TryGetOriginal(tmp.text, out string original))
                    {
                        if (tmp.text != original)
                        {
                            tmp.text = original;
                            restoredCount++;
                        }
                    }
                }

                // 还原 Unity UI Text 文本
                Text[] uiTexts = Resources.FindObjectsOfTypeAll<Text>();
                foreach (var txt in uiTexts)
                {
                    if (txt == null || !txt.gameObject.activeInHierarchy) continue;
                    if (TranslationManager.TryGetOriginal(txt.text, out string original))
                    {
                        if (txt.text != original)
                        {
                            txt.text = original;
                            restoredCount++;
                        }
                    }
                }

                FontManager.RestoreOriginalFonts();

                Log.LogInfo($"[UU9 Translater] F6 还原完成，成功将 {restoredCount} 处文本还原为英文原文。");
            }
            catch (Exception ex)
            {
                Log.LogError($"[UU9 Translater] 还原英文原文 UI 出错: {ex.Message}");
            }
        }

        private void RefreshSceneTexts(bool logCompletion = true)
        {
            try
            {
                int replacedCount = 0;

                // 刷新场景内所有 TextMeshPro 文本
                TMP_Text[] tmpTexts = Resources.FindObjectsOfTypeAll<TMP_Text>();
                foreach (var tmp in tmpTexts)
                {
                    if (tmp == null || !tmp.gameObject.activeInHierarchy) continue;
                    FontManager.ApplyFont(tmp);
                    if (TranslationManager.TryGetTranslation(tmp.text, out string translated))
                    {
                        if (tmp.text != translated)
                        {
                            FontManager.ApplyTranslatedFont(tmp, translated);
                            tmp.text = translated;
                            FontManager.RefreshLayout(tmp);
                            replacedCount++;
                        }
                    }
                }

                // 刷新场景内所有 Unity UI Text 文本
                Text[] uiTexts = Resources.FindObjectsOfTypeAll<Text>();
                foreach (var txt in uiTexts)
                {
                    if (txt == null || !txt.gameObject.activeInHierarchy) continue;
                    FontManager.ApplyFont(txt);
                    if (TranslationManager.TryGetTranslation(txt.text, out string translated))
                    {
                        if (txt.text != translated)
                        {
                            txt.text = translated;
                            replacedCount++;
                        }
                    }
                }

                if (logCompletion)
                {
                    Log.LogInfo($"[UU9 Translater] F5 刷新完成，成功更新 {replacedCount} 处文本。");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[UU9 Translater] 刷新场景 UI 文本出错: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            _harmony?.UnpatchSelf();
            if (_instance == this) _instance = null;
        }
    }
}
