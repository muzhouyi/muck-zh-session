using BepInEx;
using HarmonyLib;
using MuckSaveGame;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
[BepInPlugin("local.muck.catalog.smoke", "Resource catalog", "1.0.0")]
[BepInDependency("MuckSaveGame.MichMcb")]
public class ResourceCatalogSmoke : BaseUnityPlugin
{
    int phase; float deadline; string result;
    void Awake()
    {
        var h=new Harmony("local.muck.catalog.smoke"); var skip=new HarmonyMethod(typeof(ResourceCatalogSmoke).GetMethod("Skip"));
        h.Patch(AccessTools.Method(typeof(SaveManager),"Save"),skip);
        foreach(var m in typeof(AchievementManager).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
            if(m.ReturnType==typeof(void)&&m.Name!="Awake"&&m.Name!="Start"&&m.Name!="AchievementChanged")h.Patch(m,skip);
        result=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"resource-catalog.txt");File.WriteAllText(result,"START\n");deadline=Time.realtimeSinceStartup+120;
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
    void Catalog()
    {
        foreach(var i in ItemManager.Instance.allItems.Values.OrderBy(i=>i.id))File.AppendAllText(result,"ITEM "+i.id+" | "+i.name+" | asset="+((UnityEngine.Object)i).name+"\n");
        var groups=Resources.FindObjectsOfTypeAll<HitableResource>().Where(r=>r is HitableTree||r is HitableRock).GroupBy(r=>Describe(r));
        foreach(var g in groups)File.AppendAllText(result,"RESOURCE count="+g.Count()+" | "+g.Key+"\n");
    }
    static string Item(InventoryItem i){return i?i.id+":"+i.name:"null";}
    static string Describe(HitableResource r)
    {
        return r.GetType().Name+" | "+r.gameObject.name+" | entity="+r.entityName+" | scene="+r.gameObject.scene.IsValid()+" | hp="+r.hp+"/"+r.maxHp+" | active="+r.gameObject.activeInHierarchy+
        " | drop="+Item(r.dropItem)+" | extras="+string.Join(",",(r.dropExtra??new InventoryItem[0]).Select(Item).ToArray())+
        " | chance="+string.Join(",",(r.dropChance??new float[0]).Select(x=>x.ToString()).ToArray())+
        " | loot="+(r.dropTable?string.Join(",",r.dropTable.loot.Select(x=>Item(x.item)+"@"+x.dropChance+" amount="+x.amountMin+".."+x.amountMax).ToArray())+" one="+r.dropTable.dropOne:"null")+
        " | mesh="+string.Join(",",r.GetComponentsInChildren<MeshFilter>(true).Select(x=>x.sharedMesh?x.sharedMesh.name:"null").ToArray())+
        " | material="+string.Join(",",r.GetComponentsInChildren<Renderer>(true).SelectMany(x=>x.sharedMaterials).Where(x=>x).Select(x=>x.name).Distinct().ToArray());
    }
}
