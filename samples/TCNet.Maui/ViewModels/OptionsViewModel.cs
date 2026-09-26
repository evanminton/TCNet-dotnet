using System.Collections.ObjectModel;
using System.Windows.Input;
using TCNet.Text;

namespace TCNet.Maui.ViewModels;

public sealed record ReferenceRow(string Title, string Detail, string Value = "");

public sealed class ReferenceGroup(string name, IEnumerable<ReferenceRow> rows) : ObservableCollection<ReferenceRow>(rows)
{
    public string Name { get; } = name;
}

/// <summary>Browsable spec reference: packets with field layouts, option tables, application codes, spec notes.</summary>
public sealed class OptionsViewModel : Services.ObservableObject
{
    private readonly List<ReferenceGroup> _all;
    private string _search = "";

    public OptionsViewModel()
    {
        _all = [];
        foreach (var p in TCNetOptionCatalog.Packets)
        {
            var rows = new List<ReferenceRow> { new(p.Functionality, $"{p.Transport} · port {p.Port} · size {p.Size} · {p.Behavior}", $"type {p.Key}") };
            rows.AddRange(p.Layout.Select(f => new ReferenceRow(f.Name, $"byte {f.Offset}, {f.Size} byte{(f.Size == 1 ? "" : "s")}", f.Offset.ToString())));
            _all.Add(new ReferenceGroup($"Packet · {p.Name}", rows));
        }
        foreach (var s in TCNetText.OptionSets)
            _all.Add(new ReferenceGroup($"Option · {s.Name} ({s.Location}{(s.IsFlags ? ", flags" : "")})",
                s.Options.Select(o => new ReferenceRow(o.Name, o.Description, o.Value.ToString()))));
        _all.Add(new ReferenceGroup("Registered application codes",
            TCNetText.ApplicationCodes.Select(a => new ReferenceRow(a.Vendor, a.Url, a.Code.ToString("X4")))));
        _all.Add(new ReferenceGroup("Spec notes", TCNetOptionCatalog.SpecNotes.Select(n => new ReferenceRow(n.Topic, n.Note))));
        Groups = new ObservableCollection<ReferenceGroup>(_all);
        CopyCommand = new Command(async () => await Clipboard.Default.SetTextAsync(TCNetOptionCatalog.ToMarkdown()));
    }

    public ObservableCollection<ReferenceGroup> Groups { get; }
    public ICommand CopyCommand { get; }

    public string Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value ?? "")) return;
            Groups.Clear();
            foreach (var g in _all)
            {
                if (_search.Length == 0 || g.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)) { Groups.Add(g); continue; }
                var rows = g.Where(r => r.Title.Contains(_search, StringComparison.OrdinalIgnoreCase)
                                        || r.Detail.Contains(_search, StringComparison.OrdinalIgnoreCase)
                                        || r.Value.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();
                if (rows.Count > 0) Groups.Add(new ReferenceGroup(g.Name, rows));
            }
        }
    }
}
