namespace TCNet.Maui.Views;

/// <summary>Every field of one packet with its meaning, plus a hex dump.</summary>
public sealed class PacketDetailPage : ContentPage
{
    public PacketDetailPage(TCNetPacket packet)
    {
        Title = packet.Name;
        string font = DeviceInfo.Platform == DevicePlatform.Android ? "monospace"
            : DeviceInfo.Platform == DevicePlatform.WinUI ? "Consolas" : "Menlo";

        var fields = new VerticalStackLayout { Spacing = 2 };
        fields.Add(new Label { Text = $"{packet.Name} · {packet.Length} bytes · protocol {packet.ProtocolVersion}", FontAttributes = FontAttributes.Bold, FontSize = 16 });
        foreach (var f in packet.Describe())
        {
            var row = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(70), new ColumnDefinition(220), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                ColumnSpacing = 8,
            };
            row.Add(new Label { Text = $"{f.Offset}+{f.Size}", FontFamily = font, FontSize = 12, TextColor = Colors.Gray }, 0);
            row.Add(new Label { Text = f.Name }, 1);
            row.Add(new Label { Text = f.Value, FontFamily = font }, 2);
            row.Add(new Label { Text = f.Meaning ?? "", TextColor = Colors.Gray, FontSize = 12 }, 3);
            fields.Add(row);
        }

        var bytes = packet.ToArray();
        var hex = new Label { Text = Wire.HexDump(bytes, 4096), FontFamily = font, FontSize = 12 };
        var status = new Label { TextColor = Colors.Gray, FontSize = 12, VerticalOptions = LayoutOptions.Center };
        async Task Copy(string text, string what)
        {
            try
            {
                await Clipboard.Default.SetTextAsync(text);
                status.Text = $"Copied {what}.";
            }
            catch (Exception ex) { status.Text = $"Copy failed: {ex.Message}"; }
        }
        var copy = new Button { Text = "Copy hex" };
        copy.Clicked += async (_, _) => await Copy(Convert.ToHexString(bytes), "hex");
        var copyText = new Button { Text = "Copy decoded text" };
        copyText.Clicked += async (_, _) => await Copy(packet.ToDisplayString(), "decoded text");

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 12,
                Spacing = 12,
                Children = { fields, new HorizontalStackLayout { Spacing = 8, Children = { copy, copyText, status } }, hex },
            },
        };
    }
}
