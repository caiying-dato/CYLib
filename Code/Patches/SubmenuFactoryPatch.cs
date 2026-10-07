using System;
using System.Runtime.CompilerServices;
using CYLib.Ui;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace CYLib.Patches;

/// <summary>
/// 让子菜单栈认识 CYLib 的配置子菜单：游戏按 Type 取子菜单实例，
/// 这里拦截 typeof(NCyConfigSubmenu)，为每个栈懒创建一个实例挂在栈节点下。
///
/// 两个栈都要打：
///   NMainMenuSubmenuStack —— 主菜单的「模组配置」按钮；
///   NRunSubmenuStack      —— 局内 设置→游戏设置 里的入口。
/// 注意与 BaseLib/RitsuLib 的同类补丁共存：它们只认自己的类型，对我们的类型返回 true 放行。
/// </summary>
[HarmonyPatch(typeof(NMainMenuSubmenuStack), nameof(NMainMenuSubmenuStack.GetSubmenuType), new[] { typeof(Type) })]
internal static class SubmenuFactoryPatch
{
    private static readonly ConditionalWeakTable<NSubmenuStack, NCyConfigSubmenu> Submenus = new();

    private static bool Prefix(NMainMenuSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        if (type != typeof(NCyConfigSubmenu)) return true;
        __result = Submenus.GetValue(__instance, CreateSubmenu);
        return false;
    }

    internal static NCyConfigSubmenu CreateSubmenu(NSubmenuStack stack)
    {
        var submenu = new NCyConfigSubmenu
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
        };
        stack.AddChild(submenu);
        return submenu;
    }
}

[HarmonyPatch(typeof(NRunSubmenuStack), nameof(NRunSubmenuStack.GetSubmenuType), new[] { typeof(Type) })]
internal static class RunSubmenuFactoryPatch
{
    private static readonly ConditionalWeakTable<NSubmenuStack, NCyConfigSubmenu> Submenus = new();

    private static bool Prefix(NRunSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        if (type != typeof(NCyConfigSubmenu)) return true;
        __result = Submenus.GetValue(__instance, SubmenuFactoryPatch.CreateSubmenu);
        return false;
    }
}
