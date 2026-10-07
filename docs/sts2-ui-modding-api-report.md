# 《杀戮尖塔2》(Slay the Spire 2) UI/模组 API 调研报告
> 依据反编译源码 v0.111.0（`sts2-v0.111.0-current`，备用 `_ref\sts2_decompiled` 目录结构一致）。
> 目标：在主菜单加"模组配置"按钮并打开自定义界面。所有类/成员给出精确全名与签名。

---

## 0. 速查结论

| 需求 | 答案 |
|---|---|
| 主菜单类 | `MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu : Godot.Control, IScreenContext` |
| 主菜单按钮类型 | `NMainMenuTextButton : NButton : NClickableControl : Godot.Control`（不是 Godot 原生 Button） |
| 点击信号 | `NClickableControl.SignalName.Released`（delegate `ReleasedEventHandler(NClickableControl button)`） |
| 取本地化文本 | `new LocString("main_menu_ui", "KEY").GetFormattedText()`（SmartFormat 模板） |
| 模组 loc 合并 | `ModManager.GetModdedLocTables(string language, string file)` → `res://<modId>/localization/<lang>/<file>`，按**文件名**合并进游戏同名表 |
| 枚举模组 | `ModManager.Mods` / `ModManager.GetLoadedMods()`，元素为 `Mod`（`manifest.id/name/version`、`state`、`path`） |
| 打开子界面 | `NMainMenu.SubmenuStack`（`NMainMenuSubmenuStack : NSubmenuStack`）`Push(NSubmenu)` / `PushSubmenuType<T>()` |
| 打开弹窗 | `NModalContainer.Instance.Add(Node modal, bool showBackstop = true)`（一次只能开一个） |
| 全屏换场景 | `NGame.Instance.RootSceneContainer.SetCurrentScene(Control)`（`NSceneContainer`） |
| 主题/字体 | `ThemeConstants`（StringName 常量）+ `StsColors` + `control.ApplyLocaleFontSubstitution(FontType, themeFontName)`；贴图 `ImageHelper.GetImagePath` → `res://images/...`，场景 `SceneHelper.GetScenePath` → `res://scenes/...` |

---

## 1. 主菜单界面：`NMainMenu`

**全名**：`MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu : Godot.Control, MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.IScreenContext`
**场景**：`res://scenes/screens/main_menu.tscn`（`private const string _scenePath`）
**工厂**：

```csharp
public static NMainMenu Create(bool openTimeline)
// 实现：PreloadManager.Cache.GetScene("res://scenes/screens/main_menu.tscn")
//        .Instantiate<NMainMenu>(PackedScene.GenEditState.Disabled);
```

### 1.1 按钮字段与布局（节点路径来自 `_Ready`）

按钮全部是 `NMainMenuTextButton`，私有字段（`_Ready` 里 `GetNode<>` 取得）：

```csharp
private NMainMenuTextButton _continueButton;      // "MainMenuTextButtons/ContinueButton"
private NMainMenuTextButton _abandonRunButton;    // "MainMenuTextButtons/AbandonRunButton"
private NMainMenuTextButton _singleplayerButton;  // "MainMenuTextButtons/SingleplayerButton"
private NMainMenuTextButton _multiplayerButton;   // "MainMenuTextButtons/MultiplayerButton"
private NMainMenuTextButton _compendiumButton;    // "MainMenuTextButtons/CompendiumButton"
private NMainMenuTextButton _timelineButton;      // "MainMenuTextButtons/TimelineButton"
private NMainMenuTextButton _settingsButton;      // "MainMenuTextButtons/SettingsButton"
private NMainMenuTextButton _quitButton;          // "MainMenuTextButtons/QuitButton"
private NPatchNotesButton _patchNotesButtonNode;  // "%PatchNotesButton"
private NOpenProfileScreenButton _openProfileScreenButton; // "%ChangeProfileButton"

private NButton[] MainMenuButtons => new NButton[8] {
    _continueButton, _abandonRunButton, _singleplayerButton, _multiplayerButton,
    _timelineButton, _settingsButton, _compendiumButton, _quitButton };
public NMainMenuSubmenuStack SubmenuStack { get; private set; }   // 节点 "%Submenus"
public NPatchNotesScreen PatchNotesScreen { get; private set; }   // 节点 "%PatchNotesScreen"
```

布局结构（由节点路径推断）：主菜单场景里有一个容器 `MainMenuTextButtons`（可用 `%MainMenuTextButtons` 唯一名访问，是竖排按钮列表，单个按钮路径为 `MainMenuTextButtons/<Name>Button`），另有 `BlurBackstop`（模糊背景 Control，挂 ShaderMaterial）、`%MainMenuBg`、`%ButtonReticleLeft/Right`（按钮聚焦时的金色尖角装饰）、`%ContinueRunInfo`、`%TimelineNotificationDot`、`%Submenus`（子菜单栈容器）。

### 1.2 `_Ready` 中按钮的创建/文本本地化/信号连接范式（原文摘录）

```csharp
public override void _Ready()
{
    ...
    _singleplayerButton = GetNode<NMainMenuTextButton>("MainMenuTextButtons/SingleplayerButton");
    _singleplayerButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(SingleplayerButtonPressed));
    _singleplayerButton.SetLocalization("SINGLE_PLAYER");     // ← key，表固定为 "main_menu_ui"
    _multiplayerButton = GetNode<NMainMenuTextButton>("MainMenuTextButtons/MultiplayerButton");
    _multiplayerButton.Connect(NClickableControl.SignalName.Released, Callable.From((Action<NButton>)OpenMultiplayerSubmenu));
    _multiplayerButton.SetLocalization("MULTIPLAYER");
    _compendiumButton = GetNode<NMainMenuTextButton>("MainMenuTextButtons/CompendiumButton");
    _compendiumButton.Connect(NClickableControl.SignalName.Released, Callable.From((Action<NButton>)OpenCompendiumSubmenu));
    _compendiumButton.SetLocalization("COMPENDIUM");
    _timelineButton = GetNode<NMainMenuTextButton>("MainMenuTextButtons/TimelineButton");
    _timelineButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OpenTimelineScreen));
    _timelineButton.SetLocalization("TIMELINE");
    ...
    ConnectMainMenuTextButtonFocusLogic();   // 给 %MainMenuTextButtons 下所有 NMainMenuTextButton 接 Focused/Unfocused（尖角动画）
    SubmenuStack = GetNode<NMainMenuSubmenuStack>("%Submenus");
    SubmenuStack.InitializeForMainMenu(this);
    SubmenuStack.Connect(NSubmenuStack.SignalName.StackModified, Callable.From(OnSubmenuStackChanged));
    ...
    if (SaveManager.Instance.SettingsSave.ModSettings == null && ModManager.Mods.Count > 0)
        NModalContainer.Instance.Add(NConfirmModLoadingPopup.Create());
    ...
}
```

