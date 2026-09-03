// SPDX-License-Identifier: GPL-3.0-only
using Cr1140.Avalonia.Input;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// Navigation FSM mirroring cr1140-baler-demo/src/router.rs.
/// Handles keypad input, updates MainViewModel state, and routes actions to screen VMs.
/// </summary>
public sealed class NavigationController
{
    private readonly MainViewModel _main;
    private readonly MenuViewModel _menu;
    private readonly DashboardViewModel _dashboard;
    private readonly BaleCounterViewModel _baleCounter;
    private readonly KnivesViewModel _knives;
    private readonly WrappingViewModel _wrapping;
    private readonly TelemetryViewModel _telemetry;
    private readonly SettingsViewModel _settings;
    private readonly KeyEventsViewModel _keyEvents;
    private readonly LedsViewModel _leds;

    private Screen _current;

    public NavigationController(MainViewModel main)
    {
        _main = main;
        _menu = new MenuViewModel();
        _dashboard = new DashboardViewModel();
        _baleCounter = new BaleCounterViewModel();
        _knives = new KnivesViewModel();
        _wrapping = new WrappingViewModel();
        _telemetry = new TelemetryViewModel();
        _settings = new SettingsViewModel();
        _keyEvents = new KeyEventsViewModel();
        _leds = new LedsViewModel();

        _current = Screen.Menu;
    }

    public Screen Current => _current;

    /// <summary>The long-lived Key Events demo VM; MainViewModel feeds it every keypad event.</summary>
    public KeyEventsViewModel KeyEvents => _keyEvents;

    public void Handle(KeypadKey key)
    {
        if (_current == Screen.Menu)
        {
            HandleMenu(key);
        }
        else
        {
            HandleSubscreen(key);
        }
    }

    private void HandleMenu(KeypadKey key)
    {
        switch (key)
        {
            case KeypadKey.Up:
                // Wrap around: if index is 0, go to last (5)
                _menu.SelectedIndex = (_menu.SelectedIndex + _menu.Items.Count - 1) % _menu.Items.Count;
                break;

            case KeypadKey.Down:
                // Wrap around: if at last, go to first
                _menu.SelectedIndex = (_menu.SelectedIndex + 1) % _menu.Items.Count;
                break;

            case KeypadKey.Enter:
                // Open the selected menu item's screen
                OpenScreen(ScreenForIndex(_menu.SelectedIndex));
                break;

            case KeypadKey.F6:
                // F6 on menu = Exit (no-op for now)
                break;

            default:
                // F1..F5 do nothing on menu
                break;
        }
    }

    private void HandleSubscreen(KeypadKey key)
    {
        // F6 on any sub-screen = Back to Menu
        if (key == KeypadKey.F6)
        {
            OpenScreen(Screen.Menu);
            return;
        }

        // Per-screen actions
        switch (_current)
        {
            case Screen.Dashboard:
                // No actions on Dashboard
                break;

            case Screen.BaleCounter:
                if (key == KeypadKey.F1) _baleCounter.AddBale();
                else if (key == KeypadKey.F2) _baleCounter.ResetSession();
                break;

            case Screen.Knives:
                if (key == KeypadKey.F1) _knives.Toggle();
                break;

            case Screen.Wrapping:
                if (key == KeypadKey.F1) _wrapping.Start();
                break;

            case Screen.Telemetry:
                if (key == KeypadKey.Up) _telemetry.ScrollUp();
                else if (key == KeypadKey.Down) _telemetry.ScrollDown();
                break;

            case Screen.Settings:
                if (key == KeypadKey.F1) _settings.ToggleFieldbus();
                else if (key == KeypadKey.F2) _main.ToggleFooterLayout();
                break;
            case Screen.KeyEvents:
                // No per-key actions — every press is surfaced by the KeyEvents demo screen.
                break;
            case Screen.Leds:
                if (key == KeypadKey.F1) _leds.CycleStatus();
                else if (key == KeypadKey.F2) _leds.CycleBacklight();
                else if (key == KeypadKey.F3) _leds.CycleMode();
                break;
        }
    }

