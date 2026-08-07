using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Mirror.Views;

public partial class ExceptionDialog : Window
{
    private readonly string _fullText;

    public ExceptionDialog(Exception exception)
    {
        InitializeComponent();

        _fullText = exception.ToString();

        MessageText.Text = $"{exception.GetType().FullName}: {exception.Message}";
        StackTraceText.Text = _fullText;
    }

    private async void CopyButton_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(_fullText);
            CopyButton.Content = "Copied!";
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
