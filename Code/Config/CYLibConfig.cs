using System;
using System.Collections.Generic;

namespace CYLib.Config;

/// <summary>
/// CYLib 对外门面：其他模组通过它注册配置页、读写配置值、订阅变更。
///
/// 最小接入示例（在你的模组初始化里）：
/// <code>
/// CYLibConfig.Register(
///     new ConfigPageBuilder("MyMod", "我的模组")
///         .Section("常规", s => s
///             .Toggle("enableX", "启用 X", true, v => MyMod.EnableX = v)
///             .IntSlider("amount", "数量", 1, 10, 3))
///         .Build());
///
/// // 随时读值：
/// bool on = CYLibConfig.GetBool("MyMod", "enableX");
/// </code>
///
/// 详细接入说明见 docs/接入指南.md。
/// </summary>
public static class CYLibConfig
{
    /// <summary>全局配置仓库。</summary>
    public static ConfigStore Store { get; } = new();

    /// <summary>任意配置值改变后触发。</summary>
    public static event Action<ConfigValueChanged>? ValueChanged
    {
        add => Store.ValueChanged += value;
        remove => Store.ValueChanged -= value;
    }

    /// <summary>新配置页注册后触发。</summary>
    public static event Action<ConfigPage>? PageRegistered
    {
        add => Store.PageRegistered += value;
        remove => Store.PageRegistered -= value;
    }

    // ───────────────────────── 注册 ─────────────────────────

    /// <summary>注册一个配置页。可在任意时机调用（UI 每次打开都重新读注册表，晚注册也会出现）。</summary>
    public static void Register(ConfigPage page) => Store.RegisterPage(page);

    /// <summary>用构建器回调注册，返回构建好的页（方便拿引用）。</summary>
    public static ConfigPage Register(string modId, string title, Action<ConfigPageBuilder> configure)
    {
        var builder = new ConfigPageBuilder(modId, title);
        configure(builder);
        var page = builder.Build();
        Register(page);
        return page;
    }

    /// <summary>新建配置页构建器（配合 <see cref="Register(ConfigPage)"/> 使用）。</summary>
    public static ConfigPageBuilder NewPage(string modId, string title) => new(modId, title);

    /// <summary>取某模组的配置页。</summary>
    public static ConfigPage? GetPage(string modId) => Store.GetPage(modId);

    /// <summary>全部已注册配置页。</summary>
    public static IReadOnlyList<ConfigPage> GetPages() => Store.GetPages();

    // ───────────────────────── 取值 / 写值 ─────────────────────────

    public static bool GetBool(string modId, string key, bool fallback = false) => Store.GetBool(modId, key, fallback);
    public static int GetInt(string modId, string key, int fallback = 0) => Store.GetInt(modId, key, fallback);
    public static double GetFloat(string modId, string key, double fallback = 0.0) => Store.GetFloat(modId, key, fallback);
    public static string GetText(string modId, string key, string fallback = "") => Store.GetText(modId, key, fallback);
    public static T Get<T>(string modId, string key, T fallback = default!) => Store.Get(modId, key, fallback);

    /// <summary>程序化写值（会触发 ValueChanged、立即落盘、并通知该项的 OnChanged 回调）。</summary>
    public static void Set(string modId, string key, object? value)
    {
        Store.Set(modId, key, value, ConfigChangeSource.Api);
        Store.Save(modId);
    }

    /// <summary>把某模组的全部设置恢复默认值并落盘。</summary>
    public static void RestoreDefaults(string modId) => Store.RestoreDefaults(modId);

    /// <summary>立即写盘（一般不需要手动调：UI 改动与 Set 都会自动保存）。</summary>
    public static void Save(string? modId = null) => Store.Save(modId);

    /// <summary>配置文件路径（调试用）。</summary>
    public static string GetFilePath(string modId) => Store.GetFilePath(modId);
}