按钮文本本地化（`NMainMenuTextButton.SetLocalization`）：

```csharp
public void SetLocalization(string locKey)
{
    _locString = new LocString("main_menu_ui", locKey);   // 表名写死 main_menu_ui
    RefreshLabel();                                       // label.Text = _locString.GetFormattedText();
}
```
并且 `_Notification(2010 /* NotificationTranslationChanged */)` 时自动 `RefreshLabel()`，语言切换自动刷新。

其他要点：
- `RefreshButtons()`（public）会改各按钮 Visible/Enable；`OnSubmenuStackChanged()` 在有子菜单打开时隐藏整个 `GetNode<Control>("MainMenuTextButtons")` 容器（把自定义按钮加进该容器即可获得同样的自动显隐）。
- `DefaultFocusedControl` 只在 `MainMenuButtons`（写死的 8 个）中挑焦点，不包含模组按钮。
- `ConnectMainMenuTextButtonFocusLogic()` 遍历 `GetNode<Control>("%MainMenuTextButtons").GetChildren().OfType<NMainMenuTextButton>()` 并接 `Focused/Unfocused`（金色尖角跟随动画）——它是 `_Ready` 时调用的，**之后新加的按钮不会自动获得该动画**。

### 1.3 追加自定义按钮的补丁挂点建议（Harmony）

游戏自带 Harmony（`HarmonyLib`，ModManager 自动 `new Harmony(author + "." + modId).PatchAll(assembly)`）。推荐 **Postfix `NMainMenu._Ready()`**：

```csharp
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu), nameof(NMainMenu._Ready))]
public static class NMainMenuReadyPatch
{
    public static void Postfix(NMainMenu __instance)
    {
        // 1) 取按钮容器（同 _Ready 用的路径）
        var buttonsBox = __instance.GetNode<Godot.Control>("MainMenuTextButtons");

        // 2) 克隆一个现有 NMainMenuTextButton（保底样式/动画/字体一致）
        var template = __instance.GetNode<NMainMenuTextButton>("MainMenuTextButtons/QuitButton");
        var myButton = (NMainMenuTextButton)template.Duplicate();
        myButton.Name = "ModConfigButton";

        // 3) 文本：注意 Duplicate 会带模板的 loc 残留，用自己的 key 重设
        myButton.SetLocalization("MOD_CONFIG");   // 需在 main_menu_ui 表里提供该 key（见 §3 模组 loc 合并）
        // 或纯文本：myButton.label.Text = "模组配置";  （label 是 public 字段 MegaLabel?）

        buttonsBox.AddChild(myButton);
        // 4) 信号（签名 void Handler(NButton)）
        myButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => OnModConfigPressed(__instance)));
    }
}
```

注意事项：
- `NMainMenuTextButton._Ready` 里有 `if (GetType() != typeof(NMainMenuTextButton)) throw new InvalidOperationException("Don't call base._Ready()!")` —— **不要继承 NMainMenuTextButton 并调用 base._Ready()**；要么直接用该类型（Duplicate），要么自己继承 `NButton` 重写 `ConnectSignals()`（它 `protected virtual`，在 `_Ready`/子类中调用）。
- 该类型 `_Ready` 里 `label = GetChild<MegaLabel>(0)`，即第 0 个子节点必须是 `MegaLabel`。
- 尖角聚焦动画：如需要，可手动 `myButton.Connect(NClickableControl.SignalName.Focused/Unfocused, ...)`，或用反射调私有 `NMainMenu.ConnectMainMenuTextButtonFocusLogic()`（它按 `%MainMenuTextButtons` 子节点遍历，加完按钮后再调一次即可覆盖）。
- 更"原生"的替代挂点：`NMainMenuSubmenuStack.GetSubmenuType(Type)`（如把配置入口塞进游戏 Modding 界面）；或 Postfix `NSettingsScreen._Ready` 加 `NSettingsButton`（见 §5 的 `NOpenModdingScreenButton` 先例）。

---

## 2. 按钮/控件体系（游戏自定义控件，非 Godot Button）

继承链（`MegaCrit.Sts2.Core.Nodes.GodotExtensions` 命名空间）：

```
Godot.Control
 └─ NClickableControl            // 可点击基类
     ├─ NButton                  // 带音效/热键/手柄图标
     │   ├─ NMainMenuTextButton  // 主菜单竖排文字按钮
     │   ├─ NSubmenuButton       // 子菜单大按钮（图标+标题+描述）
     │   ├─ NShortSubmenuButton  // 子菜单短按钮
     │   ├─ NSettingsButton → NOpenModdingScreenButton
     │   ├─ NBackButton / NConfirmButton / NProceedButton / NTickbox / NGoldArrowButton ...
     │   └─ (NCompendiumBottomButton / NPatchNotesButton / NOpenProfileScreenButton ...)
     └─ NDropdown
```

### 2.1 `NClickableControl : Godot.Control` — 信号与方法（精确签名）

```csharp
[Signal] public delegate void ReleasedEventHandler(NClickableControl button);
[Signal] public delegate void FocusedEventHandler(NClickableControl button);
[Signal] public delegate void UnfocusedEventHandler(NClickableControl button);
[Signal] public delegate void MouseReleasedEventHandler(InputEvent inputEvent);
[Signal] public delegate void MousePressedEventHandler(InputEvent inputEvent);

// 事件（包装上面的信号）：
public event ReleasedEventHandler Released;
public event FocusedEventHandler Focused;
public event UnfocusedEventHandler Unfocused;

public bool IsEnabled { get; }              // => _isEnabled
public void SetEnabled(bool enabled);
public void Enable();
public void Disable();
public void ForceClick();
public void DebugPress(); public void DebugRelease();

protected virtual void ConnectSignals();
protected virtual void OnFocus();  protected virtual void OnUnfocus();
protected virtual void OnPress();  protected virtual void OnRelease();
protected virtual void OnEnable(); protected virtual void OnDisable();
public override void _GuiInput(InputEvent inputEvent);
```

