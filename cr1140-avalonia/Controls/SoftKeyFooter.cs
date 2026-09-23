// SPDX-License-Identifier: GPL-3.0-only
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Cr1140.Avalonia.Input;

namespace Cr1140.Avalonia.Controls;

/// <summary>Ordering of the <see cref="SoftKeyFooter"/> cells.</summary>
public enum SoftKeyFooterLayout
{
    /// <summary>
    /// Match the CR1140/CR1141 physical 6-key keypad: <c>F6 F4 F2 · d-pad · F1 F3 F5</c>,
    /// or the CR1102 8-key keypad: <c>F1 F2 F3 F4 · d-pad · F5 F6 F7 F8</c>
    /// (the Enter + arrow cluster sits between the two key groups), so each on-screen
    /// label sits over the button that triggers it. This is the default.
    /// </summary>
    Physical,

    /// <summary>Natural reading order, left-to-right: <c>F1 F2 F3 F4 F5 F6</c> (6 keys) or
    /// <c>F1 F2 F3 F4 F5 F6 F7 F8</c> (8 keys).</summary>
    Natural,
}

/// <summary>
/// A soft-key footer for CR1140/CR1102 operator panels. Shows the label for each function
/// key (F1..F6 by default, or F1..F8 when <see cref="FunctionKeyCount"/> is 8) in one of
/// two orders selected by <see cref="Layout"/>: <see cref="SoftKeyFooterLayout.Physical"/>
/// (default) matches the physical keypad (<c>F6 F4 F2 · d-pad · F1 F3 F5</c> for 6 keys,
/// <c>F1 F2 F3 F4 · d-pad · F5 F6 F7 F8</c> for 8 keys);
/// <see cref="SoftKeyFooterLayout.Natural"/> runs <c>F1..F6</c> (or <c>F1..F8</c>)
/// left-to-right. Set the <see cref="F1"/>..<see cref="F6"/> (or <see cref="F7"/>,
/// <see cref="F8"/>) properties to the current per-key labels (empty string ⇒ blank cell).
/// </summary>
/// <remarks>
/// Derives from <see cref="Border"/>, so <see cref="Border.Background"/>,
/// <see cref="Border.BorderBrush"/>, <see cref="Border.BorderThickness"/> style the
/// footer strip. The dark defaults suit an operator panel and can be overridden.
/// </remarks>
public class SoftKeyFooter : Border
{
    /// <summary>Selects the cell order. Defaults to <see cref="SoftKeyFooterLayout.Physical"/>.</summary>
    public static readonly StyledProperty<SoftKeyFooterLayout> LayoutProperty =
        AvaloniaProperty.Register<SoftKeyFooter, SoftKeyFooterLayout>(nameof(Layout), SoftKeyFooterLayout.Physical);

    /// <summary>Whether to show the centre d-pad cell in <see cref="SoftKeyFooterLayout.Physical"/> mode. Ignored in Natural mode. Default <c>true</c>.</summary>
    public static readonly StyledProperty<bool> ShowDPadProperty =
        AvaloniaProperty.Register<SoftKeyFooter, bool>(nameof(ShowDPad), true);

    /// <summary>The number of function keys supported by this footer (6 or 8). Default is 6 for CR1140/CR1141.</summary>
    public static readonly StyledProperty<int> FunctionKeyCountProperty =
        AvaloniaProperty.Register<SoftKeyFooter, int>(nameof(FunctionKeyCount), 6);

    /// <summary>
    /// Selects whether the footer is a horizontal strip (<c>Horizontal</c>, the default —
    /// keys along the bottom/top edge) or a vertical column (<c>Vertical</c> — keys down
    /// one side, e.g. the CR1102's right-edge keypad). Default is
    /// <see cref="global::Avalonia.Layout.Orientation.Horizontal"/>.
    /// </summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<SoftKeyFooter, Orientation>(nameof(Orientation), Orientation.Horizontal);

