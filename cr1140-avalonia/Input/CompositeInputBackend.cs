// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;

namespace Cr1140.Avalonia.Input;

/// <summary>
/// An Avalonia <see cref="IInputBackend"/> that fans a single LinuxFramebuffer platform's
/// input lifecycle out to several inner backends, so a panel can run more than one input
/// source at once. The CR1102 needs this: Avalonia's stock touchscreen backend
/// (<c>EvDevBackend</c> over the PCAP touch node) plus the custom
/// <see cref="EvdevKeypadInput"/> for the physical F1–F8 / nav keys. <c>StartLinuxDirect</c>
/// accepts only one <see cref="IInputBackend"/>, so wrap them here.
/// </summary>
/// <remarks>
/// <see cref="Initialize"/> and <see cref="SetInputRoot"/> are forwarded to every inner
/// backend in order. On a keypad-only SKU (CR1140/CR1141) there is nothing to compose —
/// pass the <see cref="EvdevKeypadInput"/> directly. Disposing the composite disposes any
/// inner backend that is <see cref="IDisposable"/>.
/// </remarks>
public sealed class CompositeInputBackend : IInputBackend, IDisposable
{
    private readonly IReadOnlyList<IInputBackend> _backends;

    /// <summary>Compose the given backends; their <see cref="Initialize"/> runs in argument order.</summary>
    /// <param name="backends">The inner input backends (e.g. touch first, then keypad).</param>
    public CompositeInputBackend(params IInputBackend[] backends)
    {
        _backends = backends ?? throw new ArgumentNullException(nameof(backends));
    }

    /// <inheritdoc />
    public void Initialize(IScreenInfoProvider screen, Action<RawInputEventArgs> onInput)
    {
        foreach (var backend in _backends)
            backend.Initialize(screen, onInput);
    }

    /// <inheritdoc />
    public void SetInputRoot(IInputRoot root)
    {
        foreach (var backend in _backends)
            backend.SetInputRoot(root);
    }

    /// <summary>Disposes every inner backend that implements <see cref="IDisposable"/>.</summary>
    public void Dispose()
    {
        foreach (var backend in _backends)
            (backend as IDisposable)?.Dispose();
    }
}
