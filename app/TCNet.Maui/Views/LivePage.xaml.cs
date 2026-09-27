using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class LivePage : ContentPage
{
    public LivePage(LiveViewModel vm)
    {
        InitializeComponent();
        Polling.Attach(this, vm);
    }
}
