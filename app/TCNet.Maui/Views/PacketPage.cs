using TCNet.Maui.Services;

namespace TCNet.Maui.Views;

/// <summary>One logged packet: every field with offset, size, value and meaning, plus a hex dump.</summary>
public sealed class PacketPage : ContentPage
{
    public PacketPage(PacketRow row)
    {
        var p = row.Packet;
        Title = p.Name;
        var bytes = p.ToArray();
        var fields = p.Describe().ToList();
        string text = p.ToDisplayString();
        string dump = Wire.HexDump(bytes);

        var copied = new Label { Style = Res<Style>("Caption"), VerticalOptions = LayoutOptions.Center };
        async Task Copy(string what, string value)
        {
            try
            {
                await Clipboard.Default.SetTextAsync(value);
                copied.Text = $"{what} copied.";
            }
            catch (Exception ex) { copied.Text = $"Could not copy: {ex.Message}"; }
        }
        var copyFields = new Button { Text = "Copy fields" };
        copyFields.Clicked += async (_, _) => await Copy("Fields", text);
        var copyHex = new Button { Text = "Copy hex" };
        copyHex.Clicked += async (_, _) => await Copy("Hex", Convert.ToHexString(bytes));
        var copyDump = new Button { Text = "Copy dump" };
        copyDump.Clicked += async (_, _) => await Copy("Dump", dump);

        var stack = new VerticalStackLayout { Padding = 12, Spacing = 8 };
        stack.Add(new Label { Text = $"{row.Direction} {row.Time} · {row.Route}", Style = Res<Style>("Heading") });
        stack.Add(new Label
        {
            Text = $"{row.From} · protocol {p.ProtocolVersion} · {p.Length} bytes"
                   + (p.WasPadded ? $" (received {p.ReceivedLength}, zero padded)" : ""),
            Style = Res<Style>("Caption"),
        });
        stack.Add(new HorizontalStackLayout { Spacing = 8, Children = { copyFields, copyHex, copyDump, copied } });

        var header = new Grid { ColumnDefinitions = Columns(), ColumnSpacing = 8 };
        AddCells(header, "Offset", "Size", "Field", "Value", "Meaning", bold: true);
        stack.Add(header);
        foreach (var f in fields)
        {
            var g = new Grid { ColumnDefinitions = Columns(), ColumnSpacing = 8 };
            AddCells(g, f.Offset.ToString(), f.Size.ToString(), f.Name, f.Value, f.Meaning ?? "", bold: false);
            stack.Add(g);
        }
        stack.Add(new Label { Text = "Hex dump", Style = Res<Style>("Heading"), Margin = new Thickness(0, 12, 0, 0) });
        stack.Add(new Label { Text = dump, Style = Res<Style>("Mono") });
        Content = new ScrollView { Content = stack };
    }

    private static ColumnDefinitionCollection Columns() =>
    [
        new ColumnDefinition(60), new ColumnDefinition(50), new ColumnDefinition(220),
        new ColumnDefinition(new GridLength(1, GridUnitType.Star)), new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)),
    ];

    private static void AddCells(Grid g, string offset, string size, string name, string value, string meaning, bool bold)
    {
        var attrs = bold ? FontAttributes.Bold : FontAttributes.None;
        g.Add(new Label { Text = offset, FontAttributes = attrs }, 0);
        g.Add(new Label { Text = size, FontAttributes = attrs }, 1);
        g.Add(new Label { Text = name, FontAttributes = attrs }, 2);
        g.Add(new Label { Text = value, FontAttributes = attrs, Style = bold ? null : Res<Style>("Mono") }, 3);
        g.Add(new Label { Text = meaning, FontAttributes = attrs, Style = bold ? null : Res<Style>("Caption") }, 4);
    }

    private static T? Res<T>(string key) where T : class =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true ? v as T : null;
}
