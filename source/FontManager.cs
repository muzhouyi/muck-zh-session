using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace UU9.Muck.Translater
{
    public static class FontManager
    {
        private static Font _chineseOSFont;
        private static TMP_FontAsset _chineseTMPFont;
        private static ManualLogSource _logger;
        private static bool _initialized;
        private static readonly Dictionary<int, TMP_FontAsset> OriginalTMPFonts = new Dictionary<int, TMP_FontAsset>();
        private static readonly Dictionary<int, Material> OriginalTMPMaterials = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Font> OriginalUIFonts = new Dictionary<int, Font>();
        private static readonly Dictionary<int, Material> ChineseMaterialsBySource = new Dictionary<int, Material>();

        public static void Initialize(ManualLogSource logger)
        {
            if (_initialized) return;
            _logger = logger;

            try
            {
                // 1. 从系统 Fonts 目录载入 CJK 字体文件
                string configuredFontFile = Path.GetFileName(Plugin.FontFile);
                if (string.IsNullOrWhiteSpace(configuredFontFile)) configuredFontFile = "msyh.ttc";
                string modFontDirectory = Path.Combine(Paths.ConfigPath, "UU9.Muck.Translater", "Font");
                string[] candidatePaths = new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), configuredFontFile),
                    Path.Combine(modFontDirectory, configuredFontFile),
                    @"C:\Windows\Fonts\msyh.ttc",   // 微软雅黑
                    @"C:\Windows\Fonts\msyh.ttf",
                    @"C:\Windows\Fonts\simhei.ttf", // 黑体
                    @"C:\Windows\Fonts\simsun.ttc", // 宋体
                    @"C:\Windows\Fonts\arial.ttf"
                };

                foreach (string path in candidatePaths)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            _chineseOSFont = new Font(path);
                            if (_chineseOSFont != null)
                            {
                                UnityEngine.Object.DontDestroyOnLoad(_chineseOSFont);
                                _chineseOSFont.hideFlags = HideFlags.HideAndDontSave;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                if (_chineseOSFont == null)
                {
                    string fontName = Path.GetFileNameWithoutExtension(configuredFontFile);
                    string[] fontNames = new string[] { fontName, "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" };
                    _chineseOSFont = Font.CreateDynamicFontFromOSFont(fontNames, 24);
                    if (_chineseOSFont != null)
                    {
                        UnityEngine.Object.DontDestroyOnLoad(_chineseOSFont);
                        _chineseOSFont.hideFlags = HideFlags.HideAndDontSave;
                    }
                }

                if (_chineseOSFont == null)
                {
                    _chineseOSFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }

                if (_chineseOSFont != null)
                {
                    // 2. 构建 TMP_FontAsset
                    try
                    {
                        // 默认的 1024 字体图集在容纳完整的中文字库之前就会很快填满。
                        // 使用更大的动态字体图集，可以确保每个翻译后的汉字都有正确的字形数据，从而保证文字排版正常。
                        _chineseTMPFont = TMP_FontAsset.CreateFontAsset(
                            _chineseOSFont,
                            90,
                            5,
                            GlyphRenderMode.SDFAA,
                            4096,
                            4096,
                            AtlasPopulationMode.Dynamic,
                            false);
                    }
                    catch { }

                    if (_chineseTMPFont != null)
                    {
                        UnityEngine.Object.DontDestroyOnLoad(_chineseTMPFont);
                        _chineseTMPFont.name = "UU9_Chinese_TMPFont";
                        _chineseTMPFont.hideFlags = HideFlags.HideAndDontSave;
                        _chineseTMPFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                        _chineseTMPFont.isMultiAtlasTexturesEnabled = false;

                        // 微调并同步 FaceInfo Metrics 比例，确保 Fallback 时行高（lineHeight）与原英文 Font Asset 完全对齐
                        try
                        {
                            if (TMP_Settings.defaultFontAsset != null)
                            {
                                var defaultFace = TMP_Settings.defaultFontAsset.faceInfo;
                                var chineseFace = _chineseTMPFont.faceInfo;

                                if (defaultFace.lineHeight > 0 && chineseFace.lineHeight > 0)
                                {
                                    float scale = (float)defaultFace.lineHeight / chineseFace.lineHeight;
                                    chineseFace.lineHeight = defaultFace.lineHeight;
                                    chineseFace.ascentLine = (int)(chineseFace.ascentLine * scale);
                                    chineseFace.descentLine = (int)(chineseFace.descentLine * scale);
                                    _chineseTMPFont.faceInfo = chineseFace;
                                }
                            }
                        }
                        catch { }

                        // 3. 安全注入 TMP 全局 Settings 降级字体表
                        try
                        {
                            if (TMP_Settings.fallbackFontAssets != null && !TMP_Settings.fallbackFontAssets.Contains(_chineseTMPFont))
                            {
                                TMP_Settings.fallbackFontAssets.Add(_chineseTMPFont);
                            }

                            if (TMP_Settings.defaultFontAsset != null)
                            {
                                if (TMP_Settings.defaultFontAsset.fallbackFontAssetTable == null)
                                {
                                    TMP_Settings.defaultFontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                                }
                                if (!TMP_Settings.defaultFontAsset.fallbackFontAssetTable.Contains(_chineseTMPFont))
                                {
                                    TMP_Settings.defaultFontAsset.fallbackFontAssetTable.Add(_chineseTMPFont);
                                }
                            }
                        }
                        catch { }
                    }
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[UU9 Translater] FontManager 初始化异常: {ex.Message}");
            }
        }

        public static void AddCharactersToFont(string text)
        {
            if (_chineseTMPFont == null || string.IsNullOrEmpty(text)) return;
            try
            {
                _chineseTMPFont.TryAddCharacters(text);
            }
            catch { }
        }

        // TMP 的备用字体可以正确显示中文，但它在计算文本首选尺寸时，
        // 仍然会使用主字体中缺失字形的尺寸信息。
        // 对于翻译后的中日韩（CJK）文字，使用中文字体资源作为主字体，
        // 这样 ContentSizeFitter / 提示框面板就能获取实际的字符宽度，从而正确计算文本布局。
        public static void ApplyTranslatedFont(TMP_Text tmpText, string translated)
        {
            if (tmpText == null || _chineseTMPFont == null || string.IsNullOrEmpty(translated)) return;

            try
            {
                AddCharactersToFont(translated);
                if (ContainsNonAscii(translated) && tmpText.font != _chineseTMPFont)
                {
                    RememberOriginalFont(tmpText);
                    Material originalMaterial = tmpText.fontSharedMaterial;
                    tmpText.font = _chineseTMPFont;
                    Material chineseMaterial = GetChineseMaterial(originalMaterial);
                    if (chineseMaterial != null)
                    {
                        tmpText.fontSharedMaterial = chineseMaterial;
                    }
                }
            }
            catch { }
        }

        public static void RefreshLayout(TMP_Text tmpText)
        {
            if (tmpText == null) return;

            try
            {
                tmpText.SetVerticesDirty();
                tmpText.SetLayoutDirty();
                tmpText.ForceMeshUpdate();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(tmpText.rectTransform);

                RectTransform parent = tmpText.rectTransform.parent as RectTransform;
                if (parent != null)
                {
                    UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(parent);
                }

            }
            catch { }
        }

        private static bool ContainsNonAscii(string text)
        {
            foreach (char character in text)
            {
                if (character > 127) return true;
            }
            return false;
        }

        public static void ApplyFont(TMP_Text tmpText)
        {
            if (tmpText == null || _chineseTMPFont == null) return;

            try
            {
                if (tmpText.font != null)
                {
                    if (tmpText.font != _chineseTMPFont)
                    {
                        if (tmpText.font.fallbackFontAssetTable == null)
                        {
                            tmpText.font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                        }

                        if (!tmpText.font.fallbackFontAssetTable.Contains(_chineseTMPFont))
                        {
                            tmpText.font.fallbackFontAssetTable.Add(_chineseTMPFont);
                        }
                    }
                }
                else
                {
                    tmpText.font = _chineseTMPFont;
                }

                // 对多行文本保持像素一致的行高/行距，避免中文字体与原字体行高(lineHeight)差异导致与右侧数值对不上行
                if (tmpText.text != null && tmpText.text.Contains("\n"))
                {
                    tmpText.lineSpacingAdjustment = 0;
                }
            }
            catch { }
        }

        public static void ApplyFont(UnityEngine.UI.Text uiText)
        {
            if (uiText == null || _chineseOSFont == null) return;

            try
            {
                RememberOriginalFont(uiText);
                uiText.font = _chineseOSFont;
            }
            catch { }
        }

        public static void RestoreOriginalFonts()
        {
            try
            {
                TMP_Text[] tmpTexts = Resources.FindObjectsOfTypeAll<TMP_Text>();
                foreach (TMP_Text tmpText in tmpTexts)
                {
                    if (tmpText == null) continue;

                    int id = tmpText.GetInstanceID();
                    if (OriginalTMPFonts.TryGetValue(id, out TMP_FontAsset originalFont))
                    {
                        if (originalFont != null)
                        {
                            tmpText.font = originalFont;
                        }
                        if (OriginalTMPMaterials.TryGetValue(id, out Material originalMaterial) && originalMaterial != null)
                        {
                            tmpText.fontSharedMaterial = originalMaterial;
                        }
                        OriginalTMPFonts.Remove(id);
                        OriginalTMPMaterials.Remove(id);
                    }
                }

                UnityEngine.UI.Text[] uiTexts = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>();
                foreach (UnityEngine.UI.Text uiText in uiTexts)
                {
                    if (uiText == null) continue;

                    int id = uiText.GetInstanceID();
                    if (OriginalUIFonts.TryGetValue(id, out Font originalFont))
                    {
                        if (originalFont != null)
                        {
                            uiText.font = originalFont;
                        }
                        OriginalUIFonts.Remove(id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[UU9 Translater] Restore original fonts failed: {ex.Message}");
            }
        }

        private static void RememberOriginalFont(TMP_Text tmpText)
        {
            int id = tmpText.GetInstanceID();
            if (!OriginalTMPFonts.ContainsKey(id))
            {
                OriginalTMPFonts[id] = tmpText.font;
                OriginalTMPMaterials[id] = tmpText.fontSharedMaterial;
            }
        }

        private static Material GetChineseMaterial(Material sourceMaterial)
        {
            if (sourceMaterial == null || _chineseTMPFont == null) return null;

            int sourceId = sourceMaterial.GetInstanceID();
            if (ChineseMaterialsBySource.TryGetValue(sourceId, out Material cachedMaterial) && cachedMaterial != null)
            {
                return cachedMaterial;
            }

            try
            {
                // 保留游戏原有的 Stencil、ZTest 和渲染队列设置
                // 不然可能出现文字在物体背后的情况
                // 只替换字体图集纹理为中文字体图集。
                Material chineseMaterial = new Material(sourceMaterial);
                chineseMaterial.name = "UU9_Chinese_" + sourceMaterial.name;
                chineseMaterial.mainTexture = _chineseTMPFont.atlasTexture;
                chineseMaterial.hideFlags = HideFlags.HideAndDontSave;
                ChineseMaterialsBySource[sourceId] = chineseMaterial;
                return chineseMaterial;
            }
            catch
            {
                return null;
            }
        }

        private static void RememberOriginalFont(UnityEngine.UI.Text uiText)
        {
            int id = uiText.GetInstanceID();
            if (!OriginalUIFonts.ContainsKey(id))
            {
                OriginalUIFonts[id] = uiText.font;
            }
        }

    }
}