信号名常量：`NClickableControl.SignalName.Released / Focused / Unfocused / MouseReleased / MousePressed`。

连接点击的标准写法（游戏内到处都是）：

```csharp
button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(Handler));
// Handler: private void Handler(NButton _) { ... }
```

### 2.2 `NButton : NClickableControl`

```csharp
protected virtual string? ClickedSfx => "event:/sfx/ui/clicks/ui_click";
protected virtual string? HoveredSfx => "event:/sfx/ui/clicks/ui_hover";
protected virtual string[] Hotkeys => Array.Empty<string>();
protected virtual string? ControllerIconHotkey { get; }
public override void _Ready();       // 调 ConnectSignals()
protected override void ConnectSignals();
public override void _Input(InputEvent inputEvent);   // 热键
```

### 2.3 `NMainMenuTextButton : NButton`（主菜单按钮）

```csharp
public MegaLabel? label;                    // 第 0 个子节点，ConnectSignals() 中 GetChild<MegaLabel>(0)
private Color _defaultColor = StsColors.cream;
private Color _hoveredColor = StsColors.gold;
private Color _downColor   = StsColors.halfTransparentWhite;
private LocString? _locString;

public void SetLocalization(string locKey);           // → new LocString("main_menu_ui", locKey)
private void RefreshLabel();                          // label.Text = _locString.GetFormattedText();
                                                      // label.ApplyLocaleFontSubstitution(FontType.Regular, ThemeConstants.Label.Font);
```

### 2.4 `NSubmenuButton : NButton` / `NShortSubmenuButton : NButton`（子菜单大/短按钮）

两者结构相似（子节点 `BgPanel`（ShaderMaterial 高亮）、`Icon`(TextureRect)、`%Title`(MegaLabel)、`%Description`(MegaRichTextLabel)）：

```csharp
public void SetIconAndLocalization(string locKeyPrefix);
// 内部：
//   new LocString("main_menu_ui", locKeyPrefix + ".title").GetFormattedText()  → _title.SetTextAutoSize(...)
//   new LocString("main_menu_ui", locKeyPrefix + ".description")               → _description.Text = ...
//   锁定时找 locKeyPrefix + ".LOCKED.description"，找不到回退并 Log.Warn
public void RefreshLabels();      // NSubmenuButton 上是 public
// NShortSubmenuButton 额外：
public static string? GetImagePath(string key)
    => ImageHelper.GetImagePath("packed/main_menu/submenu_icon_" + key.ToLowerInvariant() + ".png");
    // 存在才返回，用 PreloadManager.Cache.GetCompressedTexture2D(imagePath) 赋给 Icon
```

### 2.5 文本控件

- `MegaCrit.Sts2.addons.mega_text.MegaLabel : Godot.Label`：
  `public void SetTextAutoSize(string text)`、`public void SetFontSize(...)`、`public bool AutoSizeEnabled`、`public int MinFontSize/MaxFontSize`、`public void RefreshFont()`。
- `MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel : Godot.RichTextLabel`（普通 `.Text` 赋值即 BBCode）。
- `MegaCrit.Sts2.Core.Localization.LocTextLabel : Godot.RichTextLabel`（绑定 LocString 的富文本）。

---

## 3. 本地化 API

命名空间 `MegaCrit.Sts2.Core.Localization`。

### 3.1 `LocString`（引用型 key，不是直接 string）

```csharp
public class LocString(string locTable, string locEntryKey) : IComparable<LocString>
{
    public string LocTable { get; }      // JSON 属性名 "table"
    public string LocEntryKey { get; }   // JSON 属性名 "key"
    public bool IsEmpty { get; }
    public IReadOnlyDictionary<string, object> Variables { get; }

    public static bool Exists(string table, string key);          // → LocManager.Instance.GetTable(table).HasEntry(key)
    public static LocString? GetIfExists(string table, string key);
    public string GetFormattedText();    // SmartFormat 格式化后的文本（绑定 UI 用这个）
    public string GetRawText();          // JSON 原文（含 {占位符}）
    public bool Exists();

    public void Add(string name, decimal variable);
    public void Add(string name, bool variable);
    public void Add(string name, string variable);
    public void Add(string name, IList<string> variable);
    public void Add(string name, LocString variable);
    public void AddObj(string name, object variable);
    public void Add(DynamicVar dynamicVar);
    public void AddVariablesFrom(LocString smartDescription);

    public static LocString KeyPathToLocString(string keyPath);   // "table/key" → LocString
    public static LocString GetRandomWithPrefix(string table, string keyPrefix, Rng? rng = null);
    public static void SubscribeToLocaleChange(LocManager.LocaleChangeCallback callback);
    public static void UnsubscribeToLocaleChange(LocManager.LocaleChangeCallback callback);
}
```

用法（游戏内范式）：

```csharp
var title = new LocString("main_menu_ui", "INVALID_SAVE_POPUP.title");
title.Add("id", modId);
NErrorPopup.Create(title, body, cancel, showReportBugButton: true);
```

### 3.2 `LocTable`

```csharp
public class LocTable
{
    public LocTable(string name, Dictionary<string, string> data, LocTable? fallback = null);
    public IEnumerable<string> Keys { get; }
    public void MergeWith(Dictionary<string, string> otherTable);   // ← 模组 loc 合并用的就是它
    public LocString GetLocString(string key);
    public string GetRawText(string key);                            // 未命中走 fallback（eng），再未命中抛 LocException
    public IReadOnlyList<LocString> GetLocStringsWithPrefix(string keyPrefix);
    public bool IsLocalKey(string key);
    public bool HasEntry(string key);                                // 含 fallback
}
```

### 3.3 `LocManager`（单例 `LocManager.Instance`）

```csharp
public static LocManager Instance { get; }
public string Language { get; }                       // 三字母码
public static List<string> Languages { get; }         // eng,zhs,zht,deu,esp,fra,ind,ita,jpn,kor,pol,ptb,rus,spa,tha,tur
public bool OverridesActive { get; }
public IReadOnlyList<LocValidationError> ValidationErrors { get; }
public const string locOverrideDir = "user://localization_override";
private static string LocalizationAssetDir => "res://localization";

public static void Initialize();
public void SetLanguage(string language);
public LocTable GetTable(string name);                // 表不存在抛 LocException
public string SmartFormat(LocString locString, Dictionary<string, object> variables);
public float GetLanguageCompletion(string language);
public void SubscribeToLocaleChange(LocaleChangeCallback callback);
public void UnsubscribeToLocaleChange(LocaleChangeCallback callback);
public void StartOverridingLanguageAsEnglish(); public void StopOverridingLanguageAsEnglish();
public delegate void LocaleChangeCallback();
```

