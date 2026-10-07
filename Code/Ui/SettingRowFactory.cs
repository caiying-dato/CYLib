using System;
using System.Globalization;
using CYLib.Config;
using Godot;

namespace CYLib.Ui;

/// <summary>一行设置项的产物：UI 根节点 + 从仓库回读值的刷新方法。</summary>
internal sealed class SettingRow
{
    public required Control Root { get; init; }
    /// <summary>从配置仓库重新读值并刷新控件显示（外部改值后调用）。</summary>
    public Action Refresh { get; init; } = static () => { };
}

/// <summary>把 <see cref="SettingDef"/> 渲染成一行控件，并接好与配置仓库的双向绑定。</summary>
internal static class SettingRowFactory
{
    public static SettingRow Create(ConfigStore store, ConfigPage page, SettingDef def)
    {
        return def switch
        {
            BoolSetting b => CreateBool(store, page, b),
            IntSetting i => i.Style == NumberStyle.Input ? CreateIntInput(store, page, i) : CreateInt(store, page, i),
            FloatSetting f => f.Style == NumberStyle.Input ? CreateFloatInput(store, page, f) : CreateFloat(store, page, f),
            TextSetting t => CreateText(store, page, t),
            ChoiceSetting c => CreateChoice(store, page, c),
            HotkeySetting h => CreateHotkey(store, page, h),
            ButtonSetting button => CreateButton(button),
            _ => new SettingRow { Root = CyStyle.CreateDescription($"[不支持的设置项类型: {def.GetType().Name}]") },
        };
    }

    // ───────────────────────── 布尔开关 ─────────────────────────

    private static SettingRow CreateBool(ConfigStore store, ConfigPage page, BoolSetting def)
    {
        var button = CyStyle.CreateButton(string.Empty, "accent", def.FontSize ?? CyStyle.FontSizeLabel);

        void Refresh()
        {
            var value = store.GetBool(page.ModId, def.Id, def.DefaultValueRaw);
            button.Text = value ? Text("CYLIB.toggle.on") : Text("CYLIB.toggle.off");
            CyStyle.ApplyToggleStateStyle(button, value); // 绿=开，红=关
        }

        button.Pressed += () =>
        {
            var current = store.GetBool(page.ModId, def.Id, def.DefaultValueRaw);
            store.Set(page.ModId, def.Id, !current, ConfigChangeSource.Ui);
            Refresh(); // 立即反映新值，不依赖事件回流
        };

        Refresh();
        var (root, refreshText) = CyStyle.CreateRow(def.Label, def.Description, button,
            valueWidth: def.ControlWidth ?? 200f);
        return new SettingRow
        {
            Root = root,
            Refresh = () => { refreshText(); Refresh(); },
        };
    }

    // ───────────────────────── 整数滑条 ─────────────────────────

    private static SettingRow CreateInt(ConfigStore store, ConfigPage page, IntSetting def)
    {
        var (box, slider, valueLabel) = CreateSliderBox(def.Min, def.Max, def.Step, def.FontSize);

        void Refresh()
        {
            var value = store.GetInt(page.ModId, def.Id, def.DefaultValueRaw);
            slider.SetValueNoSignal(Math.Clamp(value, def.Min, def.Max));
            valueLabel.Text = FormatValue(value, def.Format);
        }

        slider.ValueChanged += v =>
        {
            var value = (int)Math.Round(v);
            valueLabel.Text = FormatValue(value, def.Format);
            store.Set(page.ModId, def.Id, value, ConfigChangeSource.Ui);
        };

        Refresh();
        var (root, refreshText) = CyStyle.CreateRow(def.Label, def.Description, box,
            valueWidth: def.ControlWidth ?? 320f);
        return new SettingRow
        {
            Root = root,
            Refresh = () => { refreshText(); Refresh(); },
        };
    }

    // ───────────────────────── 浮点滑条 ─────────────────────────

