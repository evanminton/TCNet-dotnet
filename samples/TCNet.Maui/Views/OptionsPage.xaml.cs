using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class OptionsPage : ContentPage
{
    public OptionsPage(OptionsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
