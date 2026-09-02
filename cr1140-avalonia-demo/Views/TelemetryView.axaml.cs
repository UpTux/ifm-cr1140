// SPDX-License-Identifier: GPL-3.0-only
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Cr1140.AvaloniaDemo.ViewModels;

namespace Cr1140.AvaloniaDemo.Views;

public partial class TelemetryView : UserControl
{
    private ScrollViewer? _scroller;
    private TelemetryViewModel? _vm;

    public TelemetryView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // The keypad has no pointer and EvdevKeypadInput injects no Avalonia key events,
    // so the ScrollViewer can't scroll itself. NavigationController turns Up/Down on
    // the Telemetry screen into ScrollUp()/ScrollDown() on the (long-lived) view model;
    // we move the ScrollViewer here in response. Subscribe on attach / release on
    // detach so a recreated view never leaks onto the shared view model.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroller = this.FindControl<ScrollViewer>("Scroller");
        if (DataContext is TelemetryViewModel vm)
        {
            _vm = vm;
            _vm.ScrollRequested += OnScrollRequested;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.ScrollRequested -= OnScrollRequested;
            _vm = null;
        }

        _scroller = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrollRequested(double delta)
    {
        if (_scroller is null)
        {
            return;
        }

        var max = Math.Max(0, _scroller.Extent.Height - _scroller.Viewport.Height);
        var y = Math.Clamp(_scroller.Offset.Y + delta, 0, max);
        _scroller.Offset = new Vector(_scroller.Offset.X, y);
    }
}
