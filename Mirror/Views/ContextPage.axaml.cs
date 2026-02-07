using Avalonia.Controls;
using Avalonia.Input;
using Mirror.ViewModels;
using OpenQA.Selenium.BiDi.BrowsingContext;
using OpenQA.Selenium.BiDi.Input;
using System;

namespace Mirror.Views;

public partial class ContextPage : UserControl
{
    private DateTime _lastMouseMoveTime = DateTime.MinValue;
    private const int MouseMoveThrottleMs = 50; // Max 20 updates/sec

    public ContextPage()
    {
        InitializeComponent();
    }

    private async void OnImagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is Image image)
        {
            var position = e.GetPosition(image);

            // Throttle mouse move to reduce BiDi traffic
            var now = DateTime.UtcNow;
            if ((now - _lastMouseMoveTime).TotalMilliseconds < MouseMoveThrottleMs)
                return;

            _lastMouseMoveTime = now;

            X.Text = $"X: {position.X:F0}";
            Y.Text = $"Y: {position.Y:F0}";

            var vm = this.DataContext as ContextViewModel;

            await vm!.Context.Input.PerformActionsAsync([new PointerActions("mirror-pointer"){
                new MovePointer((int)position.X, (int)position.Y)
            }]);
        }
    }

    private async void OnImageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Size.Text = $"{e.NewSize.Width:F0}x{e.NewSize.Height:F0}";

        var vm = this.DataContext as ContextViewModel;

        await vm!.Context.SetViewportAsync(new() { Viewport = new Viewport((long)e.NewSize.Width, (long)e.NewSize.Height) });
    }

    private async void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Image image)
        {
            var position = e.GetPosition(image);
            var vm = this.DataContext as ContextViewModel;

            await vm!.Context.Input.PerformActionsAsync([new PointerActions("mirror-pointer"){
                new MovePointer((int)position.X, (int)position.Y),
                new DownPointer(0)
            }]);

            Size.Text = "Pressed";
        }
    }

    private async void OnImagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Image image)
        {
            var position = e.GetPosition(image);
            var vm = this.DataContext as ContextViewModel;

            await vm!.Context.Input.PerformActionsAsync([new PointerActions("mirror-pointer"){
                new MovePointer((int)position.X, (int)position.Y),
                new UpPointer(0)
            }]);

            await vm!.Context.Input.ReleaseActionsAsync();

            Size.Text = "Released";
        }
    }

    private async void OnImagePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is Image image)
        {
            var position = e.GetPosition(image);
            var vm = this.DataContext as ContextViewModel;
            var delta = e.Delta;

            await vm!.Context.Input.PerformActionsAsync([new WheelActions("mirror-wheel"){
                new ScrollWheel((int)position.X, (int)position.Y, -(int)(delta.X * 100), -(int)(delta.Y * 100))
            }]);
        }
    }

    private async void OnImageKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = this.DataContext as ContextViewModel;

        if (e.KeySymbol is null || e.KeySymbol.Length > 1)
            return;

        var key = e.KeySymbol[0];

        await vm!.Context.Input.PerformActionsAsync([new KeyActions("mirror-keyboard"){
            new DownKey(key)
        }]);
    }

    private async void OnImageKeyUp(object? sender, KeyEventArgs e)
    {
        var vm = this.DataContext as ContextViewModel;

        if (e.KeySymbol is null || e.KeySymbol.Length > 1)
            return;

        var key = e.KeySymbol[0];

        await vm!.Context.Input.PerformActionsAsync([new KeyActions("mirror-keyboard"){
            new UpKey(key)
        }]);

        await vm!.Context.Input.ReleaseActionsAsync();
    }
}
