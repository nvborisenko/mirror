using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQA.Selenium.BiDi;
using OpenQA.Selenium.BiDi.BrowsingContext;
using OpenQA.Selenium.BiDi.Network;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Mirror.ViewModels;

public partial class NetworkViewModel(BrowsingContext context) : ViewModelBase, IAsyncDisposable
{
    private Collector? _networkDataCollector = null!;

    private Subscription? _onBeforeRequestSubscription;
    private Subscription? _onResponseCompletedSubscription;
    public async Task InitializeAsync()
    {
        var dataCollectorResult = await context.BiDi.Network.AddDataCollectorAsync([DataType.Request, DataType.Response], 300_000, new() { Contexts = [context] });
        _networkDataCollector = dataCollectorResult.Collector;

        _onBeforeRequestSubscription = await context.Network.OnBeforeRequestSentAsync(async e =>
        {
            var requestViewModel = new NetworkRequestViewModel(e, _networkDataCollector);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Requests.Add(requestViewModel);
            });
        });

        _onResponseCompletedSubscription = await context.Network.OnResponseCompletedAsync(async e =>
        {
            var requestViewModel = Requests.FirstOrDefault(r => r.Request.Id == e.Request.Request.Id);

            if (requestViewModel != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    requestViewModel.Status = e.Response.Status.ToString();
                    requestViewModel.Duration = TimeSpan.FromMilliseconds(e.Request.Timings.ResponseEnd - e.Request.Timings.RequestStart);
                    requestViewModel.ResponseHeaders = [.. e.Response.Headers.Select(h => new HeaderModel(h.Name, (string)h.Value))];
                });
            }
        });
    }

    public ObservableCollection<NetworkRequestViewModel> Requests { get; } = [];

    [RelayCommand]
    public void ClearRequests()
    {
        Requests.Clear();
    }

    [RelayCommand]
    public async Task ChangeCache(bool disabled)
    {
        await context.Network.SetCacheBehaviorAsync(disabled ? OpenQA.Selenium.BiDi.Network.CacheBehavior.Bypass : OpenQA.Selenium.BiDi.Network.CacheBehavior.Default);
    }

    public async ValueTask DisposeAsync()
    {
        if (_onBeforeRequestSubscription is not null)
        {
            await _onBeforeRequestSubscription.DisposeAsync();
        }

        if (_onResponseCompletedSubscription is not null)
        {
            await _onResponseCompletedSubscription.DisposeAsync();
        }

        if (_networkDataCollector is not null)
        {
            await _networkDataCollector.BiDi.Network.RemoveDataCollectorAsync(_networkDataCollector);
        }
    }
}

public partial class NetworkRequestViewModel(BeforeRequestSentEventArgs requestData, Collector collector) : ViewModelBase
{
    public Request Request => requestData.Request.Request;

    public string Method => requestData.Request.Method;

    public string Url => requestData.Request.Url;

    public string UrlDisplay => new Uri(Url).PathAndQuery;

    public string Initiator => requestData.Initiator.Type.ToString();

    public IReadOnlyList<HeaderModel> RequestHeaders => [.. requestData.Request.Headers.Select(req => new HeaderModel(req.Name, (string)req.Value))];

    [ObservableProperty]
    private bool _isExpanded = false;

    [ObservableProperty]
    private string _status = "Pending";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationDisplay))]
    private TimeSpan? _duration;

    public string? DurationDisplay => Duration is not null ? $"{(int)Duration.Value.TotalMilliseconds} ms" : null;

    [ObservableProperty]
    private IReadOnlyList<HeaderModel>? _responseHeaders;

    [ObservableProperty]
    private BytesValue? _requestBody;

    [ObservableProperty]
    private bool _isPreviewSelected;

    partial void OnIsPreviewSelectedChanged(bool value)
    {
        if (value)
        {
            _ = LoadRequestBodyAsync();
        }
    }

    [RelayCommand]
    public async Task LoadRequestBodyAsync()
    {
        try
        {
            RequestBody = await requestData.BiDi.Network.GetDataAsync(DataType.Request, requestData.Request.Request, new() { Collector = collector });
        }
        catch (Exception ex)
        {
            RequestBody = ex.Message;
        }
    }

    [ObservableProperty]
    private BytesValue? _responseBody;

    [ObservableProperty]
    private bool _isResponseSelected;

    partial void OnIsResponseSelectedChanged(bool value)
    {
        if (value)
        {
            _ = LoadResponseBodyAsync();
        }
    }

    [RelayCommand]
    public async Task LoadResponseBodyAsync()
    {
        try
        {
            ResponseBody = await requestData.BiDi.Network.GetDataAsync(DataType.Response, requestData.Request.Request, new() { Collector = collector });
        }
        catch (Exception ex)
        {
            ResponseBody = ex.Message;
        }
    }
}

public class HeaderModel(string name, string value)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
}