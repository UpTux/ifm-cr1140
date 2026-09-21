// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Avalonia.Threading;
using Cr1140.Avalonia.Input;
using Cr1140.Avalonia.Controls;
using Cr1140.Avalonia.Devices;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// Root view model for MainView. Exposes Title, CurrentContent, and SoftKeys.
/// Subscribes to keypad input and dispatches to NavigationController.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly NavigationController _nav;
    private readonly IKeypadInput _keypad;

    private string _title;
    private object? _currentContent;
    private SoftKeyViewModel[] _softKeys;
    private SoftKeyFooterLayout _footerLayout = SoftKeyFooterLayout.Physical;
    private readonly int _functionKeyCount;
    private readonly Dock _footerDock;
    private readonly Orientation _footerOrientation;

    public MainViewModel(IKeypadInput keypad, DeviceProfile profile)
    {
        _keypad = keypad;
        _title = "Baler";
        _currentContent = null;
        _functionKeyCount = profile.FunctionKeyCount;

        // Dock the soft-key footer on the edge the device's physical keys sit on: the
        // CR1140/CR1141 keys run along the bottom (horizontal strip); the CR1102's eight
        // keys are a vertical column on the right, so the footer docks right and each label
        // lines up with its button.
        (_footerDock, _footerOrientation) = profile.SoftKeyEdge switch
        {
            SoftKeyEdge.Top => (Dock.Top, Orientation.Horizontal),
            SoftKeyEdge.Left => (Dock.Left, Orientation.Vertical),
            SoftKeyEdge.Right => (Dock.Right, Orientation.Vertical),
            _ => (Dock.Bottom, Orientation.Horizontal),
        };

        // Eight soft-key slots (F1..F8); the footer renders only FunctionKeyCount of them.
        _softKeys = new[]
        {
            new SoftKeyViewModel("F1", ""),
            new SoftKeyViewModel("F2", ""),
            new SoftKeyViewModel("F3", ""),
            new SoftKeyViewModel("F4", ""),
            new SoftKeyViewModel("F5", ""),
            new SoftKeyViewModel("F6", ""),
            new SoftKeyViewModel("F7", ""),
            new SoftKeyViewModel("F8", "")
        };

        _nav = new NavigationController(this);

        // Subscribe to keypad input (dispatched to UI thread)
        _keypad.KeyPressed += OnKeyPressed;

        // Mirror every keypad event into the Key Events demo screen.
        _keypad.KeyReleased += k => RecordKeyEvent(KeyEventKind.Released, k);
        _keypad.KeyPressed += k => RecordKeyEvent(KeyEventKind.Pressed, k);
        _keypad.KeyTapped += k => RecordKeyEvent(KeyEventKind.Tapped, k);
        _keypad.KeyDoubleTapped += k => RecordKeyEvent(KeyEventKind.DoubleTapped, k);
        _keypad.KeyHeld += k => RecordKeyEvent(KeyEventKind.Held, k);
        _keypad.KeyHolding += k => RecordKeyEvent(KeyEventKind.Holding, k);

        // Initialize to Menu screen
        _nav.Initialize();
    }

    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public object? CurrentContent
    {
        get => _currentContent;
        private set => SetField(ref _currentContent, value);
    }

    public IReadOnlyList<SoftKeyViewModel> SoftKeys => _softKeys;

    /// <summary>The number of function keys the footer renders (6 for CR1140/CR1141, 8 for CR1102).</summary>
    public int FunctionKeyCount => _functionKeyCount;

    /// <summary>The panel edge the soft-key footer docks to (bottom for CR1140/CR1141, right for CR1102).</summary>
    public Dock FooterDock => _footerDock;

    /// <summary>Whether the footer is a horizontal strip or a vertical column, matching the keypad edge.</summary>
    public Orientation FooterOrientation => _footerOrientation;

    public SoftKeyFooterLayout FooterLayout
    {
        get => _footerLayout;
        private set => SetField(ref _footerLayout, value);
    }

    /// <summary>Toggle the soft-key footer between physical-keypad and natural (F1..F6) order.</summary>
    internal void ToggleFooterLayout()
    {
        FooterLayout = _footerLayout == SoftKeyFooterLayout.Physical
            ? SoftKeyFooterLayout.Natural
            : SoftKeyFooterLayout.Physical;
    }

    private void OnKeyPressed(KeypadKey key)
    {
        // Remap the hardware key to the logical soft-key for the current footer
        // layout (identity in Physical; physical-position based in Natural), then
        // dispatch to the UI thread.
        var logical = SoftKeyLayoutMap.ToLogical(key, _footerLayout, _functionKeyCount);
        Dispatcher.UIThread.Post(() => _nav.Handle(logical));
    }

    /// <summary>Forward a keypad event to the Key Events demo VM (on the UI thread).</summary>
    private void RecordKeyEvent(KeyEventKind kind, KeypadKey key)
        => Dispatcher.UIThread.Post(() => _nav.KeyEvents.Record(kind, key));

    /// <summary>
    /// The live soft-key label the given hardware key triggers under the current footer
    /// layout (null for non-function keys or an empty label). Lets the desktop emulator
    /// caption its on-screen keypad buttons so they follow the footer as it toggles.
    /// </summary>
    public string? CaptionForKey(KeypadKey hardware)
    {
        var logical = SoftKeyLayoutMap.ToLogical(hardware, _footerLayout, _functionKeyCount);
        int idx = (int)logical;
        if (idx < 0 || idx >= _softKeys.Length)
            return null;
        var label = _softKeys[idx].Label;
        return string.IsNullOrEmpty(label) ? null : label;
    }

    /// <summary>
    /// Called by NavigationController to update the screen state.
    /// Internal so only NavigationController can call it.
    /// </summary>
    internal void UpdateScreen(string title, object content, SoftKeyViewModel[] softKeys)
    {
        Title = title;
        CurrentContent = content;

        // Update the soft key labels (same 8 instances kept for binding stability).
        for (int i = 0; i < _softKeys.Length; i++)
        {
            _softKeys[i].Label = softKeys[i].Label;
        }

        // Notify that SoftKeys collection "changed" (though we mutated the items)
        OnPropertyChanged(nameof(SoftKeys));
    }
}