    /// <summary>Label for the F1 soft-key.</summary>
    public static readonly StyledProperty<string?> F1Property = AvaloniaProperty.Register<SoftKeyFooter, string?>(nameof(F1));
    /// <summary>Label for the F2 soft-key.</summary>
    public static readonly StyledProperty<string?> F2Property = AvaloniaProperty.Register<SoftKeyFooter, string?>(nameof(F2));
    /// <summary>Label for the F3 soft-key.</summary>
    public static readonly StyledProperty<string?> F3Property = AvaloniaProperty.Register<SoftKeyFooter, string?>(nameof(F3));
    /// <summary>Label for the F4 soft-key.</summary>
    public static readonly StyledProperty<string?> F4Property = AvaloniaProperty.Register<SoftKeyFooter, string?>(nameof(F4));
    /// <summary>Label for the F5 soft-key.</summary>
    public static readonly StyledProperty<string?> F5Property = AvaloniaProperty.Register<SoftKeyFooter, string?>(nameof(F5));
    /// <summary>Label for the F6 soft-key.</summary>
    public static readonly StyledProperty<string?> F6Property = AvaloniaProperty.Register<SoftKeyFooter, string?>(nameof(F6));
    /// <summary>Label for the F7 soft-key (only shown when <see cref="FunctionKeyCount"/> is 8).</summary>
    public static readonly StyledProperty<string?> F7Property = AvaloniaProperty.Register<SoftKeyFooter, string?>(nameof(F7));
    /// <summary>Label for the F8 soft-key (only shown when <see cref="FunctionKeyCount"/> is 8).</summary>
    public static readonly StyledProperty<string?> F8Property = AvaloniaProperty.Register<SoftKeyFooter, string?>(nameof(F8));

    /// <summary>Brush for the per-cell divider lines. Default <c>#333333</c>.</summary>
    public static readonly StyledProperty<IBrush?> DividerBrushProperty =
        AvaloniaProperty.Register<SoftKeyFooter, IBrush?>(nameof(DividerBrush), new SolidColorBrush(Color.Parse("#333333")));
    /// <summary>Foreground of the small "F1".."F8" key ids. Default <c>#888888</c>.</summary>
    public static readonly StyledProperty<IBrush?> KeyForegroundProperty =
        AvaloniaProperty.Register<SoftKeyFooter, IBrush?>(nameof(KeyForeground), new SolidColorBrush(Color.Parse("#888888")));
    /// <summary>Foreground of the per-key action labels. Default <c>#00AAFF</c>.</summary>
    public static readonly StyledProperty<IBrush?> LabelForegroundProperty =
        AvaloniaProperty.Register<SoftKeyFooter, IBrush?>(nameof(LabelForeground), new SolidColorBrush(Color.Parse("#00AAFF")));
    /// <summary>Foreground of the centre d-pad text. Default <c>#6A7B8A</c>.</summary>
    public static readonly StyledProperty<IBrush?> DPadForegroundProperty =
        AvaloniaProperty.Register<SoftKeyFooter, IBrush?>(nameof(DPadForeground), new SolidColorBrush(Color.Parse("#6A7B8A")));
    /// <summary>Background of the centre d-pad cell. Default <c>#141414</c>.</summary>
    public static readonly StyledProperty<IBrush?> DPadBackgroundProperty =
        AvaloniaProperty.Register<SoftKeyFooter, IBrush?>(nameof(DPadBackground), new SolidColorBrush(Color.Parse("#141414")));
    /// <summary>Top line of the centre d-pad cell. Default <c>"▲ ▼"</c>.</summary>
    public static readonly StyledProperty<string> DPadLine1Property =
        AvaloniaProperty.Register<SoftKeyFooter, string>(nameof(DPadLine1), "▲ ▼");
    /// <summary>Bottom line of the centre d-pad cell. Default <c>"◀ OK ▶"</c>.</summary>
    public static readonly StyledProperty<string> DPadLine2Property =
        AvaloniaProperty.Register<SoftKeyFooter, string>(nameof(DPadLine2), "◀ OK ▶");

    private TextBlock?[] _labels = new TextBlock?[6];

