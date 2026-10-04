# 从源码构建

普通玩家使用 Release 中的统一安装包。仓库 Source code 不包含游戏本体或完整运行包。

开发构建需 PowerShell 7、Roslyn 编译程序集，以及自行安装的 Muck 和 BepInEx 5：

```powershell
.\source\Build-Plugin.ps1 -GameDir 'Muck游戏目录' -CompilerAssemblyDir 'Roslyn程序集目录'
.\session-source\Build-Session.ps1 -GameDir 'Muck游戏目录' -RoslynDir 'Roslyn程序集目录'
```

存档插件输出 `session-source/revision/MuckSaveGame.dll`，编译时嵌入 `MuckSaveGameAssets`。游戏 Managed 程序集由本地游戏提供，不随仓库分发。

逻辑检查：`session-source/Test-Core.ps1`、`session-source/Test-Convenience.ps1`、`session-source/Test-Navigation14.ps1`。安装恢复检查在解压的玩家安装包上运行：

```powershell
.	ests\Test-Installer.ps1 -ReferenceGameDir 'Muck游戏目录' -PackageDir '玩家安装包解压目录'
```

`*Smoke.cs` 为开发用隔离游戏检查，不随玩家包加载。只在独立游戏副本和临时世界运行，不装入正式存档环境；较旧 Smoke 对应其发布时版本。
