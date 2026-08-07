using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenQA.Selenium.BiDi;
using OpenQA.Selenium.BiDi.BrowsingContext;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using System.Threading.Channels;

namespace Mirror.ViewModels;

public partial class ContextViewModel : ViewModelBase, IAsyncDisposable
{
    public ContextViewModel(BrowsingContext context)
    {
        Context = context;
        NetworkViewModel = new(context);
    }

    private CancellationTokenSource? _screenshotCts;
    private Task? _screenshotTask;
    private readonly Channel<byte> _captureChannel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite
    });

    const string DefaultTitle = "New Tab";

    [ObservableProperty]
    private double _screenshotQuality = 0.6;

    [ObservableProperty]
    private Bitmap _screenshot = null!;

    [ObservableProperty]
    private string _title = DefaultTitle;

    private ISubscription? _onLoadSubscription;

    public Task InitializeAsync() => Task.CompletedTask;

    public void RequestCapture() => _captureChannel.Writer.TryWrite(0);

    public void StartScreenCapture()
    {
        if (_screenshotCts is not null) return;

        _screenshotCts = new CancellationTokenSource();
        _screenshotTask = Task.Run(() => ScreenCaptureLoopAsync(_screenshotCts.Token));
    }

    public async Task StopScreenCaptureAsync()
    {
        if (_screenshotCts is null) return;

        await _screenshotCts.CancelAsync();
        _screenshotCts.Dispose();
        _screenshotCts = null;

        if (_screenshotTask is not null)
        {
            try { await _screenshotTask.ConfigureAwait(false); } catch { }
            _screenshotTask = null;
        }
    }

    private async Task ScreenCaptureLoopAsync(CancellationToken cancellationToken)
    {
        // Capture the initial state
        await CaptureAndUpdateAsync();

        while (await _captureChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            // Drain all pending signals
            while (_captureChannel.Reader.TryRead(out _)) { }

            await CaptureAndUpdateAsync();
        }
    }

    private async Task CaptureAndUpdateAsync()
    {
        try
        {
            var screenshot = await Context.CaptureScreenshotAsync(new()
            {
                Origin = ScreenshotOrigin.Viewport,
                Format = new ImageFormat("image/jpeg") { Quality = ScreenshotQuality }
            });

            var data = screenshot.Data;

            MemoryMarshal.TryGetArray(data, out var segment);
            using var ms = new MemoryStream(segment.Array!, segment.Offset, segment.Count, false);
            var bitmap = new Bitmap(ms);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var oldScreenshot = Screenshot;
                Screenshot = bitmap;
                oldScreenshot?.Dispose();
            }, DispatcherPriority.Background);
        }
        catch (Exception) when (!_screenshotCts?.IsCancellationRequested ?? true)
        {
            // Ignore exceptions (e.g., context closed)
        }
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
        await StopScreenCaptureAsync();

        if (_onLoadSubscription is not null)
        {
            await _onLoadSubscription.DisposeAsync();
        }

        await NetworkViewModel.DisposeAsync();
    }
}
