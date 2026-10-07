using System;
using System.Collections.Generic;
using Godot;

namespace CYLib.Config;

/// <summary>
/// 配置页构建器。典型用法：
/// <code>
/// var page = new ConfigPageBuilder("MyMod", "我的模组")
///     .Section("常规", s => s
///         .Toggle("enableX", "启用 X", true, v => MyMod.EnableX = v, "说明文字")
///         .IntSlider("amount", "数量", 1, 10, 3))
///     .Build();
/// CYLibConfig.Register(page);
/// </code>
/// 所有文本参数都是 <see cref="LazyText"/>：可以直接传字符串字面量，
/// 也可以传 <c>new LazyText(() =&gt; 本地化取文本)</c>，让文案在界面渲染时才解析（推荐）。
/// 每个设置项方法末尾都有 <c>controlWidth</c> / <c>fontSize</c> 两个可选参数：
/// 不传 = 用平台默认（宽度 320、字号 20），觉得默认不合适再由各模组按项微调。
/// </summary>
public sealed class ConfigPageBuilder
{
    private readonly ConfigPage _page;

    public ConfigPageBuilder(string modId, LazyText title)
    {
        _page = new ConfigPage(modId, title);
    }

    /// <summary>页说明，显示在配置页头部。</summary>
    public ConfigPageBuilder Describe(LazyText description)
    {
        _page.Description = description;
        return this;
    }

    /// <summary>排序权重（小的在前）。默认 0。</summary>
    public ConfigPageBuilder Order(int order)
    {
        _page.Order = order;
        return this;
    }

    /// <summary>添加一个小节（配置组）。</summary>
    public ConfigPageBuilder Section(LazyText title, Action<ConfigSectionBuilder> configure, LazyText description = default)
    {
        var section = new ConfigSection(title, description);
        _page.Sections.Add(section);
        configure(new ConfigSectionBuilder(section));
        return this;
    }

    /// <summary>直接添加一个已构造好的设置项到指定小节（小节不存在会创建）。</summary>
    public ConfigPageBuilder Add(SettingDef setting, string sectionTitle = "")
    {
        var section = FindOrCreateSection(sectionTitle);
        section.Items.Add(setting);
        return this;
    }

    private ConfigSection FindOrCreateSection(string title)
    {
        foreach (var section in _page.Sections)
            if (section.Title.Resolve() == title)
                return section;

        var created = new ConfigSection(title);
        _page.Sections.Add(created);
        return created;
    }

    /// <summary>构建配置页。</summary>
    public ConfigPage Build() => _page;
}

/// <summary>小节构建器：链式声明设置项。</summary>
public sealed class ConfigSectionBuilder
{
    private readonly ConfigSection _section;

    internal ConfigSectionBuilder(ConfigSection section)
    {
        _section = section;
    }

    /// <summary>小节初始是否折叠。</summary>
    public ConfigSectionBuilder Collapsed(bool collapsed = true)
    {
        _section.Collapsed = collapsed;
        return this;
    }

    /// <summary>直接添加任意条目（SettingDef / InfoItem / HeaderItem）。</summary>
    public ConfigSectionBuilder Add(object item)
    {
        if (item is not (SettingDef or InfoItem or HeaderItem))
            throw new ArgumentException("只能添加 SettingDef / InfoItem / HeaderItem。", nameof(item));
        _section.Items.Add(item);
        return this;
    }

    /// <summary>布尔开关。</summary>
    public ConfigSectionBuilder Toggle(
        string id, LazyText label, bool defaultValue = false,
        Action<bool>? onChanged = null, LazyText description = default,
        float? controlWidth = null, int? fontSize = null)
    {
        Add(new BoolSetting(id, label, defaultValue)
        {
            OnChanged = onChanged,
            Description = description,
            ControlWidth = controlWidth,
            FontSize = fontSize,
        });
        return this;
    }

    /// <summary>整数滑条。</summary>
    public ConfigSectionBuilder IntSlider(
        string id, LazyText label, int min, int max, int defaultValue,
        int step = 1, Action<int>? onChanged = null, LazyText description = default, string? format = null,
        float? controlWidth = null, int? fontSize = null)
    {
        Add(new IntSetting(id, label, defaultValue)
        {
            Min = min,
            Max = max,
            Step = step,
            OnChanged = onChanged,
            Description = description,
            Format = format,
            ControlWidth = controlWidth,
            FontSize = fontSize,
        });
        return this;
    }

    /// <summary>数字输入框（直接填整数；提交时解析，越界按 Min/Max 夹取，解析失败红框提示并回退）。</summary>
    public ConfigSectionBuilder IntInput(
        string id, LazyText label, int min, int max, int defaultValue,
        Action<int>? onChanged = null, LazyText description = default,
        float? controlWidth = null, int? fontSize = null)
    {
        Add(new IntSetting(id, label, defaultValue)
        {
            Min = min,
            Max = max,
            Style = NumberStyle.Input,
            OnChanged = onChanged,
            Description = description,
            ControlWidth = controlWidth,
            FontSize = fontSize,
        });
        return this;
    }

