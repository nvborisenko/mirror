using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQA.Selenium;
using OpenQA.Selenium.BiDi;
using OpenQA.Selenium.BiDi.BrowsingContext;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mirror.ViewModels;

public partial class BrowserViewModel(MainWindowViewModel mainWindowViewModel, Type type, string logoPath) : ViewModelBase
{
    private IWebDriver? _webDriver;
    private BiDi? _bidi;

    public Bitmap LogoPath { get; } = new(AssetLoader.Open(new Uri(logoPath)));

    private readonly Lock _contextsLock = new();
    public ObservableCollection<ContextViewModel> Contexts { get; } = [];


    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartBrowserCommand), nameof(StartEmulationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isHeadless = true;

    [ObservableProperty]
    private bool _isBetaChannel = false;

    [ObservableProperty]
    private bool _isIsolated = false;

    [ObservableProperty]
    private string _browserVersion;

    private bool CanStartBrowser() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanStartBrowser))]
    private async Task StartBrowser()
    {
        await StartBrowserCore();
    }

    private static readonly SemaphoreSlim _semaphoreStartBrowser = new(1, 1);

    private async Task<BrowsingContext> StartBrowserCore()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = true;
            });

        BrowsingContext createdContext = null;

        try
        {
            await _semaphoreStartBrowser.WaitAsync();

            // Run browser creation on a background thread to avoid blocking UI
            await Task.Run(async () =>
            {
                if (_webDriver is null)
                {
                    if (type == typeof(OpenQA.Selenium.Chrome.ChromeDriver))
                    {
                        var driverService = OpenQA.Selenium.Chrome.ChromeDriverService.CreateDefaultService();
                        driverService.HideCommandPromptWindow = true;

                        var options = new OpenQA.Selenium.Chrome.ChromeOptions { UseWebSocketUrl = true };

                        if (IsHeadless)
                        {
                            options.AddArgument("--headless=new");
                        }

                        if (IsBetaChannel)
                        {
                            options.BrowserVersion = "beta";
                        }

                        _webDriver = new OpenQA.Selenium.Chrome.ChromeDriver(driverService, options);
                    }
                    else if (type == typeof(OpenQA.Selenium.Firefox.FirefoxDriver))
                    {
                        var driverService = OpenQA.Selenium.Firefox.FirefoxDriverService.CreateDefaultService();
                        driverService.HideCommandPromptWindow = true;

                        var options = new OpenQA.Selenium.Firefox.FirefoxOptions { UseWebSocketUrl = true };

                        if (IsHeadless)
                        {
                            options.AddArgument("--headless");
                        }

                        if (IsBetaChannel)
                        {
                            options.BrowserVersion = "beta";
                        }

                        _webDriver = new OpenQA.Selenium.Firefox.FirefoxDriver(driverService, options);
                    }
                    else if (type == typeof(OpenQA.Selenium.Edge.EdgeDriver))
                    {
                        var driverService = OpenQA.Selenium.Edge.EdgeDriverService.CreateDefaultService();
                        driverService.HideCommandPromptWindow = true;

                        var options = new OpenQA.Selenium.Edge.EdgeOptions { UseWebSocketUrl = true };

                        if (IsHeadless)
                        {
                            options.AddArgument("--headless=new");
                        }

                        if (IsBetaChannel)
                        {
                            options.BrowserVersion = "beta";
                        }

                        _webDriver = new OpenQA.Selenium.Edge.EdgeDriver(driverService, options);
                    }

                    BrowserVersion = ((IHasCapabilities)_webDriver).Capabilities.GetCapability("browserVersion")?.ToString() ?? "Unknown";

                    _bidi = await _webDriver.AsBiDiAsync();

                    createdContext = (await _bidi.BrowsingContext.GetTreeAsync()).Contexts[0].Context;

                    var firstContext = new ContextViewModel(createdContext);

                    await firstContext.InitializeAsync();

                    // Update UI from background thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _contextsLock.Enter();
                        Contexts.Add(firstContext);
                        _contextsLock.Exit();
                    });

                    await _bidi.BrowsingContext.OnContextCreatedAsync(async e =>
                    {
                        if (e.Parent is null && !Contexts.Any(c => c.Context.Equals(e.Context)))
                        {
                            var vm = new ContextViewModel(e.Context);

                            await vm.InitializeAsync();

                            // Update UI from background thread
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                _contextsLock.Enter();
                                Contexts.Add(vm);
                                _contextsLock.Exit();
                            });
                        }
                    });

                    await _bidi.BrowsingContext.OnContextDestroyedAsync(async e =>
                    {
                        var vm = Contexts.FirstOrDefault(vm => vm.Context.Equals(e.Context));

                        if (vm is not null)
                        {
                            // Update UI from background thread
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                _contextsLock.Enter();
                                Contexts.Remove(vm);
                                _contextsLock.Exit();
                            });
                        }

                        if (mainWindowViewModel.CurrentView is ContextViewModel contextViewModel && contextViewModel.Equals(vm))
                        {
                            mainWindowViewModel.CurrentView = null;
                        }

                        if (Contexts.Count == 0)
                        {
                            await StopBrowser();
                        }
                    });
                }
                else
                {
                    if (IsIsolated)
                    {
                        var userContext = await _bidi.Browser.CreateUserContextAsync();

                        createdContext = (await _bidi.BrowsingContext.CreateAsync(ContextType.Tab, new() { UserContext = userContext.UserContext })).Context;
                    }
                    else
                    {
                        createdContext = (await _bidi.BrowsingContext.CreateAsync(ContextType.Tab)).Context;
                    }
                }
            });
        }
        finally
        {
            _semaphoreStartBrowser.Release();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = false;
            });
        }

        return createdContext;
    }

    [RelayCommand]
    private async Task StopBrowser()
    {
        _contextsLock.Enter();

        await Task.Run(async () =>
        {
            if (_bidi is not null)
            {
                try
                {
                    // await _bidi.DisposeAsync();
                }
                catch (Exception)
                {
                    // Ignore exceptions during disposal
                }
                finally
                {
                    _bidi = null;
                }
            }

            _webDriver?.Dispose();

            _webDriver = null;
        });

        _contextsLock.Exit();
    }

    private bool CanStartEmulation() => !IsBusy && Contexts.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStartEmulation))]
    public async Task StartEmulation()
    {
        List<Task> tasks = [];

        for (int i = 0; i < 20; i++)
        {
            tasks.Add(EmulationScenarioAsync());
        }

        await Task.WhenAll(tasks);
    }

    private async Task EmulationScenarioAsync()
    {
        var context = await StartBrowserCore();
        await context.NavigateAsync("https://nuget.org", new() { Wait = ReadinessState.Complete });
        await Task.Delay(3_000);
        await context.CloseAsync();
    }
}
