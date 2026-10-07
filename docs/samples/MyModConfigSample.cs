// ============================================================================
// CYLib 接入示例：一个模组的完整配置声明（可直接抄进你的模组再改）。
//
// 前置条件：
//   1. MyMod.json 里声明依赖：  "dependencies": [{ "id": "CYLib", "min_version": "v0.1.0" }]
//   2. csproj 里引用 CYLib.dll（Private=false）：
//        <Reference Include="CYLib">
//          <HintPath>..\CYLib\dist\CYLib\CYLib.dll</HintPath>
//          <Private>false</Private>
//        </Reference>
// ============================================================================

using System;
using CYLib.Config;

namespace MyMod.Config;

/// <summary>
/// 我的模组的配置页。一个类就够：声明设置项 + 把值同步到模组的运行时状态。
/// </summary>
public static class MyModConfig
{
    /// <summary>模组 id：必须与 MyMod.json 的 id 完全一致（也是配置文件名）。</summary>
    public const string ModId = "MyMod";

    // ── 模组运行时读取的状态（由配置驱动）────────────────────────
    public static bool EnableX = true;
    public static int Power = 3;
    public static double VolumeScale = 1.0;
    public static string PlayerTitle = "";
    public static string Difficulty = "normal";

    /// <summary>在你的 [ModInitializer] 方法里调用一次。</summary>
    public static void Register()
    {
        CYLibConfig.Register(
            new ConfigPageBuilder(ModId, "我的模组")
                .Describe("这里写配置页的总体说明，会显示在页面顶部。")
                .Order(10)   // 左栏排序权重，小的在前（CYLib 自己排最后）
                .Section("常规", s => s
                    .Toggle("enable_x", "启用 X 功能",
                        defaultValue: true,
                        onChanged: v => EnableX = v,
                        description: "关闭后 X 功能完全不生效。")
                    .IntSlider("power", "强度",
                        min: 1, max: 10, defaultValue: 3,
                        onChanged: v => Power = v,
                        description: "影响 X 的数值强度。")
                    .FloatSlider("volume", "音量倍率",
                        min: 0.0, max: 2.0, defaultValue: 1.0, step: 0.05,
                        onChanged: v => VolumeScale = v,
                        format: "{0:0.00}x"))
                .Section("角色", s => s
                    .Text("player_title", "称号",
                        defaultValue: "",
                        onChanged: v => PlayerTitle = v,
                        description: "显示在角色名旁边的自定义称号。",
                        maxLength: 12)
                    .Choice("difficulty", "难度",
                        defaultValue: "normal",
                        onChanged: v => Difficulty = v,
                        description: "影响本模组新增内容的强度。",
                        choices: new (string Value, LazyText Label)[]
                        {
                            ("normal", "普通"),
                            ("hard", "困难"),
                            ("nightmare", "噩梦"),
                        }))
                .Section("高级", s => s
                    .Button("clear_cache", "清理缓存",
                        onClick: () => { /* 你的清理逻辑 */ },
                        description: "删除本模组生成的临时数据。")
                    .Info("提示：这里的改动会立即生效，无需重启。")
                    // 需要"存盘但不显示"的内部状态？用 Add 直接构造：
                    .Add(new BoolSetting("internal_flag", "内部标记") { Hidden = true })
                    // 需要条件显隐？用 VisibleIf：
                    .Add(new BoolSetting("x_extra", "X 的额外选项")
                    {
                        Description = "仅在启用 X 时可用。",
                        VisibleIf = () => CYLibConfig.GetBool(ModId, "enable_x"),
                        EnabledIf = () => CYLibConfig.GetBool(ModId, "enable_x"),
                    }))
                .Build());

        // ── 启动时把已存的值同步进运行时状态（用户上次的配置要生效）──
        EnableX = CYLibConfig.GetBool(ModId, "enable_x", EnableX);
        Power = CYLibConfig.GetInt(ModId, "power", Power);
        VolumeScale = CYLibConfig.GetFloat(ModId, "volume", VolumeScale);
        PlayerTitle = CYLibConfig.GetText(ModId, "player_title", PlayerTitle);
        Difficulty = CYLibConfig.GetText(ModId, "difficulty", Difficulty);

        // ── 可选：订阅全局变更（例如配置界面开着的时候实时生效）──
        CYLibConfig.ValueChanged += change =>
        {
            if (change.ModId != ModId) return;
            switch (change.Key)
            {
                case "enable_x":
                    EnableX = change.NewValue is bool b && b;
                    break;
                case "power":
                    Power = change.NewValue is int i ? i : Power;
                    break;
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // v0.1.2 新增能力示例：动态信息行 / 数字输入框 / 热键 / 校验反馈。
    // 实际使用时把这几行并进上面的 Section 里即可。
    // ─────────────────────────────────────────────────────────────────
    public static void RegisterAdvancedExample()
    {
        new ConfigPageBuilder(ModId, "我的模组")
            .Section("进阶示例", s => s
                // 动态信息行：每次配置变动后自动重新求值，做"随值变化的预览"
                .Info(() => $"当前强度预览：{Power * 10}%")
                // 醒目提示（金色大字，同样支持动态文本）
                .InfoTitle(() => EnableX ? "X 功能已启用" : "X 功能已关闭")

                // 数字输入框：直接填数（存 int，越界自动夹取）
                .IntInput("retry_count", "重试次数",
                    min: 0, max: 99, defaultValue: 3,
                    description: "失败后自动重试的次数。")

                // 小数输入框：直接填小数（存 double；输入不规范会红框回退，不自动纠正）
                .FloatInput("growth", "成长系数",
                    min: 0, max: 99999, defaultValue: 1,
                    description: "支持小数，如 1.5。",
                    controlWidth: 160)   // 尺寸可调：不传用平台默认（320/字号 20）

                // 热键行：点击→按键捕获，支持 Ctrl/Shift/Alt+键；
                // 平台只存值（如 "Ctrl+F8"），按键派发请自己挂输入处理
                .Hotkey("toggle_key", "总开关热键", defaultValue: "F8",
                    description: "按此键切换总开关。")

                // 文本 + 校验拒绝反馈：validator 拒绝时红框 + onRejected 回调
                .Text("player_code", "好友码",
                    validator: v => v.Length == 8,
                    onRejected: rejected => Log.Warn($"好友码不合法：{rejected}"),
                    description: "必须是 8 位。"))
            .Build();
    }
}

// ============================================================================
// 在你的入口类里调用：
//
// [ModInitializer(nameof(Initialize))]
// public static class MyModEntry
// {
//     public static void Initialize()
//     {
//         MyMod.Config.MyModConfig.Register();
//         // ...你的其它初始化
//     }
// }
// ============================================================================
