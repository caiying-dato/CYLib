using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace CYLib;

/// <summary>
/// CYLib 入口：CY 系列模组的配置平台。
/// 职责：
///   1. 提供配置注册/取值 API（CYLib.Config 命名空间，供其他模组调用）；
///   2. 在主界面注入「模组配置」入口并渲染各模组的配置页（CYLib.Ui 命名空间）；
///   3. 配置持久化（%APPDATA%\SlayTheSpire2\mod_configs\）。
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class CYLibEntry
{
    public const string ModId = "CYLib";
    public const string Version = "0.1.4";

    internal static readonly Logger Log = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        Log.Info($"CYLib {Version} initializing.");

        // 先注册 CYLib 自己的配置页（其它模组在各自的初始化里注册自己的页）
        Config.CyLibOwnConfig.Register();

        // ★ 逐个补丁类打，别用 PatchAll()：一个补丁失败不拖垮整个模组，日志还能定位到类。
        var harmony = new Harmony(ModId);
        foreach (var type in typeof(CYLibEntry).Assembly
                     .GetTypes()
                     .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            try
            {
                var patched = harmony.CreateClassProcessor(type).Patch();
                Log.Info($"Patched {type.FullName} ({patched.Count} method(s)).");
            }
            catch (Exception ex)
            {
                Log.Error($"FAILED to patch {type.FullName}: {ex}");
            }
        }

        Log.Info("CYLib initialized.");
    }
}
