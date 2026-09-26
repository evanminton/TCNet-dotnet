using TCNet.Maui.Services;
using TCNet.Maui.ViewModels;
using TCNet.Maui.Views;

namespace TCNet.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<TCNetService>();
        builder.Services.AddTransient<AppShell>();

        builder.Services.AddSingleton<LiveViewModel>();
        builder.Services.AddSingleton<NodesViewModel>();
        builder.Services.AddTransient<NodeDetailViewModel>();
        builder.Services.AddSingleton<MixerViewModel>();
        builder.Services.AddSingleton<PacketsViewModel>();
        builder.Services.AddSingleton<SendViewModel>();
        builder.Services.AddSingleton<OptionsViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // Shell and pages are per window (a page can only have one parent); the view models keep the state.
        builder.Services.AddTransient<LivePage>();
        builder.Services.AddTransient<NodesPage>();
        builder.Services.AddTransient<NodeDetailPage>();
        builder.Services.AddTransient<MixerPage>();
        builder.Services.AddTransient<PacketsPage>();
        builder.Services.AddTransient<SendPage>();
        builder.Services.AddTransient<OptionsPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}