    private static SettingRow CreateFloat(ConfigStore store, ConfigPage page, FloatSetting def)
    {
        var (box, slider, valueLabel) = CreateSliderBox(def.Min, def.Max, def.Step, def.FontSize);

        void Refresh()
        {
            var value = store.GetFloat(page.ModId, def.Id, def.DefaultValueRaw);
            slider.SetValueNoSignal(Math.Clamp(value, def.Min, def.Max));
            valueLabel.Text = FormatValue(value, def.Format);
        }

        slider.ValueChanged += v =>
        {
            valueLabel.Text = FormatValue(v, def.Format);
            store.Set(page.ModId, def.Id, v, ConfigChangeSource.Ui);
        };

        Refresh();
        var (root, refreshText) = CyStyle.CreateRow(def.Label, def.Description, box,
            valueWidth: def.ControlWidth ?? 320f);
        return new SettingRow
        {
            Root = root,
            Refresh = () => { refreshText(); Refresh(); },
        };
    }

    private static (HBoxContainer Box, HSlider Slider, Label ValueLabel) CreateSliderBox(
        double min, double max, double step, int? valueFontSize = null)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 12);

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step > 0 ? step : 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        slider.CustomMinimumSize = new Vector2(220, 32);
        box.AddChild(slider);

        var valueLabel = CyStyle.CreateLabel(string.Empty, valueFontSize ?? CyStyle.FontSizeLabel,
            CyStyle.Gold, HorizontalAlignment.Right);
        valueLabel.CustomMinimumSize = new Vector2(96, 32);
        box.AddChild(valueLabel);

        return (box, slider, valueLabel);
    }

    // ───────────────────────── 单行文本 ─────────────────────────

    private static SettingRow CreateText(ConfigStore store, ConfigPage page, TextSetting def)
    {
        var edit = new LineEdit
        {
            Text = store.GetText(page.ModId, def.Id, def.DefaultValueRaw),
            PlaceholderText = def.Placeholder.Resolve(),
            FocusMode = Control.FocusModeEnum.All,
        };
        if (def.MaxLength > 0) edit.MaxLength = def.MaxLength;
        CyStyle.ApplyFont(edit, "font", fontSize: def.FontSize ?? CyStyle.FontSizeLabel);
        edit.TextChanged += _ => CyStyle.ShowInvalidEdit(edit, false); // 重新输入即消除红框

        void Commit(string text)
        {
            if (def.Validator != null && !def.Validator(text))
            {
                // 非法输入：红框提示 + 回读旧值 + 通知模组（模组可弹自己的提示）
                CyStyle.ShowInvalidEdit(edit, true);
                try
                {
                    def.OnRejected?.Invoke(text);
                }
                catch (Exception ex)
                {
                    CYLibEntry.Log.Error($"[ConfigUI] OnRejected 回调抛异常: {ex}");
                }

                edit.Text = store.GetText(page.ModId, def.Id, def.DefaultValueRaw);
                return;
            }

            CyStyle.ShowInvalidEdit(edit, false);
            store.Set(page.ModId, def.Id, text, ConfigChangeSource.Ui);
        }

        edit.TextSubmitted += Commit;
        edit.FocusExited += () => Commit(edit.Text);

        var (textRoot, textRefresh) = CyStyle.CreateRow(def.Label, def.Description, edit,
            valueWidth: def.ControlWidth ?? 320f);
        return new SettingRow
        {
            Root = textRoot,
            Refresh = () =>
            {
                textRefresh();
                edit.Text = store.GetText(page.ModId, def.Id, def.DefaultValueRaw);
            },
        };
    }

    // ───────────────────────── 数字输入框 ─────────────────────────

    private static SettingRow CreateIntInput(ConfigStore store, ConfigPage page, IntSetting def)
    {
        var edit = new LineEdit
        {
            FocusMode = Control.FocusModeEnum.All,
        };
        CyStyle.ApplyFont(edit, "font", fontSize: def.FontSize ?? CyStyle.FontSizeLabel);
        edit.TextChanged += _ => CyStyle.ShowInvalidEdit(edit, false); // 重新输入即消除红框

        void Commit(string text)
        {
            if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                CyStyle.ShowInvalidEdit(edit, false);
                var clamped = Math.Clamp(value, def.Min, def.Max);
                store.Set(page.ModId, def.Id, clamped, ConfigChangeSource.Ui);
                edit.Text = clamped.ToString(CultureInfo.InvariantCulture); // 显示夹取后的值
            }
            else
            {
                // 解析失败：红框提示 + 回读旧值
                CyStyle.ShowInvalidEdit(edit, true);
                edit.Text = store.GetInt(page.ModId, def.Id, def.DefaultValueRaw)
                    .ToString(CultureInfo.InvariantCulture);
            }
        }

        edit.TextSubmitted += Commit;
        edit.FocusExited += () => Commit(edit.Text);

        void Refresh()
        {
            edit.Text = store.GetInt(page.ModId, def.Id, def.DefaultValueRaw)
                .ToString(CultureInfo.InvariantCulture);
        }

        Refresh();
        var (root, refreshText) = CyStyle.CreateRow(def.Label, def.Description, edit,
            valueWidth: def.ControlWidth ?? 320f);
        return new SettingRow
        {
            Root = root,
            Refresh = () => { refreshText(); Refresh(); },
        };
    }

    // ───────────────────────── 小数输入框 ─────────────────────────

    private static SettingRow CreateFloatInput(ConfigStore store, ConfigPage page, FloatSetting def)
    {
        var edit = new LineEdit
        {
            FocusMode = Control.FocusModeEnum.All,
        };
        CyStyle.ApplyFont(edit, "font", fontSize: def.FontSize ?? CyStyle.FontSizeLabel);
        edit.TextChanged += _ => CyStyle.ShowInvalidEdit(edit, false); // 重新输入即消除红框

        void Commit(string text)
        {
            // 严格解析：不替用户"脑补"格式（如全角句号），不规范一律红框回退
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                CyStyle.ShowInvalidEdit(edit, false);
                var clamped = Math.Clamp(value, def.Min, def.Max);
                store.Set(page.ModId, def.Id, clamped, ConfigChangeSource.Ui);
                edit.Text = clamped.ToString(CultureInfo.InvariantCulture); // 显示夹取后的值
            }
            else
            {
                CyStyle.ShowInvalidEdit(edit, true);
                edit.Text = store.GetFloat(page.ModId, def.Id, def.DefaultValueRaw)
                    .ToString(CultureInfo.InvariantCulture);
            }
        }

        edit.TextSubmitted += Commit;
        edit.FocusExited += () => Commit(edit.Text);

        void Refresh()
        {
            edit.Text = store.GetFloat(page.ModId, def.Id, def.DefaultValueRaw)
                .ToString(CultureInfo.InvariantCulture);
        }

        Refresh();
        var (root, refreshText) = CyStyle.CreateRow(def.Label, def.Description, edit,
            valueWidth: def.ControlWidth ?? 320f);
        return new SettingRow
        {
            Root = root,
            Refresh = () => { refreshText(); Refresh(); },
        };
    }

    // ───────────────────────── 热键绑定 ─────────────────────────

    private static SettingRow CreateHotkey(ConfigStore store, ConfigPage page, HotkeySetting def)
    {
        var button = CyStyle.CreateButton(string.Empty, "ghost", def.FontSize ?? CyStyle.FontSizeLabel);
        var capturing = false;

        void Refresh()
        {
            if (capturing) return; // 捕获中不覆盖提示文字

            var value = store.GetText(page.ModId, def.Id, def.DefaultValueRaw);
            button.Text = string.IsNullOrEmpty(value)
                ? Text("CYLIB.hotkey.unbound")
                : value;

            // 同页撞键提示：值相同时描红边 + 提示
            var conflict = false;
            if (!string.IsNullOrEmpty(value))
            {
                foreach (var other in page.AllSettings())
                {
                    if (other is HotkeySetting otherHotkey && otherHotkey.Id != def.Id &&
                        store.GetText(page.ModId, otherHotkey.Id, otherHotkey.DefaultValueRaw) == value)
                    {
                        conflict = true;
                        break;
                    }
                }
            }

            button.AddThemeStyleboxOverride("normal",
                CyStyle.CreateButtonStyle(CyStyle.PanelBgStrong, conflict ? CyStyle.Red : CyStyle.Border));
            button.TooltipText = conflict ? Text("CYLIB.hotkey.conflict") : string.Empty;
        }

        void StopCapture()
        {
            capturing = false;
            Refresh();
        }

        button.Pressed += () =>
        {
            capturing = true;
            button.Text = Text("CYLIB.hotkey.capture");
            button.GrabFocus();
        };

        button.FocusExited += StopCapture; // 点到别处即取消捕获

        button.GuiInput += (InputEvent e) =>
        {
            if (!capturing) return;
            if (e is not InputEventKey { Pressed: true, Echo: false } key) return;

            button.GetViewport()?.SetInputAsHandled(); // 吞掉按键，别触发游戏/其它模组的热键

            var code = key.Keycode;
            if (code == Key.Escape)
            {
                StopCapture();
                return;
            }

            if (code == Key.Backspace || code == Key.Delete)
            {
                capturing = false;
                store.Set(page.ModId, def.Id, string.Empty, ConfigChangeSource.Ui);
                Refresh();
                return;
            }

            if (code is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta or Key.Capslock)
                return; // 纯修饰键：继续等下一个键

            var combo = "";
            if (key.CtrlPressed) combo += "Ctrl+";
            if (key.ShiftPressed) combo += "Shift+";
            if (key.AltPressed) combo += "Alt+";
            combo += OS.GetKeycodeString(code);

            capturing = false;
            store.Set(page.ModId, def.Id, combo, ConfigChangeSource.Ui);
            Refresh();
        };

        Refresh();
        var (root, refreshText) = CyStyle.CreateRow(def.Label, def.Description, button,
            valueWidth: def.ControlWidth ?? 260f);
        return new SettingRow
        {
            Root = root,
            Refresh = () => { refreshText(); Refresh(); },
        };
    }

    // ───────────────────────── 下拉选项 ─────────────────────────

    private static SettingRow CreateChoice(ConfigStore store, ConfigPage page, ChoiceSetting def)
    {
        var option = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.All,
        };
        CyStyle.ApplyFont(option, "font", fontSize: def.FontSize ?? CyStyle.FontSizeLabel);

        for (var i = 0; i < def.Choices.Count; i++)
        {
            option.AddItem(def.Choices[i].Label.Resolve(), i);
            option.SetItemMetadata(i, def.Choices[i].Value);
        }

        void Refresh()
        {
            var value = store.GetText(page.ModId, def.Id, def.DefaultValueRaw);
            for (var i = 0; i < def.Choices.Count; i++)
            {
                if (def.Choices[i].Value == value)
                {
                    option.Select(i);
                    return;
                }
            }
            option.Select(-1);
        }

        option.ItemSelected += index =>
        {
            if (index < 0 || index >= def.Choices.Count) return;
            store.Set(page.ModId, def.Id, def.Choices[(int)index].Value, ConfigChangeSource.Ui);
        };

        Refresh();
        var (root, refreshText) = CyStyle.CreateRow(def.Label, def.Description, option,
            valueWidth: def.ControlWidth ?? 320f);
        return new SettingRow
        {
            Root = root,
            Refresh = () => { refreshText(); Refresh(); },
        };
    }

    // ───────────────────────── 动作按钮 ─────────────────────────

    private static SettingRow CreateButton(ButtonSetting def)
    {
        var button = CyStyle.CreateButton(def.Label.Resolve(), "ghost", def.FontSize ?? CyStyle.FontSizeLabel);
        button.Pressed += () =>
        {
            try
            {
                def.OnClick?.Invoke();
            }
            catch (Exception ex)
            {
                CYLibEntry.Log.Error($"[ConfigUI] 按钮 {def.Id} 的回调抛异常: {ex}");
            }
        };

        var (root, refreshText) = CyStyle.CreateRow(string.Empty, def.Description, button,
            valueWidth: def.ControlWidth ?? 200f);
        return new SettingRow
        {
            Root = root,
            Refresh = () =>
            {
                refreshText();
                button.Text = def.Label.Resolve(); // 按钮文字即 Label，支持动态
            },
        };
    }

    // ───────────────────────── 工具 ─────────────────────────

    private static string FormatValue(double value, string? format)
    {
        if (string.IsNullOrEmpty(format))
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, value);
        }
        catch
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string Text(string key) => CyText.Get(key);
}
