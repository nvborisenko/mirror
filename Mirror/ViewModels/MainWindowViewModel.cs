using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mirror.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        Browsers =
        [
            new BrowserViewModel(typeof(OpenQA.Selenium.Chrome.ChromeDriver), "avares://Mirror/Assets/Chrome-Logo.png"),
            new BrowserViewModel(typeof(OpenQA.Selenium.Edge.EdgeDriver), "avares://Mirror/Assets/Edge-Logo.png"),
            new BrowserViewModel(typeof(OpenQA.Selenium.Firefox.FirefoxDriver), "avares://Mirror/Assets/Firefox-Logo.png")
        ];
    }

    public ObservableCollection<BrowserViewModel> Browsers { get; }

    [ObservableProperty]
    private BrowserViewModel _currentBrowser = null!;
}
