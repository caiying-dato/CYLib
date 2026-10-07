using System;
using System.Collections.Generic;
using Godot;

namespace CYLib.Config;

/// <summary>数字设置项的形态。</summary>
public enum NumberStyle
{
    /// <summary>滑条（默认）。</summary>
    Slider,
    /// <summary>数字输入框（直接填数，提交时解析并按 Min/Max 夹取）。</summary>
    Input,
}

/// <summary>
/// 设置项定义的公共基类。
/// 一个设置项 = 一个持久化 key + 一个 UI 控件 + 一个默认值。
/// 具体模组通过 <see cref="ConfigSectionBuilder"/> 或直接 new 出这些类型来声明自己的配置。
/// </summary>
public abstract class SettingDef
{
    /// <summary>页内唯一的设置项 id（同时是持久化 key）。只允许字母、数字、下划线、点、连字符。</summary>
    public string Id { get; }

    /// <summary>显示名称。可以是字面量，也可以是渲染时求值的解析器（推荐本地化用后者）。</summary>
    public LazyText Label { get; set; }

    /// <summary>说明文字，显示在悬浮提示里；为空则不显示。</summary>
    public LazyText Description { get; set; }

    /// <summary>为 true 时该项仍持久化、但不出现在 UI 里。</summary>
    public bool Hidden { get; set; }

    /// <summary>为 true 时 UI 上标注“需要重启生效”。</summary>
    public bool RequiresRestart { get; set; }

    /// <summary>
    /// “需要重启生效”的原因说明（可选），显示在标注旁；支持动态文本。
    /// 有了它就不必把原因塞进 <see cref="Description"/>。
    /// </summary>
    public LazyText RestartNote { get; set; }

    /// <summary>动态可见性：返回 false 时 UI 上隐藏该项（例如“高级模式”关闭时不显示高级项）。</summary>
    public Func<bool>? VisibleIf { get; set; }

    /// <summary>动态可用性：返回 false 时控件置灰（例如某功能总开关关闭时其子项不可调）。</summary>
    public Func<bool>? EnabledIf { get; set; }

    /// <summary>
    /// 值控件宽度（像素）。null = 平台默认（320）。
    /// 先用默认值，觉得不合适再由各模组按项微调（如数字框传 160）。
    /// </summary>
    public float? ControlWidth { get; set; }

    /// <summary>
    /// 值控件字号。null = 平台默认（输入框/按钮 20）。
    /// 先用默认值，觉得不合适再由各模组按项微调。
    /// </summary>
    public int? FontSize { get; set; }

    /// <summary>该项默认值（“重置为默认”时写回的值）。</summary>
    public abstract object? DefaultValue { get; }

    /// <summary>该项承载值的类型。</summary>
    public abstract Type ValueType { get; }

    /// <summary>值被改动时（UI 或 API 写入）回调，参数为新值。</summary>
    public abstract void NotifyChanged(object? newValue);

    protected SettingDef(string id, LazyText label)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("设置项 id 不能为空。", nameof(id));
        Id = id;
        Label = label;
    }
}

/// <summary>布尔开关。</summary>
public sealed class BoolSetting : SettingDef
{
    public bool DefaultValueRaw { get; set; }
    public Action<bool>? OnChanged { get; set; }

    public BoolSetting(string id, LazyText label, bool defaultValue = false) : base(id, label)
    {
        DefaultValueRaw = defaultValue;
    }

    public override object? DefaultValue => DefaultValueRaw;
    public override Type ValueType => typeof(bool);
    public override void NotifyChanged(object? newValue) => OnChanged?.Invoke(newValue is bool b && b);
}

/// <summary>整数（滑条或数字输入框）。</summary>
public sealed class IntSetting : SettingDef
{
    public int DefaultValueRaw { get; set; }
    public int Min { get; set; }
    public int Max { get; set; } = 100;
    public int Step { get; set; } = 1;
    /// <summary>UI 形态：滑条（默认）或数字输入框。</summary>
    public NumberStyle Style { get; set; } = NumberStyle.Slider;
    /// <summary>数值显示格式，如 "{0:0} px"；空则用默认数字格式。</summary>
    public string? Format { get; set; }
    public Action<int>? OnChanged { get; set; }

