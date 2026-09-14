using Microsoft.UI.Xaml.Controls;
using LogCollector.ViewModels;

namespace LogCollector;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await ViewModel.LoadLogsCommand.ExecuteAsync(null);
    }
}
