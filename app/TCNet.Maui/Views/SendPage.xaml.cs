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
        Appearing += (_, _) => _vm.TargetsCommand.Execute(null);
    }

    private void OnTargetsFocused(object? sender, FocusEventArgs e) => _vm.TargetsCommand.Execute(null);
}
