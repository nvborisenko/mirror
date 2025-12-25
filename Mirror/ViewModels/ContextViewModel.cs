using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenQA.Selenium.BiDi.BrowsingContext;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Mirror.ViewModels;

public partial class ContextViewModel(BrowsingContext context) : ViewModelBase
{
    const string DefaultTitle = "New Tab";

    private Task _screenshotTask;
    private ReadOnlyMemory<byte> _previousScreenshotData;

    [ObservableProperty]
    private Bitmap _screenshot;

    [ObservableProperty]
    private string _title = DefaultTitle;

    public async Task InitializeAsync()
    {
        //await Context.OnLoadAsync(async e =>
        //{
        //    if (e.Context.Equals(Context))
        //    {
        //        //Title = await e.Context.Script.EvaluateAsync<string>("document.title", true) ?? DefaultTitle;
        //    }
        //});

        _screenshotTask = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var screenshot = await Context.CaptureScreenshotAsync(new() { Origin = ScreenshotOrigin.Viewport });

                    var data = screenshot.Data;

                    // Compare with previous screenshot data
                    if (!data.Span.SequenceEqual(_previousScreenshotData.Span))
                    {
                        _previousScreenshotData = data;
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

                await Task.Delay(50);
            }
        });

        await NetworkViewModel.InitializeAsync();
    }

    public BrowsingContext Context { get; } = context;

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

    public NetworkViewModel NetworkViewModel { get; } = new(context);
}
