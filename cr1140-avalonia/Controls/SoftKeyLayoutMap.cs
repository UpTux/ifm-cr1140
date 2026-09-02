// SPDX-License-Identifier: GPL-3.0-only
using Cr1140.Avalonia.Input;

namespace Cr1140.Avalonia.Controls;

/// <summary>
/// The single source of truth for the CR1140/CR1141 physical soft-key order, and
/// the hardware-to-logical key remap that keeps a <see cref="SoftKeyFooter"/> and
/// its key handling consistent across both <see cref="SoftKeyFooterLayout"/> modes.
/// </summary>
/// <remarks>
/// The panel's six function keys are physically laid out, left-to-right,
/// <c>F6 F4 F2 · d-pad · F1 F3 F5</c>. In <see cref="SoftKeyFooterLayout.Physical"/>
/// the footer shows each key over its real button, so no remap is needed. In
/// <see cref="SoftKeyFooterLayout.Natural"/> the footer shows <c>F1..F6</c> in
/// reading order, so the button at physical position <c>i</c> must act as the
/// logical key shown there (<c>F(i+1)</c>): call <see cref="ToLogical"/> on each
/// incoming key-press so actions line up with the on-screen labels.
/// </remarks>
public static class SoftKeyLayoutMap
{
    /// <summary>The six function keys in physical left-to-right order on the keypad.</summary>
    public static readonly IReadOnlyList<KeypadKey> PhysicalOrder = new[]
    {
        KeypadKey.F6, KeypadKey.F4, KeypadKey.F2, KeypadKey.F1, KeypadKey.F3, KeypadKey.F5
    };

    /// <summary>The six function keys in natural reading order (<c>F1..F6</c>).</summary>
    public static readonly IReadOnlyList<KeypadKey> NaturalOrder = new[]
    {
        KeypadKey.F1, KeypadKey.F2, KeypadKey.F3, KeypadKey.F4, KeypadKey.F5, KeypadKey.F6
    };

    /// <summary>
    /// Translate a hardware key-press into the logical soft-key for a given footer
    /// layout. <see cref="SoftKeyFooterLayout.Physical"/> is the identity;
    /// <see cref="SoftKeyFooterLayout.Natural"/> maps the button at physical
    /// position <c>i</c> to <see cref="NaturalOrder"/>[i] (e.g. hardware F6 → F1).
    /// Non-function keys (arrows, Enter) pass through unchanged.
    /// </summary>
    /// <param name="hardware">The key as reported by the keypad (e.g. from EvdevKeypadInput).</param>
    /// <param name="layout">The footer layout currently shown to the operator.</param>
    /// <returns>The logical soft-key to act on.</returns>
    public static KeypadKey ToLogical(KeypadKey hardware, SoftKeyFooterLayout layout)
    {
        if (layout == SoftKeyFooterLayout.Physical)
        {
            return hardware;
        }

        for (int i = 0; i < PhysicalOrder.Count; i++)
        {
            if (PhysicalOrder[i] == hardware)
            {
                return NaturalOrder[i];
            }
        }

        // Arrows / Enter and anything not in the function-key set are unchanged.
        return hardware;
    }
}
