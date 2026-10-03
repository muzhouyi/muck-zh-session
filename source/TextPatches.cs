using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine.UI;

namespace UU9.Muck.Translater
{
    // ==========================================
    // TMPro.TMP_Text Hooks
    // ==========================================
    [HarmonyPatch(typeof(TMP_Text))]
    internal static class TMP_Text_Patches
    {
        // 1. Hook TMP_Text 核心内部赋值方法 SetTextInternal
        [HarmonyPrefix]
        [HarmonyPatch("SetTextInternal")]
        public static void SetTextInternal_Prefix(TMP_Text __instance, ref string __0)
        {
            TranslateAndUpdateFont(__instance, ref __0);
        }

        // 2. Hook TMP_Text.text setter 属性
        [HarmonyPrefix]
        [HarmonyPatch("text", MethodType.Setter)]
        public static void SetTextProperty_Prefix(TMP_Text __instance, ref string value)
        {
            TranslateAndUpdateFont(__instance, ref value);
        }

        // 3. Hook TMP 的数值格式化重载。提示文本通常通过
        // SetText("Wood - {0}", count) 生成，这种方式会绕过 text
        // 属性的 setter。在这个版本中，TMP 会将这些参数以 float 形式暴露出来。
        [HarmonyPrefix]
        [HarmonyPatch("SetText", new Type[] { typeof(string), typeof(bool) })]
        public static void SetText_Prefix(TMP_Text __instance, ref string __0)
        {
            TranslateAndUpdateFont(__instance, ref __0);
        }

        [HarmonyPrefix]
        [HarmonyPatch("SetText", new Type[] { typeof(string), typeof(float) })]
        public static void SetText_OneValue_Prefix(TMP_Text __instance, ref string __0)
        {
            TranslateAndUpdateFont(__instance, ref __0);
        }

        [HarmonyPrefix]
        [HarmonyPatch("SetText", new Type[] { typeof(string), typeof(float), typeof(float) })]
        public static void SetText_TwoValues_Prefix(TMP_Text __instance, ref string __0)
        {
            TranslateAndUpdateFont(__instance, ref __0);
        }

        [HarmonyPrefix]
        [HarmonyPatch("SetText", new Type[] { typeof(string), typeof(float), typeof(float), typeof(float) })]
        public static void SetText_ThreeValues_Prefix(TMP_Text __instance, ref string __0)
        {
            TranslateAndUpdateFont(__instance, ref __0);
        }

        // 4. Hook SetText(StringBuilder) 重载
        [HarmonyPrefix]
        [HarmonyPatch("SetText", new Type[] { typeof(StringBuilder) })]
        public static void SetText_StringBuilder_Prefix(TMP_Text __instance, ref StringBuilder __0)
        {
            try
            {
                if (__0 == null || __instance == null) return;
                string str = __0.ToString();
                FontManager.ApplyFont(__instance);

                if (TranslationManager.TryGetTranslation(str, out string translated))
                {
                    FontManager.ApplyTranslatedFont(__instance, translated);
                    __0 = new StringBuilder(translated);
                }
            }
            catch { }
        }

        private static void TranslateAndUpdateFont(TMP_Text component, ref string text)
        {
            try
            {
                if (!Plugin.IsTranslationEnabled) return;
                if (component == null || string.IsNullOrEmpty(text)) return;

                FontManager.ApplyFont(component);

                if (TranslationManager.TryGetTranslation(text, out string translated))
                {
                    FontManager.ApplyTranslatedFont(component, translated);
                    text = translated;
                }
            }
            catch { }
        }
    }

