using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

/// <summary>Starts and stops a polling view model with the page.</summary>
internal static class Polling
{
    public static void Attach(ContentPage page, PollingViewModel vm)
    {
        page.BindingContext = vm;
        page.Appearing += (_, _) => vm.Start();
        page.Disappearing += (_, _) => vm.Stop();
    }
}
