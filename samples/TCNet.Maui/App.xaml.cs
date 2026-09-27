using Microsoft.Extensions.DependencyInjection;
using TCNet.Maui.Services;

namespace TCNet.Maui;

public partial class App : Application
{
    private readonly TCNetService _service;
    private readonly IServiceProvider _services;
    private int _openWindows;

    public App(TCNetService service, IServiceProvider services)
    {
        InitializeComponent();
        _service = service;
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // A fresh shell per window: a page can only have one parent, and Android recreates the window on activity restart.
        var window = new Window(_services.GetRequiredService<AppShell>()) { Title = "TCNet Monitor" };
        // One node serves every window: start it with the first window, stop it when the last one closes.
        window.Created += async (_, _) =>
        {
            if (Interlocked.Increment(ref _openWindows) != 1) return;
            try { await _service.StartAsync(); }
            catch (Exception ex) { _service.ReportError("Start", ex); }
        };
        window.Destroying += async (_, _) =>
        {
            if (Interlocked.Decrement(ref _openWindows) != 0) return;
            try { await _service.StopAsync(); }
            catch (Exception ex) { _service.ReportError("Stop", ex); }
        };
        return window;
    }
}
