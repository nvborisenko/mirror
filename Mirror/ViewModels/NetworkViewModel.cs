using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQA.Selenium.BiDi.BrowsingContext;
using OpenQA.Selenium.BiDi.Network;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Mirror.ViewModels;

public partial class NetworkViewModel(BrowsingContext context) : ViewModelBase
{
    public async Task InitializeAsync()
    {
        await context.Network.OnBeforeRequestSentAsync(async e =>
        {
            var requestViewModel = new NetworkRequestViewModel(e);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Requests.Add(requestViewModel);
            });
        });

        await context.Network.OnResponseCompletedAsync(async e =>
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
}

public partial class NetworkRequestViewModel(OpenQA.Selenium.BiDi.Network.BeforeRequestSentEventArgs requestData) : ViewModelBase
{
    public OpenQA.Selenium.BiDi.Network.Request Request => requestData.Request.Request;

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
}

public class HeaderModel(string name, string value)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
}