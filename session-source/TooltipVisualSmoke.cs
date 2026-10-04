using BepInEx;
using HarmonyLib;
using MuckSaveGame;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
[BepInPlugin("local.muck.tooltip.visual", "Resource catalog", "1.0.0")]
[BepInDependency("MuckSaveGame.MichMcb")]
public class TooltipVisualSmoke : BaseUnityPlugin
{
    int phase; float deadline, visualTime; string result;
    void Awake()
    {
        var h=new Harmony("local.muck.tooltip.visual"); var skip=new HarmonyMethod(typeof(TooltipVisualSmoke).GetMethod("Skip"));
        h.Patch(AccessTools.Method(typeof(SaveManager),"Save"),skip);
        foreach(var m in typeof(AchievementManager).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
            if(m.ReturnType==typeof(void)&&m.Name!="Awake"&&m.Name!="Start"&&m.Name!="AchievementChanged")h.Patch(m,skip);
        result=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tooltip-visual-result.txt");File.WriteAllText(result,"START\n");deadline=Time.realtimeSinceStartup+120;
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
            {Prepare("Red Apple");visualTime=Time.realtimeSinceStartup+0.4f;phase=3;}
            else if(phase==3&&Time.realtimeSinceStartup>=visualTime){Capture("tooltip-food.png");Prepare("Steel Sword");visualTime=Time.realtimeSinceStartup+0.4f;phase=4;}
            else if(phase==4&&Time.realtimeSinceStartup>=visualTime){Capture("tooltip-sword.png");File.AppendAllText(result,"DONE\n");UnityEngine.Object.FindObjectOfType<GameManager>().LeaveGame();Application.Quit();phase=5;}
        }
        catch(Exception e){File.AppendAllText(result,"FAILED "+e+"\n");SessionControl.Reset();Application.Quit();phase=3;}
    }
    void Prepare(string name)
    {
        InventoryUI.Instance.transform.parent.gameObject.SetActive(true);ItemInfo.Instance.gameObject.SetActive(true);
        var cell=InventoryUI.Instance.cells[0];cell.currentItem=ItemManager.Instance.GetItemByName(name);cell.UpdateCell();
        cell.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));
    }
    void Capture(string name)
    {
        var info=ItemInfo.Instance;var image=(UnityEngine.UI.RawImage)AccessTools.Field(typeof(ItemInfo),"image").GetValue(info);
        var fit=AccessTools.Method(typeof(ItemInfo),"FitToText");var clamp=typeof(ItemStatsTooltip).GetMethod("KeepOnScreen",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(var pos in new[]{Vector3.zero,new Vector3(Screen.width,Screen.height,0)})
        {
            info.transform.position=pos;fit.Invoke(info,null);clamp.Invoke(null,new object[]{info});var corners=new Vector3[4];image.rectTransform.GetWorldCorners(corners);
            if(corners[0].x<7.9f||corners[0].y<7.9f||corners[2].x>Screen.width-7.9f||corners[2].y>Screen.height-7.9f)throw new Exception("Tooltip exceeds screen at "+pos);
            File.AppendAllText(result,"PASS edge bounds "+name+" at "+pos+"\n");
        }
        info.transform.position=new Vector3(620,430,0);fit.Invoke(info,null);
        var camera=MoveCamera.Instance.mainCam;
        var canvases=Resources.FindObjectsOfTypeAll<Canvas>().Where(c=>c.gameObject.scene.IsValid()&&c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        foreach(var c in canvases){c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;}
        var rt=new RenderTexture(Screen.width,Screen.height,24);var old=camera.targetTexture;camera.targetTexture=rt;Canvas.ForceUpdateCanvases();camera.Render();
        var active=RenderTexture.active;RenderTexture.active=rt;var pixels=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);pixels.Apply();
        File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name),pixels.EncodeToPNG());
        RenderTexture.active=active;camera.targetTexture=old;foreach(var c in canvases)c.renderMode=RenderMode.ScreenSpaceOverlay;
        UnityEngine.Object.Destroy(pixels);rt.Release();UnityEngine.Object.Destroy(rt);
    }
}
