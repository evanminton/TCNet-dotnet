using System.Collections.ObjectModel;
using System.Windows.Input;
using TCNet.Maui.Services;
using TCNet.Text;

namespace TCNet.Maui.ViewModels;

/// <summary>Build, inspect, edit and send any TCNet packet.</summary>
public sealed class SendViewModel : ObservableObject
{
    private readonly TCNetService _service;
    private PacketInfo? _packet;
    private string _hex = "", _target = "broadcast:60000", _result = "";
    private string? _selectedTarget = "broadcast:60000";

    public SendViewModel(TCNetService service)
    {
        _service = service;
        PacketTypes = TCNetOptionCatalog.Packets;
        BuildCommand = new Command(Build);
        DecodeCommand = new Command(Decode);
        SendCommand = new Command(async () => await Send());
        OptInCommand = new Command(async () => await Quick(n => n.SendOptInAsync()));
        StatusCommand = new Command(async () => await Quick(n => n.SendStatusAsync()));
        TimeCommand = new Command(async () => await Quick(n => n.PublishTimeAsync(_service.Playback.BuildTime())));
        RefreshTargetsCommand = new Command(RefreshTargets);
        RefreshTargets();
        Packet = PacketTypes[0];
    }

    public IReadOnlyList<PacketInfo> PacketTypes { get; }
    public ObservableCollection<string> Targets { get; } = [];
    public ObservableCollection<TCNetField> Fields { get; } = [];

    public PacketInfo? Packet
    {
        get => _packet;
        set { if (SetProperty(ref _packet, value)) Build(); }
    }

    public string Hex { get => _hex; set => SetProperty(ref _hex, value); }
    public string Target { get => _target; set => SetProperty(ref _target, value ?? ""); }

    /// <summary>Picker selection; copied into <see cref="Target"/> when set (the picker nulls it when its list changes).</summary>
    public string? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value) && value is not null) Target = value;
        }
    }
    public string Result { get => _result; private set => SetProperty(ref _result, value); }

    public ICommand BuildCommand { get; }
    public ICommand DecodeCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand OptInCommand { get; }
    public ICommand StatusCommand { get; }
    public ICommand TimeCommand { get; }
    public ICommand RefreshTargetsCommand { get; }

    private void RefreshTargets()
    {
        var selected = SelectedTarget;
        Targets.Clear();
        Targets.Add("broadcast:60000");
        Targets.Add("broadcast:60001");
        Targets.Add("broadcast:60002");
        foreach (var n in _service.Nodes) Targets.Add($"node:{n.Key}");
        Targets.Add("127.0.0.1:65023");
        // Restore the picker selection without touching Target (the user may have typed a different one).
        _selectedTarget = selected is not null && Targets.Contains(selected) ? selected : null;
        OnPropertyChanged(nameof(SelectedTarget));
    }

    /// <summary>Creates a fresh packet of the selected type with this node's header and shows its bytes.</summary>
    private void Build()
    {
        if (Packet is null) return;
        var p = Packet.CreateSample();
        if (p is TextPayloadPacket t) t.Text = p is ControlPacket ? "layer/1/state=3;" : "Hello";
        if (p is RequestPacket rq) { rq.DataType = DataType.Metrics; rq.Layer = 1; }
        if (p is TimePacket) p = _service.Playback.BuildTime();
        if (p is StatusPacket) p = _service.Playback.BuildStatus();
        _service.Node?.StampHeader(p);
        Hex = Convert.ToHexString(p.ToArray());
        Decode();
    }

    private void Decode()
    {
        Fields.Clear();
        try
        {
            var bytes = Wire.ParseHex(Hex);
            if (!TCNetPacket.TryParse(bytes, out var p, out var err)) { Result = $"Not a valid packet: {err}"; return; }
            foreach (var f in p!.Describe()) Fields.Add(f);
            Result = $"{p.Name}, {bytes.Length} bytes{(p.WasPadded ? $" (shorter than {p.Length}; zero padded)" : "")}";
        }
        catch (FormatException ex) { Result = ex.Message; }
    }

    private async Task Send()
    {
        try
        {
            if (_service.Node is not { } node) { Result = "Node not running."; return; }
            if (string.IsNullOrWhiteSpace(Target)) { Result = "No target selected."; return; }
            var ep = _service.ResolveTarget(Target);
            if (ep is null) { Result = $"Unknown target '{Target}'."; return; }
            var bytes = Wire.ParseHex(Hex);
            await node.SendRawAsync(bytes, ep);
            Result = $"Sent {bytes.Length} bytes to {ep}.";
        }
        catch (Exception ex) { Result = ex.Message; }
    }

    private async Task Quick(Func<TCNet.Networking.TCNetNode, Task> action)
    {
        if (_service.Node is not { } node) { Result = "Node not running."; return; }
        try { await action(node); Result = "Sent."; }
        catch (Exception ex) { Result = ex.Message; }
    }
}
