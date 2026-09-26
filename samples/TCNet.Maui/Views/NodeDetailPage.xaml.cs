using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class NodeDetailPage : ContentPage
{
    private readonly NodeDetailViewModel _vm;

    public NodeDetailPage(NodeDetailViewModel vm)
    {
        InitializeComponent();
        DataTypePicker.ItemDisplayBinding = new Binding("Description");
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Stop();
    }
}
