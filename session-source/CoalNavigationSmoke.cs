using BepInEx;
using HarmonyLib;
using MuckSaveGame;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
[BepInPlugin("local.muck.coal.smoke", "Coal navigation regression", "1.0.0")]
[BepInDependency("MuckSaveGame.MichMcb")]
public class CoalNavigationSmoke : BaseUnityPlugin
{
    private int phase;
    private float deadline;
    private string result;
    private void Awake()
    {
        var harmony=new Harmony("local.muck.coal.smoke");
        var skip=new HarmonyMethod(typeof(CoalNavigationSmoke).GetMethod("Skip"));
        harmony.Patch(AccessTools.Method(typeof(SaveManager),"Save"),skip);
        foreach(var m in typeof(AchievementManager).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
            if(m.ReturnType==typeof(void)&&m.Name!="Awake"&&m.Name!="Start"&&m.Name!="AchievementChanged") harmony.Patch(m,skip);
        result=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"coal-result.txt");File.WriteAllText(result,"START\n");deadline=Time.realtimeSinceStartup+120;
    }
    public static bool Skip(){return false;}
    private void Check(bool value,string message){if(!value)throw new Exception(message);File.AppendAllText(result,"PASS: "+message+"\n");}
    private static object Field(object o,string name){return o.GetType().GetField(name).GetValue(o);}
    private static object Nav(string name,params object[] args){return typeof(Navigation).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}
    private void Update()
    {
        try
        {
            if(Time.realtimeSinceStartup>deadline)throw new Exception("Timeout phase "+phase);
            var menu=UnityEngine.Object.FindObjectOfType<MenuUI>();
            if(phase==0&&menu&&SteamManager.Instance&&LocalClient.instance){menu.StartLobby();phase=1;}
            else if(phase==1&&menu&&SteamManager.Instance.currentLobby.Id.Value!=0&&Server.clients.ContainsKey(0)&&Server.clients[0].player!=null&&menu.lobbyUi.activeInHierarchy)
            {SteamManager.Instance.currentLobby.SetPrivate();LobbySettings.Instance.seed.text="-20301004";UIManager.useAutoSave=false;menu.StartGame();phase=2;}
            else if(phase==2&&MuckSaveGame.World.doSave&&Navigation.WorldReady&&PlayerMovement.Instance)
            {Test();File.AppendAllText(result,"ALL PASSED\n");UnityEngine.Object.FindObjectOfType<GameManager>().LeaveGame();Application.Quit();phase=3;}
        }
        catch(Exception e){File.AppendAllText(result,"FAILED: "+e+"\n");SessionControl.Reset();Application.Quit();phase=3;}
    }
    private HitableRock Fixture(string name,Vector3 position,InventoryItem item,InventoryItem extra)
    {
        var go=new GameObject(name);go.SetActive(false);go.transform.position=position;
        var r=go.AddComponent<HitableRock>();r.maxHp=100;r.dropItem=item;r.dropTable=ScriptableObject.CreateInstance<LootDrop>();r.dropTable.loot=new[]{new LootDrop.LootItems{item=item,dropChance=1,amountMin=1,amountMax=1}};
        if(extra){r.dropExtra=new[]{extra};r.dropChance=new[]{1f};r.dropTable.loot=r.dropTable.loot.Concat(new[]{new LootDrop.LootItems{item=extra,dropChance=0.5f,amountMin=1,amountMax=1}}).ToArray();}
        return r;
    }
    private void Test()
    {
        Nav("ScanResources");var list=(IList)typeof(Navigation).GetField("targets",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        var coal=ItemManager.Instance.GetItemByName("Coal");var rockItem=ItemManager.Instance.GetItemByName("Rock");string key="item:"+coal.id;
        foreach(var group in list.Cast<object>().Where(t=>(string)Field(t,"Key")==key).GroupBy(t=>{var r=(HitableResource)Field(t,"Resource");return r ? r.gameObject.name+" / "+r.entityName+" / primary="+r.dropItem.name+" / extra="+Field(t,"Extra") : "Ground coal pickup";}))
            Logger.LogInfo("COAL SOURCE: "+group.Key+" count="+group.Count());
        var origin=PlayerMovement.Instance.transform.position;
        var chance=Fixture("Coal test incidental rock",origin,rockItem,coal);
        var far=Fixture("Coal test dedicated 10m",origin+Vector3.right*10,coal,null);
        var near=Fixture("Coal test dedicated 5m",origin+Vector3.right*5,coal,null);
        Nav("ScanResources");var all=list.Cast<object>().ToArray();
        var three=all.Where(t=>ReferenceEquals(Field(t,"Resource"),chance)&&Field(t,"Key").ToString()==key||ReferenceEquals(Field(t,"Resource"),far)||ReferenceEquals(Field(t,"Resource"),near)).ToArray();
        list.Clear();foreach(var t in three)list.Add(t);
        var target=Nav("Nearest",key);
        Check(ReferenceEquals(Field(target,"Resource"),near)&&!(bool)Field(target,"Extra"),"5m coal deposit wins over 0m incidental rock");
        PlayerMovement.Instance.transform.position=origin+Vector3.right*9;
        target=Nav("Nearest",key);Check(ReferenceEquals(Field(target,"Resource"),far),"moving selects the closest dedicated deposit");PlayerMovement.Instance.transform.position=origin;
        Nav("TrackAwake",near);near.hp=0;target=Nav("Nearest",key);
        Check(ReferenceEquals(Field(target,"Resource"),far),"depleting nearest deposit selects another deposit before the rock");
        Nav("TrackAwake",far);far.hp=0;target=Nav("Nearest",key);
        Check(ReferenceEquals(Field(target,"Resource"),chance)&&(bool)Field(target,"Extra"),"incidental rock is used only when no deposit remains");
        var selected=(List<string>)typeof(Navigation).GetField("selected",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);selected.Clear();selected.Add(key);Nav("RefreshTargets");
        var pins=(IList)typeof(Navigation).GetField("pins",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        Check(pins.Cast<object>().Any(p=>Field(p,"Title").ToString().Contains("概率掉落")),"rock fallback is explicitly labelled probability drop");
        Nav("TrackAwake",chance);chance.hp=0;Check(Nav("Nearest",key)==null,"no deposit or incidental source returns no target");
        Nav("RefreshTargets");Check(pins.Cast<object>().All(p=>Field(p,"Position")==null),"missing resource clears the previous marker");
        var pickupObject=new GameObject("Coal pickup fixture");pickupObject.SetActive(false);pickupObject.transform.position=origin+Vector3.right*3;
        var pickup=pickupObject.AddComponent<PickupInteract>();pickup.item=coal;pickup.amount=3;chance.hp=100;
        Nav("ScanResources");var pickupTargets=list.Cast<object>().Where(t=>ReferenceEquals(Field(t,"Pickup"),pickup)||ReferenceEquals(Field(t,"Resource"),chance)&&Field(t,"Key").ToString()==key).ToArray();list.Clear();foreach(var t in pickupTargets)list.Add(t);
        target=Nav("Nearest",key);Check(ReferenceEquals(Field(target,"Pickup"),pickup)&&!(bool)Field(target,"Extra"),"ready-to-pick coal wins over a closer random-drop rock");
        Nav("RefreshTargets");Check(pins.Cast<object>().Any(p=>Field(p,"Title").ToString().Contains("可拾取")),"ground coal is labelled as ready to pick up");
        pickup.amount=0;target=Nav("Nearest",key);Check(ReferenceEquals(Field(target,"Resource"),chance),"an exhausted pickup restores the clearly labelled fallback");
        UnityEngine.Object.Destroy(pickupObject);
        list.Clear();foreach(var t in all)list.Add(t);selected.Clear();
        UnityEngine.Object.Destroy(chance.gameObject);UnityEngine.Object.Destroy(far.gameObject);UnityEngine.Object.Destroy(near.gameObject);
    }
}
