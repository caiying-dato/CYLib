using System;
using CYLib.Ui;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace CYLib.Patches;

/// <summary>
/// 在 设置 → 游戏设置（General）里加一行「模组配置 (CYLib)」入口，
/// 与 BaseLib / RitsuLib 的做法一致：复制原版 "Modding" 那一行（自带样式与按钮），
/// 换掉文案与点击行为，插在它旁边。
///
/// 点击后把 CYLib 的配置子菜单压进当前子菜单栈（主菜单栈或局内栈都可以）。
/// </summary>
[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
internal static class SettingsScreenEntryPatch
{
    private const string RowNodeName = "CyLibModConfig";
    private const string DividerNodeName = "CyLibModConfigDivider";

    /// <summary>行标题：统一入口开启时「模组配置」，关闭时带 (CYLib) 标识与其它库的同名入口区分。</summary>
    private static string RowTitle() =>
        ExternalConfigProviders.MergeEntries
            ? CyText.Get("CYLIB.settings_row.title", "模组配置")
            : CyText.Get("CYLIB.settings_row.title_distinct", "模组配置 (CYLib)");

    private static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            InjectEntry(__instance);
        }
        catch (Exception ex)
        {
            CYLibEntry.Log.Error("[CYLib] 无法在设置界面注入「模组配置」入口，不影响其它功能。");
            CYLibEntry.Log.Error(ex.ToString());
        }
    }

    private static void InjectEntry(NSettingsScreen screen)
    {
        var panel = screen.GetNodeOrNull<NSettingsPanel>("%GeneralSettings");
        var content = panel?.Content;
        if (content == null)
        {
            CYLibEntry.Log.Warn("[CYLib] 找不到设置界面的 GeneralSettings 面板，跳过设置入口注入。");
            return;
        }

        if (content.GetNodeOrNull<Control>(RowNodeName) != null)
            return; // 已注入

        // ── 首选：复制原版 "Modding" 行（观感与原生完全一致）─────────
        var sourceRow = content.GetNodeOrNull<Control>("Modding");
        Control row;
        if (sourceRow != null)
        {
            row = (Control)sourceRow.Duplicate();
            row.Name = RowNodeName;
            ClearUniqueNames(row);
            ResetFocusNeighbors(row);
            row.Visible = true; // 局内时原版 Modding 行是隐藏的，复制件必须强制可见
            content.AddChild(row);

            // 行标题
            if (row.GetNodeOrNull<MegaRichTextLabel>("Label") is { } richLabel)
                richLabel.Text = RowTitle();
            else if (row.GetNodeOrNull<MegaLabel>("Label") is { } megaLabel)
                megaLabel.SetTextAutoSize(RowTitle());

            // 行内按钮（NOpenModdingScreenButton / NSettingsButton）
            var button = FindDescendant<NSettingsButton>(row);
            if (button != null)
            {
                if (button.GetNodeOrNull<MegaLabel>("Label") is { } buttonLabel)
                    buttonLabel.SetTextAutoSize(CyText.Get("CYLIB.settings_row.button", "打开配置"));
                button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OpenConfig(screen)));
            }
            else
            {
                CYLibEntry.Log.Warn("[CYLib] Modding 行里找不到按钮，入口行将不可点击。");
            }

            // 分隔线：同样复制原版
            var sourceDivider = content.GetNodeOrNull<Control>("ModdingDivider");
            if (sourceDivider != null)
            {
                var divider = (Control)sourceDivider.Duplicate();
                divider.Name = DividerNodeName;
                ClearUniqueNames(divider);
                divider.Visible = true;
                content.AddChild(divider);

                // 插到 Modding 行（及其分隔线）后面
                var anchor = sourceDivider.GetIndex();
                content.MoveChild(divider, anchor + 1);
                content.MoveChild(row, anchor + 2);
            }
            else
            {
                content.MoveChild(row, sourceRow.GetIndex() + 1);
            }
        }
        else
        {
            // ── 兜底：原版行结构变了就自建一行 ─────────────────────
            row = CyStyle.CreateSettingsRow(
                RowTitle(),
                CyText.Get("CYLIB.settings_row.button", "打开配置"),
                () => OpenConfig(screen));
            row.Name = RowNodeName;
            content.AddChild(row);
        }

        // 重新计算面板高度与焦点导航（原版只在自己增删行时刷新）。
        // 两个都是私有方法（Godot 注册方法），用 Call 走引擎调度调用。
        try
        {
            panel?.Call(NSettingsPanel.MethodName.RefreshSize);
            panel?.Call(NSettingsPanel.MethodName.UpdateNavigation);
        }
        catch (Exception ex)
        {
            CYLibEntry.Log.Warn($"[CYLib] 刷新设置面板尺寸失败（仅影响排版）: {ex.Message}");
        }

        // 统一入口：帧末隐藏其它配置库的重复行（不管双方补丁先后顺序）
        Callable.From(() => ExternalConfigProviders.HideDuplicateSettingsRows(screen)).CallDeferred();
    }

    private static void OpenConfig(NSettingsScreen screen)
    {
        for (var node = screen.GetParent(); node != null; node = node.GetParent())
        {
            if (node is NSubmenuStack stack)
            {
                stack.PushSubmenuType(typeof(NCyConfigSubmenu));
                return;
            }
        }

        CYLibEntry.Log.Warn("[CYLib] 找不到子菜单栈，无法打开模组配置界面。");
    }

    /// <summary>去掉复制子树里的"唯一节点名"标记，避免与原件的 %Name 查找冲突。</summary>
    private static void ClearUniqueNames(Node node)
    {
        node.UniqueNameInOwner = false;
        foreach (var child in node.GetChildren())
            ClearUniqueNames(child);
    }

    /// <summary>复制件保留着原版的焦点邻居路径（指向别的行），清空让引擎自动计算。</summary>
    private static void ResetFocusNeighbors(Node node)
    {
        if (node is Control control)
        {
            var empty = new NodePath();
            control.FocusNeighborLeft = empty;
            control.FocusNeighborTop = empty;
            control.FocusNeighborRight = empty;
            control.FocusNeighborBottom = empty;
            control.FocusNext = empty;
            control.FocusPrevious = empty;
        }

        foreach (var child in node.GetChildren())
            ResetFocusNeighbors(child);
    }

    private static T? FindDescendant<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindDescendant<T>(child);
            if (found != null) return found;
        }

        return null;
    }
}

/// <summary>
/// 入口去重也要挂在 OnSubmenuOpened 上：RitsuLib 每次打开设置都会把它的行
/// `Visible = true` 重新显示，只在 _Ready 隐藏一次会被它翻回来。
/// 延迟到帧末执行，保证跑在所有补丁（含 RitsuLib 的 Priority.Last）之后。
/// </summary>
[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen.OnSubmenuOpened))]
internal static class SettingsScreenDedupePatch
{
    private static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            Callable.From(() => ExternalConfigProviders.HideDuplicateSettingsRows(__instance)).CallDeferred();
        }
        catch (Exception ex)
        {
            CYLibEntry.Log.Warn($"[CYLib] 隐藏重复配置入口失败: {ex.Message}");
        }
    }
}
