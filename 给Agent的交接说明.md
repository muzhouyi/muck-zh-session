# 给安装 Agent 的交接

先读 README-先读我.md，再操作。用户希望把这套汉化、保存继续和房主联机暂停安装到自己的 Muck。

## 安装流程

1. 找到本机 Steam 的 Muck 游戏目录。目录应有 Muck.exe 和 Muck_Data/Managed/Assembly-CSharp.dll。使用接手机器的实际路径。
2. 游戏如果正在运行，让用户正常退出，绝不强制结束可能正在玩的游戏。
3. 运行 Verify-Package.ps1 校验交接包。运行 Install-MuckSession.ps1 -GameDir <实际路径> -DryRun 检查范围。
4. 安装器限定 Windows x64、Mono、游戏 Assembly-CSharp.dll SHA256 为 5F9D0DC1E0E72F5493013C92D05ED9FB8628019F54F8BC02162DAD0D2F20810C。版本不同则先检查兼容，不能绕过校验盲装。
5. 旧 SaveUtility 与此包冲突，嵌套的 MuckSaveGame 会重复加载。先备份冲突插件并停止加载它们，保留用户存档；安装器发现冲突会停止。不要递归删除 BepInEx，不要改写游戏本体。
6. 正式运行 Install-MuckSession.ps1。它保留现有 BepInEx 5、全局设置和无关模组，按文件做备份；失败自动回滚。首次安装共 35 文件，复用加载器时 14 文件。恢复入口沿用 Restore-MuckChinese.ps1。
7. 从 Steam 启动游戏后，运行 Verify-MuckSession.ps1，确认两个 DLL 的散列、译文补充和启动日志。它不替代实机联机测试。
8. 告知用户：两边安装完全相同的包，重新创建房间，房主 F7 存档、F8 暂停继续。按 README 的双人核对流程测试。

## 技术状态

汉化内部版本 1.0.2；存档插件 GUID `MuckSaveGame.MichMcb`，个人版 0.9.3，基于上游 0.9.1。新增代码在 session-source/revision，MIT 授权保留；资产包保留为程序集嵌入资源，编译时必须带上。可使用游戏现有 Managed 引用、BepInEx core 和 Roslyn 编译，不要把游戏程序集加入分发包。

客户端收到服务器“存在存档”且玩家、背包、状态组件和本地玩家记录就绪后发送一次加载请求；主机等世界应用完毕再发送玩家数据，避免恢复时序丢背包。存档请求包含递增编号，五类玩家数据含相同编号，主机核对客户端 Steam 身份和编号，全部收齐后才提交；超时保留旧存档。Steam 房间成员数据键 `muck-session-version` 记录通信协议版本 0.9.2（模组版本 0.9.3），协议没有改变。关键修复：旧版只挂 SteamLobby.InitLobby，该方法仅房主调用；新版在 Plugin.Update 使用 SteamManager.Instance.currentLobby，每两秒发布标记，客户端加入后也会发布，缺失时不再直接断言未安装。重复提示有 30 秒间隔。暂停服务器包 ID 120；存档原有包 100–107 的协议已修改，必须同版本。

写入采用同目录临时文件、Flush(true)、File.Replace，并保留 `.bak`。失败清理自己创建的临时文件，退出菜单时恢复时间缩放，保存未完成时阻止普通退局。

测试结果与边界见 README。没有真实双机验证，不要向用户承诺完整的地图状态恢复或所有联机边界无问题。上游资源采集状态并未序列化，锅炉和掉落物仍需联机核对。

## 继续开发和测试

session-source/Build-Session.ps1 参数 GameDir 和 RoslynDir 可按机器填写。session-source/Test-Core.ps1 检查快照和原子写入；tests/Test-Installer.ps1 在临时游戏目录测试安装、回滚、保留无关文件和冲突保护。

SessionSmoke.cs 是开发用测试插件源码，不是正常模组。它会自动建立私有测试房间和临时世界、改变测试背包和生命并退出游戏，不能在用户的游玩会话中加载。若明确需要复测，只能关闭游戏后在隔离测试状态下使用，并先确认 `Saves/__session_smoke.mucksave` 不是用户的文件。正常 payload 中不包含 SessionSmoke.dll。

不要上传用户日志、Steam 账户资料或存档。交接包里只有加载器、模组、译文、源码、文档和校验清单。