    static SoftKeyFooter()
    {
        // Structural / style changes rebuild the cell tree.
        LayoutProperty.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        ShowDPadProperty.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        FunctionKeyCountProperty.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        OrientationProperty.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        DividerBrushProperty.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        KeyForegroundProperty.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        LabelForegroundProperty.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        DPadForegroundProperty.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        DPadBackgroundProperty.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        DPadLine1Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        DPadLine2Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.Rebuild());
        // A label change just updates the matching cell's text (no rebuild).
        F1Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.UpdateLabel(0));
        F2Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.UpdateLabel(1));
        F3Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.UpdateLabel(2));
        F4Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.UpdateLabel(3));
        F5Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.UpdateLabel(4));
        F6Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.UpdateLabel(5));
        F7Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.UpdateLabel(6));
        F8Property.Changed.AddClassHandler<SoftKeyFooter>((c, _) => c.UpdateLabel(7));
    }

    /// <summary>Creates a footer with the default dark operator-panel styling.</summary>
    public SoftKeyFooter()
    {
        Background = new SolidColorBrush(Color.Parse("#1A1A1A"));
        BorderBrush = new SolidColorBrush(Color.Parse("#333333"));
        Rebuild();
    }

    /// <inheritdoc cref="LayoutProperty"/>
    public SoftKeyFooterLayout Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
    /// <inheritdoc cref="ShowDPadProperty"/>
    public bool ShowDPad { get => GetValue(ShowDPadProperty); set => SetValue(ShowDPadProperty, value); }
    /// <inheritdoc cref="FunctionKeyCountProperty"/>
    public int FunctionKeyCount { get => GetValue(FunctionKeyCountProperty); set => SetValue(FunctionKeyCountProperty, value); }
    /// <inheritdoc cref="OrientationProperty"/>
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    /// <inheritdoc cref="F1Property"/>
    public string? F1 { get => GetValue(F1Property); set => SetValue(F1Property, value); }
    /// <inheritdoc cref="F2Property"/>
    public string? F2 { get => GetValue(F2Property); set => SetValue(F2Property, value); }
    /// <inheritdoc cref="F3Property"/>
    public string? F3 { get => GetValue(F3Property); set => SetValue(F3Property, value); }
    /// <inheritdoc cref="F4Property"/>
    public string? F4 { get => GetValue(F4Property); set => SetValue(F4Property, value); }
    /// <inheritdoc cref="F5Property"/>
    public string? F5 { get => GetValue(F5Property); set => SetValue(F5Property, value); }
    /// <inheritdoc cref="F6Property"/>
    public string? F6 { get => GetValue(F6Property); set => SetValue(F6Property, value); }
    /// <inheritdoc cref="F7Property"/>
    public string? F7 { get => GetValue(F7Property); set => SetValue(F7Property, value); }
    /// <inheritdoc cref="F8Property"/>
    public string? F8 { get => GetValue(F8Property); set => SetValue(F8Property, value); }
    /// <inheritdoc cref="DividerBrushProperty"/>
    public IBrush? DividerBrush { get => GetValue(DividerBrushProperty); set => SetValue(DividerBrushProperty, value); }
    /// <inheritdoc cref="KeyForegroundProperty"/>
    public IBrush? KeyForeground { get => GetValue(KeyForegroundProperty); set => SetValue(KeyForegroundProperty, value); }
    /// <inheritdoc cref="LabelForegroundProperty"/>
    public IBrush? LabelForeground { get => GetValue(LabelForegroundProperty); set => SetValue(LabelForegroundProperty, value); }
    /// <inheritdoc cref="DPadForegroundProperty"/>
    public IBrush? DPadForeground { get => GetValue(DPadForegroundProperty); set => SetValue(DPadForegroundProperty, value); }
    /// <inheritdoc cref="DPadBackgroundProperty"/>
    public IBrush? DPadBackground { get => GetValue(DPadBackgroundProperty); set => SetValue(DPadBackgroundProperty, value); }
    /// <inheritdoc cref="DPadLine1Property"/>
    public string DPadLine1 { get => GetValue(DPadLine1Property); set => SetValue(DPadLine1Property, value); }
    /// <inheritdoc cref="DPadLine2Property"/>
    public string DPadLine2 { get => GetValue(DPadLine2Property); set => SetValue(DPadLine2Property, value); }

    private string? LabelFor(int i) => i switch
    {
        0 => F1, 1 => F2, 2 => F3, 3 => F4, 4 => F5, 5 => F6, 6 => F7, 7 => F8, _ => null
    };

    private static int KeyIndex(KeypadKey k) => k switch
    {
        KeypadKey.F1 => 0, KeypadKey.F2 => 1, KeypadKey.F3 => 2,
        KeypadKey.F4 => 3, KeypadKey.F5 => 4, KeypadKey.F6 => 5,
        KeypadKey.F7 => 6, KeypadKey.F8 => 7, _ => -1
    };

    private void UpdateLabel(int i)
    {
        if (i < _labels.Length && _labels[i] is { } tb)
        {
            tb.Text = LabelFor(i) ?? string.Empty;
        }
    }

    private void Rebuild()
    {
        var vertical = Orientation == Orientation.Vertical;

        // Chrome depends on the edge the footer docks to: a horizontal strip has a fixed
        // height and a top divider from the content; a vertical column has a fixed width
        // and a left divider. (Set here rather than in the ctor so a runtime Orientation
        // change re-flows the chrome.)
        if (vertical)
        {
            Height = double.NaN;
            Width = 132;
            BorderThickness = new Thickness(2, 0, 0, 0);
        }
        else
        {
            Width = double.NaN;
            Height = 64;
            BorderThickness = new Thickness(0, 2, 0, 0);
        }

        // Key indices (0=F1 .. n-1=Fn) in current order. The physical order is the single
        // source of truth in SoftKeyLayoutMap; the d-pad (Enter + arrows) sits physically
        // in the centre in BOTH modes — between the two key groups, i.e. between the top
        // and bottom halves of a vertical column (or the left/right halves of a horizontal strip).
        var count = FunctionKeyCount;
        var keyOrder = (Layout == SoftKeyFooterLayout.Physical
            ? SoftKeyLayoutMap.PhysicalOrderFor(count)
            : SoftKeyLayoutMap.NaturalOrderFor(count)).Select(KeyIndex).ToArray();

        var order = new List<int>(count + 1);
        if (ShowDPad)
        {
            order.AddRange(keyOrder.Take(count / 2));
            order.Add(-1);
            order.AddRange(keyOrder.Skip(count / 2));
        }
        else
        {
            order.AddRange(keyOrder);
        }

        _labels = new TextBlock?[count];

        var grid = vertical
            ? new UniformGrid { Rows = order.Count, Columns = 1 }
            : new UniformGrid { Columns = order.Count, Rows = 1 };
        foreach (var idx in order)
        {
            grid.Children.Add(idx < 0 ? BuildDPad(vertical) : BuildKeyCell(idx, vertical));
        }

        Child = grid;
    }

    private Control BuildKeyCell(int i, bool vertical)
    {
        var keyText = new TextBlock
        {
            Text = "F" + (i + 1),
            FontSize = 10,
            Foreground = KeyForeground,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2)
        };
        var labelText = new TextBlock
        {
            Text = LabelFor(i) ?? string.Empty,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = LabelForeground,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };
        _labels[i] = labelText;

        var inner = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(keyText, 0);
        Grid.SetRow(labelText, 1);
        inner.Children.Add(keyText);
        inner.Children.Add(labelText);

        return new Border
        {
            BorderBrush = DividerBrush,
            BorderThickness = vertical ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0),
            Padding = new Thickness(4),
            Child = inner
        };
    }

    private Control BuildDPad(bool vertical)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = DPadLine1,
            FontSize = 12,
            Foreground = DPadForeground,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = DPadLine2,
            FontSize = 12,
            Foreground = DPadForeground,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        return new Border
        {
            BorderBrush = DividerBrush,
            BorderThickness = vertical ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0),
            Background = DPadBackground,
            Padding = new Thickness(4),
            Child = stack
        };
    }
}
