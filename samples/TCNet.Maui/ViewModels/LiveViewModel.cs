using System.Collections.ObjectModel;
using TCNet.Maui.Services;

namespace TCNet.Maui.ViewModels;

public sealed class LiveViewModel : ObservableObject
{
    private readonly TCNetService _service;
    private IDispatcherTimer? _timer;
    private NodeItem? _source;
    private string _header = "Waiting for a node that sends Time packets…";

    public LiveViewModel(TCNetService service)
    {
        _service = service;
        Layers = new ObservableCollection<LayerViewModel>(Enumerable.Range(0, 8).Select(i => new LayerViewModel(i)));
    }

    public ObservableCollection<NodeItem> Sources => _service.Nodes;
    public ObservableCollection<LayerViewModel> Layers { get; }

    public NodeItem? Source { get => _source; set => SetProperty(ref _source, value); }
    public string Header { get => _header; private set => SetProperty(ref _header, value); }

    public void Start()
    {
        _timer ??= CreateTimer();
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    private IDispatcherTimer CreateTimer()
    {
        var t = Application.Current!.Dispatcher.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(100);
        t.Tick += (_, _) => Tick();
        return t;
    }

    private void Tick()
    {
        if (Source is null || !Sources.Contains(Source))
            Source = Sources.FirstOrDefault(n => n.Node.LastTime is not null) ?? Sources.FirstOrDefault();

        var node = Source?.Node;
        if (node is null) { Header = "Waiting for a node that sends Time packets…"; }
        else
        {
            var age = node.LastTimeReceived is { } t ? (DateTime.UtcNow - t).TotalMilliseconds : double.NaN;
            Header = node.LastTime is null
                ? $"{node.NodeName}: no Time packets yet"
                : $"{node.NodeName} · SMPTE {Text.TCNetText.Describe(node.LastTime.SmpteMode)} · last time packet {age:0} ms ago";
        }

        foreach (var l in Layers)
        {
            if (l.Update(node) && node is not null && _service.Settings.AutoRequestWaveform && _service.Node is { } local && node.EndPoint is not null)
                _ = RequestWaveform(local, node, l.LayerNumber);
        }
    }

    private static async Task RequestWaveform(TCNet.Networking.TCNetNode local, TCNet.Networking.TCNetRemoteNode node, byte layer)
    {
        try { await local.RequestSmallWaveformAsync(node, layer); } catch { }
    }
}
