using System;
namespace BepInEx { public static class Paths { public static string ConfigPath; } }
namespace BepInEx.Logging { public class ManualLogSource { public void LogInfo(object x) { Console.WriteLine(x); } public void LogError(object x) {throw new Exception(x.ToString());} } }
namespace UU9.Muck.Translater { public static class FontManager { public static void AddCharactersToFont(string x){} } }
public static class TranslationRegression {
    static int count;
    static void Check(string source, string expected) {
        string value;
        if (!UU9.Muck.Translater.TranslationManager.TryGetTranslation(source, out value) || value != expected)
            throw new Exception(source + " => " + value + "; expected " + expected);
        string original;
        if (!UU9.Muck.Translater.TranslationManager.TryGetOriginal(value, out original) || original != source)
            throw new Exception("English restore failed: " + source);
        string repeated;
        if (UU9.Muck.Translater.TranslationManager.TryGetTranslation(value, out repeated))
            throw new Exception("Output translated a second time: " + value);
        count++;
    }
    public static void Run(string configPath) { BepInEx.Paths.ConfigPath = configPath;
        UU9.Muck.Translater.TranslationManager.Initialize(new BepInEx.Logging.ManualLogSource());
        Check("Put rock on hotbar", "将石头放到快捷栏");
        Check("Find and pick up a rock", "找到并拾取一块石头");
        Check("Open inventory [Tab]", "打开物品栏 [Tab]");
        Check("Slap a tree", "用石头砍树");
        Check("Open inventory and craft a workbench", "打开物品栏，制作工作台");
        Check("Place the workbench with [Mouse1]", "按 [Mouse1] 放置工作台");
        Check("Can revive Alice in 12 seconds", "12 秒后可复活 Alice");
        Check("Hold F to revive", "长按 F 键复活");
        Check("Press E to trade with Smith", "按 E 键与 铁匠 交易");
        Check("-DAY 5-", "-第 5 天-");
        Check("<color=orange>Alice</color> won!", "<color=orange>Alice</color> 获胜！");
        Check("<size=50%>(Press \"F\" to open", "<size=50%>(按 F 键打开)");
        Check("<i><color=blue>Full set gives 60% increased attack length</i>", "<i><color=blue>集齐全套后，攻击距离增加 60%</i>");
        Check("5 Gold\n<size=75%>open chest", "5 金币\n<size=75%>开启箱子");
        Check("Inventory full\nPlace the workbench with [Mouse1]", "物品栏已满\n按 [Mouse1] 放置工作台");
        Check("A powerful source of energy", "强大的能量源");
        Check("I'm gonna need a blade for this", "还需要一片剑刃");
        Check("Put 岩石 开启 hotbar", "将石头放到快捷栏");
        Console.WriteLine("PASS: " + count + " translation, markup, key binding and English restore checks.");
    }
}

