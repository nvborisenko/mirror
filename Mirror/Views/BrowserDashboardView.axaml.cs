using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.VisualTree;
using Mirror.ViewModels;

namespace Mirror.Views;

public partial class BrowserDashboardView : UserControl
{
    private ImplicitAnimationCollection? _implicitAnimations;

    public static readonly AttachedProperty<bool> EnableAnimationsProperty =
        AvaloniaProperty.RegisterAttached<BrowserDashboardView, Border, bool>("EnableAnimations");

    public static void SetEnableAnimations(Border border, bool value)
    {
        border.SetValue(EnableAnimationsProperty, value);
        var page = border.FindAncestorOfType<BrowserDashboardView>();
        if (page is null)
        {
            border.AttachedToVisualTree += delegate { SetEnableAnimations(border, true); };
            return;
        }

        if (ElementComposition.GetElementVisual(page) is null)
            return;

        page.ApplyImplicitAnimations(border);
    }
    public static bool GetEnableAnimations(Border border) => border.GetValue(EnableAnimationsProperty);

    public BrowserDashboardView()
    {
        InitializeComponent();
    }

    private void EnsureImplicitAnimations()
    {
        if (_implicitAnimations is not null) return;

        var compositor = ElementComposition.GetElementVisual(this)!.Compositor;

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Target = "Offset";
        offsetAnimation.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
        offsetAnimation.Duration = TimeSpan.FromMilliseconds(300);

        var animationGroup = compositor.CreateAnimationGroup();
        animationGroup.Add(offsetAnimation);

        _implicitAnimations = compositor.CreateImplicitAnimationCollection();
        _implicitAnimations["Offset"] = animationGroup;
    }

    internal void ApplyImplicitAnimations(Border border)
    {
        if (ElementComposition.GetElementVisual(this) is null)
            return;

        EnsureImplicitAnimations();

        if (border.GetVisualParent() is Visual visualParent
            && ElementComposition.GetElementVisual(visualParent) is CompositionVisual compositionVisual)
        {
            compositionVisual.ImplicitAnimations = _implicitAnimations;
        }
    }

    private async void Border_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ContextViewModel context)
        {
            var nav = this.FindAncestorOfType<NavigationPage>();
            if (nav is null) return;

            var contextPage = new ContextPage { DataContext = context };
            var page = new ContentPage
            {
                Content = contextPage,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            NavigationPage.SetHasNavigationBar(page, false);
            await nav.PushAsync(page);
        }
    }

    private void CloseButton_Tapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }

    private void Card_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // DataContext may not be set yet; Card_DataContextChanged will handle it
    }

    private void Card_DetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Border border) return;

        if (border.Tag is ContextViewModel oldVm)
        {
            _ = oldVm.StopScreenCaptureAsync();
            border.Tag = null;
        }
    }

    private void Card_DataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not Border border) return;

        // Stop capture on the previously bound VM (recycling case)
        if (border.Tag is ContextViewModel oldVm)
            _ = oldVm.StopScreenCaptureAsync();

        if (border.DataContext is ContextViewModel newVm)
        {
            border.Tag = newVm;
            newVm.StartScreenCapture();
        }
        else
        {
            border.Tag = null;
        }
    }

    private void ButtonSpinner_Spin(object? sender, SpinEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;
        if (vm?.CurrentBrowser == null) return;

        if (e.Direction == SpinDirection.Increase)
        {
            vm.CurrentBrowser.EmulationThreads++;
        }
        else
        {
            if (vm.CurrentBrowser.EmulationThreads != 1)
            {
                vm.CurrentBrowser.EmulationThreads--;
            }
        }
    }
}
