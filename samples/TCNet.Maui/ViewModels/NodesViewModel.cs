using System.Collections.ObjectModel;
using TCNet.Maui.Services;

namespace TCNet.Maui.ViewModels;

public sealed class NodesViewModel(TCNetService service)
{
    public TCNetService Service { get; } = service;
    public ObservableCollection<NodeItem> Nodes => Service.Nodes;
}
