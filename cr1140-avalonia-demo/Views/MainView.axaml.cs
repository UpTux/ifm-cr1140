using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Cr1140.Avalonia.Diagnostics;
using Cr1140.Avalonia.Input;
using Cr1140.AvaloniaDemo;

namespace Cr1140.AvaloniaDemo.Views;

public partial class MainView : UserControl
{
    private bool _perfAttached;
    public MainView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_perfAttached) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        top.AttachPerfOverlay(Program.FrameStats, new PerfOverlayOptions
        {
            ToggleKeypad = Program.Keypad,
            ToggleKey = KeypadKey.F5,
            ToggleGesture = PerfOverlayToggleGesture.DoubleTapped,
            StartVisible = Program.PerfStartVisible
        });
        _perfAttached = true;
    }
}
