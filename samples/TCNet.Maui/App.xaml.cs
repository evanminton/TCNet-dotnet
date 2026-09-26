using TCNet.Maui.Services;

namespace TCNet.Maui;

public partial class App : Application
{
    private readonly TCNetService _service;
    private readonly AppShell _shell;

    public App(TCNetService service, AppShell shell)
    {
        InitializeComponent();
        _service = service;
        _shell = shell;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell) { Title = "TCNet Monitor" };
        window.Created += async (_, _) => await _service.StartAsync();
        window.Destroying += async (_, _) => await _service.StopAsync();
        return window;
    }
}
