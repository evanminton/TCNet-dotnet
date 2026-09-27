using TCNet.Maui.Services;
using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class PacketsPage : ContentPage
{
    public PacketsPage(PacketsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PacketRow row) return;
        List.SelectedItem = null;
        await Navigation.PushAsync(new PacketPage(row));
    }
}
