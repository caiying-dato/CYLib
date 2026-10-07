using CYLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Localization.Fonts;

namespace CYLib.Ui;

/// <summary>
/// CYLib 的 UI 风格工具：配色、字体、StyleBox 与控件工厂。
/// 配色对齐游戏主菜单（cream #FFF6E2 / gold #EFC851），控件全部纯代码构建、不依赖场景资源。
/// </summary>
internal static class CyStyle
{
    // 对齐 MegaCrit.Sts2.Core.Helpers.StsColors 的同名颜色
    public static readonly Color Cream = new("fff6e2");
    public static readonly Color Gold = new("efc851");
    public static readonly Color Red = new("ff5555");
    public static readonly Color PanelBg = new(0.05f, 0.05f, 0.08f, 0.55f);
    public static readonly Color PanelBgStrong = new(0.05f, 0.05f, 0.08f, 0.78f);
    public static readonly Color AccentBg = new(0.23f, 0.48f, 0.51f, 1f);   // #3b7a83
    public static readonly Color Border = new(1f, 0.96f, 0.89f, 0.25f);
    public static readonly Color Disabled = new(1f, 0.96f, 0.89f, 0.35f);

    public const int FontSizeTitle = 34;
    public const int FontSizeSection = 24;
    public const int FontSizeLabel = 20;
    public const int FontSizeSmall = 15;

    /// <summary>
    /// 给任意控件套上"跟随当前语言"的字体（中文等 CJK 语言需要替换字体，否则缺字）。
    /// themeFontName：Label/Button/LineEdit 用 "font"，RichTextLabel 用 normal_font 等。
    /// fontSize：可选字号覆盖；不传则保持主题默认。
    /// </summary>
    public static void ApplyFont(Control control, StringName themeFontName,
        FontType fontType = FontType.Regular, int? fontSize = null)
    {
        control.ApplyLocaleFontSubstitution(fontType, themeFontName);
        if (fontSize is int size)
            control.AddThemeFontSizeOverride("font_size", size);
    }

