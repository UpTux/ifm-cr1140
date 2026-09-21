// SPDX-License-Identifier: GPL-3.0-only
using Cr1140.Avalonia.Input;

namespace Cr1140.Avalonia.Controls;

/// <summary>
/// The single source of truth for the CR1140/CR1141/CR1102 physical soft-key order, and
/// the hardware-to-logical key remap that keeps a <see cref="SoftKeyFooter"/> and
/// its key handling consistent across both <see cref="SoftKeyFooterLayout"/> modes.
/// Supports 6-key (CR1140/CR1141) and 8-key (CR1102) layouts.
/// </summary>
/// <remarks>
/// The CR1140/CR1141 panel's six function keys are physically laid out, left-to-right,
/// <c>F6 F4 F2 · d-pad · F1 F3 F5</c>. The CR1102's eight keys use a different faceplate —
/// a single sequential column <c>F1 F2 F3 F4 · d-pad · F5 F6 F7 F8</c> (top→bottom on the
/// right bezel, live-confirmed 2026-09-21). In <see cref="SoftKeyFooterLayout.Physical"/>
/// the footer shows each key over its real button, so no remap is needed. In
/// <see cref="SoftKeyFooterLayout.Natural"/> the footer shows <c>F1..F6</c> (or F1..F8)
/// in reading order, so the button at physical position <c>i</c> must act as the
/// logical key shown there (<c>F(i+1)</c>): call <see cref="ToLogical(KeypadKey, SoftKeyFooterLayout)"/> on each
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
    /// The function keys in natural reading order for a keypad with
    /// <paramref name="functionKeyCount"/> keys: 6 keys → <c>F1..F6</c>, 8 keys →
    /// <c>F1..F8</c>. Equals <see cref="NaturalOrder"/> when
    /// <paramref name="functionKeyCount"/> is 6.
    /// </summary>
    /// <param name="functionKeyCount">The number of function keys (e.g. 6 or 8).</param>
    public static IReadOnlyList<KeypadKey> NaturalOrderFor(int functionKeyCount)
    {
        var keys = new KeypadKey[functionKeyCount];
        for (int n = 1; n <= functionKeyCount; n++)
        {
            keys[n - 1] = (KeypadKey)(n - 1); // F1=0 .. F8=7 (contiguous at the start of the enum)
        }
        return keys;
    }

    /// <summary>
    /// The physical function-key order for a keypad with <paramref name="functionKeyCount"/>
    /// keys. The two real ifm faceplates differ, so this is keyed by key count (which uniquely
    /// identifies the device), not one formula:
    /// <list type="bullet">
    /// <item><description><b>6 keys (CR1140/CR1141)</b> — a horizontal strip below the screen,
    /// even keys descending left of the d-pad and odd keys ascending right:
    /// <c>F6 F4 F2 · d-pad · F1 F3 F5</c> (equals <see cref="PhysicalOrder"/>).</description></item>
    /// <item><description><b>8 keys (CR1102)</b> — a single vertical column on the right bezel in
    /// confirmed sequential silk-screen order <c>F1 F2 F3 F4 · d-pad · F5 F6 F7 F8</c>
    /// (top→bottom) [live ✓ 2026-09-21]; sequential, so it equals <see cref="NaturalOrderFor"/>.</description></item>
    /// </list>
    /// </summary>
    /// <param name="functionKeyCount">The number of function keys (e.g. 6 or 8).</param>
    public static IReadOnlyList<KeypadKey> PhysicalOrderFor(int functionKeyCount)
    {
        // CR1102 (8 keys): a different faceplate from the CR1140/CR1141 — the eight keys are a
        // single sequential column F1 F2 F3 F4 · d-pad · F5 F6 F7 F8 (top→bottom on the right
        // bezel), confirmed on the live device 2026-09-21. Sequential == natural order.
        if (functionKeyCount == 8)
            return NaturalOrderFor(functionKeyCount);

        // CR1140/CR1141 (6 keys): horizontal bottom strip — even keys descending to the left of
        // the d-pad, odd keys ascending to the right: F6 F4 F2 · d-pad · F1 F3 F5.
        var left = new List<KeypadKey>();
        var right = new List<KeypadKey>();
        for (int n = 1; n <= functionKeyCount; n++)
        {
            var key = (KeypadKey)(n - 1); // F1=0 .. F8=7 (contiguous at the start of the enum)
            if (n % 2 == 0)
                left.Insert(0, key);
            else
                right.Add(key);
        }
        left.AddRange(right);
        return left;
    }

    /// <summary>
    /// Translate a hardware key-press into the logical soft-key for a given footer
    /// layout and function-key count. <see cref="SoftKeyFooterLayout.Physical"/> is
    /// the identity; <see cref="SoftKeyFooterLayout.Natural"/> maps the button at
    /// physical position <c>i</c> to <see cref="NaturalOrderFor"/>(functionKeyCount)[i]
    /// (e.g. hardware F6 → F1 for 6 keys). Non-function keys (arrows, Enter) pass
    /// through unchanged.
    /// </summary>
    /// <param name="hardware">The key as reported by the keypad (e.g. from EvdevKeypadInput).</param>
    /// <param name="layout">The footer layout currently shown to the operator.</param>
    /// <param name="functionKeyCount">The number of function keys (6 or 8).</param>
    /// <returns>The logical soft-key to act on.</returns>
    public static KeypadKey ToLogical(KeypadKey hardware, SoftKeyFooterLayout layout, int functionKeyCount)
    {
        if (layout == SoftKeyFooterLayout.Physical)
        {
            return hardware;
        }

        var physicalOrder = PhysicalOrderFor(functionKeyCount);
        var naturalOrder = NaturalOrderFor(functionKeyCount);
        for (int i = 0; i < physicalOrder.Count; i++)
        {
            if (physicalOrder[i] == hardware)
            {
                return naturalOrder[i];
            }
        }

        // Arrows / Enter and anything not in the function-key set are unchanged.
        return hardware;
    }

    /// <summary>
    /// Translate a hardware key-press into the logical soft-key for a 6-key footer.
    /// See the 3-argument overload for details.
    /// </summary>
    /// <param name="hardware">The key as reported by the keypad.</param>
    /// <param name="layout">The footer layout currently shown.</param>
    /// <returns>The logical soft-key to act on.</returns>
    public static KeypadKey ToLogical(KeypadKey hardware, SoftKeyFooterLayout layout)
    {
        return ToLogical(hardware, layout, 6);
    }
}
