// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// Represents one soft-key footer cell (F1..F6).
/// </summary>
public sealed class SoftKeyViewModel : ViewModelBase
{
    private string _key;
    private string _label;

    public SoftKeyViewModel(string key, string label)
    {
        _key = key;
        _label = label;
    }

    public string Key
    {
        get => _key;
        set => SetField(ref _key, value);
    }

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }
}