### 3.4 loc JSON 文件结构

- 路径：`res://localization/<lang>/<table>.json`（`<lang>` 为三字母码，如 `eng`、`zhs`；`<table>` 即表名，如 `main_menu_ui`、`cards`、`settings_ui`…）。
- 结构：**扁平 `Dictionary<string,string>`**（`JsonSerializer.Deserialize<Dictionary<string,string>>`）：

```json
{
  "SINGLE_PLAYER": "单人模式",
  "INVALID_SAVE_POPUP.title": "存档损坏",
  "INVALID_SAVE_POPUP.description_run": "无法读取存档。",
  "MAP_POINT_HISTORY.abandon.0": "{character} 直接放弃了。"
}
```

  key 是**点号分层命名的扁平字符串**（没有嵌套 JSON 层级）；value 是文本模板。
- 占位符：**SmartFormat 语法**（`https://github.com/axuno/SmartFormat`），如 `{character}`、`{Damage:diff()}`、`{pronounPossessive}`；变量用 `LocString.Add(name, value)` 传入（名字中的空格会被替换为 `-`）。
- 富文本：value 支持 **BBCode**（`LocManager.ConvertToW` 注释明说 "template expressions `{like:this}` and BBCode `[tags]`"）。除 Godot 原生 BBCode 外，游戏在 `MegaCrit.Sts2.Core.RichTextTags` 注册了自定义 effect 标签：
  `[purple] [pink] [red] [green] [blue] [aqua] [orange] [gold]`（颜色）、`[fade_in] [fly_in] [sine] [jitter] [scramble] [thinky_dots] [ancient_banner]`（动效）。
- 语言回退：非 eng 语言自动以 eng 同名表为 `fallback`；某语言缺整个目录时回退 `res://localization/eng`。
- 调试覆盖：`user://localization_override/<lang>/<table>.json`（扁平）与 `user://localization_override/slaythespire2/...`（Weblate 嵌套）可覆盖文本，启用后 `LocManager.OverridesActive == true`。

### 3.5 模组 loc 合并（确切签名与调用处）

```csharp
// MegaCrit.Sts2.Core.Modding.ModManager
/// Returns the filenames of all the loc tables available in loaded mods for the given language and filename.
/// For example, if "eng" and "cards.json" are provided, this returns all mods that supply a cards.json in english.
public static IEnumerable<string> GetModdedLocTables(string language, string file)
{
    foreach (Mod mod in _mods)
    {
        if (mod.state == ModLoadState.Loaded)
        {
            string text = $"res://{mod.manifest.id}/localization/{language}/{file}";
            if (ResourceLoader.Exists(text))
                yield return text;
        }
    }
}
```

调用处（`MegaCrit.Sts2.Core.Localization.LocManager.LoadTablesFromPath`，语言加载时）：

```csharp
IEnumerable<string> enumerable = ListLocalizationFiles(text);      // text = res://localization/<lang>
foreach (string item2 in enumerable)                               // 遍历的是【游戏本体的】表文件名
{
    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(item2);
    ...
    LocTable locTable = new LocTable(fileNameWithoutExtension, dictionary3, fallback);
    ...
    foreach (string moddedLocTable in ModManager.GetModdedLocTables(language, item2))
    {
        Log.Info($"Found loc table from mod: {language} {item2}. Merging with base loc table");
        Dictionary<string, string> dictionary4 = LoadTable(moddedLocTable);
        locTable.MergeWith(dictionary4);
    }
    dictionary2[fileNameWithoutExtension] = locTable;
}
```

**关键结论（模组打包 loc 时必须知道）**：
1. 模组 loc 文件放 PCK 内 `res://<你的modId>/localization/<lang>/<表名>.json`，文件名必须与游戏本体表名一致（如 `main_menu_ui.json`、`settings_ui.json`、`cards.json`）。
2. 合并是按 **base 表**循环的：**模组不能创建全新的 loc 表**（新表名不会进 `_tables`，`LocManager.GetTable("my_table")` 会抛 `LocException`）。自定义 key 请塞进现有表并用自己的前缀防冲突（例如 `main_menu_ui` 里放 `"MOD_CONFIG.TITLE"`）。
3. 合并发生在 `LocManager.Initialize()`/`SetLanguage()` 时，且只合并 `ModLoadState.Loaded` 的模组；key 冲突时模组值**覆盖**游戏值（`MergeWith` 直接赋值）。
4. 只提供 `eng` 也可以：其他语言缺失时走 fallback 到 eng 表。

---

## 4. 模组系统 `MegaCrit.Sts2.Core.Modding`

### 4.1 `ModManager`（`public static class`）

```csharp
public static ModManagerState State { get; }                 // None | Initialized | Skipped
public static IReadOnlyList<Mod> Mods { get; }               // 全部检测到的模组（含未加载）
public static bool PlayerAgreedToModLoading { get; }         // => _settings?.PlayerAgreedToModLoading ?? false
public static bool UnmoddedSavesWereCopied { get; }
public static Dictionary<string, Action> TestInitializers { get; }

public static event Action<Mod>? OnModDetected;              // 运行时新增模组（Steam WS 安装）
public static event MetricsUploadHook? OnMetricsUpload;      // 专供模组挂 metrics
public delegate void MetricsUploadHook(SerializableRun run, bool isVictory, ulong localPlayerId);

public static async Task Initialize(IModManagerFileIo fileIo, ModSettings? settings, SemanticVersion? gameVersion);
public static IEnumerable<Mod> GetLoadedMods();              // _mods.Where(m => m.state == ModLoadState.Loaded)
public static IEnumerable<string> GetModdedLocTables(string language, string file);   // 见 §3.5
public static List<string>? GetGameplayRelevantModNameList();     // "id-version" 列表（联机校验用）
public static List<string>? GetNonGameplayRelevantModNameList();
public static void CallMetricsHooks(SerializableRun run, bool isVictory, ulong localPlayerId);
public static void AssociateAssemblyWithMod(string modId, Assembly assembly);  // 动态程序集关联
public static bool IsRunningModded();
public static bool HasHarmonyPatches();
public static (bool IsSupported, PlatformBranch? MaxSupportedBranch) EvaluateBranchSupport(
    PlatformBranch currentBranch, IReadOnlyList<(string MinBranch, string MaxBranch)> supportedVersions);
```

