using System.Diagnostics;
using OpenQA.Selenium.BiDi.Browser;
using OpenQA.Selenium.BiDi.BrowsingContext;
using OpenQA.Selenium.BiDi.Input;
using OpenQA.Selenium.BiDi.Script;
using Selenium.WebDriver.BiDi.Cdp;

namespace Mirror.Emulation.Tests;

/// <summary>
/// Same scenario as BrowserViewModel.StartEmulationAsync: each test starts N scenarios concurrently in a shared
/// browser, every scenario in its own isolated browsing context (separate user context), searching nuget.org.
/// Tests run one after another so each concurrency level is measured without interference.
/// </summary>
[NotInParallel]
public class EmulationTests
{
    private static readonly ActivitySource Source = new("Mirror.Emulation");

    [ClassDataSource<BrowserFixture>(Shared = SharedType.PerTestSession)]
    public required BrowserFixture Browser { get; init; }

    [Test]
    [Timeout(600_000)]
    [Arguments(1)]
    [Arguments(10)]
    [Arguments(20)]
    [Arguments(30)]
    [Arguments(40)]
    [Arguments(50)]
    public async Task SearchNuGet(int parallel, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(1, parallel).Select(i => ScenarioAsync(i, cancellationToken)).ToArray();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Inspect all scenarios below, not only the first failure.
        }

        var failed = tasks.Where(t => !t.IsCompletedSuccessfully).ToArray();
        var stats = tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToArray();
        var requests = stats.Sum(s => s.Requests);
        var responses = stats.Sum(s => s.Responses);

        Console.WriteLine($"[{Browser.Browser} {Browser.BrowserVersion}] parallel={parallel}: duration={sw.Elapsed.TotalSeconds:F1}s, " +
            $"succeeded={stats.Length}, failed={failed.Length}, requests={requests}, responses={responses}");

        if (failed.Length > 0)
        {
            throw new AggregateException($"{failed.Length} of {parallel} scenarios failed", failed.SelectMany(t => t.Exception?.InnerExceptions ?? [new TaskCanceledException()]));
        }
    }

    private async Task<NetworkStats> ScenarioAsync(int scenario, CancellationToken cancellationToken)
    {
        await Task.Yield();

        using var _ = Source.StartActivity($"scenario {scenario}");

        var bidi = Browser.BiDi;
        UserContext userContext;
        BrowsingContext context;

        using (Source.StartActivity("create context"))
        {
            userContext = (await bidi.Browser.CreateUserContextAsync()).UserContext;
            context = (await bidi.BrowsingContext.CreateAsync(ContextType.Tab, new() { UserContext = userContext })).Context;
        }

        var stats = Browser.Track(context);

        try
        {
            if (Browser.IsChromium)
            {
                // Workaround to perform actions fast in background tabs
                var cdp = await context.AsCdpAsync();
#pragma warning disable BIDICDP001
                await cdp.Emulation.SetFocusEmulationEnabledAsync(true);
#pragma warning restore BIDICDP001
            }

            using (Source.StartActivity("navigate"))
            {
                await context.NavigateAsync("https://nuget.org", new() { Wait = ReadinessState.Complete })
                    .WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
            }

            using (Source.StartActivity("type query"))
            {
                var inputNode = (await context.LocateNodesAsync(new CssLocator("[name='q']"))).Nodes[0];

                await context.Script.CallFunctionAsync("el => el.focus()", true, new() { Arguments = [new SharedReferenceLocalValue(inputNode.SharedId)] });

                await context.Input.PerformActionsAsync([
                    new KeySourceActions(
                        "keyboard",
                        [.. "Mirror".SelectMany<char, IKeySourceAction>(c => [new KeyDownAction(c), new KeyUpAction(c)])])
                ]);
            }

            using (Source.StartActivity("search"))
            {
                var searchButton = (await context.LocateNodesAsync(new CssLocator("button.btn-search"))).Nodes[0];

                await using var onLoadReader = await context.Load.StreamAsync();

                await context.Input.PerformActionsAsync([
                    new PointerSourceActions("pointer2",
                    [
                        new PointerMoveAction(10, 10) { Origin = new ElementOrigin(searchButton) },
                        new PauseAction { Duration = 300 },
                        new PointerDownAction(0),
                        new PauseAction { Duration = 20 },
                        new PointerUpAction(0)
                    ])
                ]);

                await onLoadReader.ReadAllAsync()
                    .FirstAsync(e => e.Url.Contains("q=Mirror", StringComparison.OrdinalIgnoreCase), cancellationToken)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }

            using (Source.StartActivity("verify results"))
            {
                var packages = await context.LocateNodesAsync(new CssLocator("a.package-title"));

                await Assert.That(packages.Nodes.Count).IsGreaterThan(0);

                foreach (var package in packages.Nodes)
                {
                    var title = await context.Script.CallFunctionAsync<string>("el => el.textContent", true, new() { Arguments = [new SharedReferenceLocalValue(package.SharedId)] });

                    await Assert.That(title).Contains("Mirror", StringComparison.OrdinalIgnoreCase);
                }
            }

            await Assert.That(stats.Requests).IsGreaterThan(0);

            return stats;
        }
        finally
        {
            Browser.Untrack(context);

            await context.CloseAsync();

            await bidi.Browser.RemoveUserContextAsync(userContext);
        }
    }
}
