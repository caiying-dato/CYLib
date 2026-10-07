using System;
using System.Collections.Generic;
using CYLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace CYLib.Ui;

/// <summary>
/// 「模组配置」全屏子菜单：左侧是已注册配置页的模组列表，右侧是选中模组的设置项。
/// 以 NSubmenu 身份挂进主菜单的 NMainMenuSubmenuStack，观感与"模式/时间线/百科"一致。
///
/// 生命周期：
///   OnSubmenuShown  → 重建模组列表与内容、订阅变更
///   OnSubmenuHidden → 保存全部配置、退订
///   _Process        → 改动后 0.5 秒防抖自动保存
/// </summary>
public partial class NCyConfigSubmenu : NSubmenu
{
    private new NBackButton? _backButton;

    private VBoxContainer _modList = null!;
    private VBoxContainer _content = null!;
    private Button? _restoreDefaultsButton;

    private ConfigPage? _currentPage;
    private readonly List<(SettingDef Def, SettingRow Row)> _rows = new();
    private readonly List<(Label Label, Func<string> Resolver)> _dynamicTexts = new();
    private readonly List<Button> _modButtons = new();

    private double _saveTimer = -1;
    private const double AutosaveDelay = 0.5;

    private const float SidebarWidth = 380f;

    protected override Control? InitialFocusedControl =>
        _modButtons.Count > 0 ? _modButtons[0] : _restoreDefaultsButton;

    public NCyConfigSubmenu()
    {
        Name = "NCyConfigSubmenu";
        SetAnchorsPreset(LayoutPreset.FullRect);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;
        MouseFilter = MouseFilterEnum.Stop;

        var frame = new MarginContainer();
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        frame.AddThemeConstantOverride("margin_left", 140);
        frame.AddThemeConstantOverride("margin_right", 140);
        frame.AddThemeConstantOverride("margin_top", 60);
        frame.AddThemeConstantOverride("margin_bottom", 120);
        AddChild(frame);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 22);
        frame.AddChild(root);

        // ── 标题行 ─────────────────────────────────────────────
        root.AddChild(CyStyle.CreateTitle(CyText.Get("CYLIB.window.title", "模组配置")));
        root.AddChild(CyStyle.CreateDescription(CyText.Get("CYLIB.window.subtitle",
            "选择左侧的模组以配置它的选项。改动会自动保存。")));

        // ── 左右两栏 ───────────────────────────────────────────
        var columns = new HBoxContainer();
        columns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        columns.AddThemeConstantOverride("separation", 26);
        root.AddChild(columns);

        // 左栏：模组列表
        var leftPanel = new PanelContainer();
        leftPanel.CustomMinimumSize = new Vector2(SidebarWidth, 0);
        leftPanel.AddThemeStyleboxOverride("panel", CyStyle.CreatePanelStyle());
        columns.AddChild(leftPanel);

        var leftScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        leftPanel.AddChild(leftScroll);

        _modList = new VBoxContainer();
        _modList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _modList.AddThemeConstantOverride("separation", 10);
        leftScroll.AddChild(_modList);

        // 右栏：设置内容
        var rightPanel = new PanelContainer();
        rightPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        rightPanel.AddThemeStyleboxOverride("panel", CyStyle.CreatePanelStyle());
        columns.AddChild(rightPanel);

        var rightScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        rightPanel.AddChild(rightScroll);

        _content = new VBoxContainer();
        _content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _content.AddThemeConstantOverride("separation", 10);
        rightScroll.AddChild(_content);

