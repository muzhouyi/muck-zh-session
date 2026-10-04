# 从源码构建

使用仓库源码（普通玩家请用 Release 安装包）、自己的 Muck 安装和 PowerShell 7（需提供 Roslyn 程序集）。游戏的 Managed 程序集不随本项目分发。

```powershell
.\source\Build-Plugin.ps1 -GameDir '你的 Muck 游戏目录'
.\session-source\Build-Session.ps1 -GameDir '你的 Muck 游戏目录' -RoslynDir $PSHOME
```

汉化输出为 `source/UU9.Muck.Translater.rebuilt.dll`；存档输出为 `session-source/revision/MuckSaveGame.dll`，编译时嵌入 `MuckSaveGameAssets`。构建存档插件前需在自己的游戏安装好 BepInEx 5。修改后运行 `tests/Test-Translation.ps1`、`session-source/Test-Core.ps1`、`session-source/Test-Convenience.ps1` 和 `tests/Test-Installer.ps1 -ReferenceGameDir '你的 Muck 游戏目录'`，更新清单后再分发。

`SessionSmoke.cs` 仅供隔离开发测试，会改变测试世界，不随正常安装加载。

`FeatureSmoke.cs` 是隔离游戏检查，覆盖导航、地图路标、真实保存、容器竞态与死亡保留；测试插件不随玩家包发布。只在单独的游戏副本/临时世界运行，勿装到正式存档环境。
