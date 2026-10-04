using BepInEx;
using HarmonyLib;
using MuckSaveGame;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
[BepInPlugin("local.muck.tooltip.toggle", "Resource catalog", "1.0.0")]
[BepInDependency("MuckSaveGame.MichMcb")]
public class TooltipToggleSmoke : BaseUnityPlugin
{
    int phase; float deadline; string result;
    void Awake()
    {
        var h=new Harmony("local.muck.tooltip.toggle"); var skip=new HarmonyMethod(typeof(TooltipToggleSmoke).GetMethod("Skip"));
        h.Patch(AccessTools.Method(typeof(SaveManager),"Save"),skip);
        foreach(var m in typeof(AchievementManager).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
            if(m.ReturnType==typeof(void)&&m.Name!="Awake"&&m.Name!="Start"&&m.Name!="AchievementChanged")h.Patch(m,skip);
        result=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tooltip-toggle-result.txt");File.WriteAllText(result,"START\n");deadline=Time.realtimeSinceStartup+120;
    }
    public static bool Skip(){return false;}
    void Update()
    {
        try
        {
            if(Time.realtimeSinceStartup>deadline)throw new Exception("Timeout "+phase);
            var menu=UnityEngine.Object.FindObjectOfType<MenuUI>();
            if(phase==0&&menu&&SteamManager.Instance&&LocalClient.instance){menu.StartLobby();phase=1;}
            else if(phase==1&&menu&&SteamManager.Instance.currentLobby.Id.Value!=0&&Server.clients.ContainsKey(0)&&Server.clients[0].player!=null&&menu.lobbyUi.activeInHierarchy)
            {SteamManager.Instance.currentLobby.SetPrivate();LobbySettings.Instance.seed.text="-20301004";UIManager.useAutoSave=false;menu.StartGame();phase=2;}
            else if(phase==2&&MuckSaveGame.World.doSave&&Navigation.WorldReady&&PlayerMovement.Instance)
            {Catalog();File.AppendAllText(result,"DONE\n");UnityEngine.Object.FindObjectOfType<GameManager>().LeaveGame();Application.Quit();phase=3;}
        }
        catch(Exception e){File.AppendAllText(result,"FAILED "+e+"\n");SessionControl.Reset();Application.Quit();phase=3;}
    }
    void Check(bool condition,string label){if(!condition)throw new Exception(label);File.AppendAllText(result,"PASS: "+label+"\n");}
    void Catalog()
    {
        var settings=Resources.FindObjectsOfTypeAll<Settings>().First(x=>x.gameObject.scene.IsValid());
        AccessTools.Method(typeof(Settings),"Start").Invoke(settings,null);
        var parent=settings.tutorial.transform.parent;
        var row=parent.Find("MuckSession.ItemStatsToggle");Check(row,"settings Start creates the item stats toggle");
        for(Transform t=settings.tutorial.transform.parent;t;t=t.parent)t.gameObject.SetActive(true);
        var label=row.GetComponentInChildren<TMPro.TextMeshProUGUI>();label.ForceMeshUpdate(true);
        Check(label.GetParsedText()=="物品数值提示","toggle has its Chinese settings label: "+label.GetParsedText()+" / raw="+label.text);
        var setting=row.GetComponentInChildren<MyBoolSetting>();var button=row.GetComponentInChildren<UnityEngine.UI.Button>();
        Check(setting.onClick.GetPersistentEventCount()==0,"cloned toggle does not retain tutorial setting callbacks");
        Check(ItemStatsTooltip.Enabled&&setting.checkMark.activeSelf,"existing users keep stats enabled by default");
        bool tutorialOriginal=settings.tutorial.checkMark.activeSelf;
        var cell=InventoryUI.Instance.cells[0];var old=cell.currentItem;
        InventoryUI.Instance.transform.parent.gameObject.SetActive(true);ItemInfo.Instance.gameObject.SetActive(true);
        var text=(TMPro.TextMeshProUGUI)AccessTools.Field(typeof(ItemInfo),"text").GetValue(ItemInfo.Instance);
        var ev=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
        var apple=ItemManager.Instance.GetItemByName("Red Apple");cell.currentItem=apple;cell.OnPointerEnter(ev);text.ForceMeshUpdate(true);
        Check(text.GetParsedText().Contains("血量 +5"),"enabled food hover displays recovery rows");
        button.onClick.Invoke();text.ForceMeshUpdate(true);
        Check(!ItemStatsTooltip.Enabled&&!setting.checkMark.activeSelf,"click disables the feature and clears its checkmark");
        Check(!text.GetParsedText().Contains("血量")&&text.GetParsedText().Contains("红苹果"),"disable immediately removes stats while keeping the currently hovered item name");
        var cfg=BepInEx.Bootstrap.Chainloader.PluginInfos["MuckSaveGame.MichMcb"].Instance.Config;
        Check(File.ReadAllText(cfg.ConfigFilePath).Contains("ShowItemStats = false"),"disabled choice is persisted to local config");
        var restored=new BepInEx.Configuration.ConfigFile(cfg.ConfigFilePath,true);ItemStatsTooltip.Initialize(restored);
        Check(!ItemStatsTooltip.Enabled,"a new config instance restores the disabled preference");
        var sword=ItemManager.Instance.GetItemByName("Steel Sword");cell.currentItem=sword;cell.OnPointerEnter(ev);text.ForceMeshUpdate(true);
        Check(!text.GetParsedText().Contains("基础伤害")&&text.GetParsedText().Contains("钢剑"),"disabled sword hover keeps vanilla description without added damage");
        button.onClick.Invoke();text.ForceMeshUpdate(true);
        Check(ItemStatsTooltip.Enabled&&setting.checkMark.activeSelf&&text.GetParsedText().Contains("基础伤害 25"),"click re-enables stats immediately for the hovered sword");
        Check(File.ReadAllText(cfg.ConfigFilePath).Contains("ShowItemStats = true"),"enabled choice is persisted");
        cell.currentItem=apple;cell.OnPointerEnter(ev);text.ForceMeshUpdate(true);
        Check(text.GetParsedText().Contains("血量 +5\n饱食度 +15\n体力 +5"),"re-enabled food retains all three separate stat rows");
        cell.OnPointerExit(ev);text.ForceMeshUpdate(true);Check(text.GetParsedText()=="","exit still clears tooltip after switching options");cell.currentItem=old;
        typeof(ItemStatsTooltip).GetMethod("AddSetting",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{settings});
        Check(parent.Cast<Transform>().Count(x=>x.name=="MuckSession.ItemStatsToggle")==1,"repeated settings setup does not duplicate the toggle");
        Check(settings.tutorial.checkMark.activeSelf==tutorialOriginal,"item stats toggle does not change tutorial preference");
        CaptureSettings(settings);
    }
    void CaptureSettings(Settings settings)
    {
        for(Transform t=settings.tutorial.transform.parent;t;t=t.parent)t.gameObject.SetActive(true);
        foreach(var text in settings.GetComponentsInChildren<TMPro.TextMeshProUGUI>())text.ForceMeshUpdate(true);
        Canvas.ForceUpdateCanvases();
        var camera=MoveCamera.Instance.mainCam;
        var canvases=Resources.FindObjectsOfTypeAll<Canvas>().Where(c=>c.gameObject.scene.IsValid()&&c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;}
        var rt=new RenderTexture(Screen.width,Screen.height,24);var old=camera.targetTexture;camera.targetTexture=rt;Canvas.ForceUpdateCanvases();camera.Render();
        var active=RenderTexture.active;RenderTexture.active=rt;var pixels=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);pixels.Apply();
        File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tooltip-setting.png"),pixels.EncodeToPNG());
        RenderTexture.active=active;camera.targetTexture=old;foreach(var c in canvases)c.renderMode=RenderMode.ScreenSpaceOverlay;
        UnityEngine.Object.Destroy(pixels);rt.Release();UnityEngine.Object.Destroy(rt);
    }
}
