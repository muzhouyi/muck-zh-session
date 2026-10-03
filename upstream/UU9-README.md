# UU9 Muck Translater

## 中文说明

为 **Muck** 提供基于词典的界面翻译 Mod。将英文原文与目标语言写入 `.cfg` 词典，即可将游戏文本翻译为中文或其他语言。

### 功能

- 支持任意语言的 `英文原文=目标文本` 词典。
- 读取 `BepInEx\config\UU9.Muck.Translater\Translation\` 内全部 `.cfg` 文件及子目录。
- 支持 `TextMeshPro` 与 `Unity UI` 文本。
- 可在配置中选择字体；优先读取 `C:\Windows\Fonts`，再读取 Mod 的`config`文件夹中的 `Font` 文件夹。
- `F5` 可热重载词典，`F6` 可临时启用/关闭翻译；两项热键可分别关闭。

### 安装

使用 Thunderstore Mod Manager / r2modman 安装即可，依赖的 BepInExPack Muck 会自动安装。

手动安装时，将 `UU9.Muck.Translater.dll` 放入：

`BepInEx\plugins\`

首次启动游戏后，配置文件位于：

`BepInEx\config\UU9.Muck.Translater\UU9.Muck.Translater.cfg`

### 词典格式

在 `BepInEx\config\UU9.Muck.Translater\Translation\` 创建或放入 UTF-8 编码的 `.cfg` 文件：

```cfg
Play=开始游戏
Options=选项
Quit=退出游戏
```

编辑词典后按 `F5` 立即重新加载；若已在配置中关闭 `F5`，则重启游戏生效。

### 配置

```cfg
[General]
FontFile = msyh.ttc

[Hotkeys]
EnableF5Reload = true
EnableF6Toggle = true
```

- `FontFile`：字体文件名，例如 `msyh.ttc`、`simhei.ttf` 或自备字体文件名。
- 字体查找顺序：`C:\Windows\Fonts` → `BepInEx\config\UU9.Muck.Translater\Font` → 内置回退字体。
- `EnableF5Reload`：是否允许 `F5` 重载词典。
- `EnableF6Toggle`：是否允许 `F6` 开关翻译。

## English

A dictionary-based translation mod for **Muck**. Add `English source text=translated text` entries to `.cfg` dictionaries to translate game UI into Chinese or other languages.

### Features

- Supports any target language through configurable dictionaries.
- Loads every `.cfg` file under `BepInEx\config\UU9.Muck.Translater\Translation\`.
- Supports TextMeshPro and Unity UI text.
- Configurable fonts: Windows system fonts are searched before the mod's `Font` directory.
- F5 reloads dictionaries and F6 toggles translation; each hotkey can be disabled independently.

### Installation

Install with Thunderstore Mod Manager or r2modman. The required BepInExPack Muck dependency is installed automatically.

For manual installation, put `UU9.Muck.Translater.dll` in:

`BepInEx\plugins\`

After the first launch, edit:

`BepInEx\config\UU9.Muck.Translater\UU9.Muck.Translater.cfg`

### Dictionary format

Create UTF-8 `.cfg` files in `BepInEx\config\UU9.Muck.Translater\Translation\`:

```cfg
Play=Jouer
Options=Options
Quit=Quitter
```

Press F5 to reload after editing, or restart the game if F5 reload is disabled.

## Support

Please include your game version, mod version, BepInEx log, and the affected dictionary entry when reporting an issue.