### 4.2 `Mod`（模组实例，字段全 public）

```csharp
public class Mod
{
    public ModSource modSource;          // None | ModsDirectory | SteamWorkshop
    public required string path;         // 模组文件夹路径（manifest 所在目录的 base dir）
    public ModLoadState state;           // None | Loaded | Failed | Disabled | DisabledDuplicate | AddedAtRuntime
    public ModManifest? manifest;
    public SemanticVersion? version;     // manifest.version 解析结果，非法则为 null
    public List<Assembly> assemblies;    // 已加载的 DLL
    public List<LocString>? errors;      // 加载期错误（本地化文案），无错误为 null
    public ulong? workshopId;            // Steam 创意工坊 ID
}
```

### 4.3 `ModManifest`（mod.json，`record`）

```csharp
public record ModManifest
{
    [JsonPropertyName("id")]               public string? id;            // 必填（缺失直接跳过该 json）
    [JsonPropertyName("name")]             public string? name;
    [JsonPropertyName("author")]           public string? author;
    [JsonPropertyName("description")]      public string? description;
    [JsonPropertyName("version")]          public string? version;       // 语义化版本字符串
    [JsonPropertyName("has_pck")]          public bool hasPck;
    [JsonPropertyName("has_dll")]          public bool hasDll;
    [JsonPropertyName("dependencies")]     public List<ModDependency>? dependencies;  // [{id, min_version}]
    [JsonPropertyName("affects_gameplay")] public bool affectsGameplay = true;
    [JsonPropertyName("min_game_version")] public string? minGameVersion;

    public static ModManifest? ReadFromStream(Stream stream, out List<LocString>? errors);
}
public class ModDependency { [JsonPropertyName("id")] public string id;
                             [JsonPropertyName("min_version")] public string? minVersion;
                             public ModDependency(string id, string? minVersion = null); }
```

### 4.4 入口点 `ModInitializerAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ModInitializerAttribute : Attribute
{
    public string initializerMethod;
    public ModInitializerAttribute(string initializerMethod);
}
```

加载逻辑（`ModManager.TryLoadMod`）：
- DLL：`Path.Combine(mod.path, modId + ".dll")`，用游戏自己的 `AssemblyLoadContext.LoadFromAssemblyPath` 加载（要求 manifest `has_dll: true`）。
- PCK：`Path.Combine(mod.path, modId + ".pck")`，`ProjectSettings.LoadResourcePack(text3)` 挂载（要求 `has_pck: true`）→ 内容以 `res://<modId>/...` 暴露（见 §3.5 的 loc 路径）。
- 入口：assembly 里所有带 `ModInitializerAttribute` 的类型，按 attribute 的 `initializerMethod` 找 **static** 方法（`BindingFlags.Static|Public|NonPublic`）`method.Invoke(null, null)`；**没有**该 attribute 时自动 `new Harmony((author ?? "unknown") + "." + modId).PatchAll(assembly)`。
- 校验顺序：`AddedAtRuntime`（启动后不可加载）→ 未同意模组加载(`Disabled`) → 同 id 重复(`Failed`) → 循环依赖(`Failed`) → min_game_version 不符(`Failed`) → 依赖缺失/版本不足(`Failed`)。

### 4.5 启用状态与设置（SettingsSave）

```csharp
public class ModSettings
{
    [JsonPropertyName("mods_enabled")] public bool PlayerAgreedToModLoading { get; set; }
    [JsonPropertyName("mod_list")]     public List<SettingsSaveMod> ModList { get; set; }
    public bool IsModDisabled(Mod mod);
    public bool IsModDisabled(string? id, ModSource source);
}
public class SettingsSaveMod
{
    [JsonPropertyName("id")]        public string Id { get; set; }
    [JsonPropertyName("source")]    public ModSource Source { get; set; }
    [JsonPropertyName("is_enabled")]public bool IsEnabled { get; set; } = true;
}
```

访问路径：`SaveManager.Instance.SettingsSave.ModSettings`（`ModSettings?`，可能为 null）。
判定"当前模组是否被用户启用"（游戏自带 UI 的做法，`NModMenuRow`）：

```csharp
bool disabled = SaveManager.Instance.SettingsSave.ModSettings?.IsModDisabled(mod) ?? false;
// 保存开关：遍历 ModSettings.ModList，匹配 mod.Id == mod.manifest?.id && mod.Source == mod.modSource 后 mod.IsEnabled = ...；再 SaveManager.Instance.SaveSettings()
```

注意：`Mod.state` 是**本次启动实际加载与否**的真值（不可卸载/重载）；`SettingsSave` 的 IsEnabled 只影响**下次启动**（游戏用 `%PendingChangesLabel` 提示"需重启"）。

### 4.6 目录/路径（模组安装与配置文件存哪）

- **模组安装目录**（`ModManager.Initialize`）：
  - `Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()), "mods")` —— 可执行文件旁的 `mods/`（递归扫描所有 `<id>.json` manifest；`Mod.path = manifest所在目录`）。
  - `.../"mods_STEAMTEST"`（测试）；Steam 创意工坊目录（`ReadSteamMods`，`ModSource.SteamWorkshop`）。
  - 一个目录下放：`<modId>.json`(manifest) + `<modId>.dll` + `<modId>.pck`。
- **模组 PCK 内资源**：`res://<modId>/...`（loc、贴图、场景都可以）。
- **配置/存档**：游戏**没有**专门的"模组配置目录 API"。可选项：
  1. 自己用 Godot `FileAccess/DirAccess` 读写 `user://<modId>/config.json`（`user://` = Godot 用户数据目录）；
  2. 复用游戏路径工具 `MegaCrit.Sts2.Core.Saves.UserDataPathProvider`：

```csharp
public static class UserDataPathProvider
{
    public static string SavesDir { get; }                                     // "saves"
    public static bool IsRunningModded { get; set; }                           // 运行 modded 时存档在 "modded/" 子目录
    public static string GetProfileScopedPath(int profileId, string dataType, PlatformType? platformOverride = null, ulong? userIdOverride = null);
    //   → "user://{steam|editor|default}/{userId}/{[modded/]profileN}/{dataType}"
    public static string GetProfileScopedBasePath(int profileId, ...);
    public static string GetAccountScopedBasePath(string? dataType, ...);
    //   → "user://{platform}/{userId}[/{dataType}]"
    public static string GetAccountDir(bool? forceModState = null);            // "modded" 或 ""
    public static string GetProfileDir(int profileId);                         // "[modded/]profileN"
    public static string GetLegacyPreAccountPath(string dataType);             // "user://" + dataType
}
```
  3. 也可以合并进游戏设置存档：给 `SettingsSave` 塞自定义字段（Harmony 序列化钩子，风险较高，不推荐）。
