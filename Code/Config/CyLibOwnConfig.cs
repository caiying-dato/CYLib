using CYLib.Config;
using Godot;

namespace CYLib.Config;

/// <summary>
/// CYLib 自己的配置页（自产自销，同时也是接入示例）。
/// 其他模组接入时照着这个文件写即可，完整示例见 docs/samples/MyModConfigSample.cs。
///
/// 注意所有文案都用 new LazyText(() => …) 惰性解析：模组初始化时本地化表可能还没加载，
/// 推迟到界面渲染时取文本才可靠。
/// </summary>
internal static class CyLibOwnConfig
{
    public const string ModId = "CYLib";

    /// <summary>"统一「模组配置」入口"设置项的 id。</summary>
    public const string MergeEntriesKey = "merge_external_entries";

    /// <summary>配置文件所在目录。</summary>
    public static string ConfigDir => System.IO.Path.GetDirectoryName(CYLibConfig.GetFilePath(ModId)) ?? string.Empty;

    public static void Register()
    {
        CYLibConfig.Register(
            new ConfigPageBuilder(ModId, new LazyText(() => Ui.CyText.Get("CYLIB.page.title", "CYLib")))
                .Describe(new LazyText(() => Ui.CyText.Get("CYLIB.page.description",
                    "CY 系列模组的配置平台。其它模组的配置页会出现在左侧列表中。")))
                .Order(int.MaxValue) // 平台自身排在最后
                .Section(new LazyText(() => Ui.CyText.Get("CYLIB.section.ui", "界面")), s => s
                    .Add(new BoolSetting(MergeEntriesKey,
                        new LazyText(() => Ui.CyText.Get("CYLIB.opt.merge_entries", "统一「模组配置」入口")))
                    {
                        DefaultValueRaw = true,
                        Description = new LazyText(() => Ui.CyText.Get("CYLIB.opt.merge_entries.desc",
                            "隐藏 BaseLib / RitsuLib 等配置库的重复入口，只保留 CYLib 的一个按钮；它们的配置菜单可在 CYLib 界面内跳转。")),
                        RequiresRestart = true,
                    }))
                .Section(new LazyText(() => Ui.CyText.Get("CYLIB.section.storage", "存储")), s => s
                    .Info(new LazyText(() => Ui.CyText.Get("CYLIB.section.storage.desc",
                        "每个模组的配置保存在各自独立的 JSON 文件里。")))
                    .Button("open_config_dir",
                        new LazyText(() => Ui.CyText.Get("CYLIB.btn.open_config_dir", "打开配置目录")),
                        OpenConfigDir))
                .Build());
    }

    private static void OpenConfigDir()
    {
        try
        {
            OS.ShellOpen(ConfigDir);
        }
        catch (System.Exception ex)
        {
            CYLibEntry.Log.Error($"[CYLib] 打开配置目录失败: {ex}");
        }
    }
}
