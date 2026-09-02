// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Avalonia.Threading;
using Cr1140.AvaloniaDemo.Input;

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

    private void OnKeyPressed(KeypadKey key)
    {
        // Dispatch keypad events to the UI thread
        Dispatcher.UIThread.Post(() => _nav.Handle(key));
    }

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
