using System.Collections.ObjectModel;
using System.Windows.Input;
using TCNet.Maui.Services;

namespace TCNet.Maui.ViewModels;

public sealed class PacketsViewModel : ObservableObject
{
    public PacketsViewModel(TCNetService service)
    {
        Service = service;
        ClearCommand = new Command(Service.ClearPackets);
    }

    public TCNetService Service { get; }
    public ObservableCollection<PacketItem> Packets => Service.Packets;
    public ICommand ClearCommand { get; }

    public bool Paused { get => Service.PausePackets; set { Service.PausePackets = value; OnPropertyChanged(); } }
    public string Filter { get => Service.PacketFilter; set { Service.PacketFilter = value; OnPropertyChanged(); } }

    public bool LogTime
    {
        get => Service.LogTimePackets;
        set { Service.LogTimePackets = value; OnPropertyChanged(); }
    }

    public bool LogSent
    {
        get => Service.LogSent;
        set { Service.LogSent = value; OnPropertyChanged(); }
    }
}
