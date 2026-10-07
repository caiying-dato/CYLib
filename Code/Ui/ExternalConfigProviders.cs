using System;
using System.Collections.Generic;
using CYLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace CYLib.Ui;

/// <summary>
/// 与 BaseLib / RitsuLib 等其它"模组配置"入口提供者的共存逻辑：
///
/// 1. 【统一入口】CYLib 提供唯一入口时，把对方的重复入口隐藏掉
///    （主菜单按钮 / 设置行），避免界面上出现好几个"模组配置"；
/// 2. 【不丢内容】在 CYLib 的模组列表底部放"跳转"按钮，一键打开对方
///    自己的配置菜单，BaseLib/RitsuLib 注册的模组配置照样可用；
/// 3. 【不硬依赖】对方的子菜单类型用反射按名字找，找不到就少一个跳转，
///    版本升级/未安装都不影响 CYLib 自身。
/// </summary>
internal static class ExternalConfigProviders
{
    /// <summary>一个外部配置入口提供者。</summary>
    internal sealed record Provider(Type SubmenuType, string NameKey, string FallbackName, bool MainMenuStackOnly);

    private static List<Provider>? _providers;

    /// <summary>已发现的外部提供者（可能为空）。</summary>
    internal static IReadOnlyList<Provider> All => _providers ??= Discover();

    /// <summary>是否开启"统一入口"（隐藏对方重复入口）。</summary>
    internal static bool MergeEntries =>
        CYLibConfig.GetBool(CyLibOwnConfig.ModId, CyLibOwnConfig.MergeEntriesKey, true);

    // ───────────────────────── 发现外部子菜单类型 ─────────────────────────

    private static List<Provider> Discover()
    {
        var list = new List<Provider>();

        // BaseLib：BaseLib.Config.UI.NModConfigSubmenu（只在主菜单栈注册了工厂）
        var baseLib = FindType("NModConfigSubmenu", "BaseLib");
        if (baseLib != null)
            list.Add(new Provider(baseLib, "CYLIB.link.baselib", "BaseLib 模组配置", MainMenuStackOnly: true));

        // RitsuLib：RitsuModSettingsSubmenu（主菜单栈 + 局内栈都注册了工厂）
        var ritsuLib = FindType("RitsuModSettingsSubmenu", "STS2RitsuLib");
        if (ritsuLib != null)
            list.Add(new Provider(ritsuLib, "CYLIB.link.ritsulib", "RitsuLib 模组设置", MainMenuStackOnly: false));

        return list;
    }

    private static Type? FindType(string simpleName, string namespacePrefix)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                continue; // 动态程序集可能加载失败，跳过即可
            }

            foreach (var type in types)
            {
                if (type.Name == simpleName &&
                    type.Namespace != null &&
                    type.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
                    return type;
            }
        }

        return null;
    }

    // ───────────────────────── 隐藏重复入口 ─────────────────────────

    /// <summary>隐藏其它库在主菜单注入的"模组配置"按钮（BaseLib 的节点名是 ModConfigButton）。</summary>
    internal static void HideDuplicateMainMenuButtons(Node mainMenu)
    {
        if (!MergeEntries) return;
        foreach (var nodeName in new[] { "ModConfigButton", "BaseLibModConfigButton", "RitsuLibModConfigButton" })
        {
            if (mainMenu.GetNodeOrNull<Control>($"MainMenuTextButtons/{nodeName}") is { } duplicate)
                HideNode(duplicate);
        }
    }

    /// <summary>隐藏其它库在 设置→游戏设置 里注入的"模组配置"行。</summary>
    internal static void HideDuplicateSettingsRows(NSettingsScreen screen)
    {
        if (!MergeEntries) return;
        var content = screen.GetNodeOrNull<NSettingsPanel>("%GeneralSettings")?.Content;
        if (content == null) return;

        foreach (var nodeName in new[] { "BaseLibModConfig", "RitsuLibModSettings", "RitsuLibModSettingsDivider" })
        {
            if (content.GetNodeOrNull<Control>(nodeName) is { } duplicate)
                HideNode(duplicate);
        }

        // BaseLib 的分隔线是复制出来的、名字不固定：隐藏 "BaseLibModConfig" 前面那条 Divider
        if (content.GetNodeOrNull<Control>("BaseLibModConfig") is { } row)
        {
            var index = row.GetIndex();
            if (index > 0 && content.GetChild(index - 1) is Control divider && divider.Name.ToString().Contains("Divider"))
                HideNode(divider);
        }
    }

    private static void HideNode(Control node)
    {
        node.Visible = false;
        node.MouseFilter = Control.MouseFilterEnum.Ignore;
        node.FocusMode = Control.FocusModeEnum.None;
    }

    // ───────────────────────── 跳转到对方的配置菜单 ─────────────────────────

    /// <summary>把对方的配置子菜单压进给定的栈；失败只记日志，不影响当前界面。</summary>
    internal static void PushSubmenu(NSubmenuStack stack, Type submenuType)
    {
        try
        {
            stack.PushSubmenuType(submenuType);
        }
        catch (Exception ex)
        {
            CYLibEntry.Log.Error($"[CYLib] 打开 {submenuType.Name} 失败: {ex}");
        }
    }
}
