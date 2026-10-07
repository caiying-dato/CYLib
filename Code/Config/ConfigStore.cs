using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CYLib.Config;

/// <summary>配置值变化事件参数。</summary>
public readonly struct ConfigValueChanged
{
    public string ModId { get; }
    public string Key { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }
    /// <summary>改动来源：UI 或 API。</summary>
    public ConfigChangeSource Source { get; }

    public ConfigValueChanged(string modId, string key, object? oldValue, object? newValue, ConfigChangeSource source)
    {
        ModId = modId;
        Key = key;
        OldValue = oldValue;
        NewValue = newValue;
        Source = source;
    }
}

public enum ConfigChangeSource
{
    /// <summary>玩家在配置界面里改的。</summary>
    Ui,
    /// <summary>模组代码通过 CYLibConfig.Set 改的。</summary>
    Api,
}

/// <summary>
/// 配置仓库：负责所有模组配置值的读写、持久化与变更通知。
/// 一般不直接使用它，用门面 <see cref="CYLibConfig"/>。
///
/// 持久化位置：&lt;游戏用户数据目录&gt;/mod_configs/&lt;modId&gt;.json
/// 文件格式（UTF-8 JSON）：
/// {
///   "version": 1,
///   "values": { "showHint": true, "maxLevel": 10 }
/// }
/// 未知 key 会被保留（前后版本兼容）；值类型与定义不符时回退默认值但不删 key。
/// </summary>
public sealed class ConfigStore
{
    public const int SchemaVersion = 1;

    private readonly object _lock = new();
    private readonly Dictionary<string, ConfigPage> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, object?>> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirtyMods = new(StringComparer.Ordinal);
    private readonly HashSet<string> _corruptFiles = new(StringComparer.Ordinal);

    /// <summary>配置文件所在目录（懒解析，失败时回退到 %APPDATA%\SlayTheSpire2\mod_configs）。</summary>
    public string RootDir => _rootDir ??= ResolveRootDir();
    private string? _rootDir;

    /// <summary>任意配置值改变后触发（UI 或 API 写入，含写入同值的情况不会触发）。</summary>
    public event Action<ConfigValueChanged>? ValueChanged;

    /// <summary>新配置页注册后触发（UI 可据此刷新列表）。</summary>
    public event Action<ConfigPage>? PageRegistered;

    // ───────────────────────── 注册 ─────────────────────────

