using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Cr1140.AvaloniaDemo.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