    public IntSetting(string id, LazyText label, int defaultValue = 0) : base(id, label)
    {
        DefaultValueRaw = defaultValue;
    }

    public override object? DefaultValue => DefaultValueRaw;
    public override Type ValueType => typeof(int);
    public override void NotifyChanged(object? newValue) => OnChanged?.Invoke(Convert.ToInt32(newValue));
}

/// <summary>浮点数（滑条或数字输入框）。</summary>
public sealed class FloatSetting : SettingDef
{
    public double DefaultValueRaw { get; set; }
    public double Min { get; set; }
    public double Max { get; set; } = 1.0;
    public double Step { get; set; } = 0.1;
    /// <summary>UI 形态：滑条（默认）或数字输入框。</summary>
    public NumberStyle Style { get; set; } = NumberStyle.Slider;
    public string? Format { get; set; }
    public Action<double>? OnChanged { get; set; }

    public FloatSetting(string id, LazyText label, double defaultValue = 0.0) : base(id, label)
    {
        DefaultValueRaw = defaultValue;
    }

    public override object? DefaultValue => DefaultValueRaw;
    public override Type ValueType => typeof(double);
    public override void NotifyChanged(object? newValue) => OnChanged?.Invoke(Convert.ToDouble(newValue));
}

/// <summary>单行文本。</summary>
public sealed class TextSetting : SettingDef
{
    public string DefaultValueRaw { get; set; }
    /// <summary>最大长度，0 表示不限制。</summary>
    public int MaxLength { get; set; }
    /// <summary>输入框占位文字（可选）。</summary>
    public LazyText Placeholder { get; set; }
    /// <summary>输入合法性校验；返回 false 时拒绝这次输入并保持旧值。</summary>
    public Func<string, bool>? Validator { get; set; }
    /// <summary>输入被 <see cref="Validator"/> 拒绝时回调（参数为被拒文本）；UI 同时给输入框红框反馈。</summary>
    public Action<string>? OnRejected { get; set; }
    public Action<string>? OnChanged { get; set; }

    public TextSetting(string id, LazyText label, string defaultValue = "") : base(id, label)
    {
        DefaultValueRaw = defaultValue;
    }

    public override object? DefaultValue => DefaultValueRaw;
    public override Type ValueType => typeof(string);
    public override void NotifyChanged(object? newValue) => OnChanged?.Invoke(newValue as string ?? string.Empty);
}

/// <summary>下拉选项（枚举式选择）。</summary>
public sealed class ChoiceSetting : SettingDef
{
    /// <summary>一个可选项：值（持久化内容） + 显示文本。</summary>
    public readonly record struct Choice(string Value, LazyText Label);

    public string DefaultValueRaw { get; set; }
    public List<Choice> Choices { get; } = new();
    public Action<string>? OnChanged { get; set; }

    public ChoiceSetting(string id, LazyText label, string defaultValue = "") : base(id, label)
    {
        DefaultValueRaw = defaultValue;
    }

    public ChoiceSetting WithChoice(string value, LazyText label)
    {
        Choices.Add(new Choice(value, label));
        return this;
    }

    public override object? DefaultValue => DefaultValueRaw;
    public override Type ValueType => typeof(string);
    public override void NotifyChanged(object? newValue) => OnChanged?.Invoke(newValue as string ?? string.Empty);
}

/// <summary>动作按钮（不持久化，点击触发回调）。</summary>
public sealed class ButtonSetting : SettingDef
{
    /// <summary>按钮配色（Godot 颜色字符串，如 "#3b7a83"）；空则用默认风格。</summary>
    public string? Color { get; set; }
    public Action? OnClick { get; set; }

    public ButtonSetting(string id, LazyText label, Action? onClick = null) : base(id, label)
    {
        OnClick = onClick;
    }

    public override object? DefaultValue => null;
    public override Type ValueType => typeof(void);
    public override void NotifyChanged(object? newValue) { }
}

