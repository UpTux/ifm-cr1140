// SPDX-License-Identifier: GPL-3.0-only
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Cr1140.AvaloniaDemo.Views;

public partial class LedsView : UserControl
{
    public LedsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
