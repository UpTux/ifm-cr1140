// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cr1140.Avalonia.Controls;
using Cr1140.Avalonia.Devices;
using Cr1140.Avalonia.Input;
using Cr1140.Avalonia.Leds;
using Cr1140.Avalonia.Output;

namespace Cr1140.Avalonia.Emulator;

/// <summary>
/// Desktop emulator window that presents a CR1140/CR1141 app inside a device bezel
/// with an on-screen keypad and live hardware state indicators.
/// </summary>
public sealed class EmulatorWindow : Window
{
    private readonly WindowKeypadInput _keypad;
    private readonly EmulatedDevice _device;
    private readonly Rectangle _dim;
    private readonly List<(RgbLed Led, SolidColorBrush Fill)> _ledIndicators;
    private readonly List<Button> _keyButtons;
    private readonly DispatcherTimer _poll;
    private readonly List<(KeypadKey Key, TextBlock Caption)> _captionLabels;
    private readonly Func<KeypadKey, string?>? _keyCaptions;

    /// <summary>
    /// Initializes a new emulator window hosting the given app root control.
    /// </summary>
    /// <param name="appRoot">The application's root view control.</param>
    /// <param name="keypad">The keypad input handler.</param>
    /// <param name="device">The emulated device hardware state.</param>
    /// <param name="options">Configuration options.</param>
    public EmulatorWindow(Control appRoot, WindowKeypadInput keypad, EmulatedDevice device, EmulatorOptions options)
    {
        _keypad = keypad;
        _device = device;
        _keyButtons = new List<Button>();
        _captionLabels = new List<(KeypadKey, TextBlock)>();
        _keyCaptions = options.KeyCaptions;

        Title = options.Title;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x22, 0x25));

        // Compute logical panel size from rotation
        int w, h;
        if (options.Rotation == DisplayRotation.Clockwise90 || options.Rotation == DisplayRotation.Clockwise270)
        {
            w = options.PanelHeight;
            h = options.PanelWidth;
        }
        else
        {
            w = options.PanelWidth;
            h = options.PanelHeight;
        }

        // Root layout
        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(16)
        };

        // SCREEN: Panel bezel with app root and dim overlay
        var screenBezel = new Border
        {
            Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x0a)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6)
        };

        var screenPanel = new Panel
        {
            Width = w,
            Height = h
        };

        appRoot.Width = w;
        appRoot.Height = h;
        screenPanel.Children.Add(appRoot);

        _dim = new Rectangle
        {
            Fill = Brushes.Black,
            IsHitTestVisible = false,
            Width = w,
            Height = h,
            Opacity = 0.0
        };
        screenPanel.Children.Add(_dim);

        screenBezel.Child = screenPanel;

        // KEYPAD — mirror the device's physical key placement: a horizontal row below the
        // screen (CR1140/CR1141, keys along the bottom) or a vertical column beside the
        // screen (CR1102, keys down the right bezel).
        var verticalKeys = options.SoftKeyEdge == SoftKeyEdge.Right || options.SoftKeyEdge == SoftKeyEdge.Left;

        StackPanel? keypadPanel = null;
        if (options.ShowKeypad)
        {
            keypadPanel = new StackPanel
            {
                Orientation = verticalKeys ? Orientation.Vertical : Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = verticalKeys ? new Thickness(12, 0, 0, 0) : new Thickness(0, 12, 0, 0)
            };

            // The on-screen function keys mirror the device's physical silk-screen order
            // (6-key: F6 F4 F2 · F1 F3 F5; 8-key CR1102: F1 F2 F3 F4 · F5 F6 F7 F8) with the
            // d-pad cluster in the centre — running left→right (horizontal) or top→bottom
            // (vertical). Each emits its fixed hardware KeypadKey, exactly like the panel;
            // the app applies SoftKeyLayoutMap.ToLogical for the footer layout.
            var phys = SoftKeyLayoutMap.PhysicalOrderFor(options.FunctionKeyCount);
            int half = phys.Count / 2; // top/left group before the d-pad, bottom/right after
            for (int i = 0; i < half; i++)
            {
                keypadPanel.Children.Add(MakeKeyButton(phys[i], phys[i].ToString()));
            }

            keypadPanel.Children.Add(BuildDpad());

            for (int i = half; i < phys.Count; i++)
            {
                keypadPanel.Children.Add(MakeKeyButton(phys[i], phys[i].ToString()));
            }
        }

        // Screen + key column side by side (vertical keypad) or the key row beneath the
        // screen (horizontal keypad).
        if (verticalKeys && keypadPanel != null)
        {
            var stage = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stage.Children.Add(screenBezel);
            stage.Children.Add(keypadPanel);
            root.Children.Add(stage);
        }
        else
        {
            root.Children.Add(screenBezel);
            if (keypadPanel != null)
            {
                root.Children.Add(keypadPanel);
            }
        }

        // BOTTOM BAR — keyboard hint (left) and per-profile LED indicators (bottom-right).
        var bottomBar = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };

        // Build one indicator per LED in the device profile
        _ledIndicators = new List<(RgbLed, SolidColorBrush)>();
        var ledCluster = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var led in device.Leds)
        {
            var indicator = new StackPanel { Orientation = Orientation.Horizontal };
            indicator.Children.Add(new TextBlock
            {
                Text = led.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            var fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            indicator.Children.Add(new Ellipse
            {
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = fill
            });
            _ledIndicators.Add((led, fill));
            ledCluster.Children.Add(indicator);
        }
        DockPanel.SetDock(ledCluster, Dock.Right);
        bottomBar.Children.Add(ledCluster);

        if (options.ShowKeyboardHints)
        {
            bottomBar.Children.Add(new TextBlock
            {
                Text = $"Keyboard: F1–F{options.FunctionKeyCount} · Arrows · Enter",
                Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa)),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        Content = root;

        // Attach physical keyboard
        _keypad.Attach(this);

        // Live refresh timer
        _poll = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _poll.Tick += OnPollTick;
    }

    /// <summary>
    /// Invoked when the window is opened; starts the hardware state polling timer.
    /// </summary>
    /// <param name="e">Event args.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _poll.Start();
    }

    /// <summary>
    /// Invoked when the window is closed; stops the hardware state polling timer.
    /// </summary>
    /// <param name="e">Event args.</param>
    protected override void OnClosed(EventArgs e)
    {
        _poll.Stop();
        base.OnClosed(e);
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        // Update dim overlay from backlight
        _dim.Opacity = (1.0 - Math.Clamp(_device.BacklightPercent, 0, 100) / 100.0) * 0.9;

        // Update all profile LED indicators
        foreach (var (led, fill) in _ledIndicators)
        {
            var color = _device.LedColor(led);
            fill.Color = (color.R == 0 && color.G == 0 && color.B == 0)
                ? Color.FromRgb(0x33, 0x33, 0x33)
                : Color.FromRgb(color.R, color.G, color.B);
        }

        // Apply keypad backlight tint if the profile has a KeypadBacklight-role LED
        IBrush keypadBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3d, 0x42));
        foreach (var led in _device.Leds)
        {
            if (led.Role == LedRole.KeypadBacklight)
            {
                var k = _device.LedColor(led);
                if (!(k.R == 0 && k.G == 0 && k.B == 0))
                {
                    keypadBrush = new SolidColorBrush(Color.FromRgb(k.R, k.G, k.B));
                }
                break;
            }
        }
        foreach (var b in _keyButtons)
        {
            b.Background = keypadBrush;
        }

        // Update per-key captions from the app-supplied provider (e.g. the live soft-key
        // label the key triggers). Follows the app's footer layout, so a footer toggle
        // rewrites the captions in place while the fixed hardware key labels stay put.
        if (_keyCaptions is not null)
        {
            foreach (var (key, caption) in _captionLabels)
            {
                var text = _keyCaptions(key);
                caption.Text = text ?? string.Empty;
                caption.IsVisible = !string.IsNullOrEmpty(text);
            }
        }
    }

    private Button MakeKeyButton(KeypadKey key, string label)
    {
        var keyText = new TextBlock
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = FontWeight.SemiBold
        };
        var caption = new TextBlock
        {
            Text = string.Empty,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0xd0, 0xd0, 0xd0)),
            Margin = new Thickness(0, 2, 0, 0)
        };

        var btn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children = { keyText, caption }
            },
            Focusable = false,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 60,
            MinHeight = 44,
            Margin = new Thickness(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        btn.AddHandler(global::Avalonia.Input.InputElement.PointerPressedEvent, (s, e) => _keypad.Press(key), RoutingStrategies.Tunnel, handledEventsToo: true);
        btn.AddHandler(global::Avalonia.Input.InputElement.PointerReleasedEvent, (s, e) => _keypad.Release(key), RoutingStrategies.Tunnel, handledEventsToo: true);
        btn.AddHandler(global::Avalonia.Input.InputElement.PointerCaptureLostEvent, (s, e) => _keypad.Release(key), RoutingStrategies.Tunnel, handledEventsToo: true);
        btn.AddHandler(global::Avalonia.Input.InputElement.PointerExitedEvent, (s, e) => _keypad.Release(key), RoutingStrategies.Tunnel, handledEventsToo: true);

        _keyButtons.Add(btn);
        _captionLabels.Add((key, caption));
        return btn;
    }

    private Grid BuildDpad()
    {
        var dpad = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        dpad.RowDefinitions.Add(new RowDefinition());
        dpad.RowDefinitions.Add(new RowDefinition());
        dpad.RowDefinitions.Add(new RowDefinition());
        dpad.ColumnDefinitions.Add(new ColumnDefinition());
        dpad.ColumnDefinitions.Add(new ColumnDefinition());
        dpad.ColumnDefinitions.Add(new ColumnDefinition());

        void Place(KeypadKey key, string glyph, int row, int col)
        {
            var b = MakeKeyButton(key, glyph);
            Grid.SetRow(b, row);
            Grid.SetColumn(b, col);
            dpad.Children.Add(b);
        }

        Place(KeypadKey.Up, "\u25b2", 0, 1);
        Place(KeypadKey.Left, "\u25c0", 1, 0);
        Place(KeypadKey.Enter, "OK", 1, 1);
        Place(KeypadKey.Right, "\u25b6", 1, 2);
        Place(KeypadKey.Down, "\u25bc", 2, 1);

        return dpad;
    }
}
