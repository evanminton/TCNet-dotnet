using System.Collections.ObjectModel;
using System.Windows.Input;
using TCNet.Maui.Services;
using TCNet.Text;

namespace TCNet.Maui.ViewModels;

public sealed record MixerChannelRow(string Name, double Fader, double Level, string Info);

/// <summary>Every Mixer Data field (type 150) from the first node that sends one.</summary>
public sealed class MixerViewModel : ObservableObject
{
    private readonly TCNetService _service;
    private IDispatcherTimer? _timer;
    private string _header = "No Mixer Data received yet. Mixer data is unicast to slaves when it changes.";
    private double _master, _crossfader;
    private MixerDataPacket? _last;

    public MixerViewModel(TCNetService service)
    {
        _service = service;
        RequestCommand = new Command(async () =>
        {
            try
            {
                if (_service.Node is not { } local) return;
                foreach (var n in local.Nodes.Where(n => n.IsMasterOrRepeater && n.EndPoint is not null))
                    await local.RequestMixerAsync(n);
            }
            catch (Exception ex) { Header = $"Mixer request failed: {ex.Message}"; }
        });
    }

    public string Header { get => _header; private set => SetProperty(ref _header, value); }
    public double Master { get => _master; private set => SetProperty(ref _master, value); }
    public double Crossfader { get => _crossfader; private set => SetProperty(ref _crossfader, value); }
    public ObservableCollection<MixerChannelRow> Channels { get; } = [];
    public ObservableCollection<TCNetField> Fields { get; } = [];
    public ICommand RequestCommand { get; }

    public void Start()
    {
        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick -= Tick;
        _timer.Tick += Tick;
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    private void Tick(object? sender, EventArgs e)
    {
        var node = _service.Node?.Nodes.FirstOrDefault(n => n.LastMixer is not null);
        var mx = node?.LastMixer;
        if (mx is null || ReferenceEquals(mx, _last)) return;
        _last = mx;
        Header = $"{mx.MixerName} (ID {mx.MixerId}, {TCNetText.Describe(mx.MixerType)}) from {node!.NodeName}";
        Master = mx.MasterFaderLevel / 255.0;
        Crossfader = mx.CrossFader / 255.0;
        Channels.Clear();
        foreach (var c in mx.Channels)
            Channels.Add(new MixerChannelRow($"CH{c.Number}", c.FaderLevel / 255.0, c.AudioLevel / 255.0,
                $"{TCNetText.Describe(c.SourceSelect)} · trim {c.TrimLevel} · EQ {c.EqHi}/{c.EqHiMid}/{c.EqLowMid}/{c.EqLow} · filter {c.FilterColor} · xf {TCNetText.Describe(c.CrossfaderAssign)}{(c.CueA ? " · CUE A" : "")}{(c.CueB ? " · CUE B" : "")}"));
        Fields.Clear();
        foreach (var f in mx.Describe()) Fields.Add(f);
    }
}
