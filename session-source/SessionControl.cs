namespace MuckSaveGame
{
    using HarmonyLib;
    using System;
    using System.Collections.Generic;
    using Steamworks;
    using UnityEngine;

    [HarmonyPatch]
    public static class SessionControl
    {
        public static readonly SnapshotGate Gate = new SnapshotGate();
        public static bool Saving;
        public static bool Paused;
        private static bool readySent;
        private static readonly Dictionary<int, string> pendingReady = new Dictionary<int, string>();
        private static readonly HashSet<int> restored = new HashSet<int>();
        private static float priorScale = 1f;
        private static float nextBroadcast;
        public const string ProtocolVersion = "0.9.2";
        private static readonly LobbySyncState lobbySync = new LobbySyncState();
        private static readonly PeerProtocolRegistry peerProtocols = new PeerProtocolRegistry();
        private const int HelloChannel = 94;
        private static float nextHello;
        private static readonly byte[] hello = PeerProtocolRegistry.Hello(ProtocolVersion);
        private static Steamworks.Data.Lobby CurrentLobby
        {
            get { return SteamManager.Instance.currentLobby; }
        }
        private static void AdvertiseVersion()
        {
            if (!SteamManager.Instance) return;
            var current = CurrentLobby;
            try
            {
                lobbySync.PublishIfDue(current.Id.Value, Time.realtimeSinceStartup,
                    () => current.SetMemberData("muck-session-version", ProtocolVersion));
            }
            catch (Exception ex)
            {
                if (lobbySync.ShouldNotify("metadata-error", Time.realtimeSinceStartup)) Plugin.Log.LogWarning("Version marker publishing will retry: " + ex.Message);
            }
        }
        private static void CompatibilityNotice(string message)
        {
            if (lobbySync.ShouldNotify(message, Time.realtimeSinceStartup)) Say(message);
        }
        private static bool CompatiblePeers()
        {
            foreach (var pair in Server.clients)
            {
                if (pair.Value == null || pair.Value.player == null || pair.Key == LocalClient.instance.myId) continue;
                if (NetworkController.Instance.networkType == NetworkController.NetworkType.Classic)
                {
                    CompatibilityNotice("联机存档暂停目前仅支持 Steam 房间。");
                    return false;
                }
                if (peerProtocols.Verified(pair.Key, pair.Value.player.steamId.Value, pair.Value.player, ProtocolVersion)) continue;
                string marker = "";
                try { if (SteamManager.Instance && CurrentLobby.Id.Value != 0) marker = CurrentLobby.GetMemberData(new Friend(pair.Value.player.steamId), "muck-session-version"); }
                catch (Exception ex) { Plugin.Log.LogDebug("Lobby metadata unavailable: " + ex.Message); }
                if (marker == ProtocolVersion)
                {
                    peerProtocols.Confirm(pair.Key, pair.Value.player.steamId.Value, pair.Value.player, marker);
                    continue;
                }
                if (marker != ProtocolVersion)
                {
                    string name = pair.Value.player.username.Replace("<", "＜").Replace(">", "＞");
                    CompatibilityNotice(string.IsNullOrEmpty(marker)
                        ? "正在等待队友「" + name + "」的联机模组确认。请双方使用 0.9.4，刚加入时稍等几秒再试。"
                        : "队友「" + name + "」的模组通信版本不一致，请双方更新为 0.9.4。");
                    return false;
                }
            }
            lobbySync.ClearNotice();
            return true;
        }
        public static void Say(string message)
        {
            Plugin.Log.LogInfo(message);
            if (ChatBox.Instance) ChatBox.Instance.AppendMessage(-1, message, "");
        }
        public static bool BeginSave()
        {
            if (!LocalClient.serverOwner || !World.doSave || string.IsNullOrEmpty(LoadManager.selectedSavePath)) return false;
            if (Saving) { Say("正在存档，请等待完成。"); return false; }
            if (!CompatiblePeers()) return false;
            bool allDead = GameManager.players.Count > 0;
            foreach (var player in GameManager.players.Values) if (!player.dead) allDead = false;
            if (World.isLeavingIsland || allDead) { Say("当前状态无法存档。"); return false; }
            Dictionary<int, string> peers = new Dictionary<int, string>();
            foreach (var pair in Server.clients)
                if (pair.Value != null && pair.Value.player != null && pair.Key != LocalClient.instance.myId)
                    peers.Add(pair.Key, pair.Value.player.steamId.Value.ToString());
            Gate.Begin(peers); Saving = true;
            return true;
        }
        public static void EndSave() { Saving = false; Gate.Clear(); }
        public static void QueueRestore(int client, string steamId)
        {
            if (!Server.clients.TryGetValue(client, out var c) || c.player == null || c.player.steamId.Value.ToString() != steamId) return;
            if (!restored.Contains(client)) pendingReady[client] = steamId;
        }
        public static void Tick()
        {
            AdvertiseVersion();
            if (GameManager.state != GameManager.GameState.Playing) return;
            UpdatePeerProtocols();
            if (!LocalClient.serverOwner && LoadManager.serverHasSaveLoaded && !readySent &&
                LocalClient.instance && GameManager.players.ContainsKey(LocalClient.instance.myId) &&
                PlayerMovement.Instance && PlayerStatus.Instance && InventoryUI.Instance && PowerupInventory.Instance)
            {
                ClientMethods.SendPlayerReady(); readySent = true;
            }
            if (LocalClient.serverOwner && World.doSave && SaveSystem.BaseGameManager.Data != null)
            {
                foreach (var pair in new Dictionary<int, string>(pendingReady))
                {
                    if (!GameManager.players.ContainsKey(pair.Key)) continue;
                    if (LoadManager.Players.TryGetValue(pair.Value, out var player)) ServerMethods.SendPlayer(pair.Key, player);
                    ServerMethods.SendTime(pair.Key, SaveSystem.BaseGameManager.Data.WorldData.Time, SaveSystem.BaseGameManager.Data.WorldData.TotalTime);
                    restored.Add(pair.Key); pendingReady.Remove(pair.Key);
                }
            }
            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (!LocalClient.serverOwner) Say("请让房主按 F7 存档。");
                else if (DateTime.Now <= UIManager.canSaveAfter) Say("存档刚完成，请稍候再保存。");
                else { World.Save(); if (Saving) UIManager.canSaveAfter = DateTime.Now + Plugin.SaveCooldown; }
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (!LocalClient.serverOwner) Say("请让房主按 F8 暂停或继续。");
                else if (Paused || CompatiblePeers()) { ApplyPause(!Paused); BroadcastPause(); }
            }
            if (LocalClient.serverOwner && Paused && Time.realtimeSinceStartup >= nextBroadcast)
            { BroadcastPause(); nextBroadcast = Time.realtimeSinceStartup + 2f; }
        }
        private static void UpdatePeerProtocols()
        {
            if (!LocalClient.instance || !NetworkController.Instance || !SteamClient.IsValid ||
                NetworkController.Instance.networkType != NetworkController.NetworkType.Steam) return;
            try
            {
                for (int i = 0; i < 16 && SteamNetworking.IsP2PPacketAvailable(HelloChannel); i++)
                {
                    var packet = SteamNetworking.ReadP2PPacket(HelloChannel);
                    if (!packet.HasValue) break;
                    string version = PeerProtocolRegistry.ParseHello(packet.Value.Data);
                    if (version == null || !LocalClient.serverOwner) continue;
                    foreach (var pair in Server.clients)
                    {
                        if (pair.Key == LocalClient.instance.myId || pair.Value == null || pair.Value.player == null ||
                            pair.Value.player.steamId.Value != packet.Value.SteamId.Value) continue;
                        if (!peerProtocols.Verified(pair.Key, packet.Value.SteamId.Value, pair.Value.player, version))
                            Plugin.Log.LogInfo("Peer connection protocol confirmed: client " + pair.Key);
                        peerProtocols.Confirm(pair.Key, packet.Value.SteamId.Value, pair.Value.player, version);
                    }
                }
                if (Time.realtimeSinceStartup < nextHello) return;
                nextHello = Time.realtimeSinceStartup + 2f;
                if (!LocalClient.serverOwner && LocalClient.instance.serverHost.Value != 0)
                    SteamNetworking.SendP2PPacket(LocalClient.instance.serverHost, hello, hello.Length, HelloChannel, P2PSend.Reliable);
                if (LocalClient.serverOwner)
                {
                    foreach (var pair in Server.clients)
                    {
                        if (pair.Key == LocalClient.instance.myId || pair.Value == null || pair.Value.player == null) continue;
                        if (peerProtocols.Verified(pair.Key, pair.Value.player.steamId.Value, pair.Value.player, ProtocolVersion)) continue;
                        try
                        {
                            if (SteamManager.Instance && CurrentLobby.Id.Value != 0 && CurrentLobby.GetMemberData(new Friend(pair.Value.player.steamId), "muck-session-version") == ProtocolVersion)
                                peerProtocols.Confirm(pair.Key, pair.Value.player.steamId.Value, pair.Value.player, ProtocolVersion);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                if (lobbySync.ShouldNotify("peer-transport-error", Time.realtimeSinceStartup)) Plugin.Log.LogWarning("Peer confirmation will retry: " + ex.Message);
            }
        }
        public static void ApplyPause(bool value)
        {
            if (Paused == value) return;
            if (value) priorScale = Time.timeScale;
            Paused = value;
            if (value)
            {
                if (OtherInput.Instance) OtherInput.Instance.Pause();
                Time.timeScale = 0f;
                Say("联机已暂停，房主按 F8 继续。可以按 F7 保存。");
            }
            else
            {
                if (OtherInput.Instance) OtherInput.Instance.Unpause();
                Time.timeScale = priorScale > 0f ? priorScale : 1f;
                Say("游戏继续。");
            }
        }
        private static void BroadcastPause()
        {
            using (Packet packet = new Packet(120))
            {
                packet.Write(Paused);
                foreach (var pair in Server.clients)
                    if (pair.Value != null && pair.Value.player != null && pair.Key != LocalClient.instance.myId)
                        Net.ServerSendTCPData(pair.Key, packet);
            }
        }
        [HarmonyPatch(typeof(LocalClient), "InitializeClientData"), HarmonyPostfix]
        private static void RegisterPause()
        { LocalClient.packetHandlers.Add(120, p => ApplyPause(p.ReadBool())); }
        [HarmonyPatch(typeof(OtherInput), "Unpause"), HarmonyPrefix]
        private static bool KeepPaused() { return !Paused; }
        [HarmonyPatch(typeof(MenuUI), "Start"), HarmonyPrefix]
        private static void ResetAtMenu() { Reset(); }
        [HarmonyPatch(typeof(GameManager), "LeaveGame"), HarmonyPrefix]
        private static bool Leave()
        {
            if (Saving) { Say("存档尚未完成，请等待完成提示后退出。"); return false; }
            Reset(); return true;
        }
        public static void Reset()
        {
            EndSave(); readySent = false; pendingReady.Clear(); restored.Clear();
            lobbySync.Reset();
            peerProtocols.Clear(); nextHello = 0;
            TeammateNames.Reset();
            if (Paused) { Paused = false; Time.timeScale = priorScale > 0f ? priorScale : 1f; }
        }
    }
}
