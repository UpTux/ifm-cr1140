// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Devices;

/// <summary>
/// The panel edge along which a device's physical function keys are arranged, in native
/// landscape orientation. Consumed by the demo shell (and the emulator bezel) to dock the
/// <see cref="Controls.SoftKeyFooter"/> on the matching edge — and orient it as a row or a
/// column — so each on-screen label sits next to the physical button that triggers it.
/// </summary>
/// <remarks>
/// The CR1140/CR1141 keypad runs along the <see cref="Bottom"/> of the 4.3" panel, so the
/// soft-key footer is a horizontal strip. The CR1102's eight keys are a vertical column on
/// the <see cref="Right"/> of the 10" panel (four keys, the nav/OK cluster, four keys),
/// so the footer is a vertical column docked right.
/// </remarks>
public enum SoftKeyEdge
{
    /// <summary>Keys along the bottom edge; the footer is a horizontal strip (CR1140/CR1141).</summary>
    Bottom,

    /// <summary>Keys along the top edge; the footer is a horizontal strip.</summary>
    Top,

    /// <summary>Keys along the left edge; the footer is a vertical column.</summary>
    Left,

    /// <summary>Keys along the right edge; the footer is a vertical column (CR1102).</summary>
    Right,
}