- `IModManagerFileIo`/`ModManagerFileIo`（`GetFilesAt/GetDirectoriesAt/FileExists/DirectoryExists/OpenStream/MakeDirRecursive/CopyFile`）是 ModManager 自己的 IO 抽象，模组直接用 Godot IO 即可。
- `ModHelper`（`MegaCrit.Sts2.Core.Modding`）是**游戏内容**扩展入口，与 UI 无关：
  `AddModelToPool<TPoolType,TModelType>()`、`SubscribeForRunStateHooks(string id, RunHookSubscriptionDelegate)`、`SubscribeForCombatStateHooks(string id, CombatHookSubscriptionDelegate)`。

---

## 5. 界面切换 / 弹窗（惯用做法 + 调用链示例）

### 5.1 顶层场景切换：`NSceneContainer` + `NGame`

```csharp
// MegaCrit.Sts2.Core.Nodes.NSceneContainer : Godot.Control
public Control? CurrentScene { get; }
public void SetCurrentScene(Control node)   // 移除并释放所有旧子节点，再 AddChild/Reparent 新节点

// MegaCrit.Sts2.Core.Nodes.NGame : Godot.Control
public static NGame? Instance { get; }
public NSceneContainer RootSceneContainer { get; }                 // 节点 "%RootSceneContainer"
public NMainMenu? MainMenu => RootSceneContainer.CurrentScene as NMainMenu;
public NTransition Transition { get; }                             // 节点 "%GameTransitionRect"
public async Task LoadRun(RunState runState, SerializableRoom? preFinishedRoom);
public async Task ReturnToMainMenu();                              // FadeOut → PreloadManager.LoadCommonAndMainMenuAssets() → LoadMainMenu()
public async Task GoToTimeline();
public void ReloadMainMenu();
public void Relocalize();
public void Quit();
```

`NGame.LoadMainMenu`（回主菜单的实现）：

```csharp
private async Task LoadMainMenu(bool openTimeline = false)
{
    ...
    NMainMenu currentScene = NMainMenu.Create(openTimeline);
    NHotkeyManager.Instance?.ClearHotkeys();
    RootSceneContainer.SetCurrentScene(currentScene);
}
```

### 5.2 主菜单子界面栈：`NSubmenuStack` / `NMainMenuSubmenuStack`

```csharp
// MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSubmenuStack : Godot.Control（abstract）
public delegate void StackModifiedEventHandler();
public event StackModifiedEventHandler StackModified;
public bool SubmenusOpen { get; }                       // => _submenus.Count > 0
public void InitializeForMainMenu(NMainMenu mainMenu);
public abstract T PushSubmenuType<T>() where T : NSubmenu;
public abstract T GetSubmenuType<T>() where T : NSubmenu;
public abstract NSubmenu PushSubmenuType(Type type);
public abstract NSubmenu GetSubmenuType(Type type);
public void Push(NSubmenu screen);      // ← public，任何 NSubmenu 子类都能压栈！
public void Pop();
public NSubmenu? Peek();
```

`Push` 实现（自带显隐/焦点/背景模糊处理）：

```csharp
public void Push(NSubmenu screen)
{
    if (_submenus.Count > 0) { var prev = _submenus.Peek(); prev.Visible = false; prev.MouseFilter = MouseFilterEnum.Ignore; }
    screen.SetStack(this);
    _submenus.Push(screen);
    screen.OnSubmenuOpened();
    screen.Visible = true;
    screen.MouseFilter = MouseFilterEnum.Stop;
    _mainMenu?.EnableBackstop();                 // 主菜单背景模糊
    ActiveScreenContext.Instance.Update();
    EmitSignal(SignalName.StackModified);
}
```

`NMainMenuSubmenuStack : NSubmenuStack`：内部用类型→缓存实例的 switch（`GetSubmenuType(Type)`），各 `NSubmenu` 用静态 `Create()` 从场景实例化（如 `NModdingScreen.Create()` → `PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("screens/modding/modding_screen")).Instantiate<NModdingScreen>(PackedScene.GenEditState.Disabled)`），惰性创建 + `this.AddChildSafely(submenu)` + `Visible = false`。

`NSubmenu`（自定义子界面的基类）：

```csharp
// MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSubmenu : Godot.Control, IScreenContext（abstract）
protected NSubmenuStack _stack;
protected Control? _lastFocusedControl;
public Control? DefaultFocusedControl => _lastFocusedControl ?? InitialFocusedControl;
protected abstract Control? InitialFocusedControl { get; }
protected virtual void ConnectSignals();        // 找子节点 "BackButton"(NBackButton) 并接 Released → _stack.Pop()
public void HideBackButtonImmediately();
public void SetStack(NSubmenuStack stack);
protected virtual void OnSubmenuShown();  protected virtual void OnSubmenuHidden();
public virtual void OnSubmenuOpened();    public virtual void OnSubmenuClosed();
```

⚠️ 与 NMainMenuTextButton 相同的坑：`_Ready` 里 `if (GetType() != typeof(NSubmenu)) throw ... "Don't call base._Ready()! Call ConnectSignals() instead."` —— 派生类要自己在 `_Ready` 里调 `ConnectSignals()`，且场景里必须有名为 `BackButton` 的 `NBackButton` 子节点（或者覆写 `ConnectSignals()` 不调 base）。

`IScreenContext`（`MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext`）：

```csharp
public interface IScreenContext
{
    Control? DefaultFocusedControl { get; }
    Control? FocusedControlFromTopBar => DefaultFocusedControl;
}
```

### 5.3 模态弹窗：`NModalContainer` + `NVerticalPopup` / `NGenericPopup`

```csharp
// MegaCrit.Sts2.Core.Nodes.CommonUi.NModalContainer : Godot.Control
public static NModalContainer? Instance { get; }
public IScreenContext? OpenModal { get; }
public void Add(Node modalToCreate, bool showBackstop = true);   // 已有弹窗时 Log.Warn 并忽略；modal 必须实现 IScreenContext
public void Clear();                                             // 关闭所有弹窗
public void ShowBackstop();  public void HideBackstop();
```

