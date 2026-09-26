using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class SendPage : ContentPage
{
    public SendPage(SendViewModel vm)
    {
        InitializeComponent();
        TypePicker.ItemDisplayBinding = new Binding("Name");
        BindingContext = vm;
    }
}
