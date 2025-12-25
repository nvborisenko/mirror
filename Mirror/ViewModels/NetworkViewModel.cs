using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQA.Selenium.BiDi.BrowsingContext;
using System;
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



    [ObservableProperty]
    private string _status = "Pending";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationDisplay))]
    private TimeSpan? _duration;

    public string? DurationDisplay => Duration is not null ? $"{(int)Duration.Value.TotalMilliseconds} ms" : null;
}