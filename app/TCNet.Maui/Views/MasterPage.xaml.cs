using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class MasterPage : ContentPage
{
    public MasterPage(MasterViewModel vm)
    {
        InitializeComponent();
        Polling.Attach(this, vm);
    }
}
