using System.Collections.ObjectModel;
using System.Windows.Input;
using TCNet.Maui.Services;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Maui.ViewModels;

public sealed record InterfaceChoice(string Label, string Address)
{
    public override string ToString() => Label;
}

/// <summary>Node configuration: every header option plus app behaviour.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly TCNetService _service;
    private string _nodeName, _nodeId, _listenerPort, _broadcast, _timeInterval;
    private OptionInfo? _role;
    private InterfaceChoice? _interface;
    private bool _optAuth, _optTcncm, _optTcnasdp, _optDnd, _autoMeta, _autoMetrics, _autoWave, _autoSync, _election, _simulate;

    public SettingsViewModel(TCNetService service)
    {
        _service = service;
        var s = service.Settings;
        Roles = TCNetText.Options<NodeType>();
        _nodeName = s.NodeName;
        _nodeId = s.NodeId.ToString();
        _listenerPort = s.ListenerPort == 0 ? "" : s.ListenerPort.ToString();
        _broadcast = s.BroadcastAddress;
        _timeInterval = s.TimeIntervalMs.ToString();
        _role = Roles.FirstOrDefault(r => r.Value == (long)s.Role);
        _optAuth = s.Options.HasFlag(NodeOptions.NeedAuthentication);
        _optTcncm = s.Options.HasFlag(NodeOptions.SupportsControlMessages);
        _optTcnasdp = s.Options.HasFlag(NodeOptions.SupportsApplicationData);
        _optDnd = s.Options.HasFlag(NodeOptions.DoNotDisturb);
        _autoMeta = s.AutoRequestMetadata;
        _autoMetrics = s.AutoRequestMetrics;
        _autoWave = s.AutoRequestWaveform;
        _autoSync = s.AutoTimeSync;
        _election = s.AutoMasterElection;
        _simulate = s.Simulate;

        Interfaces.Add(new InterfaceChoice("All interfaces (0.0.0.0)", ""));
        foreach (var i in TCNetNetwork.GetInterfaces()) Interfaces.Add(new InterfaceChoice(i.ToString(), i.Address.ToString()));
        _interface = Interfaces.FirstOrDefault(i => i.Address == s.InterfaceAddress) ?? Interfaces[0];

        ApplyCommand = new Command(async () => await Run("Apply", Apply));
        StartCommand = new Command(async () => await Run("Start", _service.StartAsync));
        StopCommand = new Command(async () => await Run("Stop", _service.StopAsync));
    }

    public TCNetService Service => _service;
    public IReadOnlyList<OptionInfo> Roles { get; }
    public ObservableCollection<InterfaceChoice> Interfaces { get; } = [];
    public ObservableCollection<string> Warnings => _service.Warnings;

    public string NodeName { get => _nodeName; set => SetProperty(ref _nodeName, value); }
    public string NodeId { get => _nodeId; set => SetProperty(ref _nodeId, value); }
    public OptionInfo? Role { get => _role; set => SetProperty(ref _role, value); }
    public InterfaceChoice? Interface { get => _interface; set => SetProperty(ref _interface, value); }
    public string ListenerPort { get => _listenerPort; set => SetProperty(ref _listenerPort, value); }
    public string Broadcast { get => _broadcast; set => SetProperty(ref _broadcast, value); }
    public string TimeInterval { get => _timeInterval; set => SetProperty(ref _timeInterval, value); }
    public bool OptAuth { get => _optAuth; set => SetProperty(ref _optAuth, value); }
    public bool OptTcncm { get => _optTcncm; set => SetProperty(ref _optTcncm, value); }
    public bool OptTcnasdp { get => _optTcnasdp; set => SetProperty(ref _optTcnasdp, value); }
    public bool OptDnd { get => _optDnd; set => SetProperty(ref _optDnd, value); }
    public bool AutoMetadata { get => _autoMeta; set => SetProperty(ref _autoMeta, value); }
    public bool AutoMetrics { get => _autoMetrics; set => SetProperty(ref _autoMetrics, value); }
    public bool AutoWaveform { get => _autoWave; set => SetProperty(ref _autoWave, value); }
    public bool AutoTimeSync { get => _autoSync; set => SetProperty(ref _autoSync, value); }
    public bool AutoMasterElection { get => _election; set => SetProperty(ref _election, value); }
    public bool Simulate { get => _simulate; set => SetProperty(ref _simulate, value); }

    public string OptionDescriptions => string.Join("\n", TCNetText.Options<NodeOptions>().Select(o => $"{o.Value}: {o.Description}"));

    public ICommand ApplyCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    private async Task Run(string action, Func<Task> body)
    {
        try { await body(); }
        catch (Exception ex) { _service.ReportError(action, ex); }
    }

    private async Task Apply()
    {
        var s = _service.Settings;
        s.NodeName = string.IsNullOrWhiteSpace(NodeName) ? "TCNETAPP" : NodeName.Trim()[..Math.Min(8, NodeName.Trim().Length)];
        NodeName = s.NodeName;
        if (int.TryParse(NodeId, out int id) && id is > 0 and <= 65535) s.NodeId = id;
        s.Role = Role is null ? NodeType.Slave : (NodeType)Role.Value;
        s.Options = (OptAuth ? NodeOptions.NeedAuthentication : 0)
                    | (OptTcncm ? NodeOptions.SupportsControlMessages : 0)
                    | (OptTcnasdp ? NodeOptions.SupportsApplicationData : 0)
                    | (OptDnd ? NodeOptions.DoNotDisturb : 0);
        s.InterfaceAddress = Interface?.Address ?? "";
        s.ListenerPort = int.TryParse(ListenerPort, out int port) && port is >= TCNetConstants.UnicastPortMin and <= TCNetConstants.UnicastPortMax ? port : 0;
        s.BroadcastAddress = Broadcast?.Trim() ?? "";
        s.TimeIntervalMs = int.TryParse(TimeInterval, out int ti) ? Math.Clamp(ti, 1, 40) : 20;
        s.AutoRequestMetadata = AutoMetadata;
        s.AutoRequestMetrics = AutoMetrics;
        s.AutoRequestWaveform = AutoWaveform;
        s.AutoTimeSync = AutoTimeSync;
        s.AutoMasterElection = AutoMasterElection;
        s.Simulate = Simulate;
        await _service.RestartAsync();
    }
}
