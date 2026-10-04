namespace MuckSaveGame
{
    using HarmonyLib;
    using System;
    using UnityEngine;
    [HarmonyPatch]
    public static class KeepInventory
    {
        private static bool retaining;
        private static InventoryItem[] cells, armor;
        private static InventoryItem mouse;
        internal static InventoryItem Clone(InventoryItem item)
        {
            if (!item) return null;
            var copy = ScriptableObject.CreateInstance<InventoryItem>();
            copy.Copy(item, item.amount); return copy;
        }
        [HarmonyPatch(typeof(PlayerStatus), "PlayerDied"), HarmonyPrefix]
        private static void BeforeDeath()
        {
            Navigation.ClosePanel();
            if (!Navigation.WorldReady || !Navigation.KeepOnDeath || !InventoryUI.Instance) return;
            cells = Array.ConvertAll(InventoryUI.Instance.allCells, c => Clone(c.currentItem));
            armor = Array.ConvertAll(PlayerStatus.Instance.armor, Clone);
            mouse = Clone(InventoryUI.Instance.currentMouseItem); retaining = true;
        }
        [HarmonyPatch(typeof(InventoryUI), "DropItemIntoWorld"), HarmonyPrefix]
        private static bool PreventDeathDrop() { return !retaining; }
        [HarmonyPatch(typeof(PlayerStatus), "PlayerDied"), HarmonyFinalizer]
        private static Exception AfterDeath(Exception __exception)
        {
            if (!retaining) return __exception;
            try
            {
                for (int i = 0; i < cells.Length && i < InventoryUI.Instance.allCells.Length; i++)
                { InventoryUI.Instance.allCells[i].currentItem = cells[i]; InventoryUI.Instance.allCells[i].UpdateCell(); }
                InventoryUI.Instance.currentMouseItem = mouse;
                InventoryUI.Instance.PlaceItem(InventoryUI.Instance.currentMouseItem);
                for (int i = 0; i < armor.Length; i++) PlayerStatus.Instance.UpdateArmor(i, armor[i] ? armor[i].id : -1);
                if (Hotbar.Instance) Hotbar.Instance.UpdateHotbar();
            }
            finally { retaining = false; cells = null; armor = null; mouse = null; }
            return __exception;
        }
    }
}
