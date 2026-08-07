using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mirror.ViewModels;
using Mirror.Views;

namespace Mirror
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Dispatcher.UIThread.UnhandledException += async (sender, e) =>
            {
                e.Handled = true;
                var owner = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                var dialog = new ExceptionDialog(e.Exception);
                if (owner is not null)
                    await dialog.ShowDialog(owner);
                else
                    dialog.Show();
            };

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        // DisableAvaloniaDataAnnotationValidation no longer needed in Avalonia 12 (BindingPlugins is internal)
    }
}
