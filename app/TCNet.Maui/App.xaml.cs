using Microsoft.Extensions.DependencyInjection;
using TCNet.Maui.Services;

namespace TCNet.Maui;

public partial class App : Application
{
    private readonly AppState _state;
    private readonly IServiceProvider _services;
    private int _windows;

    public App(AppState state, IServiceProvider services)
    {
        InitializeComponent();
        _state = state;
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Each window gets its own shell; one node serves all windows (started with the first, stopped with the last).
        var window = new Window(_services.GetRequiredService<AppShell>()) { Title = "TCNet" };
        window.Created += async (_, _) =>
        {
            if (Interlocked.Increment(ref _windows) == 1) await _state.StartAsync();
        };
        window.Destroying += async (_, _) =>
        {
            if (Interlocked.Decrement(ref _windows) == 0) await _state.StopAsync();
        };
        return window;
    }
}
