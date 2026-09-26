using TCNet.Maui.Services;
using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class NodesPage : ContentPage
{
    public NodesPage(NodesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not NodeItem item) return;
        List.SelectedItem = null;
        await Shell.Current.GoToAsync("node", new Dictionary<string, object> { ["key"] = item.Key });
    }
}
