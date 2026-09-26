using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        RolePicker.ItemDisplayBinding = new Binding("Description");
        BindingContext = vm;
    }
}
