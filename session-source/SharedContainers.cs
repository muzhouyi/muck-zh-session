namespace MuckSaveGame
{
    using HarmonyLib;
    using Steamworks;
    using InventoryItem = global::InventoryItem;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.EventSystems;
    [HarmonyPatch]
    public static class SharedContainers
    {
        private sealed class Request
        {
            public string World, Id;
            public int Chest, Cell;
            public ItemStack Expected, Mouse;
            public bool Right;
        }
        private sealed class Receipt { public Request Request; public bool Accepted; public TradeResult Result; }
        private static readonly ReceiptBook<Receipt> receipts = new ReceiptBook<Receipt>();
        private static Request pending;
        private static float retryAt, warningAt;
        public static bool Pending { get { return pending != null; } }
        private static bool Enabled { get { return Navigation.WorldReady && Navigation.SharedEnabled; } }
        private static bool Supported(Chest chest) { return chest && (chest.GetType() == typeof(Chest) || chest is FurnaceSync); }
        private static ItemStack Stack(InventoryItem item) { return item ? new ItemStack(item.id, item.amount) : new ItemStack(-1, 0); }
        private static void WriteStack(Packet p, ItemStack s) { p.Write(s.Id); p.Write(s.Amount); }
        private static ItemStack ReadStack(Packet p) { return new ItemStack(p.ReadInt(), p.ReadInt()); }
        [HarmonyPatch(typeof(ChestManager), "IsChestOpen"), HarmonyPrefix]
        private static bool AllowShared(ChestManager __instance, int __0, ref bool __result)
        {
            Chest chest;
            if (!Enabled || !LocalClient.serverOwner || !__instance.chests.TryGetValue(__0, out chest) || !Supported(chest) || !SessionControl.CompatiblePeers()) return true;
            __result = false; return false;
        }
        [HarmonyPatch(typeof(InventoryCell), "OnPointerDown"), HarmonyPrefix]
        private static bool Click(InventoryCell __instance, PointerEventData __0)
        {
            if (Pending) return false;
            if (!Enabled || __instance.cellType != InventoryCell.CellType.Chest || !OtherInput.Instance || !Supported(OtherInput.Instance.currentChest)) return true;
            if (__0.button != PointerEventData.InputButton.Left && __0.button != PointerEventData.InputButton.Right) return false;
            if (SessionControl.Saving || !InventoryUI.Instance || !PlayerStatus.Instance || PlayerStatus.Instance.IsPlayerDead()) return false;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) { SessionControl.Say("共用容器请用左键取放整组、右键取半组或放一个。"); return false; }
            InventoryItem mouse = InventoryUI.Instance.currentMouseItem;
            if (mouse && !Traverse.Create(__instance).Method("IsItemCompatibleWithCell", mouse).GetValue<bool>()) return false;
            pending = new Request { World = Navigation.WorldId, Id = Guid.NewGuid().ToString("N"), Chest = OtherInput.Instance.currentChest.id, Cell = __instance.cellId,
                Expected = Stack(__instance.currentItem), Mouse = Stack(mouse), Right = __0.button == PointerEventData.InputButton.Right };
            SendPending(); return false;
        }
        private static void SendPending()
        {
            if (pending == null) return;
            retryAt = Time.realtimeSinceStartup + 1f;
            if (LocalClient.serverOwner) { var reply = Execute(LocalClient.instance.myId, pending); Receive(reply); return; }
            using (var p = new Packet(122))
            {
                p.Write(pending.World); p.Write(pending.Id); p.Write(pending.Chest); p.Write(pending.Cell);
                WriteStack(p, pending.Expected); WriteStack(p, pending.Mouse); p.Write(pending.Right); Net.ClientSendTCPData(p);
            }
        }
        public static void Tick()
        {
            if (Pending && GameManager.state == GameManager.GameState.Playing && Time.realtimeSinceStartup >= retryAt)
            {
                SendPending();
                if (Pending && Time.realtimeSinceStartup >= warningAt) { warningAt = Time.realtimeSinceStartup + 10f; SessionControl.Say("容器操作正在等待房主确认，请暂时不要退出或移动物品。"); }
            }
        }
        private static Receipt Execute(int from, Request r)
        {
            string owner = Server.clients.TryGetValue(from, out var client) && client.player != null ? client.player.steamId.Value.ToString() : "";
            if (string.IsNullOrEmpty(owner) || r.Id == null || r.Id.Length != 32) return new Receipt { Request = r, Result = new TradeResult { Slot = r.Expected, Mouse = r.Mouse } };
            return receipts.RunOnce(owner + "/" + r.Id, () => ExecuteNew(from, r, owner));
        }
        private static Receipt ExecuteNew(int from, Request r, string owner)
        {
            var reply = new Receipt { Request = r, Result = new TradeResult { Slot = r.Expected, Mouse = r.Mouse } };
            Chest chest;
            // Same-world validation prevents delayed packets from changing a later game.
            if (!Enabled || r.World != Navigation.WorldId || string.IsNullOrEmpty(owner) || r.Id.Length != 32 || !SessionControl.CompatiblePeers() ||
                !ChestManager.Instance || !ChestManager.Instance.chests.TryGetValue(r.Chest, out chest) || !Supported(chest) || r.Cell < 0 || r.Cell >= chest.cells.Length)
                return reply;
            var slot = Stack(chest.cells[r.Cell]); reply.Result.Slot = slot;
            if (SessionControl.Saving || !GameManager.players.TryGetValue(from, out var player) || !player || player.dead ||
                Vector3.Distance(player.transform.position, chest.transform.position) > 15f || !slot.Equals(r.Expected)) return reply;
            InventoryItem mouseType = null;
            if (!r.Mouse.Empty && (!ItemManager.Instance.allItems.TryGetValue(r.Mouse.Id, out mouseType) || r.Mouse.Amount > mouseType.max || r.Mouse.Amount <= 0)) return reply;
            if (chest is FurnaceSync && mouseType && ((r.Cell == 0 && mouseType.fuel == null) || (r.Cell == 1 && (!mouseType.processable || mouseType.processType != InventoryItem.ProcessType.Smelt)) || r.Cell == 2)) return reply;
            InventoryItem type = mouseType;
            if (!type && !slot.Empty) ItemManager.Instance.allItems.TryGetValue(slot.Id, out type);
            reply.Result = ContainerTrade.Click(slot, r.Mouse, r.Right, type && type.stackable, type ? Math.Max(1, type.max) : 1);
            reply.Accepted = true;
            ChestManager.Instance.UpdateChest(r.Chest, r.Cell, reply.Result.Slot.Id, reply.Result.Slot.Amount);
            ServerSend.UpdateChest(from, r.Chest, r.Cell, reply.Result.Slot.Id, reply.Result.Slot.Amount);
            return reply;
        }
        private static void HandleRequest(int from, Packet p)
        {
            var r = new Request { World = p.ReadString(), Id = p.ReadString(), Chest = p.ReadInt(), Cell = p.ReadInt(), Expected = ReadStack(p), Mouse = ReadStack(p), Right = p.ReadBool() };
            var reply = Execute(from, r);
            if (ChestManager.Instance && ChestManager.Instance.chests.TryGetValue(r.Chest, out var latest) && r.Cell >= 0 && r.Cell < latest.cells.Length) reply.Result.Slot = Stack(latest.cells[r.Cell]);
            using (var answer = new Packet(122))
            {
                answer.Write(r.World); answer.Write(r.Id); answer.Write(r.Chest); answer.Write(r.Cell); answer.Write(reply.Accepted);
                WriteStack(answer, reply.Result.Slot); WriteStack(answer, reply.Result.Mouse); Net.ServerSendTCPData(from, answer);
            }
        }
        private static void HandleReply(Packet p)
        {
            var r = new Request { World = p.ReadString(), Id = p.ReadString(), Chest = p.ReadInt(), Cell = p.ReadInt() };
            bool accepted = p.ReadBool(); var result = new TradeResult { Slot = ReadStack(p), Mouse = ReadStack(p) };
            Receive(new Receipt { Request = r, Accepted = accepted, Result = result });
        }
        private static InventoryItem Create(ItemStack s)
        {
            if (s.Empty || !ItemManager.Instance.allItems.TryGetValue(s.Id, out var item)) return null;
            var copy = ScriptableObject.CreateInstance<InventoryItem>(); copy.Copy(item, s.Amount); return copy;
        }
        private static void Receive(Receipt reply)
        {
            if (pending == null || reply.Request.Id != pending.Id || reply.Request.World != pending.World) return;
            var request = pending;
            // Do not apply an old receipt to a later world or a different cursor state.
            if (request.World != Navigation.WorldId || !InventoryUI.Instance || !Stack(InventoryUI.Instance.currentMouseItem).Equals(request.Mouse))
            { SessionControl.Say("容器状态发生变化，操作确认暂未应用。请保留当前会话以便核对。"); return; }
            if (reply.Accepted) { InventoryUI.Instance.currentMouseItem = Create(reply.Result.Mouse); InventoryUI.Instance.PlaceItem(InventoryUI.Instance.currentMouseItem); }
            else SessionControl.Say("这一格已被队友或熔炉更新，本次没有移动物品，请重新点击。");
            if (ChestManager.Instance && ChestManager.Instance.chests.ContainsKey(request.Chest))
                ChestManager.Instance.UpdateChest(request.Chest, request.Cell, reply.Result.Slot.Id, reply.Result.Slot.Amount);
            pending = null;
            RefreshVisible(request.Chest);
        }
        private static void RefreshVisible(int id)
        {
            if (!OtherInput.Instance || !OtherInput.Instance.currentChest || OtherInput.Instance.currentChest.id != id || !InventoryUI.Instance) return;
            foreach (var cell in InventoryUI.Instance.CraftingUi ? InventoryUI.Instance.CraftingUi.GetComponentsInChildren<InventoryCell>(true) : new InventoryCell[0])
                if (cell.cellType == InventoryCell.CellType.Chest && cell.cellId >= 0 && cell.cellId < OtherInput.Instance.currentChest.cells.Length)
                { cell.currentItem = KeepInventory.Clone(OtherInput.Instance.currentChest.cells[cell.cellId]); cell.UpdateCell(); }
        }
        [HarmonyPatch(typeof(ChestManager), "UpdateChest"), HarmonyPostfix]
        private static void UpdateVisible(int __0) { if (!Pending) RefreshVisible(__0); }
        [HarmonyPatch(typeof(ServerHandle), "UpdateChest"), HarmonyPrefix]
        private static bool PreventLegacyOverwrite(int __0, Packet __1)
        {
            if (!Enabled || !ChestManager.Instance) return true;
            int chestId = __1.ReadInt(false);
            Chest chest;
            return __0 == LocalClient.instance.myId || !ChestManager.Instance.chests.TryGetValue(chestId, out chest) || !Supported(chest);
        }
        [HarmonyPatch(typeof(InventoryUI), "DropItem"), HarmonyPrefix]
        private static bool NoDropWhilePending() { return !Pending && !(Navigation.KeepOnDeath && PlayerStatus.Instance && PlayerStatus.Instance.IsPlayerDead()); }
        [HarmonyPatch(typeof(InventoryUI), "AddItemToInventory"), HarmonyPrefix]
        private static bool NoPickupWhilePending(InventoryItem __0, ref int __result) { if (!Pending) return true; __result = __0 ? __0.amount : 0; return false; }
        [HarmonyPatch(typeof(OtherInput), "ToggleInventory"), HarmonyPrefix]
        private static bool NoCloseWhilePending() { return !Pending; }
        [HarmonyPatch(typeof(InventoryUI), "CraftItem"), HarmonyPrefix]
        private static bool NoCraftWhilePending() { return !Pending; }
        [HarmonyPatch(typeof(LocalClient), "InitializeClientData"), HarmonyPostfix]
        private static void RegisterClient() { LocalClient.packetHandlers[122] = HandleReply; }
        [HarmonyPatch(typeof(Server), "InitializeServerPackets"), HarmonyPostfix]
        private static void RegisterServer() { Server.PacketHandlers[122] = HandleRequest; }
        public static void Reset() { pending = null; receipts.Clear(); retryAt = warningAt = 0; }
    }
}
