using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class LivePage : ContentPage
{
    private readonly LiveViewModel _vm;

    public LivePage(LiveViewModel vm)
    {
        InitializeComponent();
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
