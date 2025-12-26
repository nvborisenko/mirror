using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mirror.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        Browsers =
        [
            new BrowserViewModel(this, typeof(OpenQA.Selenium.Chrome.ChromeDriver), "avares://Mirror/Assets/Chrome-Logo.png"),
            new BrowserViewModel(this, typeof(OpenQA.Selenium.Edge.EdgeDriver), "avares://Mirror/Assets/Edge-Logo.png"),
            new BrowserViewModel(this, typeof(OpenQA.Selenium.Firefox.FirefoxDriver), "avares://Mirror/Assets/Firefox-Logo.png")
        ];

        DisplayContent = this;
    }

    public ObservableCollection<BrowserViewModel> Browsers { get; }

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private BrowserViewModel _currentBrowser;

    [ObservableProperty]
    private object _displayContent;

    partial void OnCurrentViewChanged(ViewModelBase? value)
    {
        DisplayContent = value ?? (object)this;
    }

    [RelayCommand]
    private void NavigateToContext(ContextViewModel context)
    {
        CurrentView = context;
    }

    [RelayCommand]
    private void NavigateBack()
    {
        CurrentView = null;
    }
}
