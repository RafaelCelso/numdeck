using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NumDeck.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter NonEmptyToVisible = new Lambda(v => v is string s && s.Length > 0 ? Visibility.Visible : Visibility.Collapsed);
    public static readonly IValueConverter FalseToVisible = new Lambda(v => v is true ? Visibility.Collapsed : Visibility.Visible);
    public static readonly IValueConverter NullToVisible = new Lambda(v => v is null ? Visibility.Visible : Visibility.Collapsed);
    public static readonly IValueConverter NotNullToVisible = new Lambda(v => v is null ? Visibility.Collapsed : Visibility.Visible);
    public static readonly IValueConverter ActiveSuffix = new Lambda(v => v is true ? " · ativo" : "");
    public static readonly IValueConverter ZeroToVisible = new Lambda(v => v is 0 ? Visibility.Visible : Visibility.Collapsed);
    public static readonly IValueConverter NonZeroToVisible = new Lambda(v => v is 0 ? Visibility.Collapsed : Visibility.Visible);
    /// <summary>File path or shell:AppsFolder\… → its icon.</summary>
    public static readonly IValueConverter ShellIcon = new Lambda(v => Services.ShellIcons.Get(v as string, 32)!);

    /// <summary>[name, id, activeId] → "● Name" for the active profile.</summary>
    public static readonly IMultiValueConverter ProfileCaption = new MultiLambda(v =>
    {
        var name = v[0] as string ?? "";
        var id = v[1] as string;
        if (id == "global") return "★ " + name;
        return id is not null && id == v[2] as string ? "● " + name : "   " + name;
    });

    public static readonly IValueConverter IsNotNull = new Lambda(v => v is not null);

    /// <summary>Action → Segoe Fluent Icons glyph.</summary>
    public static readonly IValueConverter ActionGlyph = new Lambda(v => Glyphs.For(v as NumDeck.Core.Actions.ActionDefinition));

    /// <summary>[label, summary] → the first non-empty text.</summary>
    public static readonly IMultiValueConverter FirstNonEmpty = new MultiLambda(v =>
        v.OfType<string>().FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "");

    /// <summary>[a, b] → true when equal (e.g. profile id = active id).</summary>
    public static readonly IMultiValueConverter EqualValues = new MultiLambda(v => v.Length == 2 && v[0] is not null && Equals(v[0], v[1]));

    /// <summary>[a, b] → true when both are the same object.</summary>
    public static readonly IMultiValueConverter SameObject = new MultiLambda(v => v.Length == 2 && v[0] is not null && ReferenceEquals(v[0], v[1]));

    private sealed class Lambda(Func<object?, object> convert) : IValueConverter
    {
        public object Convert(object? value, Type t, object? p, CultureInfo c) => convert(value);
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => Binding.DoNothing;
    }

    private sealed class MultiLambda(Func<object?[], object> convert) : IMultiValueConverter
    {
        public object Convert(object?[] values, Type t, object? p, CultureInfo c) => convert(values);
        public object[] ConvertBack(object? value, Type[] t, object? p, CultureInfo c) => throw new NotSupportedException();
    }
}
