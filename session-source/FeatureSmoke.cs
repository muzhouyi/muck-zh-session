using BepInEx;
using HarmonyLib;
using MuckSaveGame;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Xml.Linq;
[BepInPlugin("local.muck.convenience.smoke", "Convenience isolated smoke test", "1.0.0")]
[BepInDependency("MuckSaveGame.MichMcb")]
public class FeatureSmoke : BaseUnityPlugin
{
    private int phase;
    private float deadline;
    private string path;
    private string resultPath;
    private void Awake()
    {
        // Test fixtures must not update the user's Steam achievements or player profile.
        var harmony = new Harmony("local.muck.convenience.smoke");
        var skip = new HarmonyMethod(typeof(FeatureSmoke).GetMethod("Skip", BindingFlags.Static | BindingFlags.Public));
        harmony.Patch(AccessTools.Method(typeof(SaveManager), "Save"), skip);
        foreach (var m in typeof(AchievementManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            if (m.ReturnType == typeof(void) && m.Name != "Awake" && m.Name != "Start" && m.Name != "AchievementChanged") harmony.Patch(m, skip);
        path = Path.Combine(SaveSystem.GetSavesBasePath(), "__convenience_smoke.mucksave");
        resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "feature-result.txt"); File.WriteAllText(resultPath, "START\n");
        deadline = Time.realtimeSinceStartup + 120f;
        Logger.LogInfo("FEATURE SMOKE START");
    }
    public static bool Skip() { return false; }
    private void Check(bool condition, string label) { if (!condition) throw new Exception(label); File.AppendAllText(resultPath, "PASS: " + label + "\n"); Logger.LogInfo("FEATURE PASS: " + label); }
    private static void Nav(string method) { typeof(Navigation).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); }
    private void Update()
    {
        try
        {
            if (Time.realtimeSinceStartup > deadline) throw new Exception("Timeout at phase " + phase);
            var menu = UnityEngine.Object.FindObjectOfType<MenuUI>();
            if (phase == 0 && menu && SteamManager.Instance && LocalClient.instance) { menu.StartLobby(); phase = 1; }
            else if (phase == 1 && menu && SteamLobby.Instance && SteamManager.Instance.currentLobby.Id.Value != 0 && Server.clients.ContainsKey(0) && Server.clients[0].player != null && menu.lobbyUi.activeInHierarchy)
            {
                SteamManager.Instance.currentLobby.SetPrivate(); LobbySettings.Instance.seed.text = "-20301004";
                UIManager.useAutoSave = false; menu.StartGame(); phase = 2;
            }
            else if (phase == 2 && MuckSaveGame.World.doSave && Navigation.WorldReady && InventoryUI.Instance && PlayerStatus.Instance)
            {
                LoadManager.selectedSavePath = path;
                Check(Server.PacketHandlers.ContainsKey(122) && LocalClient.packetHandlers.ContainsKey(122) && Server.PacketHandlers.ContainsKey(123), "navigation and transaction packet handlers registered");
                Check(Navigation.SharedEnabled && Navigation.KeepOnDeath, "host convenience settings activated");
                Nav("MarkHome"); Nav("ScanResources"); Nav("OpenPanel");
                Check(Navigation.PanelOpen && Cursor.visible && OtherInput.lockCamera, "resource menu opens without moving the camera");
                phase = 3;
            }
            else if (phase == 3)
            {
                Nav("RefreshTargets"); Navigation.Render();
                var resources = (System.Collections.ICollection)typeof(Navigation).GetField("resources", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Check(resources.Count > 0, "living world resources discovered");
                var materials = (System.Collections.IDictionary)typeof(Navigation).GetField("materials", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Check(!materials.Values.Cast<InventoryItem>().Any(i => i.name.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0 || i.name.IndexOf("anvil", StringComparison.OrdinalIgnoreCase) >= 0), "building items are excluded from resource menu");
                var item = ItemManager.Instance.allItems.Values.First(i => i && i.craftable && i.type == InventoryItem.ItemType.Sword && i.requirements.Length > 0);
                typeof(Navigation).GetMethod("SelectRecipe", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { item });
                var selected = (System.Collections.ICollection)typeof(Navigation).GetField("selected", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Check(selected.Count > 0 && !Navigation.PanelOpen, "weapon recipe selects navigable ingredients and closes menu");
                Nav("RefreshTargets"); Navigation.Render();
                Check(Map.Instance.mapMarkers.Any(m => m != null && m.worldObject && m.worldObject.name == "MuckSession.MapPin"), "home and resource pins visible on game map");
                var nav = new XElement("Data"); NavigationSave.Instance.SaveXml(nav);
                Check(nav.Elements("Home").Any(), "home included in save extension");
                SaveSystem.Save(path);
                Check(XDocument.Load(path).Root.Elements("Data").Any(d => (string)d.Attribute("type") == "MuckSession.Navigation"), "navigation extension written in real save file");
                TestContainer(); TestFurnace();
                Nav("OpenPanel"); phase = 4;
            }
            else if (phase == 4)
            {
                if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "capture-menu.flag"))) CaptureMenu();
                TestDeath(); phase = 5;
            }
            else if (phase == 5)
            {
                File.AppendAllText(resultPath, "ALL PASSED\n"); Logger.LogInfo("FEATURE ALL PASSED"); Navigation.ClosePanel(); SessionControl.Reset(); Application.Quit(); phase = 6;
            }
        }
        catch (Exception ex) { File.AppendAllText(resultPath, "FAILED: " + ex + "\n"); Logger.LogError("FEATURE FAILED: " + ex); SessionControl.Reset(); Application.Quit(); phase = 6; }
    }
    private void CaptureMenu()
    {
        Navigation.Render();
        var camera = MoveCamera.Instance.mainCam;
        var canvas = GameObject.Find("MuckSession.Navigation").GetComponent<Canvas>();
        var target = new RenderTexture(1280, 720, 24);
        var previous = camera.targetTexture;
        canvas.GetComponent<UnityEngine.UI.CanvasScaler>().enabled = false; canvas.scaleFactor = 1f;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f;
        camera.targetTexture = target; Canvas.ForceUpdateCanvases(); camera.Render();
        var active = RenderTexture.active; RenderTexture.active = target;
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "feature-menu.png"), pixels.EncodeToPNG());
        RenderTexture.active = active; camera.targetTexture = previous; canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        UnityEngine.Object.Destroy(pixels); target.Release(); UnityEngine.Object.Destroy(target);
    }
    private void TestFurnace()
    {
        var go = new GameObject("Isolated test furnace"); go.transform.position = PlayerMovement.Instance.transform.position;
        var chest = go.AddComponent<FurnaceSync>(); chest.id = 19000002; chest.chestSize = 3; chest.cells = new InventoryItem[3]; chest.locked = new bool[3];
        ChestManager.Instance.chests.Add(chest.id, chest);
        Check(!ChestManager.Instance.IsChestOpen(chest.id), "furnace permits simultaneous open");
        var ore = ItemManager.Instance.allItems.Values.First(i => i && i.processable && i.processType == InventoryItem.ProcessType.Smelt && i.processedItem);
        var type = typeof(SharedContainers).GetNestedType("Request", BindingFlags.NonPublic);
        Func<int, ItemStack, object> request = (cell, mouse) => {
            var r = Activator.CreateInstance(type, true);
            foreach (var p in new object[][] {new object[]{"World",Navigation.WorldId}, new object[]{"Id",Guid.NewGuid().ToString("N")},new object[]{"Chest",chest.id},new object[]{"Cell",cell},new object[]{"Expected",new ItemStack(-1,0)},new object[]{"Mouse",mouse},new object[]{"Right",false}}) type.GetField((string)p[0]).SetValue(r,p[1]);
            return r;
        };
        var execute = typeof(SharedContainers).GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Static);
        var accepted = execute.Invoke(null, new object[] {LocalClient.instance.myId, request(1, new ItemStack(ore.id, 2))});
        Check((bool)accepted.GetType().GetField("Accepted").GetValue(accepted) && chest.cells[1].amount == 2, "furnace accepts raw smelting material");
        var rejected = execute.Invoke(null, new object[] {LocalClient.instance.myId, request(2, new ItemStack(ore.id, 1))});
        Check(!(bool)rejected.GetType().GetField("Accepted").GetValue(rejected) && chest.cells[2] == null, "furnace output forbids inserting items");
        ChestManager.Instance.UpdateChest(chest.id, 1, ore.id, 1); ChestManager.Instance.UpdateChest(chest.id, 2, ore.processedItem.id, 1);
        var take = execute.Invoke(null, new object[] {LocalClient.instance.myId, request(2, new ItemStack(-1, 0))});
        Check(!(bool)take.GetType().GetField("Accepted").GetValue(take), "stale furnace output click cannot overwrite new output");
        ChestManager.Instance.chests.Remove(chest.id); UnityEngine.Object.Destroy(go);
    }
    private void TestContainer()
    {
        var go = new GameObject("Isolated test chest"); go.transform.position = PlayerMovement.Instance.transform.position;
        var chest = go.AddComponent<Chest>(); chest.id = 19000001; chest.chestSize = 2; chest.cells = new InventoryItem[2]; chest.locked = new bool[2];
        var rock = ItemManager.Instance.GetItemByName("Rock");
        var stack = ScriptableObject.CreateInstance<InventoryItem>(); stack.Copy(rock, 7); chest.cells[0] = stack;
        ChestManager.Instance.chests.Add(chest.id, chest);
        chest.Use(true); Check(!ChestManager.Instance.IsChestOpen(chest.id), "shared chest bypasses exclusive-open lock");
        var requestType = typeof(SharedContainers).GetNestedType("Request", BindingFlags.NonPublic);
        object request = Activator.CreateInstance(requestType, true);
        Action<string, object> set = (name, value) => requestType.GetField(name).SetValue(request, value);
        set("World", Navigation.WorldId); set("Id", Guid.NewGuid().ToString("N")); set("Chest", chest.id); set("Cell", 0);
        set("Expected", new ItemStack(rock.id, 7)); set("Mouse", new ItemStack(-1, 0)); set("Right", false);
        var execute = typeof(SharedContainers).GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Static);
        var receipt = execute.Invoke(null, new object[] { LocalClient.instance.myId, request });
        Check((bool)receipt.GetType().GetField("Accepted").GetValue(receipt) && chest.cells[0] == null, "host atomically accepts one take operation");
        var replay = execute.Invoke(null, new object[] { LocalClient.instance.myId, request });
        Check(ReferenceEquals(receipt, replay) && chest.cells[0] == null, "network retry reuses receipt without changing the chest again");
        set("Id", Guid.NewGuid().ToString("N")); var loser = execute.Invoke(null, new object[] { LocalClient.instance.myId, request });
        Check(!(bool)loser.GetType().GetField("Accepted").GetValue(loser), "another consumer of the stale slot is rejected");
        ChestManager.Instance.chests.Remove(chest.id); UnityEngine.Object.Destroy(go);
    }
    private void TestDeath()
    {
        var item = ItemManager.Instance.GetItemByName("Rock"); InventoryUI.Instance.cells[0].ForceAddItem(item, 7);
        var mouse = ScriptableObject.CreateInstance<InventoryItem>(); mouse.Copy(item, 3); InventoryUI.Instance.PlaceItem(mouse);
        var armorItem = ItemManager.Instance.allItems.Values.FirstOrDefault(i => i && i.tag == InventoryItem.ItemTag.Helmet);
        if (armorItem) { InventoryUI.Instance.armorCells[0].ForceAddItem(armorItem, 1); PlayerStatus.Instance.UpdateArmor(0, armorItem.id); }
        int drops = ItemManager.Instance.list.Count;
        AccessTools.Method(typeof(PlayerStatus), "PlayerDied").Invoke(PlayerStatus.Instance, new object[] { 0, 0 });
        Check(PlayerStatus.Instance.IsPlayerDead() && PlayerStatus.Instance.hp == 0f, "normal death still occurs");
        Check(InventoryUI.Instance.cells[0].currentItem && InventoryUI.Instance.cells[0].currentItem.amount == 7, "inventory stack retained after actual PlayerDied");
        Check(InventoryUI.Instance.currentMouseItem && InventoryUI.Instance.currentMouseItem.amount == 3, "cursor-held stack retained after actual PlayerDied");
        Check(ItemManager.Instance.list.Count == drops, "death did not create world item drops");
        if (armorItem) Check(PlayerStatus.Instance.armor[0] && PlayerStatus.Instance.armor[0].id == armorItem.id, "equipped armor retained");
        PlayerMovement.Instance.gameObject.SetActive(true); PlayerStatus.Instance.Respawn();
        Check(!PlayerStatus.Instance.IsPlayerDead() && InventoryUI.Instance.cells[0].currentItem.amount == 7, "revive preserves retained items");
    }
}

