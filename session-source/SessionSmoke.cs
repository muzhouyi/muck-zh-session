using BepInEx;
using System;
using System.IO;
using System.Collections;
using UnityEngine;
using MuckSaveGame;
using WorldSave = MuckSaveGame.World;
using System.Xml.Linq;
using System.Reflection;
[BepInPlugin("local.muck.session.smoke", "Session Smoke Test", "1.0.0")]
[BepInDependency("MuckSaveGame.MichMcb")]
public class SessionSmoke : BaseUnityPlugin
{
    private string path;
    private int phase;
    private float deadline;
    private float mark;
    private float day;
    private int rock;
    private Steamworks.Data.Lobby Lobby { get { return (Steamworks.Data.Lobby)typeof(SteamLobby).GetField("currentLobby", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(SteamLobby.Instance); } }
    private void Awake()
    {
        path = Path.Combine(SaveSystem.GetSavesBasePath(), "__session_smoke.mucksave");
        deadline = Time.realtimeSinceStartup + 90f;
        Logger.LogInfo("SMOKE START");
    }
    private void Check(bool ok, string label)
    { if (!ok) throw new Exception(label); Logger.LogInfo("SMOKE PASS: " + label); }
    private void ExerciseReceivers()
    {
        var peers = new System.Collections.Generic.Dictionary<int, string> { {1,"smoke-peer-one"}, {2,"smoke-peer-two"} };
        SessionControl.Gate.Begin(peers); SessionControl.Saving = true;
        foreach (var peer in peers)
        {
            for (int part = 0; part < 5; part++)
            {
                using (Packet outgoing = new Packet())
                {
                    outgoing.Write(peer.Value); outgoing.Write(SessionControl.Gate.Generation);
                    if (part == 0) { outgoing.Write((short)rock); outgoing.Write((short)(11 + peer.Key)); outgoing.Write((short)-1); outgoing.Write((short)0); }
                    if (part == 1) { outgoing.Write((short)0); outgoing.Write((short)2); outgoing.Write((short)1); }
                    if (part == 2) { outgoing.Write(1f); outgoing.Write(2f); outgoing.Write(3f); }
                    if (part == 3)
                    {
                        outgoing.Write(62f); outgoing.Write(100); outgoing.Write(80f); outgoing.Write(100f);
                        outgoing.Write(0f); outgoing.Write(0); outgoing.Write(70f); outgoing.Write(100f); outgoing.Write(0);
                    }
                    if (part == 4) { for (int i=0;i<5;i++) outgoing.Write((short)-1); outgoing.Write(0); }
                    using (Packet incoming = new Packet(outgoing.ToArray())) Server.PacketHandlers[100 + part](peer.Key, incoming);
                }
            }
            if (peer.Key == 1) Check(!SessionControl.Gate.Complete, "host waits for second peer snapshot");
        }
        Check(SessionControl.Gate.Complete, "five network data receivers complete two peer snapshots");
        Check(LoadManager.Players["smoke-peer-two"].Inventory[0].Amount == 13 && LoadManager.Players["smoke-peer-two"].Powerups[1] == 2, "peer inventory and powerups captured by packet handlers");
        SessionControl.EndSave();
    }
    private void Update()
    {
        try
        {
            if (Time.realtimeSinceStartup > deadline) throw new Exception("Timeout in phase " + phase);
            MenuUI menu = UnityEngine.Object.FindObjectOfType<MenuUI>();
            if (phase == 0 && menu && SteamManager.Instance && LocalClient.instance)
            { menu.StartLobby(); phase = 1; }
            else if ((phase == 1 || phase == 6) && menu && SteamLobby.Instance && Lobby.Id.Value != 0 && Server.clients.ContainsKey(0) && Server.clients[0].player != null && menu.lobbyUi.activeInHierarchy)
            {
                if (SteamManager.Instance.currentLobby.GetMemberData(new Steamworks.Friend(Steamworks.SteamClient.SteamId), "muck-session-version") != SessionControl.ProtocolVersion) return;
                Check(true, "current SteamManager room marker published by Update");
                Lobby.SetPrivate();
                LobbySettings.Instance.seed.text = "-20300123";
                UIManager.useAutoSave = false;
                if (phase == 6) LoadManager.selectedSavePath = path;
                menu.StartGame(); phase = phase == 1 ? 2 : 7;
            }
            else if (phase == 2 && WorldSave.doSave && InventoryUI.Instance && PlayerStatus.Instance)
            {
                LoadManager.selectedSavePath = path;
                rock = ItemManager.Instance.GetItemByName("Rock").id;
                ExerciseReceivers();
                InventoryUI.Instance.cells[0].ForceAddItem(ItemManager.Instance.allItems[rock], 7);
                PlayerStatus.Instance.hp = 73f;
                SessionControl.ApplyPause(true); day = DayCycle.time; mark = Time.realtimeSinceStartup;
                phase = 30;
            }
            else if (phase == 30)
            { day = DayCycle.time; mark = Time.realtimeSinceStartup; phase = 3; }
            else if (phase == 3 && Time.realtimeSinceStartup - mark >= 2f)
            {
                Logger.LogInfo("SMOKE PAUSE: scale=" + Time.timeScale + " before=" + day + " after=" + DayCycle.time + " state=" + GameManager.state);
                Check(Time.timeScale == 0f && Math.Abs(DayCycle.time - day) < 0.00001f, "pause freezes world time");
                OtherInput.Instance.Unpause(); Check(Time.timeScale == 0f, "ordinary resume cannot bypass synchronized pause");
                WorldSave.Save(); phase = 4;
            }
            else if (phase == 4 && !SessionControl.Saving)
            {
                Check(File.Exists(path), "save created while paused");
                Check(XDocument.Load(path).Root != null, "save is valid XML");
                var saved = SaveSystem.Load(path);
                Check(saved.PlayerData.ClientPlayers["smoke-peer-two"].Inventory[0].Amount == 13 && saved.PlayerData.ClientPlayers["smoke-peer-two"].Powerups[1] == 2, "peer inventory and powerups survive XML roundtrip");
                SessionControl.ApplyPause(false); Check(Time.timeScale > 0f, "resume restores time scale");
                UnityEngine.Object.FindObjectOfType<GameManager>().LeaveGame(); phase = 5;
            }
            else if (phase == 5 && menu)
            { menu.StartLobby(); phase = 6; }
            else if (phase == 7 && WorldSave.doSave && InventoryUI.Instance && PlayerStatus.Instance)
            {
                Check(InventoryUI.Instance.cells[0].currentItem && InventoryUI.Instance.cells[0].currentItem.id == rock && InventoryUI.Instance.cells[0].currentItem.amount == 7, "inventory survives real world reload");
                Check(Math.Abs(PlayerStatus.Instance.hp - 73f) < 2f, "health survives real world reload");
                WorldSave.Save(); phase = 8;
            }
            else if (phase == 8 && !SessionControl.Saving)
            {
                Check(File.Exists(path + ".bak"), "previous save backup created");
                Logger.LogInfo("SMOKE ALL PASSED");
                UnityEngine.Object.FindObjectOfType<GameManager>().LeaveGame(); Application.Quit(); phase = 9;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("SMOKE FAILED: " + ex);
            SessionControl.Reset(); Application.Quit(); phase = 9;
        }
    }
}
