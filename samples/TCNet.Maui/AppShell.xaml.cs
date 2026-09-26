using TCNet.Maui.Views;

namespace TCNet.Maui;

public partial class AppShell : Shell
{
    // Routes are global; register once even though a new shell is created per window.
    static AppShell() => Routing.RegisterRoute("node", typeof(NodeDetailPage));

    public AppShell()
    {
        InitializeComponent();
    }
}
