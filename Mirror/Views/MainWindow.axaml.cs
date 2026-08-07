using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Mirror.ViewModels;
using System.Threading.Tasks;

namespace Mirror.Views;

public partial class MainWindow : Window
{
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var dashboard = new BrowserDashboardView();
        var page = new ContentPage
        {
            Content = dashboard,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        NavigationPage.SetHasNavigationBar(page, false);
        await NavPage.PushAsync(page, null);

        if (DataContext is MainWindowViewModel vm)
        {
            foreach (var browser in vm.Browsers)
            {
                browser.ContextDestroyed += OnContextDestroyed;
            }
        }
    }

    private void OnContextDestroyed(ContextViewModel context)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (NavPage.StackDepth > 1
                && NavPage.CurrentPage is ContentPage { Content: ContextPage { DataContext: ContextViewModel shown } }
                && shown == context)
            {
                await NavPage.PopAsync();
            }
        });
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_isClosing || DataContext is not MainWindowViewModel vm)
            return;

        e.Cancel = true;

        _ = Task.Run(async () =>
        {
            if (NavPage.StackDepth > 1 && NavPage.CurrentPage is ContentPage { Content: ContextPage { DataContext: ContextViewModel contextViewModel } })
            {
                await contextViewModel.CloseContextCommand.ExecuteAsync(null);
                await Dispatcher.UIThread.InvokeAsync(async () => await NavPage.PopAsync());
                return;
            }

            await Parallel.ForEachAsync(vm.Browsers, async (browser, ct) =>
                await browser.StopBrowserCommand.ExecuteAsync(null));

            _isClosing = true;
            await Dispatcher.UIThread.InvokeAsync(Close);
        });
    }
}
