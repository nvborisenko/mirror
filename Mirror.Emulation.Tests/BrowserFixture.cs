using System.Collections.Concurrent;
using OpenQA.Selenium;
using OpenQA.Selenium.BiDi;
using OpenQA.Selenium.BiDi.BrowsingContext;
using OpenQA.Selenium.BiDi.Network;
using TUnit.Core.Interfaces;

namespace Mirror.Emulation.Tests;

/// <summary>
/// One browser per test session (like the Mirror app), listening to network traffic of all contexts.
/// Configured via env vars: EMULATION_BROWSER (chrome|edge|firefox), EMULATION_HEADLESS (default true).
/// </summary>
public sealed class BrowserFixture : IAsyncInitializer, IAsyncDisposable
{
    private IWebDriver? _webDriver;
    private Collector? _collector;
    private ISubscription? _subscription;

    private readonly ConcurrentDictionary<BrowsingContext, NetworkStats> _stats = new();

    public IBiDi BiDi { get; private set; } = null!;

    public string Browser { get; } = (Environment.GetEnvironmentVariable("EMULATION_BROWSER") ?? "chrome").ToLowerInvariant();

    public bool IsChromium => Browser is "chrome" or "edge";

    public string BrowserVersion { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var headless = !string.Equals(Environment.GetEnvironmentVariable("EMULATION_HEADLESS"), "false", StringComparison.OrdinalIgnoreCase);

        _webDriver = Browser switch
        {
            "chrome" => new OpenQA.Selenium.Chrome.ChromeDriver(ChromiumOptions(new OpenQA.Selenium.Chrome.ChromeOptions(), headless)),
            "edge" => new OpenQA.Selenium.Edge.EdgeDriver(ChromiumOptions(new OpenQA.Selenium.Edge.EdgeOptions(), headless)),
            "firefox" => new OpenQA.Selenium.Firefox.FirefoxDriver(FirefoxOptions(headless)),
            _ => throw new NotSupportedException($"Browser '{Browser}' is not supported"),
        };

        BrowserVersion = ((IHasCapabilities)_webDriver).Capabilities.GetCapability("browserVersion")?.ToString() ?? "Unknown";

        BiDi = await _webDriver.AsBiDiAsync() ?? throw new InvalidOperationException("Failed to initialize BiDi connection");

        _collector = (await BiDi.Network.AddDataCollectorAsync([DataType.Request, DataType.Response], 300_000)).Collector;

        _subscription = await BiDi.SubscribeAsync<OpenQA.Selenium.BiDi.EventArgs>(
            [NetworkEvent.BeforeRequestSent, NetworkEvent.ResponseCompleted],
            e =>
            {
                switch (e)
                {
                    case BeforeRequestSentEventArgs req when req.Context is not null && _stats.TryGetValue(req.Context, out var s):
                        Interlocked.Increment(ref s.Requests);
                        break;
                    case ResponseCompletedEventArgs res when res.Context is not null && _stats.TryGetValue(res.Context, out var s):
                        Interlocked.Increment(ref s.Responses);
                        break;
                }

                return Task.CompletedTask;
            });
    }

    public NetworkStats Track(BrowsingContext context) => _stats.GetOrAdd(context, _ => new NetworkStats());

    public void Untrack(BrowsingContext context) => _stats.TryRemove(context, out _);

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null) await _subscription.DisposeAsync();
        if (_collector is not null) await BiDi.Network.RemoveDataCollectorAsync(_collector);
        if (BiDi is not null) await BiDi.DisposeAsync();
        _webDriver?.Dispose();
    }

    private static T ChromiumOptions<T>(T options, bool headless) where T : OpenQA.Selenium.Chromium.ChromiumOptions
    {
        options.UseWebSocketUrl = true;
        if (headless) options.AddArgument("--headless=new");
        return options;
    }

    private static OpenQA.Selenium.Firefox.FirefoxOptions FirefoxOptions(bool headless)
    {
        var options = new OpenQA.Selenium.Firefox.FirefoxOptions { UseWebSocketUrl = true };
        if (headless) options.AddArgument("--headless");
        return options;
    }
}

public sealed class NetworkStats
{
    public int Requests;
    public int Responses;
}