现成弹窗模板：

```csharp
// NVerticalPopup : Godot.Control（场景 res://scenes/ui/vertical_popup.tscn）
public NPopupYesNoButton YesButton { get; }  public NPopupYesNoButton NoButton { get; }
public void SetText(LocString title, LocString body);   // 也有 SetText(string, string)
public void InitYesButton(LocString yesButton, Action<NButton> onPressed);
public void InitNoButton(LocString noButton, Action<NButton> onPressed);
public void HideNoButton();  public void Close();  public void DisconnectSignals();  public void DisconnectHotkeys();

// NGenericPopup : Godot.Control, IScreenContext（场景 res://scenes/ui/generic_popup.tscn）
public static NGenericPopup? Create();
public Task<bool> WaitForConfirmation(LocString body, LocString header, LocString? noButton, LocString yesButton);

// NErrorPopup : NVerticalPopup, IScreenContext（场景 res://scenes/ui/error_popup.tscn）
public static NErrorPopup? Create(LocString title, LocString body, LocString? cancel, bool showReportBugButton);
public static NErrorPopup? Create(string title, string body, bool showReportBugButton);
```

### 5.4 游戏自带"从主菜单打开子界面/弹窗"调用链示例

**示例 A：主菜单 → 设置 → 模组界面（两级子菜单）**

```csharp
// NMainMenu._Ready:
_settingsButton.Connect(NClickableControl.SignalName.Released, Callable.From((Action<NButton>)OpenSettingsMenu));
public void OpenSettingsMenu() { _lastHitButton = _settingsButton; SubmenuStack.PushSubmenuType<NSettingsScreen>(); }

// NSettingsScreen._Ready（NSettingsScreen : NSubmenu）:
_moddingScreenButton = GetNode<NOpenModdingScreenButton>("%ModdingButton");
_moddingScreenButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OpenModdingScreen));
private void OpenModdingScreen(NButton _) { _stack.PushSubmenuType<NModdingScreen>(); }   // _stack 即 NSubmenuStack
```

**示例 B：主菜单 → 确认弹窗（含 await 等待结果）**

```csharp
private static async Task ConfirmAndQuit()
{
    NGenericPopup nGenericPopup = NGenericPopup.Create();
    NModalContainer.Instance.Add(nGenericPopup);
    if (await nGenericPopup.WaitForConfirmation(
            new LocString("main_menu_ui", "QUIT_CONFIRM_POPUP.body"),
            new LocString("main_menu_ui", "QUIT_CONFIRM_POPUP.header"),
            new LocString("main_menu_ui", "GENERIC_POPUP.cancel"),
            new LocString("main_menu_ui", "GENERIC_POPUP.confirm")))
        NGame.Instance?.Quit();
}
```

**模组配置界面的两种落地方式**：
1. **子菜单式（推荐，视觉最统一）**：自己做 `MyModConfigScreen : NSubmenu`（场景放 PCK `res://<modId>/scenes/mod_config.tscn`，含 `BackButton`(NBackButton) 子节点），按钮点击后 `NGame.Instance.MainMenu.SubmenuStack.Push(myScreen)`（`Push` 是 public）。栈机制会自动处理返回按钮、背景模糊、上层显隐。
2. **弹窗式（简单）**：做一个 `Control + IScreenContext`，`NModalContainer.Instance.Add(myPanel)`；注意同一时间只能有一个 modal，用完 `NModalContainer.Instance.Clear()` 或自己 QueueFree + `OpenModal` 归属问题（`Clear()` 会关掉所有弹窗）。

---

## 6. 主题 / 字体 / 纹理（res:// 约定）

### 6.1 路径约定（代码内工具）

```csharp
// MegaCrit.Sts2.Core.Helpers.SceneHelper
public static string GetScenePath(string innerPath) => "res://scenes/" + innerPath + ".tscn";
// MegaCrit.Sts2.Core.Helpers.ImageHelper
public static string GetImagePath(string innerPath) => "res://images/" + innerPath;   // 例: "ui/mods/folder.png"
// MegaCrit.Sts2.Core.Assets.AssetCache（PreloadManager.Cache）
public PackedScene GetScene(string path);
public Texture2D GetTexture2D(string path);
public Material GetMaterial(string path);
public CompressedTexture2D GetCompressedTexture2D(string path);
public bool ContainsKey(string path);  public void SetAsset(string path, Resource resource);
```

游戏内实际资源目录约定：
- 场景：`res://scenes/**.tscn`（如 `res://scenes/screens/main_menu.tscn`、`res://scenes/ui/vertical_popup.tscn`、`res://scenes/screens/settings_screen.tscn`、`res://scenes/screens/modding/modding_screen.tscn`）。
- 贴图：`res://images/...`（如 `res://images/ui/mods/steam_logo.png`、`res://images/packed/main_menu/submenu_icon_standard.png`）。
- 材质：`res://materials/**.tres`（如 `res://materials/transitions/fade_transition_mat.tres`、`res://materials/ui/hover_tip_debuff.tres`）。
- 字体：`res://themes/fonts/<lang>/*.tres`（如 `res://themes/fonts/zhs/noto_sans_mono_cjksc_regular_shared.tres`）。
- 本地化：`res://localization/<lang>/<table>.json`。
- 模组 PCK 内资源：一律 `res://<modId>/...` 前缀。

### 6.2 `ThemeConstants`（`MegaCrit.Sts2.addons.mega_text.ThemeConstants`，静态 StringName 常量）

```csharp
ThemeConstants.Label.FontSize / Font / LineSpacing / OutlineSize / FontColor / FontOutlineColor / FontShadowColor
ThemeConstants.RichTextLabel.NormalFont / BoldFont / ItalicsFont / LineSpacing
             / NormalFontSize / BoldFontSize / BoldItalicsFontSize / ItalicsFontSize / MonoFontSize / AllFontSizes
             / DefaultColor / FontOutlineColor / FontShadowColor
ThemeConstants.Control.Focus                 // "focus"（聚焦 stylebox 名）
ThemeConstants.MarginContainer.MarginLeft/Right/Top/Bottom
ThemeConstants.BoxContainer.Separation
ThemeConstants.FlowContainer.HSeparation / VSeparation
ThemeConstants.TextEdit.Font / ThemeConstants.LineEdit.Font
```

用法范式（游戏内所有 UI 文本都这么做，模组 UI 应照抄以跟随本地化字体）：

