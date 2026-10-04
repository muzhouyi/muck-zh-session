# Muck 汉化、存档与联机暂停

给 Windows Steam Muck 增加中文界面、保存继续、房主暂停，以及队友名字与方向提示。最新项目版本 **v1.2**（存档插件 0.9.4）。

## 下载哪个文件

打开 [最新版下载页](https://github.com/muzhouyi/muck-zh-session/releases/latest)，展开 **Assets**：

- **第一次安装**：[Muck-v1.2-install.zip](https://github.com/muzhouyi/muck-zh-session/releases/download/v1.2/Muck-v1.2-install.zip)，包含所需模组与加载器。
- **已经装过本套补丁**：[Muck-v1.2-update.zip](https://github.com/muzhouyi/muck-zh-session/releases/download/v1.2/Muck-v1.2-update.zip)，只更新存档暂停模组。
- `.sha256` 是校验文件；**Source code** 是开发源码，普通玩家不用下载。

## 怎么安装

1. 房主和朋友都正常退出 Muck，完整解压 ZIP。
2. 首次安装双击 **安装.cmd**；自动寻找游戏，找不到时选择含 Muck.exe 的游戏目录。看到“安装完成”后，从 Steam 启动。
3. 小更新包：在 Steam 库右键 Muck → 管理 → 浏览本地文件，把旧 `BepInEx/plugins/MuckSaveGame.dll` 复制到桌面备份，再用包里的同名文件替换。不要改动 Saves 和汉化文件，备份不要放在 plugins 中。
4. **双方都安装相同版本、重启游戏，再重新建房。**

无需 Agent、手动编译或 PowerShell 7。完整包会备份修改文件并保留无关模组。适用 Windows x64 Steam Muck 1.3 / Build 7077400；游戏版本或已有模组冲突时会停止。

## 游戏里怎么用

| 按键 | 功能 |
| --- | --- |
| F5 / F6 | 刷新汉化 / 切换中英文 |
| F7 | 房主保存；等“存档完成”后再退出 |
| F8 | 房主暂停整局，再按继续 |
| T | 开关队友名字、距离和方向；聊天输入时不触发 |

下次由**原房主、原账号**建房 → 原队友加入 → 在大厅点“读取存档” → 选存档 → 开始。难度、模式和一天长度保持一致。更多步骤、恢复方法与常见问题见[玩家使用说明](使用说明.md)，下载包内也有可直接打开的“使用说明.html”。

## 更新与限制

v1.1 修复加入房间后误判队友模组；v1.2 改进持续联机确认并加入队友提示。[完整更新记录](CHANGELOG.md)。尚未完成真实双机验证；读档会刷新已采集的树木和矿石，锅炉和掉落物仍需核对。先用小进度验证保存、读取与暂停。

## 引用与修改


| 上游 / 作者 | 本项目使用及修改 |
| --- | --- |
| [Muck.Translater · UU9i](https://github.com/UU9i/Muck.Translater) | 汉化基础；补充词典、动态提示与字体处理，保留作者标识。 |
| [MuckSaveGame · MichMcb](https://thunderstore.io/c/muck/p/MichMcb/MuckSaveGame/) / [MuckMods 源码](https://github.com/Michmcb/MuckMods) | 基于 0.9.1，加入联机暂停、F7 保存、队友数据就绪检查、请求编号及原子写入；上游沿自 flarfo 的 SaveUtility，MIT 许可。 |
| [BepInExPack Muck](https://thunderstore.io/c/muck/p/BepInEx/BepInExPack_Muck/) / [BepInEx 5.4.11](https://github.com/BepInEx/BepInEx/tree/v5.4.11) | 模组加载器与依赖，随安装包提供。 |
| [UnityDoorstop](https://github.com/NeighTools/UnityDoorstop/tree/v3.3.1.0)、[HarmonyX](https://github.com/BepInEx/HarmonyX)、[Mono.Cecil](https://github.com/jbevain/cecil)、[MonoMod](https://github.com/MonoMod/MonoMod) | 加载、补丁和程序集处理组件；保留各自许可证。 |

第三方版权、许可证和原说明保存在 [upstream](upstream)。UU9i 上游未附独立 LICENSE，不将汉化部分标注为 MIT；本仓库也不以单一许可证覆盖所有组件。本项目是个人修订整合包，非游戏或上游作者的官方发布。

