using MegaCrit.Sts2.Core.Localization;

namespace CYLib.Ui;

/// <summary>
/// CYLib 自身界面文案的本地化读取。
///
/// ★ 游戏的模组本地化机制是"按文件名合并进游戏同名表"，不能新建表名，
///   所以 CYLib 的文案放在 settings_ui / main_menu_ui 两张已有表里，key 一律以 CYLIB 前缀避免覆盖游戏原文。
///   文件位于 CYLib/localization/&lt;lang&gt;/settings_ui.json 等。
/// </summary>
internal static class CyText
{
    public const string SettingsTable = "settings_ui";
    public const string MainMenuTable = "main_menu_ui";

    /// <summary>读 settings_ui 表里的 CYLib 文案；查不到时回退 fallback（再不行回退 key 本身）。</summary>
    public static string Get(string key, string? fallback = null) => Lookup(SettingsTable, key, fallback);

    /// <summary>读 main_menu_ui 表里的文案。</summary>
    public static string GetMainMenu(string key, string? fallback = null) => Lookup(MainMenuTable, key, fallback);

    private static string Lookup(string table, string key, string? fallback)
    {
        try
        {
            if (LocString.Exists(table, key))
                return new LocString(table, key).GetFormattedText();
        }
        catch
        {
            /* 表尚未加载等异常情况：走回退 */
        }

        return fallback ?? key;
    }

    /// <summary>包装成 LocString（给需要 LocString 的游戏 API 用，如弹窗）。</summary>
    public static LocString Loc(string key) => new(SettingsTable, key);
}
