using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class NodePage : ContentPage
{
    public NodePage(NodeViewModel vm)
    {
        InitializeComponent();
        DataPicker.ItemDisplayBinding = new Binding("Description");
        Polling.Attach(this, vm);
    }
}