    /// <summary>注册一个配置页。同 modId 重复注册会替换旧页（并保留已存的值）。</summary>
    public void RegisterPage(ConfigPage page)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));
        lock (_lock)
        {
            _pages[page.ModId] = page;
            EnsureLoaded(page.ModId);
        }
        PageRegistered?.Invoke(page);
    }

    /// <summary>取某模组的配置页；未注册返回 null。</summary>
    public ConfigPage? GetPage(string modId)
    {
        lock (_lock) return _pages.GetValueOrDefault(modId);
    }

    /// <summary>全部已注册配置页（按 Order、modId 排序）。</summary>
    public IReadOnlyList<ConfigPage> GetPages()
    {
        lock (_lock)
        {
            var list = new List<ConfigPage>(_pages.Values);
            list.Sort((a, b) =>
            {
                var byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.ModId, b.ModId);
            });
            return list;
        }
    }

    // ───────────────────────── 取值 / 写值 ─────────────────────────

    /// <summary>读取原始值（未注册或未持久化时返回定义默认值；都没有则 null）。</summary>
    public object? Get(string modId, string key)
    {
        lock (_lock)
        {
            EnsureLoaded(modId);
            if (_values.TryGetValue(modId, out var map) && map.TryGetValue(key, out var stored))
                return CoerceToDefaultOnMismatch(modId, key, stored);
            return FindDefault(modId, key);
        }
    }

    /// <summary>读取布尔值；兼容"字符串形式"的存量值（如手改过的旧配置）。</summary>
    public bool GetBool(string modId, string key, bool fallback = false) => Get(modId, key) switch
    {
        bool b => b,
        string s when bool.TryParse(s, out var pb) => pb,
        _ => fallback,
    };

    /// <summary>读取整数；兼容 long/double/字符串形式的存量值（旧版 TextSetting 变通存的数字字符串可直接读回）。</summary>
    public int GetInt(string modId, string key, int fallback = 0) => Get(modId, key) switch
    {
        int i => i,
        long l => (int)Math.Clamp(l, int.MinValue, int.MaxValue),
        double d => (int)Math.Round(d),
        float f => (int)Math.Round(f),
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pi) => pi,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var pd)
            => (int)Math.Round(pd),
        _ => fallback,
    };

    /// <summary>读取浮点数；兼容 int/float/字符串形式的存量值。</summary>
    public double GetFloat(string modId, string key, double fallback = 0.0) => Get(modId, key) switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var pd) => pd,
        _ => fallback,
    };
    public string GetText(string modId, string key, string fallback = "") => Get(modId, key) as string ?? fallback;

    /// <summary>泛型取值；类型不符时返回 fallback。</summary>
    public T Get<T>(string modId, string key, T fallback = default!)
    {
        var raw = Get(modId, key);
        if (raw is T typed) return typed;
        try
        {
            return raw == null ? fallback : (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture)!;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>写入值。值会被类型校验（与设置项定义的类型不符时抛异常）。返回是否真的发生了变化。</summary>
    public bool Set(string modId, string key, object? value, ConfigChangeSource source = ConfigChangeSource.Api)
    {
        object? oldValue;
        object? normalized;
        lock (_lock)
        {
            EnsureLoaded(modId);
            normalized = Normalize(modId, key, value);

            var map = _values.TryGetValue(modId, out var m) ? m : _values[modId] = new Dictionary<string, object?>(StringComparer.Ordinal);
            map.TryGetValue(key, out oldValue);
            if (Equals(oldValue, normalized)) return false;
            map[key] = normalized;
            _dirtyMods.Add(modId);
        }

        NotifyChanged(modId, key, oldValue, normalized, source);
        FindSettingDef(modId, key)?.NotifyChanged(normalized);
        return true;
    }

    private SettingDef? FindSettingDef(string modId, string key)
    {
        lock (_lock)
            return _pages.TryGetValue(modId, out var page) ? page.FindSetting(key) : null;
    }

    /// <summary>把某模组的所有设置项恢复为默认值。</summary>
    public void RestoreDefaults(string modId)
    {
        var changed = new List<ConfigValueChanged>();
        lock (_lock)
        {
            if (!_pages.TryGetValue(modId, out var page)) return;
            EnsureLoaded(modId);
            var map = _values.TryGetValue(modId, out var m) ? m : _values[modId] = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var def in page.AllSettings())
            {
                if (def is ButtonSetting) continue;
                var newValue = def.DefaultValue;
                map.TryGetValue(def.Id, out var oldValue);
                if (Equals(oldValue, newValue)) continue;
                map[def.Id] = newValue;
                _dirtyMods.Add(modId);
                changed.Add(new ConfigValueChanged(modId, def.Id, oldValue, newValue, ConfigChangeSource.Api));
            }
        }

        foreach (var change in changed)
        {
            NotifyChanged(change.ModId, change.Key, change.OldValue, change.NewValue, ConfigChangeSource.Api);
            FindSettingDef(modId, change.Key)?.NotifyChanged(change.NewValue);
        }

        Save(modId);
    }

    // ───────────────────────── 持久化 ─────────────────────────

    /// <summary>立即写盘（可选指定 modId；不传则写全部脏文件）。</summary>
    public void Save(string? modId = null)
    {
        var toSave = new List<string>();
        lock (_lock)
        {
            if (modId != null)
            {
                if (_dirtyMods.Contains(modId) || !_values.ContainsKey(modId)) toSave.Add(modId);
            }
            else
            {
                toSave.AddRange(_dirtyMods);
            }

            foreach (var id in toSave)
            {
                _dirtyMods.Remove(id);
                try
                {
                    WriteFile(id);
                }
                catch (Exception ex)
                {
                    CYLibEntry.Log.Error($"[ConfigStore] 保存 {id} 的配置失败: {ex}");
                }
            }
        }
    }

    /// <summary>配置文件路径。</summary>
    public string GetFilePath(string modId) => Path.Combine(RootDir, modId + ".json");

    private void EnsureLoaded(string modId)
    {
        if (_values.ContainsKey(modId)) return;
        _values[modId] = LoadFile(modId);
    }

    private Dictionary<string, object?> LoadFile(string modId)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        var path = GetFilePath(modId);
        try
        {
            if (!File.Exists(path)) return map;

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return map;

            var root = JsonNode.Parse(text) as JsonObject;
            if (root == null) throw new JsonException("根节点不是对象");

            if (root["values"] is JsonObject values)
            {
                foreach (var (key, node) in values)
                    map[key] = FromJsonNode(node);
            }
        }
        catch (Exception ex)
        {
            // 文件损坏：保留原文件（改名 .corrupt 备份），避免覆盖用户手改内容，用默认值继续。
            _corruptFiles.Add(modId);
            try
            {
                if (File.Exists(path))
                    File.Move(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), overwrite: true);
            }
            catch { /* 备份失败就算了，不能因此崩 */ }

            CYLibEntry.Log.Error($"[ConfigStore] 配置文件损坏，已备份为 .corrupt 并使用默认值: {path}\n{ex}");
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return map;
    }

    private void WriteFile(string modId)
    {
        var path = GetFilePath(modId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var values = new JsonObject();
        if (_values.TryGetValue(modId, out var map))
        {
            foreach (var (key, value) in map)
                values[key] = ToJsonNode(value);
        }

        var root = new JsonObject
        {
            ["version"] = SchemaVersion,
            ["values"] = values,
        };

        // 原子写：先写临时文件再替换，避免写一半崩溃留下半个文件。
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }

    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create((double)f),
        string s => JsonValue.Create(s),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    private static object? FromJsonNode(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<long>(out var l) => l <= int.MaxValue && l >= int.MinValue ? (object)(int)l : l,
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => node.ToJsonString(),
    };

    /// <summary>按设置项定义把外部值规整为合法值（类型不对、越界、非法选项都会被拉回）。</summary>
    private object? Normalize(string modId, string key, object? value)
    {
        var def = _pages.TryGetValue(modId, out var page) ? page.FindSetting(key) : null;
        if (def == null) return NormalizeByRuntimeType(value);

        switch (def)
        {
            case BoolSetting:
                return value is bool b ? b : def.DefaultValue;
            case IntSetting s:
            {
                var i = value switch
                {
                    int iv => iv,
                    long lv => (int)lv,
                    double dv => (int)Math.Round(dv),
                    string sv when int.TryParse(sv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pv) => pv,
                    _ => s.DefaultValueRaw,
                };
                return Math.Clamp(i, s.Min, s.Max);
            }
            case FloatSetting s:
            {
                var d = value switch
                {
                    double dv => dv,
                    float fv => fv,
                    int iv => iv,
                    string sv when double.TryParse(sv, NumberStyles.Float, CultureInfo.InvariantCulture, out var pv) => pv,
                    _ => s.DefaultValueRaw,
                };
                return Math.Clamp(d, s.Min, s.Max);
            }
            case TextSetting s:
            {
                var text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (s.MaxLength > 0 && text.Length > s.MaxLength) text = text[..s.MaxLength];
                return text;
            }
            case ChoiceSetting s:
            {
                var text = value as string ?? string.Empty;
                foreach (var choice in s.Choices)
                    if (choice.Value == text)
                        return text;
                return s.DefaultValueRaw;
            }
            default:
                return value;
        }
    }

    private static object? NormalizeByRuntimeType(object? value) => value switch
    {
        bool or int or long or double or float or string or null => value,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    private object? CoerceToDefaultOnMismatch(string modId, string key, object? stored)
    {
        var def = _pages.TryGetValue(modId, out var page) ? page.FindSetting(key) : null;
        if (def == null || def is ButtonSetting) return stored;

        var ok = def switch
        {
            BoolSetting => stored is bool,
            IntSetting => stored is int or long or double || IsNumericString(stored),
            FloatSetting => stored is double or float or int or long || IsNumericString(stored),
            TextSetting or ChoiceSetting or HotkeySetting => stored is string or null,
            _ => true,
        };
        return ok ? stored : def.DefaultValue;
    }

    /// <summary>字符串能否解析为数字（旧版本用 TextSetting 变通存的数字字符串要放行）。</summary>
    private static bool IsNumericString(object? value) =>
        value is string s &&
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private object? FindDefault(string modId, string key)
    {
        var def = _pages.TryGetValue(modId, out var page) ? page.FindSetting(key) : null;
        return def is ButtonSetting ? null : def?.DefaultValue;
    }

    private void NotifyChanged(string modId, string key, object? oldValue, object? newValue, ConfigChangeSource source)
    {
        try
        {
            ValueChanged?.Invoke(new ConfigValueChanged(modId, key, oldValue, newValue, source));
        }
        catch (Exception ex)
        {
            CYLibEntry.Log.Error($"[ConfigStore] ValueChanged 监听器抛异常: {ex}");
        }
    }

    private static string ResolveRootDir()
    {
        try
        {
            // 与游戏自身的用户数据目录保持一致（Godot user:// 目录）。
            var userDir = Godot.OS.GetUserDataDir();
            if (!string.IsNullOrEmpty(userDir))
                return Path.Combine(userDir, "mod_configs");
        }
        catch
        {
            /* Godot 未初始化时走兜底 */
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2", "mod_configs");
    }
}
