using TCNet.Maui.ViewModels;

namespace TCNet.Maui.Views;

public partial class ReferencePage : ContentPage
{
    public ReferencePage(ReferenceViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
