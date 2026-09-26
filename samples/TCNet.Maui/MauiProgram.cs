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
        builder.Services.AddSingleton<AppShell>();

        builder.Services.AddSingleton<LiveViewModel>();
        builder.Services.AddSingleton<NodesViewModel>();
        builder.Services.AddTransient<NodeDetailViewModel>();
        builder.Services.AddSingleton<MixerViewModel>();
        builder.Services.AddSingleton<PacketsViewModel>();
        builder.Services.AddSingleton<SendViewModel>();
        builder.Services.AddSingleton<OptionsViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        builder.Services.AddSingleton<LivePage>();
        builder.Services.AddSingleton<NodesPage>();
        builder.Services.AddTransient<NodeDetailPage>();
        builder.Services.AddSingleton<MixerPage>();
        builder.Services.AddSingleton<PacketsPage>();
        builder.Services.AddSingleton<SendPage>();
        builder.Services.AddSingleton<OptionsPage>();
        builder.Services.AddSingleton<SettingsPage>();

        return builder.Build();
    }
}
