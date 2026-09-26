using TCNet.Maui.Views;

namespace TCNet.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("node", typeof(NodeDetailPage));
    }
}