    /// <summary>浮点滑条。</summary>
    public ConfigSectionBuilder FloatSlider(
        string id, LazyText label, double min, double max, double defaultValue,
        double step = 0.1, Action<double>? onChanged = null, LazyText description = default, string? format = null,
        float? controlWidth = null, int? fontSize = null)
    {
        Add(new FloatSetting(id, label, defaultValue)
        {
            Min = min,
            Max = max,
            Step = step,
            OnChanged = onChanged,
            Description = description,
            Format = format,
            ControlWidth = controlWidth,
            FontSize = fontSize,
        });
        return this;
    }

    /// <summary>小数输入框（直接填小数；提交时解析，越界按 Min/Max 夹取，解析失败红框提示并回退）。</summary>
    public ConfigSectionBuilder FloatInput(
        string id, LazyText label, double min, double max, double defaultValue,
        Action<double>? onChanged = null, LazyText description = default,
        float? controlWidth = null, int? fontSize = null)
    {
        Add(new FloatSetting(id, label, defaultValue)
        {
            Min = min,
            Max = max,
            Style = NumberStyle.Input,
            OnChanged = onChanged,
            Description = description,
            ControlWidth = controlWidth,
            FontSize = fontSize,
        });
        return this;
    }

    /// <summary>单行文本。validator 拒绝时红框提示并回调 onRejected。</summary>
    public ConfigSectionBuilder Text(
        string id, LazyText label, string defaultValue = "",
        Action<string>? onChanged = null, LazyText description = default,
        int maxLength = 0, LazyText placeholder = default, Func<string, bool>? validator = null,
        Action<string>? onRejected = null,
        float? controlWidth = null, int? fontSize = null)
    {
        Add(new TextSetting(id, label, defaultValue)
        {
            OnChanged = onChanged,
            Description = description,
            MaxLength = maxLength,
            Placeholder = placeholder,
            Validator = validator,
            OnRejected = onRejected,
            ControlWidth = controlWidth,
            FontSize = fontSize,
        });
        return this;
    }

    /// <summary>下拉选项。choices 每项是 (值, 显示文本)。
    /// controlWidth/fontSize 放在 choices 前面（params 必须是最后一个参数）。</summary>
    public ConfigSectionBuilder Choice(
        string id, LazyText label, string defaultValue,
        Action<string>? onChanged = null, LazyText description = default,
        float? controlWidth = null, int? fontSize = null,
        params (string Value, LazyText Label)[] choices)
    {
        var setting = new ChoiceSetting(id, label, defaultValue)
        {
            OnChanged = onChanged,
            Description = description,
            ControlWidth = controlWidth,
            FontSize = fontSize,
        };
        foreach (var (value, choiceLabel) in choices)
            setting.WithChoice(value, choiceLabel);
        Add(setting);
        return this;
    }

    /// <summary>热键绑定行：点击后按下按键即绑定（支持 Ctrl/Shift/Alt+键 组合），
    /// Esc 取消、Backspace 清除。平台只负责录入/存储/展示，按键派发由模组自己处理。
    /// 值为组合键字符串，如 "F8"、"Ctrl+Shift+K"；空串 = 未绑定。</summary>
    public ConfigSectionBuilder Hotkey(
        string id, LazyText label, string defaultValue = "",
        Action<string>? onChanged = null, LazyText description = default,
        float? controlWidth = null, int? fontSize = null)
    {
        Add(new HotkeySetting(id, label, defaultValue)
        {
            OnChanged = onChanged,
            Description = description,
            ControlWidth = controlWidth,
            FontSize = fontSize,
        });
        return this;
    }

    /// <summary>动作按钮（不保存值）。</summary>
    public ConfigSectionBuilder Button(
        string id, LazyText label, Action onClick, LazyText description = default, string? color = null,
        float? controlWidth = null, int? fontSize = null)
    {
        Add(new ButtonSetting(id, label, onClick)
        {
            Description = description,
            Color = color,
            ControlWidth = controlWidth,
            FontSize = fontSize,
        });
        return this;
    }

    /// <summary>纯展示文字。</summary>
    public ConfigSectionBuilder Info(LazyText text)
    {
        Add(new InfoItem(text));
        return this;
    }

    /// <summary>纯展示文字（动态：每次配置变动后重新求值，可随值变化刷新）。</summary>
    public ConfigSectionBuilder Info(Func<string> text) => Info(new LazyText(text));

    /// <summary>纯展示文字（自定义字号/颜色；fontSize 传 CyLib.Ui.CyStyle 的常量即可）。</summary>
    public ConfigSectionBuilder Info(LazyText text, int fontSize, Color? color = null)
    {
        Add(new InfoItem(text) { FontSize = fontSize, Color = color });
        return this;
    }

    /// <summary>醒目提示（金色大字）。</summary>
    public ConfigSectionBuilder InfoTitle(LazyText text)
    {
        Add(new InfoItem(text) { FontSize = Ui.CyStyle.FontSizeSection, Color = Ui.CyStyle.Gold });
        return this;
    }

    /// <summary>醒目提示（金色大字，动态文本）。</summary>
    public ConfigSectionBuilder InfoTitle(Func<string> text) => InfoTitle(new LazyText(text));

    /// <summary>小节内的分组标题。</summary>
    public ConfigSectionBuilder Header(LazyText title, LazyText description = default)
    {
        Add(new HeaderItem(title, description));
        return this;
    }
}