        // 右栏页头（随选中模组变化，在 ShowPage 里重建）
    }

    public override void _Ready()
    {
        // 基类 NSubmenu 要求一个名为 BackButton 的 NBackButton 子节点（ConnectSignals 会接管它）。
        _backButton = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/back_button"))
            .Instantiate<NBackButton>();
        _backButton.Name = "BackButton";
        AddChild(_backButton);

        ConnectSignals();
        SetProcess(true); // 自动保存防抖计时
    }

    protected override void OnSubmenuShown()
    {
        // 先减后加：防止重复订阅（连续两次 Shown 而没有 Hidden 的情况）
        CYLibConfig.PageRegistered -= OnPageRegistered;
        CYLibConfig.PageRegistered += OnPageRegistered;
        CYLibConfig.ValueChanged -= OnValueChanged;
        CYLibConfig.ValueChanged += OnValueChanged;

        RebuildModList();
        if (_currentPage == null || CYLibConfig.GetPage(_currentPage.ModId) == null)
            _currentPage = CYLibConfig.GetPages().FirstOrDefaultWithVisibleSettings();
        ShowPage(_currentPage);
    }

    protected override void OnSubmenuHidden()
    {
        CYLibConfig.PageRegistered -= OnPageRegistered;
        CYLibConfig.ValueChanged -= OnValueChanged;
        FlushSave();
    }

    public override void _Process(double delta)
    {
        if (_saveTimer < 0) return;
        _saveTimer -= delta;
        if (_saveTimer <= 0) FlushSave();
    }

    // ───────────────────────── 自动保存 ─────────────────────────

    private void MarkDirty()
    {
        _saveTimer = AutosaveDelay;
    }

    private void FlushSave()
    {
        _saveTimer = -1;
        try
        {
            CYLibConfig.Save();
        }
        catch (Exception ex)
        {
            CYLibEntry.Log.Error($"[ConfigUI] 保存配置失败: {ex}");
        }
    }

    // ───────────────────────── 左栏：模组列表 ─────────────────────────

    private void RebuildModList()
    {
        foreach (var child in _modList.GetChildren())
        {
            _modList.RemoveChild(child);
            child.QueueFree();
        }
        _modButtons.Clear();

        var pages = CYLibConfig.GetPages();
        if (pages.Count == 0)
        {
            _modList.AddChild(CyStyle.CreateDescription(CyText.Get("CYLIB.list.empty",
                "还没有模组注册配置页。")));
            AddProviderLinks();
            return;
        }

        foreach (var page in pages)
        {
            if (!page.HasVisibleSettings() && page.ModId != _currentPage?.ModId) continue;

            var button = CyStyle.CreateButton(page.Title.Resolve(), "ghost");
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.CustomMinimumSize = new Vector2(SidebarWidth - 40, 56);
            button.Alignment = HorizontalAlignment.Left;

            var target = page;
            button.Pressed += () =>
            {
                if (_currentPage?.ModId != target.ModId)
                    ShowPage(target);
            };

            _modList.AddChild(button);
            _modButtons.Add(button);
        }

        RefreshModButtonStyles();

        // 上下焦点串成一条链，方便键盘/手柄导航
        for (var i = 0; i < _modButtons.Count; i++)
        {
            _modButtons[i].FocusNeighborLeft = new NodePath(".");
            _modButtons[i].FocusNeighborRight = new NodePath(".");
            if (_modButtons.Count < 2) continue;
            var up = _modButtons[(i - 1 + _modButtons.Count) % _modButtons.Count];
            var down = _modButtons[(i + 1) % _modButtons.Count];
            _modButtons[i].FocusNeighborTop = _modButtons[i].GetPathTo(up);
            _modButtons[i].FocusNeighborBottom = _modButtons[i].GetPathTo(down);
        }

        AddProviderLinks();
    }

    /// <summary>
    /// 列表底部：跳转到 BaseLib / RitsuLib 自己的配置菜单（装了才有）。
    /// 统一入口模式下它们的重复按钮被隐藏，但配置内容从这里一键可达。
    /// </summary>
    private void AddProviderLinks()
    {
        var providers = ExternalConfigProviders.All;
        if (providers.Count == 0) return;

        var stack = FindSubmenuStack();
        var any = false;

        foreach (var provider in providers)
        {
            if (provider.MainMenuStackOnly && stack is not NMainMenuSubmenuStack) continue;
            if (!any)
            {
                any = true;
                _modList.AddChild(CyStyle.CreateDescription(CyText.Get("CYLIB.link.header", "其它配置库")));
            }

            var button = CyStyle.CreateButton(CyText.Get(provider.NameKey, provider.FallbackName), "ghost");
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.CustomMinimumSize = new Vector2(SidebarWidth - 40, 56);
            button.Alignment = HorizontalAlignment.Left;

            var target = provider.SubmenuType;
            button.Pressed += () =>
            {
                if (stack != null) ExternalConfigProviders.PushSubmenu(stack, target);
            };

            _modList.AddChild(button);
        }
    }

    private NSubmenuStack? FindSubmenuStack()
    {
        for (var node = GetParent(); node != null; node = node.GetParent())
            if (node is NSubmenuStack stack) return stack;
        return null;
    }

    private void RefreshModButtonStyles()
    {
        foreach (var button in _modButtons)
        {
            var selected = _currentPage != null && button.Text == _currentPage.Title.Resolve();
            var style = CyStyle.CreateButtonStyle(
                selected ? CyStyle.PanelBgStrong : CyStyle.PanelBg,
                selected ? CyStyle.Gold : CyStyle.Border);
            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", CyStyle.CreateButtonStyle(
                selected ? CyStyle.PanelBgStrong : CyStyle.PanelBg, CyStyle.Gold, hover: true));
            button.AddThemeStyleboxOverride("focus", CyStyle.CreateButtonStyle(
                selected ? CyStyle.PanelBgStrong : CyStyle.PanelBg, CyStyle.Gold));
        }
    }

    // ───────────────────────── 右栏：设置内容 ─────────────────────────

    private void ShowPage(ConfigPage? page)
    {
        _currentPage = page;

        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
        _rows.Clear();
        _dynamicTexts.Clear();
        _restoreDefaultsButton = null;

        _content.AddChild(CyStyle.CreateTitle(page != null ? page.Title.Resolve() : string.Empty, CyStyle.FontSizeSection));

        var pageDescription = page?.Description.Resolve() ?? string.Empty;
        if (!string.IsNullOrEmpty(pageDescription))
            _content.AddChild(CyStyle.CreateDescription(pageDescription));

        if (page == null)        {
            _content.AddChild(CyStyle.CreateDescription(CyText.Get("CYLIB.content.empty",
                "没有可显示的配置页。")));
            RefreshModButtonStyles();
            return;
        }

        foreach (var section in page.Sections)
            AddSection(page, section);

        // ── 底部操作区 ───────────────────────────────────────
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 16);
        footer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
        _content.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
        _content.AddChild(footer);

        var restore = CyStyle.CreateButton(CyText.Get("CYLIB.btn.restore_defaults", "重置为默认"), "danger");
        restore.Pressed += OnRestoreDefaultsPressed;
        footer.AddChild(restore);
        _restoreDefaultsButton = restore;

        var pathLabel = CyStyle.CreateDescription(CyText.Get("CYLIB.config_path", "配置文件：") + CYLibConfig.GetFilePath(page.ModId));
        pathLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pathLabel.VerticalAlignment = VerticalAlignment.Center;
        footer.AddChild(pathLabel);

        RefreshModButtonStyles();
    }

    private void AddSection(ConfigPage page, ConfigSection section)
    {
        // 小节标题（可折叠）
        var contentBox = new VBoxContainer();
        contentBox.AddThemeConstantOverride("separation", 4);

        var header = CyStyle.CreateButton(SectionHeaderText(section, !section.Collapsed), "ghost", CyStyle.FontSizeSection);
        header.Alignment = HorizontalAlignment.Left;
        header.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.Pressed += () =>
        {
            contentBox.Visible = !contentBox.Visible;
            header.Text = SectionHeaderText(section, contentBox.Visible);
        };
        _content.AddChild(header);

        var sectionDescription = section.Description.Resolve();
        if (!string.IsNullOrEmpty(sectionDescription))
            _content.AddChild(CyStyle.CreateDescription(sectionDescription));

        _content.AddChild(contentBox);

        foreach (var item in section.Items)
        {
            switch (item)
            {
                case SettingDef def:
                {
                    var row = SettingRowFactory.Create(CYLibConfig.Store, page, def);
                    var root = row.Root;

                    if (def.Hidden)
                        break;

                    // 一次刷新 = 控件值 + 条件显隐 + 条件置灰（三者都随改动重新求值）
                    void RefreshRow()
                    {
                        if (def.VisibleIf != null)
                            root.Visible = SafePredicate(def.VisibleIf);
                        if (def.EnabledIf != null)
                            SetEnabledRecursive(root, SafePredicate(def.EnabledIf));
                        row.Refresh();
                    }

                    RefreshRow();
                    _rows.Add((def, new SettingRow { Root = root, Refresh = RefreshRow }));

                    if (def.RequiresRestart)
                        WrapWithRestartHint(root, def);

                    contentBox.AddChild(root);
                    break;
                }
                case InfoItem info:
                {
                    // 支持动态文本（随配置变动刷新）与自定义字号/颜色
                    var label = CyStyle.CreateLabel(
                        info.Text.Resolve(),
                        info.FontSize ?? CyStyle.FontSizeSmall,
                        info.Color ?? new Color(CyStyle.Cream.R, CyStyle.Cream.G, CyStyle.Cream.B, 0.7f));
                    contentBox.AddChild(label);
                    _dynamicTexts.Add((label, () => info.Text.Resolve()));
                    break;
                }
                case HeaderItem headerItem:
                {
                    var titleLabel = CyStyle.CreateLabel(headerItem.Title.Resolve(), CyStyle.FontSizeLabel, CyStyle.Gold);
                    contentBox.AddChild(titleLabel);
                    _dynamicTexts.Add((titleLabel, () => headerItem.Title.Resolve()));

                    if (!headerItem.Description.IsEmpty || headerItem.Description.IsDynamic)
                    {
                        var descLabel = CyStyle.CreateDescription(headerItem.Description.Resolve());
                        contentBox.AddChild(descLabel);
                        _dynamicTexts.Add((descLabel, () => headerItem.Description.Resolve()));
                    }

                    break;
                }
            }
        }
    }

    private static string SectionHeaderText(ConfigSection section, bool expanded) =>
        (expanded ? "▼  " : "▶  ") + section.Title.Resolve();

    private void WrapWithRestartHint(Control row, SettingDef def)
    {
        // 追加到说明文字下方（塞进行尾会挤歪控件列，之前排版就是这么坏的）；
        // RestartNote 说明"为什么需要重启"，支持动态文本
        string BuildNote()
        {
            var note = CyText.Get("CYLIB.restart_required", "（需要重启生效）");
            var reason = def.RestartNote.Resolve();
            return string.IsNullOrEmpty(reason) ? note : $"{note}：{reason}";
        }

        var label = CyStyle.AppendRowNote(row, BuildNote());
        if (label != null && def.RestartNote.IsDynamic)
            _dynamicTexts.Add((label, BuildNote));
    }

    private static void SetEnabledRecursive(Control control, bool enabled)
    {
        control.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.55f);
        if (control is BaseButton button) button.Disabled = !enabled;
        if (control is LineEdit edit) edit.Editable = enabled;
        if (control is OptionButton option) option.Disabled = !enabled;
    }

    private static bool SafePredicate(Func<bool> predicate)
    {
        try
        {
            return predicate();
        }
        catch (Exception ex)
        {
            CYLibEntry.Log.Error($"[ConfigUI] 设置项可见性/可用性回调抛异常: {ex}");
            return true;
        }
    }

    // ───────────────────────── 事件 ─────────────────────────

    private void OnPageRegistered(ConfigPage page)
    {
        // 注册发生在界面打开期间：刷新左栏（右栏保持当前页）
        RebuildModList();
    }

    private void OnValueChanged(ConfigValueChanged change)
    {
        MarkDirty();

        if (_currentPage == null || change.ModId != _currentPage.ModId) return;

        // 任何来源的改动都要回读刷新：UI 点击也必须让开关/条件显隐立即反映新值，
        // 否则开关"点了没反应"（值存了但显示不更新）。
        foreach (var (def, row) in _rows)
        {
            if (def.Id == change.Key || def.VisibleIf != null || def.EnabledIf != null ||
                def.Label.IsDynamic || def.Description.IsDynamic)
                row.Refresh();
        }

        // 动态展示文字（Info/Header 的解析器文本）随任何改动重新求值
        foreach (var (label, resolver) in _dynamicTexts)
        {
            if (!IsInstanceValid(label)) continue;
            var text = resolver();
            if (label.Text != text) label.Text = text;
        }
    }

    private async void OnRestoreDefaultsPressed()
    {
        if (_currentPage == null) return;

        try
        {
            var popup = NGenericPopup.Create();
            var container = NModalContainer.Instance;
            if (popup == null || container == null)
                throw new InvalidOperationException("弹窗容器不可用");
            container.Add(popup);
            var confirmed = await popup.WaitForConfirmation(
                CyText.Loc("CYLIB.restore_confirm.body"),
                CyText.Loc("CYLIB.restore_confirm.header"),
                null,
                CyText.Loc("CYLIB.restore_confirm.yes"));

            if (!confirmed || _currentPage == null) return;

            CYLibConfig.RestoreDefaults(_currentPage.ModId);
            ShowPage(_currentPage);
        }
        catch (Exception ex)
        {
            CYLibEntry.Log.Error($"[ConfigUI] 重置确认弹窗失败，直接重置: {ex}");
            if (_currentPage != null)
            {
                CYLibConfig.RestoreDefaults(_currentPage.ModId);
                ShowPage(_currentPage);
            }
        }
    }
}

internal static class ConfigPageListExtensions
{
    public static ConfigPage? FirstOrDefaultWithVisibleSettings(this IReadOnlyList<ConfigPage> pages)
    {
        foreach (var page in pages)
            if (page.HasVisibleSettings())
                return page;
        return pages.Count > 0 ? pages[0] : null;
    }
}