    /// <summary>普通文本标签（奶油色，可指定字号）。</summary>
    public static Label CreateLabel(string text, int fontSize = FontSizeLabel, Color? color = null,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = align,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color ?? Cream);
        label.AddThemeConstantOverride("line_spacing", 2);
        ApplyFont(label, "font");
        return label;
    }

    /// <summary>标题（金色大字）。</summary>
    public static Label CreateTitle(string text, int fontSize = FontSizeTitle) =>
        CreateLabel(text, fontSize, Gold);

    /// <summary>说明文字（小号、半透明）。</summary>
    public static Label CreateDescription(string text) =>
        CreateLabel(text, FontSizeSmall, new Color(Cream.R, Cream.G, Cream.B, 0.7f));

    /// <summary>圆角面板背景。</summary>
    public static StyleBoxFlat CreatePanelStyle(Color? bg = null, int radius = 8, int padding = 12) => new()
    {
        BgColor = bg ?? PanelBg,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        BorderColor = Border,
        ContentMarginLeft = padding,
        ContentMarginRight = padding,
        ContentMarginTop = padding,
        ContentMarginBottom = padding,
    };

    /// <summary>按钮样式（常规 / 强调 / 危险三种色调）。</summary>
    public static StyleBoxFlat CreateButtonStyle(Color bg, Color? borderColor = null, int radius = 6, bool hover = false) => new()
    {
        BgColor = hover ? bg.Lightened(0.12f) : bg,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        BorderColor = borderColor ?? Border,
        ContentMarginLeft = 14,
        ContentMarginRight = 14,
        ContentMarginTop = 8,
        ContentMarginBottom = 8,
    };

    /// <summary>
    /// 创建一个奶油色文字的按钮。tone：normal（深底）/ accent（青绿）/ danger（红）/ ghost（透明底描边）。
    /// </summary>
    public static Button CreateButton(string text, string tone = "normal", int fontSize = FontSizeLabel)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ClipText = true,
        };

        var (bg, hover) = tone switch
        {
            "accent" => (AccentBg, AccentBg),
            "danger" => (new Color(0.55f, 0.12f, 0.12f, 0.9f), new Color(0.55f, 0.12f, 0.12f, 0.9f)),
            "ghost" => (PanelBgStrong, PanelBgStrong),
            _ => (PanelBgStrong, PanelBgStrong),
        };

        button.AddThemeStyleboxOverride("normal", CreateButtonStyle(bg));
        button.AddThemeStyleboxOverride("hover", CreateButtonStyle(hover, Gold, hover: true));
        button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(hover, Gold));
        button.AddThemeStyleboxOverride("focus", CreateButtonStyle(bg, Gold));
        button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(bg with { A = 0.4f }));
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeColorOverride("font_color", Cream);
        button.AddThemeColorOverride("font_hover_color", Cream);
        button.AddThemeColorOverride("font_pressed_color", Gold);
        button.AddThemeColorOverride("font_focus_color", Cream);
        button.AddThemeColorOverride("font_disabled_color", Disabled);
        ApplyFont(button, "font");
        return button;
    }

    /// <summary>
    /// 布尔开关状态指示：开 = 绿，关 = 红，一眼可辨。
    /// 覆盖 CreateButton 生成的样式，切状态时重调用即可换色。
    /// </summary>
    public static void ApplyToggleStateStyle(Button button, bool on)
    {
        var bg = on
            ? new Color(0.18f, 0.42f, 0.27f, 0.95f)  // 深绿
            : new Color(0.49f, 0.18f, 0.21f, 0.95f); // 深红
        var border = on
            ? new Color(0.44f, 0.79f, 0.56f)         // 亮绿
            : new Color(0.85f, 0.48f, 0.51f);        // 亮红

        button.AddThemeStyleboxOverride("normal", CreateButtonStyle(bg, border));
        button.AddThemeStyleboxOverride("hover", CreateButtonStyle(bg, border, hover: true));
        button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(bg, border));
        button.AddThemeStyleboxOverride("focus", CreateButtonStyle(bg, border));
        button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(bg with { A = 0.4f }, border));
    }

    /// <summary>输入框"非法输入"红框反馈；show=false 恢复默认样式。</summary>
    public static void ShowInvalidEdit(LineEdit edit, bool show)
    {
        if (show)
        {
            var style = CreateButtonStyle(new Color(0.35f, 0.08f, 0.08f, 0.4f), Red, radius: 4);
            edit.AddThemeStyleboxOverride("normal", style);
            edit.AddThemeStyleboxOverride("focus", style);
        }
        else
        {
            edit.RemoveThemeStyleboxOverride("normal");
            edit.RemoveThemeStyleboxOverride("focus");
        }
    }

    /// <summary>
    /// 行容器：左为标题+说明，右为具体控件（右对齐成一列，垂直居中）。
    /// 标题/说明收 LazyText：解析器文本在每次调用返回的 RefreshText 时重新求值（动态行文案）。
    /// </summary>
    public static (Control Root, Action RefreshText) CreateRow(
        LazyText title, LazyText description, Control valueControl, float valueWidth = 320f)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        margin.AddChild(row);

        // 左列：标题 + 说明（+ 可追加的备注，见 AppendRowNote）
        var textCol = new VBoxContainer();
        textCol.AddThemeConstantOverride("separation", 2);
        textCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        textCol.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(textCol);

        Label? titleLabel = null;
        if (!title.IsEmpty || title.IsDynamic)
        {
            titleLabel = CreateLabel(title.Resolve());
            textCol.AddChild(titleLabel);
        }

        Label? descLabel = null;
        if (!description.IsEmpty || description.IsDynamic)
        {
            descLabel = CreateDescription(description.Resolve());
            textCol.AddChild(descLabel);
        }

        // 右列：控件，统一右对齐（ShrinkEnd），同一列宽 → 各行控件右边缘对齐
        valueControl.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        valueControl.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        valueControl.CustomMinimumSize = new Vector2(valueWidth, 48);
        row.AddChild(valueControl);

        void RefreshText()
        {
            if (titleLabel != null) titleLabel.Text = title.Resolve();
            if (descLabel != null) descLabel.Text = description.Resolve();
        }

        return (margin, RefreshText);
    }

    /// <summary>在某一行的说明文字下方追加一条备注（如"（需要重启生效）"），不影响控件排版。返回备注 Label 以便动态刷新。</summary>
    public static Label? AppendRowNote(Control row, string note)
    {
        if (row is not MarginContainer margin || margin.GetChildCount() == 0) return null;
        if (margin.GetChild(0) is not HBoxContainer hbox || hbox.GetChildCount() == 0) return null;
        if (hbox.GetChild(0) is not VBoxContainer textCol) return null;
        var label = CreateDescription(note);
        textCol.AddChild(label);
        return label;
    }

    /// <summary>兜底用的设置行（原版 Modding 行复制失败时）：左标题右按钮。</summary>
    public static Control CreateSettingsRow(string title, string buttonText, Action onClick)
    {
        var (row, _) = CreateRow(title, default, CreateButton(buttonText, "ghost"), valueWidth: 320f);
        if (row is MarginContainer margin && margin.GetChild(0) is HBoxContainer hbox &&
            hbox.GetChildCount() > 1 && hbox.GetChild(1) is Button button)
        {
            button.Pressed += () => onClick();
        }

        return row;
    }
}
