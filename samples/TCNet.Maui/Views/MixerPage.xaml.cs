using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class MixerPage : ContentPage
{
    private readonly MixerViewModel _vm;

    public MixerPage(MixerViewModel vm)
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
