namespace Cr1140.Avalonia.Input;

/// <summary>
/// Timing thresholds that turn raw key down/up events into higher-level gestures
/// (tap, double-tap, hold). Passed to <see cref="KeyGestureDetector"/> and, via the
/// constructor, to <see cref="EvdevKeypadInput"/>.
/// </summary>
public sealed class KeyGestureOptions
{
    /// <summary>
    /// How long a key must stay down before it counts as a hold rather than a tap.
    /// When crossed, <see cref="KeyGestureDetector.Held"/> fires once. Default 500&#160;ms.
    /// </summary>
    public TimeSpan HoldThreshold { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Interval between repeated <see cref="KeyGestureDetector.Holding"/> pulses while a
    /// key stays down past <see cref="HoldThreshold"/> (the auto-repeat cadence). Default 150&#160;ms.
    /// </summary>
    public TimeSpan HoldRepeatInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Maximum gap between two taps (release-to-release) for them to count as a double-tap.
    /// A lone tap is reported as <see cref="KeyGestureDetector.Tapped"/> once this window
    /// elapses with no second tap. Default 300&#160;ms.
    /// </summary>
    public TimeSpan DoubleTapWindow { get; init; } = TimeSpan.FromMilliseconds(300);
}

/// <summary>
/// A pure, allocation-free state machine that derives <see cref="Tapped"/>,
/// <see cref="DoubleTapped"/>, <see cref="Held"/>, and <see cref="Holding"/> gestures from
/// raw key <see cref="Down"/>/<see cref="Up"/> events plus a periodic <see cref="Tick"/>.
/// </summary>
/// <remarks>
/// <para>
/// Timestamps are caller-supplied monotonic milliseconds (e.g. <see cref="Environment.TickCount64"/>),
/// so the detector is fully deterministic and host-testable without any hardware, threads, or clock.
/// <see cref="EvdevKeypadInput"/> owns an instance, feeds it from the evdev reader thread, and drives
/// <see cref="Tick"/> from a timer.
/// </para>
/// <para>
/// This type is <b>not</b> thread-safe: the owner must serialize <see cref="Down"/>, <see cref="Up"/>,
/// and <see cref="Tick"/> (and therefore event delivery). Per-key state is kept in a fixed array indexed
/// by <see cref="KeypadKey"/>, so keys are tracked independently and nothing is allocated after construction.
/// </para>
/// </remarks>
public sealed class KeyGestureDetector
{
    private static readonly int KeyCount = Enum.GetValues<KeypadKey>().Length;

    private readonly long _holdThresholdMs;
    private readonly long _holdRepeatMs;
    private readonly long _doubleTapMs;
    private readonly State[] _states;

    /// <summary>Fires once for a completed short press with no second tap inside the double-tap window.</summary>
    public event Action<KeypadKey>? Tapped;

    /// <summary>Fires when two taps of the same key complete within <see cref="KeyGestureOptions.DoubleTapWindow"/>.</summary>
    public event Action<KeypadKey>? DoubleTapped;

    /// <summary>Fires once when a key has stayed down past <see cref="KeyGestureOptions.HoldThreshold"/>.</summary>
    public event Action<KeypadKey>? Held;

    /// <summary>
    /// Fires repeatedly (every <see cref="KeyGestureOptions.HoldRepeatInterval"/>) while a key stays down
    /// after <see cref="Held"/>, giving press-and-hold auto-repeat.
    /// </summary>
    public event Action<KeypadKey>? Holding;

    /// <summary>Creates a detector with the given thresholds (or defaults when <paramref name="options"/> is null).</summary>
    public KeyGestureDetector(KeyGestureOptions? options = null)
    {
        options ??= new KeyGestureOptions();
        _holdThresholdMs = ToMs(options.HoldThreshold);
        _holdRepeatMs = Math.Max(1, ToMs(options.HoldRepeatInterval));
        _doubleTapMs = ToMs(options.DoubleTapWindow);
        _states = new State[KeyCount];
    }

    /// <summary>
    /// True when no key is down and no tap is awaiting double-tap resolution — i.e. <see cref="Tick"/>
    /// has no pending work and the owner may stop its timer until the next key event.
    /// </summary>
    public bool IsIdle
    {
        get
        {
            for (int i = 0; i < _states.Length; i++)
            {
                ref var s = ref _states[i];
                if (s.Down || s.PendingSingle)
                    return false;
            }
            return true;
        }
    }

    /// <summary>Feeds a key-down at <paramref name="nowMs"/> (monotonic milliseconds).</summary>
    public void Down(KeypadKey key, long nowMs)
    {
        ref var s = ref _states[(int)key];
        s.Down = true;
        s.DownAt = nowMs;
        s.Held = false;
    }

    /// <summary>Feeds a key-up at <paramref name="nowMs"/> (monotonic milliseconds), resolving tap/double-tap/hold.</summary>
    public void Up(KeypadKey key, long nowMs)
    {
        ref var s = ref _states[(int)key];
        if (!s.Down)
            return; // up without a matching down (spurious) — ignore

        s.Down = false;
        bool wasHeld = s.Held;
        s.Held = false;

        if (wasHeld)
            return; // releasing a recognized hold is never a tap

        long duration = nowMs - s.DownAt;
        if (duration >= _holdThresholdMs)
        {
            // Long press whose Held pulse the Tick loop had not emitted yet — recognize it now.
            Held?.Invoke(key);
            return;
        }

        // Completed short press = a tap. Pair with a prior pending tap to form a double-tap.
        if (s.PendingSingle && nowMs - s.LastTapUpAt <= _doubleTapMs)
        {
            s.PendingSingle = false;
            DoubleTapped?.Invoke(key);
        }
        else
        {
            s.PendingSingle = true;
            s.LastTapUpAt = nowMs;
        }
    }

    /// <summary>
    /// Advances time to <paramref name="nowMs"/>, emitting <see cref="Held"/>/<see cref="Holding"/> for keys
    /// still down and <see cref="Tapped"/> for a lone tap whose double-tap window has elapsed.
    /// </summary>
    public void Tick(long nowMs)
    {
        for (int i = 0; i < _states.Length; i++)
        {
            ref var s = ref _states[i];
            var key = (KeypadKey)i;

            if (s.Down)
            {
                if (!s.Held)
                {
                    if (nowMs - s.DownAt >= _holdThresholdMs)
                    {
                        s.Held = true;
                        s.NextRepeatAt = nowMs + _holdRepeatMs;
                        Held?.Invoke(key);
                    }
                }
                else if (nowMs >= s.NextRepeatAt)
                {
                    s.NextRepeatAt = nowMs + _holdRepeatMs;
                    Holding?.Invoke(key);
                }
            }
            else if (s.PendingSingle && nowMs - s.LastTapUpAt >= _doubleTapMs)
            {
                s.PendingSingle = false;
                Tapped?.Invoke(key);
            }
        }
    }

    private static long ToMs(TimeSpan span) => (long)span.TotalMilliseconds;

    private struct State
    {
        public bool Down;          // key currently pressed
        public long DownAt;        // timestamp of the current press
        public bool Held;          // hold threshold crossed for the current press
        public long NextRepeatAt;  // next Holding pulse deadline (valid once Held)
        public bool PendingSingle; // a tap is awaiting double-tap resolution
        public long LastTapUpAt;   // release timestamp of the pending tap
    }
}
