# Muck 汉化与联机便利补丁

为 Windows Steam Muck 提供汉化、保存继续、联机暂停、回家与资源导航、队友提示、共用容器和死亡保留物品。当前版本 **v1.4.2**。

## 下载与安装

下载 [**Muck-v1.4.2-install.zip**](https://github.com/muzhouyi/muck-zh-session/releases/download/v1.4.2/Muck-v1.4.2-install.zip)，正常退出游戏，完整解压后双击 **安装.cmd**。已有本套补丁会自动更新，没有则安装，保留存档、个人配置和无关模组。

**房主与队友都安装相同版本，重启游戏后重新建房。** 仅提供统一安装包；Source code 是开发源码。适用 Windows x64 Steam Muck 1.3 / Build 7077400。

## 怎么使用

| 操作 | 功能 |
| --- | --- |
| C | 导航菜单：资源、制作目标、宝箱与交易、家 |
| H | 新增家1～家8；导航菜单可更新或删除 |
| T | 开关队友名字、距离和方向 |
| F7 / F8 | 房主保存 / 暂停；保存完成后再退出 |
| F5 / F6 | 刷新汉化 / 切换中英文 |

完整安装、读档、恢复和功能说明见[使用说明](使用说明.md)，各版变化见[更新记录](CHANGELOG.md)。已完成本机检查，真实双机联机仍待验证；读档会刷新树木和矿石。

## 引用与修改



| 上游 / 作者 | 本项目使用及修改 |
| --- | --- |
| [Muck.Translater · UU9i](https://github.com/UU9i/Muck.Translater) | 汉化基础；补充词典、动态提示与字体处理，保留作者标识。 |
| [MuckSaveGame · MichMcb](https://thunderstore.io/c/muck/p/MichMcb/MuckSaveGame/) / [MuckMods 源码](https://github.com/Michmcb/MuckMods) | 基于 0.9.1，加入联机暂停、F7 保存、队友数据就绪检查、请求编号及原子写入；上游沿自 flarfo 的 SaveUtility，MIT 许可。 |
| [BepInExPack Muck](https://thunderstore.io/c/muck/p/BepInEx/BepInExPack_Muck/) / [BepInEx 5.4.11](https://github.com/BepInEx/BepInEx/tree/v5.4.11) | 模组加载器与依赖，随安装包提供。 |
| [UnityDoorstop](https://github.com/NeighTools/UnityDoorstop/tree/v3.3.1.0)、[HarmonyX](https://github.com/BepInEx/HarmonyX)、[Mono.Cecil](https://github.com/jbevain/cecil)、[MonoMod](https://github.com/MonoMod/MonoMod) | 加载、补丁和程序集处理组件；保留各自许可证。 |

导航交互参考 [LocalMapMarkers · PigeonsMods](https://thunderstore.io/c/muck/p/PigeonsMods/LocalMapMarkers/)，独立实现，未复制或打包其插件。

第三方版权、许可证和原说明保存在 [upstream](upstream)。UU9i 上游未附独立 LICENSE，不将汉化部分标注为 MIT；本仓库也不以单一许可证覆盖所有组件。本项目是个人修订整合包，非游戏或上游作者的官方发布。

