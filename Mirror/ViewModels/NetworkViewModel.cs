using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQA.Selenium.BiDi;
using OpenQA.Selenium.BiDi.BrowsingContext;
using OpenQA.Selenium.BiDi.Network;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Mirror.ViewModels;

public partial class NetworkViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly BrowsingContext _context;

    private const int SampleCount = 60;
    private readonly int[] _rawSamples = new int[SampleCount];
    private int _writeIndex = 0;
    private int _pendingCount = 0;

    // Shared single timer for all instances
    private static DispatcherTimer? s_sharedTimer;
    private static readonly List<WeakReference<NetworkViewModel>> s_instances = [];

    private static void EnsureSharedTimer()
    {
        if (s_sharedTimer is not null) return;

        Dispatcher.UIThread.VerifyAccess();
        s_sharedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        s_sharedTimer.Tick += static (_, _) =>
        {
            lock (s_instances)
            {
                for (int i = s_instances.Count - 1; i >= 0; i--)
                {
                    if (s_instances[i].TryGetTarget(out var vm))
                        vm.OnSampleTimerTick();
                    else
                        s_instances.RemoveAt(i);
                }
            }
        };
        s_sharedTimer.Start();
    }

    [ObservableProperty]
    private double[] _activitySamples = new double[SampleCount];

    public NetworkViewModel(BrowsingContext context)
    {
        _context = context;
        Dispatcher.UIThread.Post(() =>
        {
            EnsureSharedTimer();
            lock (s_instances)
                s_instances.Add(new WeakReference<NetworkViewModel>(this));
        });
    }

    private bool _hasActivity;

    private void OnSampleTimerTick()
    {
        var count = _pendingCount;
        _pendingCount = 0;

        _rawSamples[_writeIndex] = count;
        _writeIndex = (_writeIndex + 1) % SampleCount;

        if (count > 0)
            _hasActivity = true;

        // Skip re-render only when entire buffer is zero
        if (!_hasActivity)
            return;

        var result = new double[SampleCount];
        bool anyNonZero = false;
        for (int i = 0; i < SampleCount; i++)
        {
            result[i] = _rawSamples[(_writeIndex + i) % SampleCount];
            if (result[i] > 0) anyNonZero = true;
        }

        _hasActivity = anyNonZero;
        ActivitySamples = result;
    }

    public ObservableCollection<NetworkRequestViewModel> Requests { get; } = [];
    private readonly Dictionary<Request, NetworkRequestViewModel> _requestMap = [];

    public async Task AddRequestAsync(BeforeRequestSentEventArgs e, Collector collector)
    {
        var requestViewModel = new NetworkRequestViewModel(e, collector);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _pendingCount++;
            _requestMap[requestViewModel.Request] = requestViewModel;
            Requests.Add(requestViewModel);
        });
    }

    public async Task UpdateResponseAsync(ResponseCompletedEventArgs e)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_requestMap.TryGetValue(e.Request.Request, out var requestViewModel))
            {
                requestViewModel.Status = e.Response.Status.ToString();
                requestViewModel.Duration = TimeSpan.FromMilliseconds(e.Request.Timings.ResponseEnd - e.Request.Timings.RequestStart);
                requestViewModel.ResponseHeaders = [.. e.Response.Headers.Select(h => new HeaderModel(h.Name, (string)h.Value))];
            }
        });
    }

    [RelayCommand]
    public void ClearRequests()
    {
        _requestMap.Clear();
        Requests.Clear();
    }

    [RelayCommand]
    public async Task ChangeCache(bool disabled)
    {
        await _context.Network.SetCacheBehaviorAsync(disabled ? OpenQA.Selenium.BiDi.Network.CacheBehavior.Bypass : OpenQA.Selenium.BiDi.Network.CacheBehavior.Default);
    }

    public ValueTask DisposeAsync()
    {
        lock (s_instances)
        {
            for (int i = s_instances.Count - 1; i >= 0; i--)
            {
                if (!s_instances[i].TryGetTarget(out var vm) || vm == this)
                    s_instances.RemoveAt(i);
            }
        }
        _requestMap.Clear();
        Requests.Clear();
        return ValueTask.CompletedTask;
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