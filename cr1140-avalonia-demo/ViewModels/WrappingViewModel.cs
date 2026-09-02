// SPDX-License-Identifier: GPL-3.0-only
using System;
using Avalonia.Threading;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// View model for the Wrapping screen — simulated ~5s timed cycle with progress bar.
/// </summary>
public sealed class WrappingViewModel : ViewModelBase
{
    private const int WrapDurationMs = 5000;
    private const int TickIntervalMs = 50;

    private readonly DispatcherTimer _wrapTimer;
    private bool _active;
    private double _progress;
    private int _elapsed;

    public WrappingViewModel()
    {
        _active = false;
        _progress = 0.0;
        _elapsed = 0;

        _wrapTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TickIntervalMs)
        };
        _wrapTimer.Tick += OnWrapTick;
    }

    public bool Active
    {
        get => _active;
        private set
        {
            if (SetField(ref _active, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public double Progress
    {
        get => _progress;
        private set => SetField(ref _progress, value);
    }

    public string StatusText => Active ? "Wrapping..." : "Idle";

    public void Start()
    {
        if (_active)
            return;

        Active = true;
        Progress = 0.0;
        _elapsed = 0;
        _wrapTimer.Start();
    }

    private void OnWrapTick(object? sender, EventArgs e)
    {
        _elapsed += TickIntervalMs;
        Progress = Math.Min(100.0, (_elapsed / (double)WrapDurationMs) * 100.0);

        if (_elapsed >= WrapDurationMs)
        {
            _wrapTimer.Stop();
            Active = false;
            Progress = 0.0;
            _elapsed = 0;
        }
    }
}
