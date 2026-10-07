using System;

namespace CYLib.Config;

/// <summary>
/// 文本既可以是字面量，也可以是"渲染时再求值"的解析器。
/// 用解析器可以把本地化推迟到界面渲染时——模组初始化阶段本地化表往往还没加载完。
/// <code>
/// // 字面量（可直接传 string，有隐式转换）：
/// new BoolSetting("a", "启用 X");
/// // 惰性本地化（推荐给需要本地化的模组）：
/// new BoolSetting("a", new LazyText(() => Loc.GetText("MYMOD.enable_x")));
/// </code>
/// </summary>
public readonly struct LazyText
{
    private readonly string? _literal;
    private readonly Func<string>? _resolver;

    /// <summary>字面量文本。</summary>
    public LazyText(string text)
    {
        _literal = text;
        _resolver = null;
    }

    /// <summary>惰性文本：渲染时才调用解析器。</summary>
    public LazyText(Func<string> resolver)
    {
        _literal = null;
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public static implicit operator LazyText(string text) => new(text);
    public static implicit operator LazyText(Func<string> resolver) => new(resolver);

    /// <summary>语法糖，等价于 <c>new LazyText(resolver)</c>。</summary>
    public static LazyText Defer(Func<string> resolver) => new(resolver);

    /// <summary>求值。解析器抛异常时回退到字面量（再不行为空串）。</summary>
    public string Resolve()
    {
        if (_resolver != null)
        {
            try
            {
                return _resolver() ?? _literal ?? string.Empty;
            }
            catch
            {
                return _literal ?? string.Empty;
            }
        }

        return _literal ?? string.Empty;
    }

    public bool IsEmpty => string.IsNullOrEmpty(_literal) && _resolver == null;

    /// <summary>是否是解析器文本（每次 <see cref="Resolve"/> 都可能得出不同结果）。</summary>
    public bool IsDynamic => _resolver != null;

    public override string ToString() => Resolve();
}
