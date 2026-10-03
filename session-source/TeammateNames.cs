namespace MuckSaveGame
{
    using BepInEx.Configuration;
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using TMPro;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;
    public static class TeammateNames
    {
        private sealed class Label { public GameObject Root; public RectTransform Rect; public TextMeshProUGUI Text; public string Last; }
        private static readonly Dictionary<int, Label> labels = new Dictionary<int, Label>();
        private static ConfigEntry<bool> enabled;
        private static Canvas canvas;
        private static RectTransform canvasRect;
        private static MethodInfo applyFont;
        public static int VisibleCount { get { int count=0; foreach (var label in labels.Values) if (canvas && canvas.enabled && label.Root.activeSelf) count++; return count; } }
        public static void Initialize(ConfigEntry<bool> setting) { enabled = setting; }
        public static void UpdateInput()
        {
            if (GameManager.state != GameManager.GameState.Playing || enabled == null) return;
            if (ChatBox.Instance && ChatBox.Instance.typing) return;
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                var tmp = selected.GetComponent<TMP_InputField>();
                var ui = selected.GetComponent<InputField>();
                if ((tmp && tmp.isFocused) || (ui && ui.isFocused)) return;
            }
            if (Input.GetKeyDown(KeyCode.T)) Toggle();
        }
        public static void Toggle()
        {
            if (enabled == null) return;
            enabled.Value = !enabled.Value;
            enabled.ConfigFile.Save();
            if (canvas) canvas.enabled = enabled.Value;
            SessionControl.Say(enabled.Value ? "队友名字显示：开启（T 键切换）" : "队友名字显示：关闭（T 键切换）");
        }
        private static void EnsureCanvas()
        {
            if (canvas) return;
            var root = new GameObject("MuckSession.TeamNames", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(root);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            canvasRect = root.GetComponent<RectTransform>();
            var fontType = Type.GetType("UU9.Muck.Translater.FontManager, UU9.Muck.Translater");
            if (fontType != null) applyFont = fontType.GetMethod("ApplyTranslatedFont", new[] { typeof(TMP_Text), typeof(string) });
        }
        private static Label CreateLabel(int id)
        {
            var root = new GameObject("Teammate " + id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            root.transform.SetParent(canvas.transform, false);
            var rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(240, 54);
            var background = root.GetComponent<Image>();
            background.color = new Color(0.025f, 0.08f, 0.105f, 0.84f); background.raycastTarget = false;
            var textObject = new GameObject("Name", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(root.transform, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(8, 3); text.rectTransform.offsetMax = new Vector2(-8, -3);
            text.fontSize = 18; text.color = new Color(0.8f, 1f, 0.94f, 1);
            text.alignment = TextAlignmentOptions.Center; text.richText = false; text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
            var label = new Label { Root = root, Rect = rect, Text = text };
            labels.Add(id, label); return label;
        }
        public static void Render()
        {
            if (enabled == null || !enabled.Value || GameManager.state != GameManager.GameState.Playing || !LocalClient.instance)
            { if (canvas) canvas.enabled = false; return; }
            Camera camera = MoveCamera.Instance && MoveCamera.Instance.mainCam ? MoveCamera.Instance.mainCam : Camera.main;
            if (!camera) { if (canvas) canvas.enabled = false; return; }
            EnsureCanvas(); canvas.enabled = true;
            canvas.scaleFactor = Mathf.Clamp(Screen.height / 1080f, 0.85f, 1.5f);
            foreach (var label in labels.Values) label.Root.SetActive(false);
            foreach (var pair in GameManager.players)
            {
                var player = pair.Value;
                if (pair.Key == LocalClient.instance.myId || !player || player.disconnected) continue;
                if (!labels.TryGetValue(pair.Key, out var label)) label = CreateLabel(pair.Key);
                Vector3 location = player.transform.position + Vector3.up * 2.3f;
                Vector3 projected = camera.WorldToScreenPoint(location);
                var point = NameplatePlacement.Place(projected.x, projected.y, projected.z, Screen.width, Screen.height,
                    128f * canvas.scaleFactor, 36f * canvas.scaleFactor);
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, new Vector2(point.X, point.Y), null, out local);
                label.Rect.anchoredPosition = local;
                string name = string.IsNullOrEmpty(player.username) ? "队友 " + pair.Key : player.username.Replace('\n', ' ').Replace('\r', ' ');
                int distance = Mathf.RoundToInt(Vector3.Distance(PlayerMovement.Instance ? PlayerMovement.Instance.transform.position : camera.transform.position, player.transform.position));
                string message = (point.Edge ? point.Arrow + " " : "") + name + "\n" + distance + " m" + (player.dead ? " · 倒地" : "");
                if (message != label.Last || label.Text.text != message)
                {
                    // SetCharArray preserves Steam names exactly instead of translating English substrings.
                    label.Text.SetCharArray(message.ToCharArray());
                    if (applyFont != null) applyFont.Invoke(null, new object[] { label.Text, message });
                    label.Last = message;
                }
                label.Root.SetActive(true);
            }
            var gone = new List<int>();
            foreach (var pair in labels) if (!GameManager.players.ContainsKey(pair.Key)) gone.Add(pair.Key);
            foreach (int id in gone) { UnityEngine.Object.Destroy(labels[id].Root); labels.Remove(id); }
        }
        public static void Reset()
        {
            foreach (var label in labels.Values) if (label.Root) UnityEngine.Object.Destroy(label.Root);
            labels.Clear(); if (canvas) canvas.enabled = false;
        }
    }
}
