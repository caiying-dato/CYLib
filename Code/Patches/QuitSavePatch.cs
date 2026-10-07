using CYLib.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace CYLib.Patches;

/// <summary>游戏退出前把所有脏配置落盘（防自动保存防抖期间退出丢改动）。</summary>
[HarmonyPatch(typeof(NGame), nameof(NGame.Quit))]
internal static class QuitSavePatch
{
    private static void Prefix()
    {
        try
        {
            CYLibConfig.Save();
        }
        catch (System.Exception ex)
        {
            CYLibEntry.Log.Error($"[CYLib] 退出时保存配置失败: {ex}");
        }
    }
}
