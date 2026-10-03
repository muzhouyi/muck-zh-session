# Muck 汉化与联机存档暂停

为 Windows Steam 版 Muck 提供中文界面、保存继续和房主联机暂停。**项目发布版本：v1.0**；内部汉化插件为 1.0.2，存档插件为修订版 0.9.2。

## 功能

- 补齐教程、物品和动态提示译文；F5 刷新，F6 切换中英文。
- 房主 F7 存档；退出后由原房主建房，在大厅读取存档继续。
- 房主 F8 暂停整局，再按继续；支持每日自动存档与上一份存档备份。

## 安装

1. 在 [v1.0 Release](https://github.com/muzhouyi/muck-zh-session/releases/tag/v1.0) 下载 `Muck-zh-session-v1.0.zip`，完整解压。
2. 所有人关闭 Muck，安装同一个包。让 Agent 阅读《给Agent的交接说明.md》，或在 Windows PowerShell 中运行：

```powershell
.\Verify-Package.ps1
.\Install-MuckSession.ps1 -GameDir '你的 Muck 游戏目录' -DryRun
.\Install-MuckSession.ps1 -GameDir '你的 Muck 游戏目录'
```

安装器保留无关模组并备份文件。已验证版本为 Muck 1.3 / Steam Build 7077400；版本不符或发现冲突会停止。详细使用、恢复与构建方法见 [使用说明](README-先读我.md)和 [交接说明](给Agent的交接说明.md)。仓库中的源码不包含加载器二进制；安装和源码构建请使用完整 Release 包。

## 引用与修改

| 上游 / 作者 | 本项目使用及修改 |
| --- | --- |
| [Muck.Translater · UU9i](https://github.com/UU9i/Muck.Translater) | 汉化基础；补充词典、动态提示与字体处理，保留作者标识。 |
| [MuckSaveGame · MichMcb](https://thunderstore.io/c/muck/p/MichMcb/MuckSaveGame/) / [MuckMods 源码](https://github.com/Michmcb/MuckMods) | 基于 0.9.1，加入联机暂停、F7 保存、队友数据就绪检查、请求编号及原子写入；上游沿自 flarfo 的 SaveUtility，MIT 许可。 |
| [BepInExPack Muck](https://thunderstore.io/c/muck/p/BepInEx/BepInExPack_Muck/) / [BepInEx 5.4.11](https://github.com/BepInEx/BepInEx/tree/v5.4.11) | 模组加载器与依赖，随安装包提供。 |
| [UnityDoorstop](https://github.com/NeighTools/UnityDoorstop/tree/v3.3.1.0)、[HarmonyX](https://github.com/BepInEx/HarmonyX)、[Mono.Cecil](https://github.com/jbevain/cecil)、[MonoMod](https://github.com/MonoMod/MonoMod) | 加载、补丁和程序集处理组件；保留各自许可证。 |

第三方版权、许可证和原说明保存在 [upstream](upstream)。UU9i 上游未附独立 LICENSE，不将汉化部分标注为 MIT；本仓库也不以单一许可证覆盖所有组件。本项目是个人修订整合包，非游戏或上游作者的官方发布。

## 已验证与限制

已检查翻译、安装恢复、存档写入保护，并在本机游戏验证保存、读取和暂停。**尚未完成两台电脑的真实联机验证**；读档会刷新已采集的树木、矿石等资源，锅炉和掉落物仍需联机核对。包内不含游戏本体、游戏依赖程序集、微软字体、个人存档或账户日志。
