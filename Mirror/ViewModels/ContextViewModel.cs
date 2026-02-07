using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenQA.Selenium.BiDi.BrowsingContext;
using System;
using System.IO;
using System.IO.Hashing;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using System.Threading;

namespace Mirror.ViewModels;

public partial class ContextViewModel : ViewModelBase, IAsyncDisposable
{
    public ContextViewModel(BrowsingContext context)
    {
        Context = context;
        NetworkViewModel = new(context);

        _cancellationTokenSource = new CancellationTokenSource();
    }

    private readonly CancellationTokenSource _cancellationTokenSource;

    const string DefaultTitle = "New Tab";

    private Task _screenshotTask;
    private ulong _previousScreenshotHash;

    [ObservableProperty]
    private double _screenshotQuality = 0.6;

    [ObservableProperty]
    private Bitmap _screenshot;

    [ObservableProperty]
    private string _title = DefaultTitle;

    public async Task InitializeAsync()
    {
        await Context.OnLoadAsync(async e =>
        {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Title = await e.Context.Script.EvaluateAsync<string>("document.title", true) ?? DefaultTitle;
                }
        });

        _screenshotTask = Task.Run(async () =>
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var screenshot = await Context.CaptureScreenshotAsync(new()
                    {
                        Origin = ScreenshotOrigin.Viewport,
                        Format = new ImageFormat("image/jpeg") { Quality = ScreenshotQuality }
                    });

                    var data = screenshot.Data;

                    // Fast hash-based comparison
                    var currentHash = XxHash64.HashToUInt64(data.Span);
                    if (currentHash != _previousScreenshotHash)
                    {
                        _previousScreenshotHash = currentHash;
                        Bitmap bitmap;

                        MemoryMarshal.TryGetArray(data, out var segment);

                        using var ms = new MemoryStream(segment.Array!, segment.Offset, segment.Count, false);
                        bitmap = new Bitmap(ms);

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var oldScreenshot = Screenshot;
                            Screenshot = bitmap;
                            oldScreenshot?.Dispose();
                        }, DispatcherPriority.Background);
                    }
                }
                catch (Exception e)
                {
                    // Ignore exceptions (e.g., context closed)
                }

                await Task.Delay(300, _cancellationTokenSource.Token);
            }
        });

        await NetworkViewModel.InitializeAsync();
    }

    public BrowsingContext Context { get; }

    [RelayCommand]
    public async Task Navigate(string url)
    {
        await Context.NavigateAsync(url);
    }

    [RelayCommand]
    private async Task CloseContext()
    {
        await Context.CloseAsync();
    }

    public NetworkViewModel NetworkViewModel { get; }

    public async ValueTask DisposeAsync()
    {
        await _cancellationTokenSource.CancelAsync();

        _cancellationTokenSource.Dispose();
    }
}
