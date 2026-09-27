using TCNet.Maui.Services;
using TCNet.Maui.ViewModels;
using TCNet.Maui.Views;

namespace TCNet.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var b = MauiApp.CreateBuilder();
        b.UseMauiApp<App>();
        var s = b.Services;
        s.AddSingleton<AppPreferences>();
        s.AddSingleton<AppState>();
        s.AddTransient<AppShell>();

        s.AddTransient<LiveViewModel>();
        s.AddTransient<NodesViewModel>();
        s.AddTransient<NodeViewModel>();
        s.AddTransient<MixerViewModel>();
        s.AddTransient<PacketsViewModel>();
        s.AddTransient<SendViewModel>();
        s.AddTransient<MasterViewModel>();
        s.AddTransient<ReferenceViewModel>();
        s.AddTransient<SettingsViewModel>();

        s.AddTransient<LivePage>();
        s.AddTransient<NodesPage>();
        s.AddTransient<NodePage>();
        s.AddTransient<MixerPage>();
        s.AddTransient<PacketsPage>();
        s.AddTransient<SendPage>();
        s.AddTransient<MasterPage>();
        s.AddTransient<ReferencePage>();
        s.AddTransient<SettingsPage>();
        return b.Build();
    }
}
