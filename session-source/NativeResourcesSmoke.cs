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
[BepInPlugin("local.muck.resources.smoke", "Native resource navigation regression", "1.0.0")]
[BepInDependency("MuckSaveGame.MichMcb")]
public class NativeResourcesSmoke : BaseUnityPlugin
{
    private int phase;
    private float deadline;
    private string result;
    private void Awake()
    {
        var harmony=new Harmony("local.muck.resources.smoke");
        var skip=new HarmonyMethod(typeof(NativeResourcesSmoke).GetMethod("Skip"));
        harmony.Patch(AccessTools.Method(typeof(SaveManager),"Save"),skip);
        foreach(var m in typeof(AchievementManager).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
            if(m.ReturnType==typeof(void)&&m.Name!="Awake"&&m.Name!="Start"&&m.Name!="AchievementChanged") harmony.Patch(m,skip);
        result=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native-resources-result.txt");File.WriteAllText(result,"START\n");deadline=Time.realtimeSinceStartup+120;
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
            {NativeTest();Test();File.AppendAllText(result,"ALL PASSED\n");UnityEngine.Object.FindObjectOfType<GameManager>().LeaveGame();Application.Quit();phase=3;}
        }
        catch(Exception e){File.AppendAllText(result,"FAILED: "+e+"\n");SessionControl.Reset();Application.Quit();phase=3;}
    }
    private void NativeTest()
    {
        Nav("ScanResources");
        var list=(IList)typeof(Navigation).GetField("targets",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        var all=list.Cast<object>().ToArray();
        var coal=ItemManager.Instance.GetItemByName("Coal");var iron=ItemManager.Instance.GetItemByName("Iron Ore");
        var dark=ItemManager.Instance.GetItemByName("Dark Oak Wood");var oak=ItemManager.Instance.GetItemByName("Oak Wood");
        string ck="item:"+coal.id,ik="item:"+iron.id,dk="item:"+dark.id,ok="item:"+oak.id;
        var deposits=Resources.FindObjectsOfTypeAll<HitableRock>().Where(r=>r.gameObject.scene.IsValid()&&r.entityName=="Coal").ToArray();
        var ironNodes=Resources.FindObjectsOfTypeAll<HitableRock>().Where(r=>r.gameObject.scene.IsValid()&&r.entityName=="Iron").ToArray();
        var darkTrees=Resources.FindObjectsOfTypeAll<HitableTree>().Where(r=>r.gameObject.scene.IsValid()&&r.entityName=="Dark oak").ToArray();
        Check(deposits.Length>0&&ironNodes.Length>0&&darkTrees.Length>0,"real generated map contains coal, iron and dark oak nodes");
        Check(deposits.All(r=>r.dropItem.id==iron.id)&&darkTrees.All(r=>r.dropItem.id==oak.id),"native prefab legacy fields reproduce both reported identity bugs");
        Check(deposits.All(r=>all.Any(t=>ReferenceEquals(Field(t,"Resource"),r)&&(string)Field(t,"Key")==ck&&!(bool)Field(t,"Extra"))),"all native coal stones are indexed as guaranteed coal");
        Check(all.Where(t=>(string)Field(t,"Key")==ik).All(t=>!deposits.Contains(Field(t,"Resource") as HitableRock)),"iron navigation excludes native coal stones");
        Check(ironNodes.All(r=>all.Any(t=>ReferenceEquals(Field(t,"Resource"),r)&&(string)Field(t,"Key")==ik&&!(bool)Field(t,"Extra"))),"all native iron stones retain iron navigation");
        Check(darkTrees.All(r=>all.Any(t=>ReferenceEquals(Field(t,"Resource"),r)&&(string)Field(t,"Key")==dk&&!(bool)Field(t,"Extra"))),"all native dark oak trees are indexed as dark oak wood");
        Check(all.Where(t=>(string)Field(t,"Key")==ok).All(t=>!darkTrees.Contains(Field(t,"Resource") as HitableTree)),"oak navigation excludes dark oak trees");
        var origin=PlayerMovement.Instance.transform.position;PlayerMovement.Instance.transform.position=deposits[0].transform.position;
        Check(ReferenceEquals(Field(Nav("Nearest",ck),"Resource"),deposits[0]),"native coal stone at player position is selected as nearest coal");
        Check(((HitableResource)Field(Nav("Nearest",ik),"Resource")).entityName=="Iron","iron still selects an iron node with coal immediately beside player");
        PlayerMovement.Instance.transform.position=darkTrees[0].transform.position;
        Check(ReferenceEquals(Field(Nav("Nearest",dk),"Resource"),darkTrees[0]),"adjacent native dark oak is found and selected");
        var selected=(List<string>)typeof(Navigation).GetField("selected",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        var pins=(IList)typeof(Navigation).GetField("pins",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        selected.Clear();selected.Add(dk);Nav("RefreshTargets");
        Check(pins.Cast<object>().Any(p=>Field(p,"Position")!=null&&Field(p,"Title").ToString().Contains("深色橡木")),"dark oak marker has a position and the correct Chinese label");
        selected.Clear();selected.Add(ck);Nav("RefreshTargets");
        Check(pins.Cast<object>().Any(p=>Field(p,"Title").ToString().Contains("煤炭石")),"coal stone marker explicitly identifies the stone source");
        var go=new GameObject("Native coal pickup test");go.SetActive(false);go.transform.position=deposits[0].transform.position+Vector3.right*2;
        var pickup=go.AddComponent<PickupInteract>();pickup.item=coal;pickup.amount=2;
        Nav("ScanResources");PlayerMovement.Instance.transform.position=pickup.transform.position;
        Check(ReferenceEquals(Field(Nav("Nearest",ck),"Pickup"),pickup),"nearer coal pickup wins over a native coal stone");
        PlayerMovement.Instance.transform.position=deposits[0].transform.position;
        Check(ReferenceEquals(Field(Nav("Nearest",ck),"Resource"),deposits[0]),"nearer native coal stone wins over a coal pickup");
        pickup.amount=0;
        PlayerMovement.Instance.transform.position=origin;selected.Clear();UnityEngine.Object.Destroy(go);
        var stone=Fixture("Loot table regression",origin,iron,coal);
        stone.dropTable.loot=new[]{new LootDrop.LootItems{item=coal,dropChance=1,amountMin=1,amountMax=2}};
        Nav("ScanResources");all=list.Cast<object>().ToArray();
        Check(all.Any(t=>ReferenceEquals(Field(t,"Resource"),stone)&&(string)Field(t,"Key")==ck)&&!all.Any(t=>ReferenceEquals(Field(t,"Resource"),stone)&&(string)Field(t,"Key")==ik),"actual loot table overrides conflicting legacy primary and extras");
        stone.dropTable.dropOne=true;Nav("ScanResources");all=list.Cast<object>().ToArray();
        Check(all.Any(t=>ReferenceEquals(Field(t,"Resource"),stone)&&(string)Field(t,"Key")==ik)&&!all.Any(t=>ReferenceEquals(Field(t,"Resource"),stone)&&(string)Field(t,"Key")==ck),"dropOne uses only dropItem exactly as vanilla CheckDrop");
        stone.dropTable.dropOne=false;stone.dropTable.loot[0].dropChance=0;Nav("ScanResources");
        Check(!list.Cast<object>().Any(t=>ReferenceEquals(Field(t,"Resource"),stone)),"zero probability loot is excluded");
        stone.dropTable.loot[0].dropChance=1;stone.dropTable.loot[0].amountMax=0;Nav("ScanResources");
        Check(!list.Cast<object>().Any(t=>ReferenceEquals(Field(t,"Resource"),stone)),"zero quantity loot is excluded");
        stone.dropTable.loot[0].amountMax=2;stone.dropTable.loot[0].amountMin=0;Nav("ScanResources");
        Check(list.Cast<object>().Any(t=>ReferenceEquals(Field(t,"Resource"),stone)&&(bool)Field(t,"Extra")),"loot which may produce zero items is labelled probability source");
        stone.dropTable=null;Nav("ScanResources");
        Check(!list.Cast<object>().Any(t=>ReferenceEquals(Field(t,"Resource"),stone)),"resource without a loot table is not advertised as gatherable");
        UnityEngine.Object.Destroy(stone.gameObject);
        Logger.LogInfo("Native coverage: coal stones="+deposits.Length+", iron nodes="+ironNodes.Length+", dark oak trees="+darkTrees.Length);
    }
    private HitableRock Fixture(string name,Vector3 position,InventoryItem item,InventoryItem extra)
    {
        var go=new GameObject(name);go.SetActive(false);go.transform.position=position;
        var r=go.AddComponent<HitableRock>();r.maxHp=100;r.dropItem=item; r.dropTable=ScriptableObject.CreateInstance<LootDrop>(); r.dropTable.loot=new[]{new LootDrop.LootItems {item=item,dropChance=1f,amountMin=1,amountMax=1}};
        if(extra){r.dropExtra=new[]{extra};r.dropChance=new[]{1f};r.dropTable.loot=r.dropTable.loot.Concat(new[]{new LootDrop.LootItems {item=extra,dropChance=0.5f,amountMin=1,amountMax=1}}).ToArray();}
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