    // ==========================================
    // 按需动态字形补全 Hook (TMP_GetTextElement_Patch)
    // ==========================================
    [HarmonyPatch]
    internal static class TMP_GetTextElement_Patch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(TMP_Text), "GetTextElement", new Type[] { typeof(int) });
            if (method == null)
            {
                method = AccessTools.Method(typeof(TMP_Text), "GetTextElement", new Type[] { typeof(uint) });
            }
            if (method == null)
            {
                method = AccessTools.Method(typeof(TMP_Text), "GetTextElement", new Type[] { typeof(char) });
            }
            if (method == null)
            {
                foreach (var candidate in typeof(TMP_Text).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) { if (candidate.Name == "GetTextElement") { method = candidate; break; } }
            }
            return method;
        }

        [HarmonyPostfix]
        public static void Postfix(object __0, ref TMP_TextElement __result)
        {
            if (__result == null && __0 != null)
            {
                try
                {
                    int code = Convert.ToInt32(__0);
                    if (code > 0)
                    {
                        string charStr = char.ConvertFromUtf32(code);
                        FontManager.AddCharactersToFont(charStr);
                    }
                }
                catch { }
            }
        }
    }

    // ==========================================
    // UnityEngine.UI.Text Hooks
    // ==========================================
    [HarmonyPatch(typeof(Text))]
    internal static class UnityEngine_UI_Text_Patches
    {
        // 1. Hook Text.text setter 属性
        [HarmonyPrefix]
        [HarmonyPatch("text", MethodType.Setter)]
        public static void SetTextProperty_Prefix(Text __instance, ref string value)
        {
            TranslateAndUpdateFont(__instance, ref value);
        }

        private static void TranslateAndUpdateFont(Text component, ref string text)
        {
            try
            {
                if (!Plugin.IsTranslationEnabled) return;
                if (component == null || string.IsNullOrEmpty(text)) return;

                FontManager.ApplyFont(component);

                if (TranslationManager.TryGetTranslation(text, out string translated))
                {
                    text = translated;
                }
            }
            catch { }
        }
    }

    // ==========================================
    // 精准 Hook TextMeshProUGUI 与 Text 的 OnEnable 生命周期函数
    // 当 UI 组件被激活/显示时触发一次精准翻译 (最高性能，无递归/无冗余计算)
    // ==========================================
    [HarmonyPatch(typeof(TextMeshProUGUI))]
    internal static class TextMeshProUGUI_OnEnable_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("OnEnable")]
        public static void Postfix(TextMeshProUGUI __instance)
        {
            try
            {
                if (!Plugin.IsTranslationEnabled || __instance == null) return;
                string text = __instance.text;
                if (string.IsNullOrEmpty(text)) return;

                FontManager.ApplyFont(__instance);
                if (TranslationManager.TryGetTranslation(text, out string translated))
                {
                    if (text != translated)
                    {
                        FontManager.ApplyTranslatedFont(__instance, translated);
                        __instance.text = translated;
                        FontManager.RefreshLayout(__instance);
                    }
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(TextMeshPro))]
    internal static class TextMeshPro_OnEnable_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("OnEnable")]
        public static void Postfix(TextMeshPro __instance)
        {
            try
            {
                if (!Plugin.IsTranslationEnabled || __instance == null) return;
                string text = __instance.text;
                if (string.IsNullOrEmpty(text)) return;

                FontManager.ApplyFont(__instance);
                if (TranslationManager.TryGetTranslation(text, out string translated))
                {
                    if (text != translated)
                    {
                        FontManager.ApplyTranslatedFont(__instance, translated);
                        __instance.text = translated;
                        FontManager.RefreshLayout(__instance);
                    }
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Text))]
    internal static class Text_OnEnable_Patch
    {
        [HarmonyPostfix]
        [HarmonyPatch("OnEnable")]
        public static void Postfix(Text __instance)
        {
            try
            {
                if (!Plugin.IsTranslationEnabled || __instance == null) return;
                string text = __instance.text;
                if (string.IsNullOrEmpty(text)) return;

                FontManager.ApplyFont(__instance);
                if (TranslationManager.TryGetTranslation(text, out string translated))
                {
                    if (text != translated)
                    {
                        __instance.text = translated;
                    }
                }
            }
            catch { }
        }
    }

    // Muck 会在这些 UI 回调执行后写入制作需求和悬停卡片文本。
    // 在最终文本设置完成后，延迟执行一次扫描。
    [HarmonyPatch]
    internal static class CraftingUI_UpdateCraftables_Patch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            Type craftingUI = AccessTools.TypeByName("CraftingUI");
            return craftingUI == null ? null : AccessTools.Method(craftingUI, "UpdateCraftables");
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.RequestSceneTextRefresh();
        }
    }

    [HarmonyPatch]
    internal static class InventoryCell_OnPointerEnter_Patch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            Type inventoryCell = AccessTools.TypeByName("InventoryCell");
            return inventoryCell == null ? null : AccessTools.Method(inventoryCell, "OnPointerEnter");
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.RequestSceneTextRefresh();
        }
    }
}