    private void OpenScreen(Screen screen)
    {
        var previous = _current;
        _current = screen;

        // Start each visit to the demo screen from a clean slate.
        if (screen == Screen.KeyEvents)
            _keyEvents.Reset();

        // The keypad-backlight animation only ticks while the LEDs screen is open.
        if (previous == Screen.Leds && screen != Screen.Leds)
            _leds.Deactivate();
        if (screen == Screen.Leds)
            _leds.Activate();

        // Update MainViewModel state
        _main.UpdateScreen(
            GetTitle(screen),
            GetContent(screen),
            GetSoftKeys(screen)
        );
    }

    private Screen ScreenForIndex(int index)
    {
        return index switch
        {
            0 => Screen.Dashboard,
            1 => Screen.BaleCounter,
            2 => Screen.Knives,
            3 => Screen.Wrapping,
            4 => Screen.Telemetry,
            5 => Screen.Settings,
            6 => Screen.KeyEvents,
            7 => Screen.Leds,
            _ => Screen.Menu
        };
    }

    private string GetTitle(Screen screen)
    {
        return screen switch
        {
            Screen.Menu => "Baler",
            Screen.Dashboard => "Dashboard",
            Screen.BaleCounter => "Bale Counter",
            Screen.Knives => "Knives",
            Screen.Wrapping => "Wrapping",
            Screen.Telemetry => "Telemetry",
            Screen.KeyEvents => "Key Events",
            Screen.Leds => "LEDs",
            Screen.Settings => "Settings",
            _ => "Baler"
        };
    }

    private object GetContent(Screen screen)
    {
        return screen switch
        {
            Screen.Menu => _menu,
            Screen.Dashboard => _dashboard,
            Screen.BaleCounter => _baleCounter,
            Screen.Knives => _knives,
            Screen.Wrapping => _wrapping,
            Screen.Telemetry => _telemetry,
            Screen.Settings => _settings,
            Screen.KeyEvents => _keyEvents,
            Screen.Leds => _leds,
            _ => _menu
        };
    }

    private SoftKeyViewModel[] GetSoftKeys(Screen screen)
    {
        var labels = GetSoftKeyLabels(screen);
        return new[]
        {
            new SoftKeyViewModel("F1", labels[0]),
            new SoftKeyViewModel("F2", labels[1]),
            new SoftKeyViewModel("F3", labels[2]),
            new SoftKeyViewModel("F4", labels[3]),
            new SoftKeyViewModel("F5", labels[4]),
            new SoftKeyViewModel("F6", labels[5])
        };
    }

    private string[] GetSoftKeyLabels(Screen screen)
    {
        return screen switch
        {
            Screen.Menu => new[] { "", "", "", "", "", "Exit" },
            Screen.Dashboard => new[] { "", "", "", "", "", "Back" },
            Screen.BaleCounter => new[] { "+1 Bale", "Reset Sess", "", "", "", "Back" },
            Screen.Knives => new[] { "Toggle", "", "", "", "", "Back" },
            Screen.Wrapping => new[] { "Start", "", "", "", "", "Back" },
            Screen.Telemetry => new[] { "", "", "", "", "", "Back" },
            Screen.Settings => new[] { "Toggle Bus", "Footer", "", "", "", "Back" },
            Screen.KeyEvents => new[] { "", "", "", "", "", "Back" },
            Screen.Leds => new[] { "Status", "Kbd Color", "Mode", "", "", "Back" },
            _ => new[] { "", "", "", "", "", "" }
        };
    }

    /// <summary>
    /// Initialize to the Menu screen. Call this after MainViewModel is fully constructed.
    /// </summary>
    public void Initialize()
    {
        OpenScreen(Screen.Menu);
    }
}
