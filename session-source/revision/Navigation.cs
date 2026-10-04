namespace MuckSaveGame
{
    using BepInEx.Configuration;
    using HarmonyLib;
    using Steamworks;
    using InventoryItem = global::InventoryItem;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using TMPro;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;
    [HarmonyPatch]
    public static class Navigation
    {
        public static bool WorldReady, SharedEnabled, KeepOnDeath;
        public static string WorldId = "";
        private static ConfigEntry<bool> sharedOption, keepOption;
        private static ConfigEntry<KeyCode> homeKey, menuKey;
        private static readonly SortedDictionary<int, Vector3> homes = new SortedDictionary<int, Vector3>();
        private static readonly HashSet<int> awakened = new HashSet<int>();
        private sealed class Target { public string Key; public Transform Transform; public HitableResource Resource; public LootContainerInteract Chest; public TraderInteract Trader; public GenerateCamp Camp; public PickupInteract Pickup; public bool Extra; }
        private static readonly List<Target> targets = new List<Target>();
        private static float nextTargets;
        private static readonly Dictionary<string, string> landmarks = new Dictionary<string, string> { {"chest:free","免费宝箱"}, {"chest:white","普通宝箱"}, {"chest:blue","稀有宝箱"}, {"chest:gold","传奇宝箱"}, {"npc:trader","交易 NPC"}, {"npc:camp","交易营地"} };
        private static readonly List<UnityEngine.UI.Button> menuButtons = new List<UnityEngine.UI.Button>();
        private static UnityEngine.UI.Button pressed;
        private static int actionFrame = -1;
        private static int tab;
        private static readonly FieldInfo chestOpened = AccessTools.Field(typeof(LootContainerInteract), "opened");
        private static readonly FieldInfo chestPrice = AccessTools.Field(typeof(LootContainerInteract), "basePrice");
        private static float nextHello, nextScan;
        private static readonly Dictionary<int, InventoryItem> materials = new Dictionary<int, InventoryItem>();
        private static readonly List<HitableResource> resources = new List<HitableResource>();
        private static readonly List<string> selected = new List<string>();
        private sealed class Pin { public GameObject Anchor; public TextMeshProUGUI Text; public Map.MapMarker MapPin; public Map Map; public Vector3? Position; public string Title; }
        private static readonly List<Pin> pins = new List<Pin>();
        private static Canvas canvas;
        private static GameObject panel;
        private static TextMeshProUGUI hint;
        private static MethodInfo applyFont, translate;
        private static readonly Dictionary<int, string> textValues = new Dictionary<int, string>();
        private static CursorLockMode previousLock;
        private static bool previousCursor, previousCameraLock;
        private static int page;
        public static bool PanelOpen { get { return panel && panel.activeSelf; } }
        public static void Initialize(ConfigFile config)
        {
            sharedOption = config.Bind("Convenience", "SharedContainers", true, "Host setting: allow simultaneous chest/furnace access using confirmed slot transactions. All players need 0.9.6.");
            keepOption = config.Bind("Convenience", "KeepInventoryOnDeath", true, "Host setting: retain inventory, armor and cursor-held items when dying. Does not change death or revive rules.");
            homeKey = config.Bind("Navigation", "SetHomeKey", KeyCode.H, "Mark current position as home. Save with F7 to retain it.");
            menuKey = config.Bind("Navigation", "MenuKey", KeyCode.C, "Open homes, resources, landmarks and crafting navigation.");
            if (menuKey.Value == KeyCode.N) menuKey.Value = KeyCode.C;
            SaveSystem.Register(NavigationSave.Instance);
            var font = Type.GetType("UU9.Muck.Translater.FontManager, UU9.Muck.Translater");
            if (font != null) applyFont = font.GetMethod("ApplyTranslatedFont", new[] { typeof(TMP_Text), typeof(string) });
            var translator = Type.GetType("UU9.Muck.Translater.TranslationManager, UU9.Muck.Translater");
            if (translator != null) translate = translator.GetMethod("TryGetTranslation", BindingFlags.Public | BindingFlags.Static);
        }
        private static string MyId { get { return SteamManager.Instance ? SteamManager.Instance.PlayerSteamId.Value.ToString() : "local"; } }
        private static bool Typing()
        {
            if (ChatBox.Instance && ChatBox.Instance.typing) return true;
            if (!EventSystem.current || !EventSystem.current.currentSelectedGameObject) return false;
            var go = EventSystem.current.currentSelectedGameObject;
            var tmp = go.GetComponent<TMP_InputField>(); var text = go.GetComponent<InputField>();
            return (tmp && tmp.isFocused) || (text && text.isFocused);
        }
        public static void Tick()
        {
            if (GameManager.state != GameManager.GameState.Playing || !PlayerMovement.Instance || !LocalClient.instance) { ClosePanel(); return; }
            if (LocalClient.serverOwner)
            {
                NavigationSave.Instance.EnsureWorld(); WorldId = NavigationSave.Instance.RoomId; WorldReady = true;
                SharedEnabled = sharedOption.Value && SessionControl.CompatiblePeers(); KeepOnDeath = keepOption.Value;
                homes.Clear(); foreach (var pair in NavigationSave.Instance.GetHomes(MyId)) homes[pair.Key] = pair.Value;
            }
            else if (Time.realtimeSinceStartup >= nextHello)
            {
                nextHello = Time.realtimeSinceStartup + 2f;
                using (var p = new Packet(123)) { p.Write(0); Net.ClientSendTCPData(p); }
            }
            EnsureCanvas();
            if (PlayerStatus.Instance && PlayerStatus.Instance.IsPlayerDead()) { ClosePanel(); return; }
            if (!Typing())
            {
                if (Input.GetKeyDown(menuKey.Value))
                {
                    if (PanelOpen) ClosePanel();
                    else if (!OtherInput.Instance || !OtherInput.Instance.IsAnyMenuOpen()) OpenPanel();
                }
                if (PanelOpen && Input.GetKeyDown(KeyCode.Escape)) ClosePanel();
                if (!PanelOpen && Input.GetKeyDown(homeKey.Value) && (!OtherInput.Instance || !OtherInput.Instance.IsAnyMenuOpen())) MarkHome();
            }
            if (PanelOpen) UpdateMenuPointer();
            if (Time.realtimeSinceStartup >= nextScan) { nextScan = Time.realtimeSinceStartup + 2f; ScanResources(); }
            if (Time.realtimeSinceStartup >= nextTargets) { nextTargets = Time.realtimeSinceStartup + 0.25f; RefreshTargets(); }
        }
        private static void MarkHome()
        {
            int slot = 1; while (slot <= NavigationSave.MaxHomes && homes.ContainsKey(slot)) slot++;
            if (slot > NavigationSave.MaxHomes) { SessionControl.Say("已有 8 个家，请按 C 在‘家’页面更新或删除一个路标。"); return; }
            SetHomeSlot(slot);
        }
        private static void SetHomeSlot(int slot)
        {
            if (!WorldReady) { SessionControl.Say("正在等待房主的导航确认，请稍后再标记家。"); return; }
            Vector3 pos = PlayerMovement.Instance.transform.position;
            if (LocalClient.serverOwner) NavigationSave.Instance.SetHome(MyId, slot, pos);
            else using (var p = new Packet(123)) { p.Write(1); p.Write(WorldId); p.Write(slot); p.Write(pos.x); p.Write(pos.y); p.Write(pos.z); Net.ClientSendTCPData(p); }
            homes[slot] = pos; RefreshTargets();
            SessionControl.Say("已标记家" + slot + "。请让房主 F7 保存，读档会恢复路标。");
        }
        private static void ClearHomeSlot(int slot)
        {
            if (!WorldReady) return;
            if (LocalClient.serverOwner) NavigationSave.Instance.RemoveHome(MyId, slot);
            else using (var p = new Packet(123)) { p.Write(2); p.Write(WorldId); p.Write(slot); Net.ClientSendTCPData(p); }
            homes.Remove(slot); RefreshTargets();
        }
        private static void HandleWorldRequest(int from, Packet p)
        {
            if (!LocalClient.serverOwner || !WorldReady || !Server.clients.TryGetValue(from, out var client) || client.player == null) return;
            int action = p.ReadInt(); string id = client.player.steamId.Value.ToString();
            if (action == 1)
            {
                string world = p.ReadString(); int slot = p.ReadInt(); var pos = new Vector3(p.ReadFloat(), p.ReadFloat(), p.ReadFloat());
                if (world == WorldId && NavigationSave.Finite(pos.x) && NavigationSave.Finite(pos.y) && NavigationSave.Finite(pos.z) &&
                    GameManager.players.TryGetValue(from, out var player) && player && Vector3.Distance(player.transform.position, pos) < 15f)
                    NavigationSave.Instance.SetHome(id, slot, pos);
            }
            else if (action == 2) { string world = p.ReadString(); int slot = p.ReadInt(); if (world == WorldId) NavigationSave.Instance.RemoveHome(id, slot); }
            using (var answer = new Packet(123))
            {
                answer.Write(WorldId); answer.Write(SharedEnabled); answer.Write(KeepOnDeath);
                var places = NavigationSave.Instance.GetHomes(id); answer.Write(places.Count);
                foreach (var pair in places) { answer.Write(pair.Key); answer.Write(pair.Value.x); answer.Write(pair.Value.y); answer.Write(pair.Value.z); }
                Net.ServerSendTCPData(from, answer);
            }
        }
        private static void HandleWorldReply(Packet p)
        {
            string id = p.ReadString(); Guid guid; if (!Guid.TryParse(id, out guid)) return;
            bool shared = p.ReadBool(), keep = p.ReadBool(); int count = p.ReadInt();
            if (count < 0 || count > NavigationSave.MaxHomes) return;
            var places = new SortedDictionary<int, Vector3>();
            for (int i = 0; i < count; i++)
            {
                int slot = p.ReadInt(); var pos = new Vector3(p.ReadFloat(), p.ReadFloat(), p.ReadFloat());
                if (slot < 1 || slot > NavigationSave.MaxHomes || !NavigationSave.Finite(pos.x) || !NavigationSave.Finite(pos.y) || !NavigationSave.Finite(pos.z)) return;
                places[slot] = pos;
            }
            WorldId = id; SharedEnabled = shared; KeepOnDeath = keep; homes.Clear(); foreach (var pair in places) homes[pair.Key] = pair.Value;
            WorldReady = true; RefreshTargets();
        }
        [HarmonyPatch(typeof(LocalClient), "InitializeClientData"), HarmonyPostfix]
        private static void RegisterClient() { LocalClient.packetHandlers[123] = HandleWorldReply; }
        [HarmonyPatch(typeof(Server), "InitializeServerPackets"), HarmonyPostfix]
        private static void RegisterServer() { Server.PacketHandlers[123] = HandleWorldRequest; }
        [HarmonyPatch(typeof(Hitable), "Awake"), HarmonyPostfix]
        private static void TrackAwake(Hitable __instance) { if (__instance is HitableResource) awakened.Add(__instance.GetInstanceID()); }
        private static bool Alive(Target t)
        {
            if (!t.Transform || !t.Transform.gameObject.scene.IsValid()) return false;
            if (t.Resource) return t.Resource.hp > 0 || (!awakened.Contains(t.Resource.GetInstanceID()) && !t.Resource.gameObject.activeInHierarchy && t.Resource.maxHp > 0);
            if (t.Chest) return !(bool)chestOpened.GetValue(t.Chest);
            if (t.Trader) { var hit = t.Trader.GetComponentInParent<Hitable>(); return t.Trader.gameObject.activeSelf && (!hit || hit.hp > 0); }
            if (t.Camp) return true;
            if (t.Pickup) return t.Pickup.item && t.Pickup.amount > 0;
            return false;
        }
        private static string ItemKey(int id) { return "item:" + id; }
        private static string TitleFor(string key)
        {
            if (landmarks.TryGetValue(key, out var title)) return title;
            int id; if (key.StartsWith("item:") && int.TryParse(key.Substring(5), out id) && materials.TryGetValue(id, out var item)) return Display(item);
            return "资源";
        }
        private static void ScanResources()
        {
            resources.Clear(); targets.Clear(); materials.Clear();
            if (ItemManager.Instance) foreach (var item in ItemManager.Instance.allItems.Values) if (item && MaterialOrder(item) < 100) materials[item.id] = item;
            // FindObjectsOfTypeAll includes generated but distance-culled chunks; scene filter excludes prefab assets.
            foreach (var r in Resources.FindObjectsOfTypeAll<HitableResource>())
            {
                if (!r || !r.gameObject.scene.IsValid() || (!(r is HitableTree) && !(r is HitableRock))) continue;
                var target = new Target { Resource = r, Transform = r.transform };
                if (!Alive(target)) continue;
                resources.Add(r);
                // Match LootExtra.CheckDrop: dropItem is used only for dropOne.
                // Vanilla Coal and DarkOak prefabs contain stale dropItem values
                // (Iron Ore / Oak Wood); their actual output comes from dropTable.
                if (!r.dropTable) continue;
                if (r.dropTable.dropOne)
                {
                    AddResourceTarget(r, r.dropItem, false);
                    continue;
                }
                foreach (var loot in r.dropTable.loot ?? new LootDrop.LootItems[0])
                    if (loot != null && loot.dropChance > 0 && loot.amountMax > 0)
                        AddResourceTarget(r, loot.item, loot.dropChance < 1f || loot.amountMin < 1);
            }
            foreach (var chest in Resources.FindObjectsOfTypeAll<LootContainerInteract>())
            {
                if (!chest || !chest.gameObject.scene.IsValid()) continue;
                int price = chest.gameObject.activeInHierarchy ? (int)chestPrice.GetValue(chest) : chest.price;
                string tier = price == 0 && chest.white >= chest.blue && chest.white >= chest.gold ? "free" : chest.gold > chest.blue && chest.gold > chest.white ? "gold" : chest.blue > chest.white ? "blue" : "white";
                var t = new Target { Key = "chest:" + tier, Transform = chest.transform, Chest = chest }; if (Alive(t)) targets.Add(t);
            }
            foreach (var trader in Resources.FindObjectsOfTypeAll<TraderInteract>())
            { var t = new Target { Key = "npc:trader", Transform = trader.transform, Trader = trader }; if (Alive(t)) targets.Add(t); }
            foreach (var camp in Resources.FindObjectsOfTypeAll<GenerateCamp>())
            { if (camp && camp.gameObject.scene.IsValid()) targets.Add(new Target { Key = "npc:camp", Transform = camp.transform, Camp = camp }); }
            foreach (var pickup in Resources.FindObjectsOfTypeAll<PickupInteract>())
            {
                if (!pickup || !pickup.gameObject.scene.IsValid() || !pickup.item || pickup.item.name.Replace(" ", "").ToLowerInvariant() != "coal") continue;
                var t = new Target { Key = ItemKey(pickup.item.id), Transform = pickup.transform, Pickup = pickup }; if (Alive(t)) targets.Add(t);
            }
        }
        private static void AddResourceTarget(HitableResource resource, InventoryItem item, bool incidental)
        {
            if (!item || MaterialOrder(item) >= 100) return;
            materials[item.id] = item;
            targets.Add(new Target { Key = ItemKey(item.id), Transform = resource.transform, Resource = resource, Extra = incidental });
        }
        private static Target Nearest(string key)
        {
            if (!PlayerMovement.Instance) return null;
            var position = PlayerMovement.Instance.transform.position; Target nearest = null; float best = float.PositiveInfinity; int bestRank = int.MaxValue;
            foreach (var t in targets) if (t.Key == key && Alive(t))
            {
                // Dedicated deposits are chosen before incidental drops, regardless of distance.
                int rank = t.Extra ? 1 : 0; float distance = (t.Transform.position - position).sqrMagnitude;
                if (rank < bestRank || (rank == bestRank && distance < best)) { bestRank = rank; best = distance; nearest = t; }
            }
            return nearest;
        }
        private static string Display(InventoryItem item)
        {
            if (!item) return "";
            string original = item.name;
            try { if (translate != null) { var args = new object[] { original, null }; if ((bool)translate.Invoke(null, args)) return (string)args[1]; } } catch { }
            return original;
        }
        private static int MaterialOrder(InventoryItem item)
        {
            // Only gatherable raw resources belong in this menu; several vanilla
            // building items also carry processable flags, so those flags alone
            // must not make an anvil or workbench a navigation target.
            string name = item.name.Replace(" ", "").ToLowerInvariant();
            if (name == "redapple") name = "apple";
            string[] names = { "wood", "birchwood", "firwood", "oakwood", "darkoakwood", "rock", "coal", "ironore", "goldore", "mithrilore", "adamantiteore", "obamiumore", "ruby", "flint", "apple", "wheat" };
            int index = Array.IndexOf(names, name); return index < 0 ? 100 : index;
        }
        private static void SelectRecipe(InventoryItem item)
        {
            selected.Clear();
            var requirements = new List<string>();
            foreach (var need in item.requirements ?? new InventoryItem.CraftRequirement[0])
            {
                if (!need.item) continue;
                requirements.Add(Display(need.item) + " × " + need.amount);
                if (materials.ContainsKey(need.item.id)) { if (!selected.Contains(ItemKey(need.item.id))) selected.Add(ItemKey(need.item.id)); continue; }
                // Resolve ingots from the game's actual smelting data, rather than a guessed tier table.
                foreach (var raw in ItemManager.Instance.allItems.Values)
                    if (raw && raw.processedItem && raw.processedItem.id == need.item.id && materials.ContainsKey(raw.id) && !selected.Contains(ItemKey(raw.id))) selected.Add(ItemKey(raw.id));
            }
            SessionControl.Say("制作“" + Display(item) + "”需要：" + string.Join("、", requirements.ToArray()) + "。已导航地图上对应木材和矿石；其他材料需另外获得。");
            RefreshTargets(); ClosePanel();
        }
        private static void RefreshTargets()
        {
            if (!canvas || !PlayerMovement.Instance) return;
            int count = homes.Count + selected.Count; while (pins.Count < count) pins.Add(CreatePin());
            int index = 0;
            foreach (var pair in homes) { var pin = pins[index++]; UpdatePin(pin, "家" + pair.Key, pair.Value, true); }
            var missing = new List<string>();
            foreach (string key in selected)
            {
                var target = Nearest(key); string title = TitleFor(key);
                if (target != null && target.Extra) title += "（概率掉落）";
                else if (target != null && target.Pickup) title += "（可拾取）";
                else if (target != null && target.Resource && target.Key.StartsWith("item:") && materials.TryGetValue(int.Parse(target.Key.Substring(5)), out var material) && material.name.Trim().Equals("Coal", StringComparison.OrdinalIgnoreCase)) title += "（煤炭石）";
                UpdatePin(pins[index++], title, target != null ? target.Transform.position + Vector3.up * 2f : (Vector3?)null, false);
                if (target == null) missing.Add(TitleFor(key));
            }
            for (; index < pins.Count; index++) pins[index].Position = null;
            SetText(hint, "H 新增家  ·  C 导航菜单" + (WorldReady ? "" : "  ·  等待房主确认") + (missing.Count > 0 ? "\n当前地图未找到：" + string.Join("、", missing.ToArray()) : ""));
        }
        private static void UpdatePin(Pin pin, string title, Vector3? position, bool isHome)
        {
            if (pin.Title != title) RemoveMapPin(pin);
            pin.Title = title; pin.Position = position; pin.Text.color = isHome ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 1f, 0.85f);
        }
        private static GameObject Object(string name, Transform parent, params Type[] types)
        {
            var go = new GameObject(name, types); go.layer = 5; go.transform.SetParent(parent, false); return go;
        }
        private static TextMeshProUGUI Label(string name, Transform parent, float size, TextAlignmentOptions alignment)
        {
            var go = Object(name, parent, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var text = go.GetComponent<TextMeshProUGUI>(); text.fontSize = size; text.color = Color.white;
            text.alignment = alignment; text.richText = false; text.raycastTarget = false;
            return text;
        }
        private static void SetText(TextMeshProUGUI text, string value)
        {
            int id = text.GetInstanceID();
            if (textValues.TryGetValue(id, out var old) && old == value && text.GetParsedText() == value) return;
            text.SetCharArray(value.ToCharArray()); if (applyFont != null) applyFont.Invoke(null, new object[] { text, value });
            textValues[id] = value;
        }
        private static void EnsureCanvas()
        {
            if (canvas) return;
            var root = new GameObject("MuckSession.Navigation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.layer = 5; UnityEngine.Object.DontDestroyOnLoad(root); canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 45;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 0.5f;
            hint = Label("Guide", root.transform, 17f, TextAlignmentOptions.TopLeft);
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0, 1); hint.rectTransform.pivot = new Vector2(0, 1); hint.rectTransform.anchoredPosition = new Vector2(18, -100); hint.rectTransform.sizeDelta = new Vector2(440, 70);
            RefreshTargets();
        }
        private static Pin CreatePin()
        {
            var text = Label("Direction", canvas.transform, 19f, TextAlignmentOptions.Center);
            text.rectTransform.sizeDelta = new Vector2(220, 65); text.color = pins.Count == 0 ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 1f, 0.85f);
            return new Pin { Text = text, Anchor = new GameObject("MuckSession.MapPin") };
        }
        public static void Render()
        {
            if (!canvas) return;
            bool playing = GameManager.state == GameManager.GameState.Playing && PlayerMovement.Instance && (!PlayerStatus.Instance || !PlayerStatus.Instance.IsPlayerDead());
            canvas.enabled = playing; if (!playing) return;
            if (PanelOpen) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; OtherInput.lockCamera = true; }
            Camera camera = MoveCamera.Instance ? MoveCamera.Instance.mainCam : Camera.main;
            if (!camera) return;
            var occupied = new List<Rect>();
            foreach (var pin in pins)
            {
                pin.Text.gameObject.SetActive(pin.Position.HasValue && !PanelOpen);
                if (!pin.Position.HasValue) { RemoveMapPin(pin); continue; }
                pin.Anchor.transform.position = pin.Position.Value;
                if (Map.Instance && (pin.Map != Map.Instance || pin.MapPin == null))
                { RemoveMapPin(pin); pin.Map = Map.Instance; pin.MapPin = pin.Map.AddMarker(pin.Anchor.transform, Map.MarkerType.Other, Texture2D.whiteTexture, pin.Text.color, pin.Title, 1f);
                    var marker = (RectTransform)pin.MapPin.marker; marker.localScale = Vector3.one; marker.sizeDelta = new Vector2(8, 8);
                    var mapText = marker.GetComponentInChildren<TextMeshProUGUI>(); mapText.fontSize = 12; mapText.enableAutoSizing = false; mapText.color = pin.Text.color; mapText.raycastTarget = false;
                    mapText.rectTransform.localScale = Vector3.one; mapText.rectTransform.anchorMin = mapText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    mapText.rectTransform.pivot = new Vector2(0.5f, 0); mapText.rectTransform.anchoredPosition = new Vector2(0, 7); mapText.rectTransform.sizeDelta = new Vector2(100, 18); SetText(mapText, pin.Title);
                    marker.GetComponent<RawImage>().raycastTarget = false; }
                var screen = camera.WorldToScreenPoint(pin.Position.Value);
                var point = NameplatePlacement.Place(screen.x, screen.y, screen.z, Screen.width, Screen.height, 120f * canvas.scaleFactor, 42f * canvas.scaleFactor);
                float originalY = point.Y, spacing = 70f * canvas.scaleFactor;
                var area = new Rect(point.X - 110f * canvas.scaleFactor, point.Y - spacing * 0.5f, 220f * canvas.scaleFactor, spacing);
                for (int attempt = 1; occupied.Any(r => r.Overlaps(area)) && attempt <= 20; attempt++)
                {
                    point.Y = Mathf.Clamp(originalY + (attempt % 2 == 1 ? 1f : -1f) * ((attempt + 1) / 2) * spacing, spacing * 0.5f, Screen.height - spacing * 0.5f);
                    area.y = point.Y - spacing * 0.5f;
                }
                occupied.Add(area);
                Vector2 local; RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, new Vector2(point.X, point.Y), null, out local);
                pin.Text.rectTransform.anchoredPosition = local;
                int distance = Mathf.RoundToInt(Vector3.Distance(PlayerMovement.Instance.transform.position, pin.Position.Value));
                SetText(pin.Text, (point.Edge ? point.Arrow + " " : "") + pin.Title + "\n" + distance + " 米");
            }
        }
        private static void RemoveMapPin(Pin pin) { if (pin.Map && pin.MapPin != null) pin.Map.RemoveMarker(pin.MapPin); pin.MapPin = null; pin.Map = null; }
        private static void Button(string title, float x, float y, float width, Action action)
        {
            var go = Object("Choice", panel.transform, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UnityEngine.UI.Button));
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, 38);
            go.GetComponent<Image>().color = new Color(0.12f, 0.24f, 0.3f, 1);
            var button = go.GetComponent<UnityEngine.UI.Button>(); menuButtons.Add(button);
            button.onClick.AddListener(() => { if (actionFrame == Time.frameCount) return; actionFrame = Time.frameCount; action(); });
            var text = Label("Text", go.transform, 16f, TextAlignmentOptions.Center); text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new Vector2(5, 0); text.rectTransform.offsetMax = new Vector2(-5, 0); SetText(text, title);
        }
        private static void BuildPanel()
        {
            if (panel) { panel.SetActive(false); UnityEngine.Object.Destroy(panel); }
            textValues.Clear();
            menuButtons.Clear(); pressed = null;
            panel = Object("NavigationMenu", canvas.transform, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)panel.transform; rect.sizeDelta = new Vector2(700, 570); panel.GetComponent<Image>().color = new Color(0.025f, 0.065f, 0.09f, 0.98f);
            var title = Label("Title", panel.transform, 23f, TextAlignmentOptions.TopLeft); title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0, 1); title.rectTransform.pivot = new Vector2(0, 1); title.rectTransform.anchoredPosition = new Vector2(20, -15); title.rectTransform.sizeDelta = new Vector2(660, 60); SetText(title, "导航 · " + new[] { "资源", "制作目标", "宝箱与交易", "家" }[tab] + "\n每项显示最近目标；H 新增家，C 关闭");
            string[] tabs = { "资源", "制作目标", "宝箱与交易", "家" };
            for (int i = 0; i < tabs.Length; i++) { int chosen = i; Button((tab == i ? "● " : "") + tabs[i], 20 + i * 170, 90, 160, () => { tab = chosen; page = 0; BuildPanel(); }); }
            int pages = 1;
            if (tab == 3)
            {
                for (int i = 1; i <= NavigationSave.MaxHomes; i++)
                {
                    int slot = i; bool exists = homes.ContainsKey(i); float y = 142 + (i - 1) * 43;
                    string name = "家" + i + (exists ? " · " + Mathf.RoundToInt(Vector3.Distance(PlayerMovement.Instance.transform.position, homes[i])) + " 米" : " · 未标记");
                    Button(name + (exists ? " / 更新到此处" : " / 标记此处"), 20, y, 485, () => { SetHomeSlot(slot); BuildPanel(); });
                    Button("删除", 515, y, 165, () => { ClearHomeSlot(slot); BuildPanel(); });
                }
            }
            else
            {
                var choices = new List<KeyValuePair<string, Action>>();
                if (tab == 1 && ItemManager.Instance)
                    foreach (var item in ItemManager.Instance.allItems.Values.Where(i => i && i.craftable && (i.type == InventoryItem.ItemType.Sword || i.type == InventoryItem.ItemType.Bow || i.type == InventoryItem.ItemType.Axe || i.type == InventoryItem.ItemType.Pickaxe)).OrderBy(i => i.tier).ThenBy(i => i.name))
                    { var recipe = item; choices.Add(new KeyValuePair<string, Action>(Display(item), () => SelectRecipe(recipe))); }
                else if (tab == 2)
                    foreach (var landmark in landmarks) { string key = landmark.Key; choices.Add(new KeyValuePair<string, Action>(landmark.Value, () => SelectTarget(key))); }
                else foreach (var item in materials.Values.OrderBy(MaterialOrder).ThenBy(i => i.name)) { string key = ItemKey(item.id); choices.Add(new KeyValuePair<string, Action>(Display(item), () => SelectTarget(key))); }
                pages = Math.Max(1, (choices.Count + 15) / 16); page = Math.Max(0, Math.Min(page, pages - 1));
                for (int i = 0; i < 16 && page * 16 + i < choices.Count; i++) { var choice = choices[page * 16 + i]; Button(choice.Key, 20 + (i % 2) * 335, 145 + (i / 2) * 43, 325, choice.Value); }
            }
            Button("上一页", 20, 508, 130, () => { page--; BuildPanel(); });
            Button("下一页 " + (page + 1) + "/" + pages, 160, 508, 155, () => { page++; BuildPanel(); });
            Button("关闭目标标记", 325, 508, 180, () => { selected.Clear(); RefreshTargets(); ClosePanel(); });
            Button("关闭（C）", 515, 508, 165, ClosePanel);
        }
        private static void SelectTarget(string key) { selected.Clear(); selected.Add(key); ScanResources(); RefreshTargets(); ClosePanel(); }
        private static UnityEngine.UI.Button HitButton(Vector2 position)
        {
            for (int i = menuButtons.Count - 1; i >= 0; i--) { var b = menuButtons[i]; if (b && b.IsInteractable() && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, position, null)) return b; }
            return null;
        }
        private static void UpdateMenuPointer()
        {
            // Own-rectangle fallback keeps this menu operable even if the game's EventSystem is inactive or another canvas consumes its raycast.
            if (Input.GetMouseButtonDown(0)) pressed = HitButton(Input.mousePosition);
            if (Input.GetMouseButtonUp(0)) { var b = HitButton(Input.mousePosition); if (pressed && b == pressed) b.onClick.Invoke(); pressed = null; }
        }
        private static void OpenPanel()
        {
            ScanResources(); previousLock = Cursor.lockState; previousCursor = Cursor.visible; previousCameraLock = OtherInput.lockCamera;
            BuildPanel(); Cursor.lockState = CursorLockMode.None; Cursor.visible = true; OtherInput.lockCamera = true;
        }
        public static void ClosePanel()
        {
            if (!PanelOpen) return; panel.SetActive(false); Cursor.lockState = previousLock; Cursor.visible = previousCursor; OtherInput.lockCamera = previousCameraLock;
        }
        [HarmonyPatch(typeof(OtherInput), "Update"), HarmonyPrefix]
        private static bool BlockMenuInput() { return !PanelOpen; }
        [HarmonyPatch(typeof(UseInventory), "Use"), HarmonyPrefix]
        private static bool BlockAttack() { return !PanelOpen && !SharedContainers.Pending; }
        [HarmonyPatch(typeof(PlayerMovement), "GetInput"), HarmonyPrefix]
        private static bool BlockMovement(ref Vector2 __result) { if (!PanelOpen) return true; __result = Vector2.zero; return false; }
        public static void Reset()
        {
            ClosePanel(); foreach (var pin in pins) { RemoveMapPin(pin); if (pin.Anchor) UnityEngine.Object.Destroy(pin.Anchor); }
            pins.Clear(); if (canvas) UnityEngine.Object.Destroy(canvas.gameObject); canvas = null; panel = null; hint = null;
            resources.Clear(); materials.Clear(); selected.Clear(); homes.Clear(); targets.Clear(); awakened.Clear(); textValues.Clear(); WorldReady = false; SharedEnabled = KeepOnDeath = false; WorldId = ""; nextHello = nextScan = nextTargets = 0; tab = page = 0; menuButtons.Clear(); pressed = null;
        }
    }
}