```csharp
title.ApplyLocaleFontSubstitution(FontType.Regular, ThemeConstants.Label.Font);
desc.ApplyLocaleFontSubstitution(FontType.Regular, ThemeConstants.RichTextLabel.NormalFont);
desc.ApplyLocaleFontSubstitution(FontType.Bold,    ThemeConstants.RichTextLabel.BoldFont);
desc.ApplyLocaleFontSubstitution(FontType.Italic,  ThemeConstants.RichTextLabel.ItalicsFont);
```

```csharp
// MegaCrit.Sts2.Core.Localization.Fonts
public enum FontType { Regular, Bold, Italic }
public static class FontControlUtils
{
    // 扩展方法：仅当 FontManager.NeedsFontSubstitution(LocManager.Instance.Language)（CJK/泰/俄等）时覆盖主题字体
    public static void ApplyLocaleFontSubstitution(this Control control, FontType fontType, StringName themeFontName);
}
```

### 6.3 `StsColors`（`MegaCrit.Sts2.Core.Helpers.StsColors`，UI 常用色板节选）

```csharp
cream = FFF6E2      gold = EFC851       aqua = 2AEBBE      red = FF5555
halfTransparentWhite / transparentWhite / quarterTransparentWhite
halfTransparentBlack / ninetyPercentBlack / screenBackdrop(黑 0.8)
darkBlue = 67AEEB   disabledTextForPotionPopup = 5E5E5E   lightGray / gray / disabledRed = BF3030 ...
```

主菜单按钮的悬停就是 `StsColors.cream → StsColors.gold` 的 tween（见 §2.3）。

### 6.4 NinePatch / 面板

游戏的面板/边框大多在 `.tscn` 里用 `NinePatchRect`/`Panel` + StyleBox 配好（C# 侧仅个别直接引用，如 `NJoinFriendButton` 里 `GetNode<NinePatchRect>("Image")`）。模组想跟随风格，最佳做法是**复制游戏场景结构**（参考 `res://scenes/ui/vertical_popup.tscn`、`res://scenes/screens/modding/modding_screen_row.tscn`），或直接 Duplicate 游戏已有控件（如 `NVerticalPopup`、`NModMenuRow` 的行样式）。聚焦样式覆写用 `ThemeConstants.Control.Focus`（`AddThemeStyleboxOverride(ThemeConstants.Control.Focus, new StyleBoxEmpty())` 是游戏去焦点框的做法）。

---

## 7. 落地配方汇总（"模组配置"按钮 + 界面）

1. **模组打包**：`mods/<modId>/` 下 `<modId>.json`（manifest：`id/name/author/version/has_dll/has_pck/min_game_version`）+ `<modId>.dll` + `<modId>.pck`。PCK 内放 `res://<modId>/localization/eng/main_menu_ui.json`（自定义 key 如 `"MOD_CONFIG.TITLE": "模组配置"`）以及自己的场景 `res://<modId>/scenes/mod_config.tscn`。
2. **入口**：DLL 里写 `[ModInitializer("Init")] static void Init()`（或不写 attribute 走 `Harmony.PatchAll`）。
3. **加按钮**：Harmony Postfix `NMainMenu._Ready`，在 `MainMenuTextButtons` 容器内 Duplicate 一个 `NMainMenuTextButton`，`SetLocalization("MOD_CONFIG")`（或直接 `label.Text`），`Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(...))`。
4. **开界面**：自定义 `MyConfigScreen : NSubmenu`（场景含 `BackButton`），点击时 `NGame.Instance.MainMenu?.SubmenuStack.Push(myScreen)`；或弹窗 `NModalContainer.Instance.Add(myPanel)`（myPanel 实现 `IScreenContext`）。
5. **读写配置**：`user://<modId>/config.json`（Godot FileAccess/DirAccess），或 `UserDataPathProvider.GetAccountScopedBasePath("<modId>")` 跟随游戏账号/存档目录结构（注意 `GetAccountDir()` 在 modded 运行时返回 `"modded"`）。
6. **枚举其它模组/展示信息**：`ModManager.Mods`（`manifest.name ?? manifest.id`、`version`、`state` 颜色参照 `NModMenuRow._Ready`：Loaded=White、Failed=StsColors.red、Disabled/AddedAtRuntime=StsColors.gray、None=StsColors.purple）；启用开关读 `SaveManager.Instance.SettingsSave.ModSettings?.IsModDisabled(mod)`。
7. **字体/颜色**：文本用 `MegaLabel`/`MegaRichTextLabel` + `ApplyLocaleFontSubstitution(...)`；颜色用 `StsColors`；悬停动画参考 `NMainMenuTextButton.OnFocus/OnUnfocus`（tween `label.Scale`/`SelfModulate`）。

### 附：主要类所在源码文件（反编译目录内，便于核对）

- `MegaCrit.Sts2.Core.Nodes.Screens.MainMenu\NMainMenu.cs`（1816 行）
- `MegaCrit.Sts2.Core.Nodes.Screens.MainMenu\NMainMenuTextButton.cs` / `NSubmenu.cs` / `NSubmenuStack.cs` / `NMainMenuSubmenuStack.cs` / `NSubmenuButton.cs` / `NShortSubmenuButton.cs` / `NSingleplayerSubmenu.cs`
- `MegaCrit.Sts2.Core.Nodes.GodotExtensions\NClickableControl.cs` / `NButton.cs`
- `MegaCrit.Sts2.Core.Nodes\NGame.cs` / `NSceneContainer.cs`
- `MegaCrit.Sts2.Core.Nodes.CommonUi\NModalContainer.cs` / `NVerticalPopup.cs` / `NErrorPopup.cs`
- `MegaCrit.Sts2.Core.Nodes.Multiplayer\NGenericPopup.cs`
- `MegaCrit.Sts2.Core.Localization\LocManager.cs` / `LocString.cs` / `LocTable.cs`
- `MegaCrit.Sts2.Core.Modding\ModManager.cs` / `Mod.cs` / `ModManifest.cs` / `ModInitializerAttribute.cs` / `ModSettings.cs` / `ModHelper.cs`
- `MegaCrit.Sts2.addons.mega_text\ThemeConstants.cs` / `MegaLabel.cs` / `MegaRichTextLabel.cs`
- `MegaCrit.Sts2.Core.Helpers\StsColors.cs` / `SceneHelper.cs` / `ImageHelper.cs`
- `MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen\NModdingScreen.cs` / `NModMenuRow.cs`（模组列表 UI 先例）
