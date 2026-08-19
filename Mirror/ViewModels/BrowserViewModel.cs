using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQA.Selenium;
using OpenQA.Selenium.BiDi;
using OpenQA.Selenium.BiDi.Browser;
using OpenQA.Selenium.BiDi.BrowsingContext;
using OpenQA.Selenium.BiDi.Input;
using OpenQA.Selenium.BiDi.Network;
using OpenQA.Selenium.BiDi.Script;
using Selenium.WebDriver.BiDi.Cdp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mirror.ViewModels;

public partial class BrowserViewModel(Type type, string logoPath) : ViewModelBase
{
    private IWebDriver? _webDriver;
    private IBiDi? _bidi;
    private Collector? _networkDataCollector;
    private ISubscription? _subscription;
    private IEventStream<MessageEventArgs>? _messageStream;
    private PreloadScript? _preloadScript;
    private Task? _messageDispatchTask;

    public event Action<ContextViewModel>? ContextDestroyed;

    public Bitmap LogoPath { get; } = new(AssetLoader.Open(new Uri(logoPath)));

    public ObservableCollection<ContextViewModel> Contexts { get; } = [];
    private readonly Dictionary<BrowsingContext, ContextViewModel> _contextMap = [];

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
    private string _browserVersion = string.Empty;

    private bool CanStartBrowser() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanStartBrowser))]
    private async Task StartBrowser()
    {
        await StartBrowserCore();
    }

    private static readonly SemaphoreSlim _semaphoreStartBrowser = new(1, 1);

    private async Task<BrowsingContext?> StartBrowserCore()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBusy = true;
            });

        BrowsingContext? createdContext = null;

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
                    else
                    {
                        throw new NotSupportedException($"Browser type {type.Name} is not supported");
                    }

                    BrowserVersion = ((IHasCapabilities)_webDriver).Capabilities.GetCapability("browserVersion")?.ToString() ?? "Unknown";

                    _bidi = await _webDriver.AsBiDiAsync() ?? throw new InvalidOperationException("Failed to initialize BiDi connection");

                    await InitializeBiDiAsync();

                    createdContext = (await _bidi.BrowsingContext.GetTreeAsync()).Contexts[0].Context;

                    var firstContext = new ContextViewModel(createdContext);

                    await firstContext.InitializeAsync();

                    // Inject screencast script into the first context (preload script only applies to future navigations)
                    var channelArg = new ChannelLocalValue(new ChannelProperties(_screencastChannel!));
                    await createdContext.Script.CallFunctionAsync(ScreencastScript, false, new CallFunctionOptions { Arguments = [channelArg] });

                    _contextMap[createdContext] = firstContext;

                    // Update UI from background thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Contexts.Add(firstContext);
                    });
                }
                else if (_bidi is not null)
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
        // Snapshot and clear the Contexts collection first so no new events add to it
        ContextViewModel[] contextsToDispose = [];
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            contextsToDispose = [.. Contexts];
            Contexts.Clear();
        });

        // Dispose all context view models (stops screenshot loops, releases subscriptions)
        foreach (var ctx in contextsToDispose)
            await ctx.DisposeAsync();

        await Task.Run(async () =>
        {
            if (_bidi is not null)
            {
                if (_messageStream is not null)
                {
                    await _messageStream.DisposeAsync();
                    _messageStream = null;
                }

                if (_messageDispatchTask is not null)
                {
                    try { await _messageDispatchTask; } catch { }
                    _messageDispatchTask = null;
                }

                if (_preloadScript is not null)
                {
                    try { await _bidi.Script.RemovePreloadScriptAsync(_preloadScript); } catch { }
                    _preloadScript = null;
                }

                if (_subscription is not null)
                {
                    await _subscription.DisposeAsync();
                    _subscription = null;
                }

                if (_networkDataCollector is not null)
                {
                    await _bidi.Network.RemoveDataCollectorAsync(_networkDataCollector);
                    _networkDataCollector = null;
                }

                await _bidi.DisposeAsync();
                _bidi = null;
            }

            _webDriver?.Dispose();

            _webDriver = null;
        });
    }

    private static readonly string ScreencastScript = """
    (channel) => {
      let dirty = true;
      function markDirty() { dirty = true; }

      setInterval(() => {
        if (dirty) {
          dirty = false;
          channel('d');
        }
      }, 100);

      new MutationObserver(markDirty).observe(document, {
        subtree: true, childList: true, attributes: true,
        characterData: true
      });

      window.addEventListener('scroll', markDirty, { capture: true, passive: true });
      window.addEventListener('load', markDirty);
      window.addEventListener('DOMContentLoaded', markDirty);

      if (document.documentElement) {
        new ResizeObserver(markDirty).observe(document.documentElement);
      }

      document.addEventListener('animationstart', markDirty, true);
      document.addEventListener('transitionrun', markDirty, true);
      document.addEventListener('input', markDirty, true);
      document.addEventListener('play', markDirty, true);
    }
    """;

    private Channel? _screencastChannel;

    private async Task InitializeBiDiAsync()
    {
        var dataCollectorResult = await _bidi!.Network.AddDataCollectorAsync([DataType.Request, DataType.Response], 300_000);
        _networkDataCollector = dataCollectorResult.Collector;

        // Install global screencast preload script
        _screencastChannel = new Channel(_bidi, "mirror-screencast");
        var channelArg = new ChannelLocalValue(new ChannelProperties(_screencastChannel));

        var preloadResult = await _bidi.Script.AddPreloadScriptAsync(
            ScreencastScript,
            new AddPreloadScriptOptions { Arguments = [channelArg] });
        _preloadScript = preloadResult.Script;

        // Subscribe to script.message globally and dispatch to contexts
        _messageStream = await _bidi.Script.Message.StreamAsync();
        _messageDispatchTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in _messageStream.ReadAllAsync())
                {
                    if (msg.Channel.Id != _screencastChannel!.Id) continue;
                    if (msg.Source.Context is not null && _contextMap.TryGetValue(msg.Source.Context, out var vm))
                    {
                        vm.RequestCapture();
                    }
                }
            }
            catch { }
        });

        _subscription = await _bidi.SubscribeAsync<OpenQA.Selenium.BiDi.EventArgs>(
            [
                NetworkEvent.BeforeRequestSent,
                NetworkEvent.ResponseCompleted,
                BrowsingContextEvent.ContextCreated,
                BrowsingContextEvent.ContextDestroyed,
            ],
            async e =>
            {
                switch (e)
                {
                    case ContextCreatedEventArgs created:
                        if (created.Parent is null && !_contextMap.ContainsKey(created.Context))
                        {
                            var vm = new ContextViewModel(created.Context);
                            _contextMap[created.Context] = vm;
                            await Dispatcher.UIThread.InvokeAsync(() => Contexts.Add(vm));
                            await vm.InitializeAsync();
                        }
                        break;

                    case ContextDestroyedEventArgs destroyed:
                        if (_contextMap.Remove(destroyed.Context, out var destroyedVm))
                        {
                            await destroyedVm.DisposeAsync();
                            await Dispatcher.UIThread.InvokeAsync(() => Contexts.Remove(destroyedVm));
                            ContextDestroyed?.Invoke(destroyedVm);
                        }
                        break;

                    case BeforeRequestSentEventArgs req:
                        if (req.Context is not null && _contextMap.TryGetValue(req.Context, out var reqVm))
                            await reqVm.NetworkViewModel.AddRequestAsync(req, _networkDataCollector!);
                        break;

                    case ResponseCompletedEventArgs res:
                        if (res.Context is not null && _contextMap.TryGetValue(res.Context, out var resVm))
                            await resVm.NetworkViewModel.UpdateResponseAsync(res);
                        break;
                }
            });
    }

    [ObservableProperty]
    private int _emulationThreads = 10;

    private bool CanStartEmulation() => !IsBusy && Contexts.Count > 0;

    [ObservableProperty]
    private int _emulationDurationSeconds;

    [RelayCommand(CanExecute = nameof(CanStartEmulation))]
    public async Task StartEmulationAsync()
    {
        var sw = Stopwatch.StartNew();

        List<Task> tasks = [];

        for (int i = 0; i < EmulationThreads; i++)
        {
            tasks.Add(EmulationScenarioAsync());
        }

        await Task.WhenAll(tasks);

        EmulationDurationSeconds = (int)sw.Elapsed.TotalSeconds;
    }

    private async Task EmulationScenarioAsync()
    {
        UserContext? userContext = null;
        BrowsingContext context;

        if (IsIsolated)
        {
            userContext = (await _bidi!.Browser.CreateUserContextAsync()).UserContext;

            context = (await _bidi!.BrowsingContext.CreateAsync(ContextType.Window, new() { UserContext = userContext })).Context;
        }
        else
        {
            context = (await _bidi!.BrowsingContext.CreateAsync(ContextType.Window)).Context;
        }

        if (_webDriver is OpenQA.Selenium.Chrome.ChromeDriver || _webDriver is OpenQA.Selenium.Edge.EdgeDriver)
        {
            // Workaround to perform actions fast
            var cdp = await context.AsCdpAsync();
#pragma warning disable BIDICDP001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            await cdp.Emulation.SetFocusEmulationEnabledAsync(true);
#pragma warning restore BIDICDP001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        }

        await context.NavigateAsync("https://nuget.org", new()
        {
            Wait = ReadinessState.Complete,
        }).WaitAsync(TimeSpan.FromSeconds(90));

        var inputNode = (await context.LocateNodesAsync(new CssLocator("[name='q']"))).Nodes[0];

        await context.Script.CallFunctionAsync("el => el.focus()", true, new() { Arguments = [new SharedReferenceLocalValue(inputNode.SharedId)] });

        await context.Input.PerformActionsAsync([
            new KeySourceActions(
                "keyboard",
                [.. "Selenium".SelectMany<char, IKeySourceAction>(c => [new KeyDownAction(c), new KeyUpAction(c)])])
        ]);

        var searchButton = (await context.LocateNodesAsync(new CssLocator("button.btn-search"))).Nodes[0];

        await using var onLoadReader = await context.Load.StreamAsync();

        await context.Input.PerformActionsAsync([
            new PointerSourceActions("pointer2",
            [
                new PointerMoveAction(10, 10)
                {
                    Origin = new ElementOrigin(searchButton)
                },
                new PauseAction(){
                    Duration = 300
                },
                new PointerDownAction(0),
                new PauseAction(){
                    Duration = 20
                },
                new PointerUpAction(0)
            ])
        ]);

        await onLoadReader.ReadAllAsync().FirstAsync(e => e.Url.Contains("q=Selenium", StringComparison.OrdinalIgnoreCase)).AsTask().WaitAsync(TimeSpan.FromSeconds(60));

        var packages = await context.LocateNodesAsync(new CssLocator("a.package-title"));

        foreach (var package in packages.Nodes)
        {
            var title = await context.Script.CallFunctionAsync<string>("el => el.textContent", true, new() { Arguments = [new SharedReferenceLocalValue(package.SharedId)] });

            if (title?.Contains("Selenium", StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new Exception($"Unexpected package title: {title}");
            }
        }

        await context.CloseAsync();
        
        if (userContext is not null)
        {
            await _bidi.Browser.RemoveUserContextAsync(userContext);
        }
    }
}
