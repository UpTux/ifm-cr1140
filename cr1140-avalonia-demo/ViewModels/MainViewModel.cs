// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Avalonia.Threading;
using Cr1140.Avalonia.Input;
using Cr1140.Avalonia.Controls;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// Root view model for MainView. Exposes Title, CurrentContent, and SoftKeys.
/// Subscribes to keypad input and dispatches to NavigationController.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly NavigationController _nav;
    private readonly EvdevKeypadInput _keypad;

    private string _title;
    private object? _currentContent;
    private SoftKeyViewModel[] _softKeys;
    private SoftKeyFooterLayout _footerLayout = SoftKeyFooterLayout.Physical;

    public MainViewModel(EvdevKeypadInput keypad)
    {
        _keypad = keypad;
        _title = "Baler";
        _currentContent = null;

        // Create 6 empty soft keys initially
        _softKeys = new[]
        {
            new SoftKeyViewModel("F1", ""),
            new SoftKeyViewModel("F2", ""),
            new SoftKeyViewModel("F3", ""),
            new SoftKeyViewModel("F4", ""),
            new SoftKeyViewModel("F5", ""),
            new SoftKeyViewModel("F6", "")
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
        var logical = SoftKeyLayoutMap.ToLogical(key, _footerLayout);
        Dispatcher.UIThread.Post(() => _nav.Handle(logical));
    }

    /// <summary>Forward a keypad event to the Key Events demo VM (on the UI thread).</summary>
    private void RecordKeyEvent(KeyEventKind kind, KeypadKey key)
        => Dispatcher.UIThread.Post(() => _nav.KeyEvents.Record(kind, key));

    /// <summary>
    /// Called by NavigationController to update the screen state.
    /// Internal so only NavigationController can call it.
    /// </summary>
    internal void UpdateScreen(string title, object content, SoftKeyViewModel[] softKeys)
    {
        Title = title;
        CurrentContent = content;

        // Update the soft key labels (we keep the same 6 instances for binding stability)
        for (int i = 0; i < 6; i++)
        {
            _softKeys[i].Label = softKeys[i].Label;
        }

        // Notify that SoftKeys collection "changed" (though we mutated the items)
        OnPropertyChanged(nameof(SoftKeys));
    }
}
