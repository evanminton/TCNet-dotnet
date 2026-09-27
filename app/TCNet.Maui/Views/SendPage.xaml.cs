using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class SendPage : ContentPage
{
    private readonly SendViewModel _vm;

    public SendPage(SendViewModel vm)
    {
        InitializeComponent();
        TypePicker.ItemDisplayBinding = new Binding("Name");
        BindingContext = _vm = vm;
        // Refreshed in place (the selection and the typed target survive); not on focus, which would change the list under an open picker.
        Appearing += (_, _) => _vm.TargetsCommand.Execute(null);
    }
}
