namespace MuckSaveGame
{
    using HarmonyLib;
    using System;
    using System.Globalization;
    using System.Reflection;
    using TMPro;
    using UnityEngine;

    [HarmonyPatch]
    public static class ItemStatsTooltip
    {
        private static readonly FieldInfo textField = AccessTools.Field(typeof(ItemInfo), "text");
        private static readonly FieldInfo imageField = AccessTools.Field(typeof(ItemInfo), "image");
        private static readonly MethodInfo fit = AccessTools.Method(typeof(ItemInfo), "FitToText");
        private static readonly MethodInfo font = Type.GetType("UU9.Muck.Translater.FontManager, UU9.Muck.Translater")?.GetMethod("ApplyTranslatedFont", new[] { typeof(TMP_Text), typeof(string) });
        private static InventoryCell hoveredCell;
        private static string Value(float number) { return number.ToString("0.##", CultureInfo.InvariantCulture); }
        public static string Stats(InventoryItem item)
        {
            if (!item) return "";
            if (item.type == InventoryItem.ItemType.Food)
                return "血量 +" + Value(item.heal) + "\n饱食度 +" + Value(item.hunger) + "\n体力 +" + Value(item.stamina);
            if (item.type == InventoryItem.ItemType.Sword) return "基础伤害 " + item.attackDamage.ToString(CultureInfo.InvariantCulture);
            return "";
        }
        [HarmonyPatch(typeof(InventoryCell), "OnPointerEnter"), HarmonyPostfix]
        private static void Hover(InventoryCell __instance)
        {
            if (!ItemInfo.Instance || !__instance.currentItem) return;
            string stats = Stats(__instance.currentItem); if (stats.Length == 0) { hoveredCell = null; return; }
            hoveredCell = __instance;
            var text = textField.GetValue(ItemInfo.Instance) as TextMeshProUGUI; if (!text) return;
            // Preserve vanilla item descriptions and crafting requirements. Close
            // their open style tags so the added numbers have a readable size.
            string original = text.text ?? "";
            string value = original + "</i></size>\n<size=70%>" + stats + "</size>";
            text.SetCharArray(value.ToCharArray());
            if (font != null) font.Invoke(null, new object[] { text, value });
            text.ForceMeshUpdate(true);
            fit.Invoke(ItemInfo.Instance, null);
            var image = imageField.GetValue(ItemInfo.Instance) as UnityEngine.UI.RawImage;
            if (image) image.raycastTarget = false;
            text.raycastTarget = false;
        }
        [HarmonyPatch(typeof(InventoryCell), "OnPointerExit"), HarmonyPostfix]
        private static void Exit(InventoryCell __instance)
        {
            if (hoveredCell != __instance) return;
            hoveredCell = null;
            if (ItemInfo.Instance) ItemInfo.Instance.SetText("", false);
        }
        [HarmonyPatch(typeof(ItemInfo), "Update"), HarmonyPostfix]
        private static void KeepOnScreen(ItemInfo __instance)
        {
            if (!hoveredCell) return;
            var image = imageField.GetValue(__instance) as UnityEngine.UI.RawImage; if (!image) return;
            var corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
            float dx = corners[0].x < 8 ? 8 - corners[0].x : corners[2].x > Screen.width - 8 ? Screen.width - 8 - corners[2].x : 0;
            float dy = corners[0].y < 8 ? 8 - corners[0].y : corners[2].y > Screen.height - 8 ? Screen.height - 8 - corners[2].y : 0;
            __instance.transform.position += new Vector3(dx, dy, 0);
        }
    }
}
