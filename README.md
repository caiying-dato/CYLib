# CYLib

杀戮尖塔 2（Slay the Spire 2）的**模组配置平台**：在游戏主界面（模式 / 时间线 / 百科 那排
按钮处）提供「模组配置」入口。各模组通过 CYLib 的 API 注册自己的配置页，由 CYLib 统一提供
配置界面、持久化存储、默认值重置与变更通知——各模组不需要自己写 UI、自己管存档。

> **给负责具体模组的会话**：接入只需两步（清单声明依赖 + 一行注册），见
> **[docs/接入指南.md](docs/接入指南.md)**，完整示例在 `docs/samples/MyModConfigSample.cs`。

[![游戏版本](https://img.shields.io/badge/游戏-v0.111.0-blue)](#)
[![版本](https://img.shields.io/badge/版本-v0.1.4-green)](#)
[![语言](https://img.shields.io/badge/语言-简体中文%20%7C%20English-green)](#)
[![许可](https://img.shields.io/badge/许可-MIT-yellow)](LICENSE)

---

## 安装

### 方式一：下载发布包（推荐）

到本仓库的 [**Releases**](../../releases) 页面下载最新的 `CYLib-vX.Y.Z.zip`，
解压后把里面的 **`CYLib` 文件夹**放进《杀戮尖塔 2》安装目录的 `mods\` 下：

1. 在 Steam 里**右键游戏 → 管理 → 浏览本地文件**，打开《杀戮尖塔 2》的安装目录。
2. 把压缩包里的 `CYLib` 文件夹解压/拖进这个安装目录的 `mods\` 里。

   ```
   Slay the Spire 2\
     mods\
       CYLib\
         CYLib.dll
         CYLib.json
         CYLib.pck
   ```

> ⚠️ **那一层 `CYLib\` 必须保留**：直接把包里的文件倒进 `mods\` 里，模组管理器看不到它，
> 而且哪里都不会报错。
>
> GitHub 上的包与局域网共享架上同名版本**内容完全一致**，只是文件名去掉了时间戳
> （`CYLib-vX.Y.Z-<日期>-<时间>.zip` → `CYLib-vX.Y.Z.zip`）。

### 方式二：Steam 创意工坊

订阅 [**CYLib**（工坊 ID 3812834784）](https://steamcommunity.com/sharedfiles/filedetails/?id=3812834784)，
由 Steam 自动安装与更新。

装好后启动游戏 → 主界面出现「**模组配置**」按钮即为生效。CYLib 是**平台模组**，
本身不改动游戏玩法，只有其它模组注册了配置页时才有内容。

---

## 功能

- **两个入口，一个按钮**：主菜单按钮「模组配置」+ 设置→游戏设置里的「模组配置」行。
  装了 BaseLib / RitsuLib 时走**统一入口**：它们的重复入口被隐藏，CYLib 界面底部
  「其它配置库」一键跳转它们自己的配置菜单（内容不丢；可在 CYLib 配置页关闭此行为）。
  关闭统一后入口文案自动带 "(CYLib)" 标识与同名按钮区分；布尔开关带绿(开)/红(关)状态指示。
- **配置界面**：左栏模组列表、右栏设置项；小节可折叠；支持开关 / 整数滑条 / 浮点滑条 /
  文本 / 下拉 / 动作按钮 / 说明文字；支持条件显隐、条件置灰、"需要重启生效"标注。
- **持久化**：每模组一个 JSON 文件（原子写 + 损坏自动备份 + 未知 key 保留），UI 改动 0.5s
  防抖落盘，关闭界面与退出游戏强制落盘。
- **默认值**：每项可一键"重置为默认"（带确认弹窗）。
- **对外 API**：builder 式声明、类型化取值、`Set` 写值、`ValueChanged` 事件、惰性本地化文本。

## 工程结构

```
CYLib/
├─ CYLib.csproj / CYLib.json          工程与模组清单（id = CYLib）
├─ Code/
│  ├─ CYLibEntry.cs                  [ModInitializer] 入口：逐个打补丁 + 注册自身配置页
│  ├─ Config/                        ★ 对外 API（其它模组只用这一层）
│  │  ├─ CYLibConfig.cs              门面：Register / Get / Set / RestoreDefaults / 事件
│  │  ├─ ConfigPageBuilder.cs        builder：Page / Section / 各设置项
│  │  ├─ ConfigModel.cs              设置项定义（Bool/Int/Float/Text/Choice/Button…）
│  │  ├─ ConfigStore.cs              注册表 + JSON 持久化 + 变更通知
│  │  ├─ LazyText.cs                 字面量 / 惰性（渲染时求值）文本
│  │  └─ CyLibOwnConfig.cs           CYLib 自己的配置页（也是接入示例）
│  ├─ Ui/                            界面（不对外）
│  │  ├─ NCyConfigSubmenu.cs         「模组配置」子菜单（左右两栏）
│  │  ├─ SettingRowFactory.cs        设置项 → 控件（双向绑定）
│  │  ├─ CyStyle.cs                  配色 / 字体 / StyleBox / 控件工厂
│  │  └─ CyText.cs                   CYLib 自身文案的本地化读取
│  └─ Patches/
│     ├─ MainMenuConfigButtonPatch.cs 主菜单「模组配置」按钮注入
│     ├─ SettingsScreenEntryPatch.cs  设置→游戏设置 的入口行注入
│     ├─ SubmenuFactoryPatch.cs      子菜单栈工厂（主菜单栈 + 局内栈）
│     └─ QuitSavePatch.cs            退出前落盘
├─ CYLib/localization/{eng,zhs}/     本地化（settings_ui / main_menu_ui 两张游戏原表）
├─ docs/                             接入指南、示例、调研报告
├─ deploy.ps1 / deploy.cmd           编译 + 装进 mods + .pck 内容断言
└─ nuget.config                      NuGet 源（packages\ 是本机离线包目录，不入库）
```

## 构建与部署

```powershell
# 编译 + 安装到 <游戏>\mods\CYLib\ + 断言 .pck 内容
.\deploy.cmd          # 或 pwsh -ExecutionPolicy Bypass -File .\deploy.ps1

# 只改了 .cs、想快速验证编译：
dotnet build .\CYLib.csproj -c Debug
```

- 游戏目录由 `Sts2PathDiscovery.props` 自动探测，可写在 `local.props` 里覆盖。
- `.pck` 由 `BSchneppe.StS2.PckPacker` 在 build 阶段生成（纯 JSON 资源不需要 MegaDot/Godot）。
- **改了 `CYLib/localization/**` 必须重新 build**（文案就在 .pck 里）。
- 运行时配置文件：`%APPDATA%\Godot\app_userdata\<游戏>\mod_configs\<modId>.json`
  （在 CYLib 配置页的"存储"小节里有"打开配置目录"按钮）。

## 设计要点（踩坑记录）

- **本地化只能扩充游戏已有的 loc 表**（游戏按文件名把模组 loc 合并进同名表），所以 CYLib
  的文案放在 `settings_ui` / `main_menu_ui` 里，key 一律 `CYLIB.` 前缀，绝不覆盖游戏原文。
- **文案惰性解析**：模组初始化时 loc 表可能没加载，所有文本参数支持 `new LazyText(() => …)`
  推迟到渲染时求值。
- **主菜单按钮在 `NMainMenu._Ready` 的 Prefix 注入**（而不是 Postfix）：游戏随后的按钮焦点
  逻辑会一并照顾到新按钮。
- **注入按钮的节点名必须唯一**（`CYLibModConfigButton`）：BaseLib 的按钮叫 `ModConfigButton`，
  同名会让防重逻辑误判"已注入"而跳过——装了 BaseLib 后 CYLib 入口直接消失就是这么来的。
- **与 BaseLib/RitsuLib 共存（统一入口）**：三方都补丁 `GetSubmenuType`，各自只认自己的
  类型、其余放行；CYLib 在帧末隐藏它们的重复入口（各自补丁先后无关），并按类型名反射
  找到它们的子菜单做站内跳转——不硬依赖、找不到就少个跳转，升级不炸。
  RitsuLib 每次打开设置会把自己的行 `Visible = true`，所以去重要挂在 `OnSubmenuOpened`
  反复执行，不能只做一次。
- **复制原生 `SettingsButton` / "Modding" 行而不是自造控件**：观感、SFX、动画与原生一致。
- **`.pck` 复制必须排在 PckPacker 打包之后**（csproj 里 `AfterTargets="PackPck"`），否则
  mods 里是上一次的旧包，改文案不生效且完全静默。
- 补丁逐个打、逐个 catch（不用 `PatchAll`），单个补丁失败不拖垮整个模组。

## 打包发布（局域网共享架）

```powershell
# 先部署（编译 + 装进 mods + 归档 dist\CYLib\，并断言 .pck 内容）
.\deploy.cmd

# 打包：核对三处版本号 + 包内路径 + 哈希，产出 release\CYLib-v0.1.0-<日期>-<时间>.zip
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\pack-release.ps1 -Version 0.1.0

# 打包 + 发布到局域网共享架（顺手删掉架上旧的 CYLib 包，拷过去再核一次哈希）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\pack-release.ps1 -Version 0.1.0 -Publish
```

- **「发布」= 只放到局域网共享架**（`%USERPROFILE%\.dsh\lan-transfer\outgoing\`），供别的设备下载测试；
  **不碰 Steam 创意工坊** —— 那要用户明确提到「创意工坊」才做（见
  `C:\DeepSeek Harness\steam创意工坊（workshop）\上传交接文档.md` 第 0 条）。
- 包名规范（2026-10-03 起）：`中文名(英文名)-v<版本>-<yyyyMMdd-HHmm>.zip`。
  CYLib 暂无中文名，所以是 `CYLib-v0.1.0-<日期>-<时间>.zip`；以后定了中文名加 `-CnName '中文名'`。
- 包内**必须保留一层 `CYLib\`**（`CYLib\CYLib.dll` 等），少了这层解到 `mods\` 里认不出模组且不报错。
- 发布记录：`CYLib-v0.1.0-20261003-0423.zip`（64,198）→ `CYLib-v0.1.2-20261004-0216.zip`（68,803）
  → `CYLib-v0.1.3-20261004-0240.zip`（69,702）→ **`CYLib-v0.1.4-20261008-0109.zip`（70,784 字节，当前在共享架上）**。
  包名里的版本号紧跟 `CYLib.json` 的 version（`v0.1.4`）；工作区 `release\` 里保留历次包。

## 创意工坊

工坊物品 ID **3812834784**（标题 `CYLib`，public，标签
`Tools & APIs` / `Modding or Configuration` / `Simplified Chinese`，无依赖，详情中英同页）。

| 版本 | 时间 | 变更 |
| --- | --- | --- |
| v0.1.3 | 2026-10-04 | 首发 |
| **v0.1.4** | **2026-10-08** | 设置项自己的标题/说明支持动态解析器（每行可显示自己的当前状态）；新增 `RestartNote` 写明「为什么需要重启」；动态信息行求值链更稳 |

- 工作区在 `C:\DeepSeek Harness\steam创意工坊（workshop）\CYLib\工作区\`
  （`content\` + `image.png` + `mod_id.txt` + `workshop.json` + `description.{zh,en}.bbcode.txt` + `_history\`）。
- 更新流程见 `C:\DeepSeek Harness\steam创意工坊（workshop）\上传交接文档.md`（第 0 条先分清「发布 ≠ 创意工坊」）。
- 「愈深愈肉（Deeper and Tankier）」v0.1.2 起、以及「更好的进阶难度（CY's BetterAscension）」v0.1.3 起，
  都把 CYLib 列为必需前置（工坊侧依赖 3812834784）。

## 许可

本模组源码采用 **MIT 许可证**，详见 [LICENSE](LICENSE)。
《杀戮尖塔 2》游戏本体与游戏资产不属于本许可证，版权归 Mega Crit 所有。

开发源码、构建脚本与完整技术文档：[https://github.com/caiying-dato/CYLib](https://github.com/caiying-dato/CYLib)
