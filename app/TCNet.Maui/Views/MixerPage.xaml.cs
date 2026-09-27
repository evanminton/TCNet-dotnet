using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class MixerPage : ContentPage
{
    public MixerPage(MixerViewModel vm)
    {
        InitializeComponent();
        Polling.Attach(this, vm);
    }
}