/// <summary>热键绑定（不负责按键派发，只存/编/显）。值为组合键字符串，如 "F8"、"Ctrl+Shift+K"；空串 = 未绑定。</summary>
public sealed class HotkeySetting : SettingDef
{
    public string DefaultValueRaw { get; set; }
    public Action<string>? OnChanged { get; set; }

    public HotkeySetting(string id, LazyText label, string defaultValue = "") : base(id, label)
    {
        DefaultValueRaw = defaultValue;
    }

    public override object? DefaultValue => DefaultValueRaw;
    public override Type ValueType => typeof(string);
    public override void NotifyChanged(object? newValue) => OnChanged?.Invoke(newValue as string ?? string.Empty);
}

/// <summary>纯展示文字（不持久化、无控件）。</summary>
public sealed class InfoItem
{
    public LazyText Text { get; set; }

    /// <summary>字号；null 用默认说明小字（15px）。</summary>
    public int? FontSize { get; set; }

    /// <summary>文字颜色；null 用默认说明色（半透明奶油）。</summary>
    public Color? Color { get; set; }

    public InfoItem(LazyText text) => Text = text;
}

/// <summary>小节内的分组标题（带可选说明）。</summary>
public sealed class HeaderItem
{
    public LazyText Title { get; set; }
    public LazyText Description { get; set; }
    public HeaderItem(LazyText title, LazyText description = default)
    {
        Title = title;
        Description = description;
    }
}

/// <summary>
/// 配置小节：一组设置项的容器。UI 上以可折叠分组呈现。
/// 小节里的条目既可以是 <see cref="SettingDef"/>，也可以是纯展示的 <see cref="InfoItem"/> / <see cref="HeaderItem"/>。
/// </summary>
public sealed class ConfigSection
{
    public LazyText Title { get; set; }
    public LazyText Description { get; set; }
    public List<object> Items { get; } = new();

    /// <summary>初始是否折叠。</summary>
    public bool Collapsed { get; set; }

    public ConfigSection(LazyText title, LazyText description = default)
    {
        Title = title;
        Description = description;
    }

    internal IEnumerable<SettingDef> Settings()
    {
        foreach (var item in Items)
            if (item is SettingDef def)
                yield return def;
    }
}

/// <summary>
/// 一个模组的配置页：页标题 + 若干小节。
/// 由 <see cref="ConfigPageBuilder"/> 构建，通过 <see cref="CYLibConfig.Register"/> 注册。
/// </summary>
public sealed class ConfigPage
{
    /// <summary>模组 id（与模组清单 CYLib.json / MyMod.json 的 id 一致），也是配置文件名。</summary>
    public string ModId { get; }

    /// <summary>页标题（通常是模组名），显示在配置界面左侧列表和右侧页头。</summary>
    public LazyText Title { get; set; }

    /// <summary>页说明（可选），显示在页头下方。</summary>
    public LazyText Description { get; set; }

    /// <summary>排序权重：小的排在前面。默认 0；CYLib 自己的页面排最后。</summary>
    public int Order { get; set; }

    public List<ConfigSection> Sections { get; } = new();

    public ConfigPage(string modId, LazyText title)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("modId 不能为空。", nameof(modId));
        ModId = modId;
        Title = title;
    }

    /// <summary>枚举页内全部设置项（跨小节）。</summary>
    public IEnumerable<SettingDef> AllSettings()
    {
        foreach (var section in Sections)
            foreach (var def in section.Settings())
                yield return def;
    }

    /// <summary>按 id 找设置项；找不到返回 null。</summary>
    public SettingDef? FindSetting(string id)
    {
        foreach (var def in AllSettings())
            if (def.Id == id)
                return def;
        return null;
    }

    /// <summary>是否有可见（非 Hidden）的设置项。没有的话左侧列表不显示该模组。</summary>
    public bool HasVisibleSettings()
    {
        foreach (var def in AllSettings())
            if (!def.Hidden)
                return true;
        return false;
    }
}
