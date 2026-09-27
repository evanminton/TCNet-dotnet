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
        if (e.CurrentSelection.FirstOrDefault() is not NodeRow row) return;
        List.SelectedItem = null;
        // This page's own shell: with several windows Shell.Current may belong to another window (or be null).
        if ((Window?.Page as Shell ?? Shell.Current) is not { } shell) return;
        await shell.GoToAsync("node", new Dictionary<string, object> { ["key"] = row.Key });
    }
}
