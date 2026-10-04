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
        private static Vector3? home;
        private static float nextHello, nextScan;
        private static readonly Dictionary<int, InventoryItem> materials = new Dictionary<int, InventoryItem>();
        private static readonly List<HitableResource> resources = new List<HitableResource>();
        private static readonly List<int> selected = new List<int>();
        private sealed class Pin { public GameObject Anchor; public TextMeshProUGUI Text; public Map.MapMarker MapPin; public Map Map; public Vector3? Position; public string Title; }
        private static readonly List<Pin> pins = new List<Pin>();
        private static Canvas canvas;
        private static GameObject panel;
        private static TextMeshProUGUI hint;
        private static MethodInfo applyFont, translate;
        private static CursorLockMode previousLock;
        private static bool previousCursor, previousCameraLock;
        private static int page;
        private static bool recipes;
        public static bool PanelOpen { get { return panel && panel.activeSelf; } }
        public static void Initialize(ConfigFile config)
        {
            sharedOption = config.Bind("Convenience", "SharedContainers", true, "Host setting: allow simultaneous chest/furnace access using confirmed slot transactions. All players need 0.9.5.");
            keepOption = config.Bind("Convenience", "KeepInventoryOnDeath", true, "Host setting: retain inventory, armor and cursor-held items when dying. Does not change death or revive rules.");
            homeKey = config.Bind("Navigation", "SetHomeKey", KeyCode.H, "Mark current position as home. Save with F7 to retain it.");
            menuKey = config.Bind("Navigation", "MenuKey", KeyCode.N, "Open resource and weapon-material navigation.");
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
                Vector3 position; home = NavigationSave.Instance.Homes.TryGetValue(MyId, out position) ? position : (Vector3?)null;
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
            if (Time.realtimeSinceStartup >= nextScan) { nextScan = Time.realtimeSinceStartup + 1f; ScanResources(); RefreshTargets(); }
        }
        private static void MarkHome()
        {
            if (!WorldReady) { SessionControl.Say("正在等待房主的导航确认，请稍后再标记家。"); return; }
            Vector3 pos = PlayerMovement.Instance.transform.position;
            if (LocalClient.serverOwner) NavigationSave.Instance.Homes[MyId] = pos;
            else using (var p = new Packet(123)) { p.Write(1); p.Write(WorldId); p.Write(pos.x); p.Write(pos.y); p.Write(pos.z); Net.ClientSendTCPData(p); }
            home = pos; RefreshTargets();
            SessionControl.Say("已标记当前位置为家。请让房主 F7 保存，之后读档会恢复路标。");
        }
        private static void ClearHome()
        {
            if (!WorldReady) return;
            if (LocalClient.serverOwner) NavigationSave.Instance.Homes.Remove(MyId);
            else using (var p = new Packet(123)) { p.Write(2); p.Write(WorldId); Net.ClientSendTCPData(p); }
            home = null; RefreshTargets();
        }
        private static void HandleWorldRequest(int from, Packet p)
        {
            if (!LocalClient.serverOwner || !WorldReady || !Server.clients.TryGetValue(from, out var client) || client.player == null) return;
            int action = p.ReadInt(); string id = client.player.steamId.Value.ToString();
            if (action == 1)
            {
                string world = p.ReadString(); var pos = new Vector3(p.ReadFloat(), p.ReadFloat(), p.ReadFloat());
                if (world == WorldId && NavigationSave.Finite(pos.x) && NavigationSave.Finite(pos.y) && NavigationSave.Finite(pos.z) &&
                    GameManager.players.TryGetValue(from, out var player) && player && Vector3.Distance(player.transform.position, pos) < 15f)
                    NavigationSave.Instance.Homes[id] = pos;
            }
            else if (action == 2 && p.ReadString() == WorldId) NavigationSave.Instance.Homes.Remove(id);
            using (var answer = new Packet(123))
            {
                answer.Write(WorldId); answer.Write(SharedEnabled); answer.Write(KeepOnDeath);
                Vector3 pos; bool exists = NavigationSave.Instance.Homes.TryGetValue(id, out pos); answer.Write(exists);
                if (exists) { answer.Write(pos.x); answer.Write(pos.y); answer.Write(pos.z); }
                Net.ServerSendTCPData(from, answer);
            }
        }
        private static void HandleWorldReply(Packet p)
        {
            string id = p.ReadString(); Guid guid;
            if (!Guid.TryParse(id, out guid)) return;
            WorldId = id; SharedEnabled = p.ReadBool(); KeepOnDeath = p.ReadBool();
            home = p.ReadBool() ? new Vector3(p.ReadFloat(), p.ReadFloat(), p.ReadFloat()) : (Vector3?)null;
            WorldReady = true; RefreshTargets();
        }
        [HarmonyPatch(typeof(LocalClient), "InitializeClientData"), HarmonyPostfix]
        private static void RegisterClient() { LocalClient.packetHandlers[123] = HandleWorldReply; }
        [HarmonyPatch(typeof(Server), "InitializeServerPackets"), HarmonyPostfix]
        private static void RegisterServer() { Server.PacketHandlers[123] = HandleWorldRequest; }
        private static void ScanResources()
        {
            resources.Clear(); materials.Clear();
            if (ItemManager.Instance)
                foreach (var item in ItemManager.Instance.allItems.Values)
                    if (item && MaterialOrder(item) < 100) materials[item.id] = item;
            if (ResourceManager.Instance) Collect(ResourceManager.Instance.list.Values);
            if (ResourceManagerPooled.Instance) Collect(ResourceManagerPooled.Instance.list.Values);
        }
        private static void Collect(IEnumerable<GameObject> objects)
        {
            foreach (var go in objects)
            {
                if (!go) continue;
                var r = go.GetComponentInChildren<HitableResource>(true);
                if (!r || !r.dropItem || MaterialOrder(r.dropItem) >= 100 || (!(r is HitableTree) && !(r is HitableRock)) || r.hp <= 0 || !go.scene.IsValid()) continue;
                resources.Add(r); materials[r.dropItem.id] = r.dropItem;
            }
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
            string[] names = { "wood", "birchwood", "firwood", "oakwood", "darkoakwood", "rock", "coal", "ironore", "goldore", "mithrilore", "adamantiteore", "obamiumore", "ruby" };
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
                if (materials.ContainsKey(need.item.id)) { if (!selected.Contains(need.item.id)) selected.Add(need.item.id); continue; }
                // Resolve ingots from the game's actual smelting data, rather than a guessed tier table.
                foreach (var raw in ItemManager.Instance.allItems.Values)
                    if (raw && raw.processedItem && raw.processedItem.id == need.item.id && materials.ContainsKey(raw.id) && !selected.Contains(raw.id)) selected.Add(raw.id);
            }
            SessionControl.Say("制作“" + Display(item) + "”需要：" + string.Join("、", requirements.ToArray()) + "。已导航地图上对应木材和矿石；其他材料需另外获得。");
            RefreshTargets(); ClosePanel();
        }
        private static void RefreshTargets()
        {
            if (!canvas || !PlayerMovement.Instance) return;
            while (pins.Count < selected.Count + 1) pins.Add(CreatePin());
            pins[0].Position = home; pins[0].Title = "家";
            var player = PlayerMovement.Instance.transform.position;
            for (int i = 0; i < selected.Count; i++)
            {
                HitableResource nearest = null; float best = float.PositiveInfinity;
                foreach (var r in resources)
                    if (r && r.hp > 0 && r.dropItem && r.dropItem.id == selected[i])
                    { float distance = (r.transform.position - player).sqrMagnitude; if (distance < best) { best = distance; nearest = r; } }
                InventoryItem material; materials.TryGetValue(selected[i], out material);
                pins[i + 1].Position = nearest ? nearest.transform.position + Vector3.up * 2f : (Vector3?)null;
                string title = material ? Display(material) : "资源";
                if (pins[i + 1].Title != title) RemoveMapPin(pins[i + 1]);
                pins[i + 1].Title = title;
            }
            for (int i = selected.Count + 1; i < pins.Count; i++) pins[i].Position = null;
            SetText(hint, "H 标记家  ·  N 找资源" + (WorldReady ? "" : "  ·  等待房主确认") + (selected.Count > 0 && pins.Skip(1).All(p => !p.Position.HasValue) ? "\n未找到所选资源；可以换一种材料" : ""));
        }
        private static GameObject Object(string name, Transform parent, params Type[] types)
        {
            var go = new GameObject(name, types); go.transform.SetParent(parent, false); return go;
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
            if (text.text == value) return;
            text.SetCharArray(value.ToCharArray()); if (applyFont != null) applyFont.Invoke(null, new object[] { text, value });
        }
        private static void EnsureCanvas()
        {
            if (canvas) return;
            var root = new GameObject("MuckSession.Navigation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(root); canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 45;
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
                { RemoveMapPin(pin); pin.Map = Map.Instance; pin.MapPin = pin.Map.AddMarker(pin.Anchor.transform, Map.MarkerType.Other, null, pin.Text.color, pin.Title, 1.2f); }
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
            go.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() => action());
            var text = Label("Text", go.transform, 16f, TextAlignmentOptions.Center); text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new Vector2(5, 0); text.rectTransform.offsetMax = new Vector2(-5, 0); SetText(text, title);
        }
        private static void BuildPanel()
        {
            if (panel) UnityEngine.Object.Destroy(panel);
            panel = Object("NavigationMenu", canvas.transform, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)panel.transform; rect.sizeDelta = new Vector2(650, 560); panel.GetComponent<Image>().color = new Color(0.025f, 0.065f, 0.09f, 0.98f);
            var title = Label("Title", panel.transform, 23f, TextAlignmentOptions.TopLeft); title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0, 1); title.rectTransform.pivot = new Vector2(0, 1); title.rectTransform.anchoredPosition = new Vector2(20, -15); title.rectTransform.sizeDelta = new Vector2(600, 60); SetText(title, "回家与资源导航\n选择材料，或选择想制作的武器／工具");
            Button("资源", 20, 90, 150, () => { recipes = false; page = 0; BuildPanel(); });
            Button("制作目标", 180, 90, 150, () => { recipes = true; page = 0; BuildPanel(); });
            Button("标记此处为家", 340, 90, 135, () => { MarkHome(); ClosePanel(); });
            Button("清除家", 485, 90, 145, () => { ClearHome(); ClosePanel(); });
            var items = recipes && ItemManager.Instance ? ItemManager.Instance.allItems.Values.Where(i => i && i.craftable && (i.type == InventoryItem.ItemType.Sword || i.type == InventoryItem.ItemType.Bow || i.type == InventoryItem.ItemType.Axe || i.type == InventoryItem.ItemType.Pickaxe)).OrderBy(i => i.tier).ThenBy(i => i.name).ToArray() : materials.Values.OrderBy(MaterialOrder).ThenBy(i => i.name).ToArray();
            int pages = Math.Max(1, (items.Length + 15) / 16); page = Math.Max(0, Math.Min(page, pages - 1));
            for (int i = 0; i < 16 && page * 16 + i < items.Length; i++)
            {
                var item = items[page * 16 + i];
                Button(Display(item), 20 + (i % 2) * 310, 145 + (i / 2) * 43, 300, () => { if (recipes) SelectRecipe(item); else { selected.Clear(); selected.Add(item.id); RefreshTargets(); ClosePanel(); } });
            }
            Button("上一页", 20, 493, 145, () => { page--; BuildPanel(); });
            Button("下一页", 175, 493, 145, () => { page++; BuildPanel(); });
            Button("关闭资源标记", 330, 493, 145, () => { selected.Clear(); RefreshTargets(); ClosePanel(); });
            Button("关闭（N）", 485, 493, 145, ClosePanel);
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
            resources.Clear(); materials.Clear(); selected.Clear(); home = null; WorldReady = false; SharedEnabled = KeepOnDeath = false; WorldId = ""; nextHello = nextScan = 0;
        }
    }
}
