// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.ObjectModel;
using System.Globalization;
using Cr1140.Avalonia.Input;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// The six key events surfaced by <see cref="EvdevKeypadInput"/>: raw down/up plus the
/// derived gestures. Order matches the <see cref="KeyEventsViewModel"/> card layout.
/// </summary>
public enum KeyEventKind
{
    Pressed,
    Released,
    Tapped,
    DoubleTapped,
    Held,
    Holding
}

/// <summary>
/// One event-type card: a fixed <see cref="Name"/> and accent <see cref="Color"/> with a
/// live <see cref="LastKey"/>, <see cref="Count"/>, and an <see cref="IsRecent"/> flag set
/// on the row that fired most recently (drives the highlight border).
/// </summary>
public sealed class KeyEventRow : ViewModelBase
{
    private string _lastKey = "—";
    private int _count;
    private bool _isRecent;

    public KeyEventRow(string name, string color)
    {
        Name = name;
        Color = color;
    }

    public string Name { get; }

    /// <summary>Accent colour (hex) for this event type; bound to brushes via a converter.</summary>
    public string Color { get; }

    public string LastKey
    {
        get => _lastKey;
        set => SetField(ref _lastKey, value);
    }

    public int Count
    {
        get => _count;
        set => SetField(ref _count, value);
    }

    public bool IsRecent
    {
        get => _isRecent;
        set => SetField(ref _isRecent, value);
    }
}

/// <summary>
/// Live demonstration of the <see cref="EvdevKeypadInput"/> event model. Every physical
/// key press flows through <c>KeyPressed → KeyReleased</c> plus the derived gestures
/// (<c>KeyTapped</c>, <c>KeyDoubleTapped</c>, <c>KeyHeld</c>, <c>KeyHolding</c>); this VM
/// shows the last key and hit-count per event type, highlights the event that just fired,
/// and keeps a rolling log. Feed it via <see cref="Record"/> on the UI thread.
/// </summary>
public sealed class KeyEventsViewModel : ViewModelBase
{
    private const int MaxLog = 10;

    private readonly KeyEventRow[] _rows;

    public KeyEventsViewModel()
    {
        _rows = new[]
        {
            new KeyEventRow("Pressed", "#00aaff"),
            new KeyEventRow("Released", "#9aa0a6"),
            new KeyEventRow("Tapped", "#22cc88"),
            new KeyEventRow("Double Tap", "#ffcc00"),
            new KeyEventRow("Held", "#ff7744"),
            new KeyEventRow("Holding", "#ff4488"),
        };
        Rows = new ObservableCollection<KeyEventRow>(_rows);
        Log = new ObservableCollection<string>();
    }

    /// <summary>The six event-type cards (Pressed, Released, Tapped, Double Tap, Held, Holding).</summary>
    public ObservableCollection<KeyEventRow> Rows { get; }

    /// <summary>Rolling event log, newest first, capped at <see cref="MaxLog"/> entries.</summary>
    public ObservableCollection<string> Log { get; }

    /// <summary>Record one key event: bumps the matching card, moves the highlight, and logs it.</summary>
    public void Record(KeyEventKind kind, KeypadKey key)
    {
        var row = _rows[(int)kind];
        row.LastKey = key.ToString();
        row.Count++;

        for (int i = 0; i < _rows.Length; i++)
            _rows[i].IsRecent = i == (int)kind;

        var stamp = System.DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        Log.Insert(0, $"{stamp}   {row.Name} · {key}");
        while (Log.Count > MaxLog)
            Log.RemoveAt(Log.Count - 1);
    }

    /// <summary>Clear all counters, last-key values, highlight, and the log (called on screen entry).</summary>
    public void Reset()
    {
        foreach (var row in _rows)
        {
            row.LastKey = "—";
            row.Count = 0;
            row.IsRecent = false;
        }
        Log.Clear();
    }
}
