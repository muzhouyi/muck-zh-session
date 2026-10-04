namespace MuckSaveGame
{
    using HarmonyLib;
    using BepInEx.Configuration;
    using System;
    using System.Globalization;
    using System.Reflection;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    [HarmonyPatch]
    public static class ItemStatsTooltip
    {
        private static readonly FieldInfo textField = AccessTools.Field(typeof(ItemInfo), "text");
        private static readonly FieldInfo imageField = AccessTools.Field(typeof(ItemInfo), "image");
        private static readonly MethodInfo fit = AccessTools.Method(typeof(ItemInfo), "FitToText");
        private static readonly MethodInfo font = Type.GetType("UU9.Muck.Translater.FontManager, UU9.Muck.Translater")?.GetMethod("ApplyTranslatedFont", new[] { typeof(TMP_Text), typeof(string) });
        private static InventoryCell hoveredCell;
        private static ConfigEntry<bool> enabled;
        public static bool Enabled { get { return enabled == null || enabled.Value; } }
        public static void Initialize(ConfigFile config)
        {
            enabled = config.Bind("Tooltips", "ShowItemStats", true, "Show food recovery values and sword base damage when hovering items. Can be changed in the game settings.");
        }
        private static void SetEnabled(bool value)
        {
            enabled.Value = value; enabled.ConfigFile.Save();
            if (hoveredCell && hoveredCell.currentItem)
            {
                // TMP's string setter may ignore the same vanilla description
                // after our SetCharArray overlay; reset its cached input first.
                var text = ItemInfo.Instance ? textField.GetValue(ItemInfo.Instance) as TextMeshProUGUI : null;
                if (text) text.text = "";
                hoveredCell.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));
                if (text) { text.ForceMeshUpdate(true); fit.Invoke(ItemInfo.Instance, null); }
            }
        }
        [HarmonyPatch(typeof(Settings), "Start"), HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void AddSetting(Settings __instance)
        {
            if (!__instance.tutorial || __instance.tutorial.transform.parent.Find("MuckSession.ItemStatsToggle")) return;
            var row = UnityEngine.Object.Instantiate(__instance.tutorial.gameObject, __instance.tutorial.transform.parent);
            row.name = "MuckSession.ItemStatsToggle";
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            label.text = "物品数值提示";
            if (font != null) font.Invoke(null, new object[] { label, "物品数值提示" });
            var setting = row.GetComponentInChildren<MyBoolSetting>();
            setting.onClick = new Setting.ButtonClickedEvent();
            setting.SetSetting(Enabled);
            var button = row.GetComponentInChildren<Button>(); button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => { SetEnabled(!Enabled); setting.SetSetting(Enabled); });
        }
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
            if (!Enabled) return;
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
            if (!Enabled || !hoveredCell) return;
            var image = imageField.GetValue(__instance) as UnityEngine.UI.RawImage; if (!image) return;
            var corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
            float dx = corners[0].x < 8 ? 8 - corners[0].x : corners[2].x > Screen.width - 8 ? Screen.width - 8 - corners[2].x : 0;
            float dy = corners[0].y < 8 ? 8 - corners[0].y : corners[2].y > Screen.height - 8 ? Screen.height - 8 - corners[2].y : 0;
            __instance.transform.position += new Vector3(dx, dy, 0);
        }
    }
}
