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
[BepInPlugin("local.muck.convenience15.smoke", "Convenience isolated smoke test", "1.0.0")]
[BepInDependency("MuckSaveGame.MichMcb")]
public class NavigationSmoke15 : BaseUnityPlugin
{
    private int phase;
    private float deadline;
    private string path;
    private string resultPath;
    private void Awake()
    {
        // Test fixtures must not update the user's Steam achievements or player profile.
        var harmony = new Harmony("local.muck.convenience15.smoke");
        var skip = new HarmonyMethod(typeof(NavigationSmoke15).GetMethod("Skip", BindingFlags.Static | BindingFlags.Public));
        harmony.Patch(AccessTools.Method(typeof(SaveManager), "Save"), skip);
        foreach (var m in typeof(AchievementManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            if (m.ReturnType == typeof(void) && m.Name != "Awake" && m.Name != "Start" && m.Name != "AchievementChanged") harmony.Patch(m, skip);
        path = Path.Combine(SaveSystem.GetSavesBasePath(), "__convenience15_smoke.mucksave");
        resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "feature15-result.txt"); File.WriteAllText(resultPath, "START\n");
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
                Nav("MarkHome"); Nav("MarkHome"); Nav("ScanResources"); Nav("OpenPanel");
                Check(Navigation.PanelOpen && Cursor.visible && OtherInput.lockCamera, "resource menu opens without moving the camera");
                phase = 3;
            }
            else if (phase == 3)
            {
                Nav("RefreshTargets"); Navigation.Render();
                var resources = (System.Collections.ICollection)typeof(Navigation).GetField("resources", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Check(resources.Count > 0, "living world resources discovered");
                var materials = (System.Collections.IDictionary)typeof(Navigation).GetField("materials", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Check(!((System.Collections.Generic.List<UnityEngine.UI.Button>)typeof(Navigation).GetField("menuButtons",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).Any(b=>b.GetComponentInChildren<TMPro.TextMeshProUGUI>().GetParsedText().Contains("工作台")||b.GetComponentInChildren<TMPro.TextMeshProUGUI>().GetParsedText().Contains("铁砧")), "building items are excluded from resource menu");
                TestNavigation(); TestNewNavigation(); TestTooltip();
                var item = ItemManager.Instance.allItems.Values.First(i => i && i.craftable && i.type == InventoryItem.ItemType.Sword && i.requirements.Length > 0);
                typeof(Navigation).GetMethod("SelectRecipe", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { item });
                var selected = (System.Collections.ICollection)typeof(Navigation).GetField("selected", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Check(selected.Count > 0 && !Navigation.PanelOpen, "weapon recipe selects navigable ingredients and closes menu");
                Nav("RefreshTargets"); Navigation.Render();
                Check(Map.Instance.mapMarkers.Any(m => m != null && m.worldObject && m.worldObject.name == "MuckSession.MapPin"), "home and resource pins visible on game map");
                var nav = new XElement("Data"); NavigationSave.Instance.SaveXml(nav);
                Check(nav.Elements("Home").Count() == 2, "two homes included in save extension");
                var restored = new NavigationSave(); restored.LoadXml(nav);
                Check(restored.GetHomes(SteamManager.Instance.PlayerSteamId.Value.ToString()).Count == 2, "two homes survive save extension roundtrip");
                SaveSystem.Save(path);
                Check(XDocument.Load(path).Root.Elements("Data").Any(d => (string)d.Attribute("type") == "MuckSession.Navigation"), "navigation extension written in real save file");
                Check(SaveListInfo.Describe(path).Contains("修改 ") && SaveListInfo.Describe(path).Contains("存活 "), "save list includes modification time and survival days");
                foreach (var pin in Map.Instance.mapMarkers.Where(m => m != null && m.worldObject && m.worldObject.name == "MuckSession.MapPin"))
                { Check(((RectTransform)pin.marker).sizeDelta.x == 8 && pin.marker.GetComponentInChildren<TMPro.TextMeshProUGUI>().fontSize == 12, "map pins use compact 8px symbol and 12px text"); }
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
                Navigation.ClosePanel(); UnityEngine.Object.FindObjectOfType<GameManager>().LeaveGame(); phase = 6;
            }
            else if (phase == 6 && menu) { menu.StartLobby(); phase = 7; }
            else if (phase == 7 && menu && menu.lobbyUi.activeInHierarchy)
            {
                var button = Resources.FindObjectsOfTypeAll<SaveButton>().FirstOrDefault(b=>b && b.gameObject.scene.IsValid() && b.FileName==Path.GetFileName(path));
                if (!button) return;
                var selection=(GameObject)typeof(UIManager).GetField("selectionGUI",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                selection.SetActive(true); Canvas.ForceUpdateCanvases();
                Check(!selection.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).Any(t=>t.text=="None"), "populated save picker contains no None row");
                Check(button.GetComponentInChildren<TMPro.TextMeshProUGUI>().text.Contains("存活 "), "real lobby save rows show survival days");
                Check(((RectTransform)button.transform).rect.height >= 75, "real lobby save rows provide enough height for two lines");
                button.OnPointerClick(new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left});
                Check(LoadManager.selectedSavePath==path,"formatted save row selects the actual file name");
                File.AppendAllText(resultPath, "ALL PASSED\n"); Logger.LogInfo("FEATURE ALL PASSED"); SessionControl.Reset(); Application.Quit(); phase = 8;
            }
        }
        catch (Exception ex) { File.AppendAllText(resultPath, "FAILED: " + ex + "\n"); Logger.LogError("FEATURE FAILED: " + ex); SessionControl.Reset(); Application.Quit(); phase = 6; }
    }

    private static object CallNav(string name,params object[] args) { return typeof(Navigation).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args); }
    private static object Field(object o,string name) { return o.GetType().GetField(name).GetValue(o); }
    private static object NavField(string name) { return typeof(Navigation).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null); }
    private void TestNewNavigation()
    {
        var selected=(System.Collections.Generic.List<string>)NavField("selected");
        var targets=(System.Collections.IList)NavField("targets"); var pins=(System.Collections.IList)NavField("pins");
        Func<string,InventoryItem> item=name=>ItemManager.Instance.GetItemByName(name);
        Func<string,string> key=name=>"item:"+item(name).id;
        CallNav("SelectRecipe",item("Steel Sword"));
        Check(selected.Count==2&&selected.Contains(key("Iron Ore"))&&selected.Contains(key("Birch Wood")),"steel sword selects both iron ore and birch wood");
        CallNav("RefreshTargets");Navigation.Render();
        Check(pins.Cast<object>().Count(p=>Field(p,"Position")!=null)==4,"two recipe resources and two homes have simultaneous map positions");
        Check(Map.Instance.mapMarkers.Count(m=>m.worldObject&&m.worldObject.name=="MuckSession.MapPin")==4,"all selected recipe ingredients receive separate real map markers");
        CallNav("SelectRecipe",item("Steel Axe"));
        Check(selected.Count==3&&selected.Contains(key("Wood"))&&selected.Contains(key("Iron Ore"))&&selected.Contains(key("Birch Wood")),"bark recipe resolves to wood while keeping ore and birch targets");
        CallNav("SelectRecipe",item("Wood Bow"));
        Check(selected.Count==2&&selected.Contains(key("Wood"))&&selected.Contains(key("Wheat")),"rope recipe resolves to wheat and wood without duplicate markers");
        CallNav("SelectTarget",key("Coal"));CallNav("RefreshTargets");Navigation.Render();
        Check(selected.Count==1&&selected[0]==key("Coal"),"single resource selection clears all prior recipe targets");
        Check(pins.Cast<object>().Count(p=>Field(p,"Position")!=null)==3,"single resource retains only its marker and existing homes");
        Check(pins.Cast<object>().Any(p=>Field(p,"Title").ToString()=="煤炭石矿"||Field(p,"Title").ToString()=="煤炭小块"),"coal uses the exact compact source name");
        Nav("ScanResources");
        Check(!targets.Cast<object>().Any(t=>Field(t,"Key").ToString()==key("Flint")),"flint is excluded from navigation targets");
        Check(!targets.Cast<object>().Any(t=>(Field(t,"Key").ToString()==key("Red Apple")||Field(t,"Key").ToString()==key("Wheat"))&&Field(t,"Resource")!=null),"apple and wheat never point to harvest or probability-drop nodes");
        Check(targets.Cast<object>().Any(t=>Field(t,"Key").ToString()==key("Red Apple")&&Field(t,"Pickup")!=null),"existing world apples are indexed as pickups");
        Check(targets.Cast<object>().Any(t=>Field(t,"Key").ToString()=="food:mushroom"),"all mushroom colors share one searchable target group");
        var origin=PlayerMovement.Instance.transform.position;
        var go=new GameObject("Food navigation fixture");go.SetActive(false);go.transform.position=origin;
        var pickup=go.AddComponent<PickupInteract>();pickup.item=item("Wheat");pickup.amount=1;
        Nav("ScanResources");Check(ReferenceEquals(Field(CallNav("Nearest",key("Wheat")),"Pickup"),pickup),"nearby existing wheat is found without a probability source");
        pickup.item=item("Sugon Shroom");Nav("ScanResources");CallNav("SelectTarget","food:mushroom");
        Check(ReferenceEquals(Field(CallNav("Nearest","food:mushroom"),"Pickup"),pickup)&&selected.Count==1,"single mushroom option finds the nearest available color");
        pickup.item=item("Coal");Nav("ScanResources");CallNav("SelectTarget",key("Coal"));CallNav("RefreshTargets");
        Check(pins.Cast<object>().Any(p=>Field(p,"Title").ToString()=="煤炭小块"),"coal pickup marker uses exactly coal small piece");
        pickup.amount=0;
        var coalStone=Resources.FindObjectsOfTypeAll<HitableRock>().First(r=>r.gameObject.scene.IsValid()&&r.entityName=="Coal");
        PlayerMovement.Instance.transform.position=coalStone.transform.position;CallNav("RefreshTargets");
        Check(pins.Cast<object>().Any(p=>Field(p,"Title").ToString()=="煤炭石矿"),"depleted pickup switches to compact coal stone marker");
        PlayerMovement.Instance.transform.position=origin;
        UnityEngine.Object.Destroy(go); selected.Clear();CallNav("OpenPanel");
        typeof(Navigation).GetField("tab",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,0);CallNav("BuildPanel");Canvas.ForceUpdateCanvases();
        var labels=((System.Collections.Generic.List<UnityEngine.UI.Button>)NavField("menuButtons")).Select(b=>b.GetComponentInChildren<TMPro.TextMeshProUGUI>().GetParsedText()).ToArray();
        Check(labels.Count(t=>t=="蘑菇")==1&&!labels.Any(t=>t.Contains("燧石")),"resource menu offers one mushroom option and no flint");
        Navigation.ClosePanel();
    }
    private void TestTooltip()
    {
        var cell=InventoryUI.Instance.cells[0]; var old=cell.currentItem;
        var text=(TMPro.TextMeshProUGUI)AccessTools.Field(typeof(ItemInfo),"text").GetValue(ItemInfo.Instance);
        var image=(UnityEngine.UI.RawImage)AccessTools.Field(typeof(ItemInfo),"image").GetValue(ItemInfo.Instance);
        InventoryUI.Instance.transform.parent.gameObject.SetActive(true);ItemInfo.Instance.gameObject.SetActive(true);
        var ev=new PointerEventData(EventSystem.current);
        var apple=ItemManager.Instance.GetItemByName("Red Apple");cell.currentItem=apple;cell.OnPointerEnter(ev);text.ForceMeshUpdate(true);
        string s=text.GetParsedText();
        Check(s.Contains("血量 +5\n饱食度 +15\n体力 +5"),"real food hover shows all three actual apple stats on separate lines");
        Check(s.Contains("红苹果"),"food tooltip keeps the translated item name");
        Check(image.rectTransform.sizeDelta.y>=text.mesh.bounds.size.y,"tooltip background expands to cover all added stat lines");
        cell.OnPointerEnter(ev);text.ForceMeshUpdate(true);
        Check(text.GetParsedText().Split(new[]{"血量"},StringSplitOptions.None).Length==2,"repeated hover does not duplicate food stat rows");
        foreach(var food in ItemManager.Instance.allItems.Values.Where(i=>i.type==InventoryItem.ItemType.Food))
        {
            cell.currentItem=food;cell.OnPointerEnter(ev);text.ForceMeshUpdate(true);
            Check(text.GetParsedText().Contains(ItemStatsTooltip.Stats(food)),"all food hover stats match native fields: "+food.name);
        }
        var sword=ItemManager.Instance.GetItemByName("Steel Sword");cell.currentItem=sword;cell.OnPointerEnter(ev);text.ForceMeshUpdate(true);
        Check(text.GetParsedText().Contains("基础伤害 25")&&!text.GetParsedText().Contains("饱食度"),"real sword hover shows base damage and clears previous food numbers");
        cell.OnPointerExit(ev);text.ForceMeshUpdate(true);
        Check(text.GetParsedText()=="","leaving item clears the tooltip");
        cell.currentItem=old;
    }
    private void TestNavigation()
    {
        Canvas.ForceUpdateCanvases();
        var buttons = ((System.Collections.Generic.List<UnityEngine.UI.Button>)typeof(Navigation).GetField("menuButtons", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).ToArray();
        var recipeTab = buttons[1];
        Vector2 center = RectTransformUtility.WorldToScreenPoint(null, recipeTab.transform.position);
        var hit = typeof(Navigation).GetMethod("HitButton", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[]{center});
        Check(ReferenceEquals(hit, recipeTab), "mouse rectangle hit reaches crafting tab");
        var ev = new PointerEventData(EventSystem.current) {button=PointerEventData.InputButton.Left,position=center};
        ExecuteEvents.Execute(recipeTab.gameObject,ev,ExecuteEvents.pointerClickHandler);
        Check((int)typeof(Navigation).GetField("tab",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)==1, "pointer click switches crafting page");
        var cfg = BepInEx.Bootstrap.Chainloader.PluginInfos["MuckSaveGame.MichMcb"].Instance.Config;
        Check(cfg["Navigation","MenuKey"].BoxedValue.ToString()=="C", "old N key migrates to C");
        var resourceTypes=Resources.FindObjectsOfTypeAll<HitableResource>().Where(r=>r && r.gameObject.scene.IsValid()).ToArray();
        Check(resourceTypes.Any(r=>!r.gameObject.activeInHierarchy), "test world contains distance-culled resource objects");
        var wood = ItemManager.Instance.GetItemByName("Wood"); var coal = ItemManager.Instance.GetItemByName("Coal");
        var fixture=new GameObject("Dormant nearest resource fixture"); fixture.SetActive(false); fixture.transform.position=PlayerMovement.Instance.transform.position;
        var rock=fixture.AddComponent<HitableRock>(); rock.maxHp=50;rock.dropItem=wood;rock.dropExtra=new[]{coal};rock.dropChance=new[]{1f};
        rock.dropTable=ScriptableObject.CreateInstance<LootDrop>();
        rock.dropTable.loot=new[]{new LootDrop.LootItems{item=wood,dropChance=1,amountMin=1,amountMax=1},new LootDrop.LootItems{item=coal,dropChance=0.5f,amountMin=1,amountMax=1}};
        Nav("ScanResources"); var nearest=typeof(Navigation).GetMethod("Nearest",BindingFlags.NonPublic|BindingFlags.Static);
        var found=nearest.Invoke(null,new object[]{"item:"+wood.id});
        Check(found!=null && ReferenceEquals(found.GetType().GetField("Resource").GetValue(found),rock), "unawakened culled nearest resource remains searchable");
        var resourceTargets=(System.Collections.IList)typeof(Navigation).GetField("targets",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        Check(resourceTargets.Cast<object>().Any(t=>ReferenceEquals(t.GetType().GetField("Resource").GetValue(t),rock)&&(string)t.GetType().GetField("Key").GetValue(t)=="item:"+coal.id&&(bool)t.GetType().GetField("Extra").GetValue(t)), "coal secondary drop sources are indexed");
        typeof(Navigation).GetMethod("TrackAwake",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{rock});
        rock.hp=0; Nav("ScanResources");found=nearest.Invoke(null,new object[]{"item:"+wood.id});
        Check(found==null || !ReferenceEquals(found.GetType().GetField("Resource").GetValue(found),rock), "depleted inactive resource is excluded");
        UnityEngine.Object.Destroy(fixture);
        var landmarks=(System.Collections.IDictionary)typeof(Navigation).GetField("landmarks",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        Check(landmarks.Contains("chest:free")&&landmarks.Contains("chest:white")&&landmarks.Contains("chest:blue")&&landmarks.Contains("chest:gold")&&landmarks.Contains("npc:trader"),"all chest tiers and trader are offered");
        Check(nearest.Invoke(null,new object[]{"npc:camp"}) != null, "unspawned trader camps can be located before approaching them");
        var targets=(System.Collections.IList)typeof(Navigation).GetField("targets",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        Logger.LogInfo("NAV TARGET COUNTS: "+string.Join(", ",targets.Cast<object>().GroupBy(t=>(string)t.GetType().GetField("Key").GetValue(t)).Select(g=>g.Key+"="+g.Count()).ToArray()));
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

