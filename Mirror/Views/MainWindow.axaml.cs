using Avalonia.Controls;
using Avalonia.Input;
using Mirror.ViewModels;
using System.Threading.Tasks;

namespace Mirror.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Border_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.DataContext is ContextViewModel context)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.NavigateToContextCommand.Execute(context);
            }
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (vm.CurrentView is ContextViewModel contextViewModel)
            {
                e.Cancel = true;

                Task.Run(async () =>
                {
                    await contextViewModel.CloseContextCommand.ExecuteAsync(null);
                }).GetAwaiter().GetResult();

                vm.NavigateBackCommand.Execute(null);
            }
            else
            {
                // Execute async cleanup and then close
                Task.Run(async () =>
                {
                    await Parallel.ForEachAsync(vm.Browsers, async (browser, ct) =>
                    {
                        await browser.StopBrowserCommand.ExecuteAsync(null);
                    });
                }).GetAwaiter().GetResult();
            }
        }

        base.OnClosing(e);
    }
}
