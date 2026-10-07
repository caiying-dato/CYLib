using System;
using CYLib.Ui;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace CYLib.Patches;

/// <summary>
/// 把「模组配置」按钮注入主菜单（模式/时间线/百科那排按钮所在的位置）。
/// 做法与 BaseLib 一致：复制游戏自带的 SettingsButton，换文案、换行为。
///
/// ★ 按钮节点名必须唯一（BaseLib 也叫 "ModConfigButton"）：
///   早期版本撞名导致防重逻辑误判"已注入"而跳过，装了 BaseLib 后 CYLib 入口就消失了。
///   现在固定用 "CYLibModConfigButton"，并只按这个名字判重。
/// </summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class MainMenuConfigButtonPatch
{
    public const string ButtonNodeName = "CYLibModConfigButton";

    private static void Prefix(NMainMenu __instance)
    {
        try
        {
            InjectMainMenuEntry(__instance);
        }
        catch (Exception ex)
        {
            CYLibEntry.Log.Error("[CYLib] 无法在主菜单注入「模组配置」按钮（游戏更新可能导致路径失效），不影响其它功能。");
            CYLibEntry.Log.Error(ex.ToString());
        }
    }

    private static void InjectMainMenuEntry(NMainMenu mainMenu)
    {
        var settingsButton = mainMenu.GetNodeOrNull<NMainMenuTextButton>("MainMenuTextButtons/SettingsButton");
        if (settingsButton == null)
        {
            CYLibEntry.Log.Warn("[CYLib] 找不到 MainMenuTextButtons/SettingsButton，跳过主菜单入口注入。");
            return;
        }

        var container = settingsButton.GetParent();
        if (container == null || container.GetNodeOrNull<NMainMenuTextButton>(ButtonNodeName) != null)
            return; // 已经注入过（防止重复）

        var modConfigButton = (NMainMenuTextButton)settingsButton.Duplicate();
        modConfigButton.Name = ButtonNodeName;
        settingsButton.AddSibling(modConfigButton);

        // 统一入口开启时：文案就是「模组配置」（其它库入口已被隐藏，无需区分）；
        // 关闭统一时：带 (CYLib) 标识与 BaseLib/RitsuLib 的同名按钮区分。
        // 注意文案在注入时定死（开关改动需重启生效，两者时机一致）。
        modConfigButton.SetLocalization(
            ExternalConfigProviders.MergeEntries ? "CYLIB.MAIN_MENU" : "CYLIB.MAIN_MENU_DISTINCT");

        modConfigButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
        {
            try
            {
                // 记住来源按钮，子菜单关闭后焦点回到它
                mainMenu._lastHitButton = modConfigButton;
            }
            catch
            {
                /* 字段访问失败不影响打开界面 */
            }

            mainMenu.SubmenuStack.PushSubmenuType(typeof(NCyConfigSubmenu));
        }));

        // 稍微加宽悬停热区，并把左右焦点邻居锁回自身（避免左右方向键被"吸"到新按钮上）
        modConfigButton.CustomMinimumSize = new Vector2(300, modConfigButton.CustomMinimumSize.Y);
        var selfNodePath = new NodePath(".");
        modConfigButton.FocusNeighborLeft = selfNodePath;
        modConfigButton.FocusNeighborRight = selfNodePath;

        // 隐藏其它库的重复入口放到帧末执行：不管双方补丁先后顺序都能收敛到"只剩一个按钮"
        Callable.From(() => Ui.ExternalConfigProviders.HideDuplicateMainMenuButtons(mainMenu)).CallDeferred();
    }
}
