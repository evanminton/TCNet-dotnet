namespace TCNet.Maui.Drawing;

/// <summary>Draws a TCNet waveform (level = bar height, colour byte = intensity, blue look) with a play head.</summary>
public sealed class WaveformDrawable : IDrawable
{
    public global::TCNet.Waveform? Waveform { get; set; }

    /// <summary>Play head position 0–1.</summary>
    public double Progress { get; set; }

    public void Draw(ICanvas canvas, RectF rect)
    {
        canvas.FillColor = Color.FromArgb("#101418");
        canvas.FillRectangle(rect);

        var bars = Waveform?.Bars;
        if (bars is { Count: > 0 })
        {
            int columns = Math.Max(1, (int)rect.Width);
            float mid = rect.Center.Y;
            for (int x = 0; x < columns; x++)
            {
                int from = x * bars.Count / columns;
                int to = Math.Max(from + 1, (x + 1) * bars.Count / columns);
                byte level = 0, color = 0;
                for (int i = from; i < to && i < bars.Count; i++)
                {
                    if (bars[i].Level > level) level = bars[i].Level;
                    if (bars[i].Color > color) color = bars[i].Color;
                }
                float h = level / 255f * rect.Height / 2f;
                bool played = (double)x / columns < Progress;
                var (r, g, b) = new WaveformBar(level, color).BlueRgb;
                canvas.StrokeColor = played ? Color.FromRgba(r, g, b, (byte)90) : Color.FromRgb(r, g, b);
                canvas.StrokeSize = 1;
                canvas.DrawLine(rect.X + x, mid - h, rect.X + x, mid + h);
            }
        }
        else
        {
            canvas.FontColor = Colors.Gray;
            canvas.FontSize = 11;
            canvas.DrawString("no waveform", rect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        float px = rect.X + (float)(Math.Clamp(Progress, 0, 1) * rect.Width);
        canvas.StrokeColor = Color.FromArgb("#E5484D");
        canvas.StrokeSize = 2;
        canvas.DrawLine(px, rect.Top, px, rect.Bottom);
    }
}

/// <summary>GraphicsView with bindable <see cref="Waveform"/> and <see cref="Progress"/>.</summary>
public sealed class WaveformView : GraphicsView
{
    private readonly WaveformDrawable _drawable = new();

    public static readonly BindableProperty WaveformProperty =
        BindableProperty.Create(nameof(Waveform), typeof(global::TCNet.Waveform), typeof(WaveformView), null, propertyChanged: (b, _, n) =>
        {
            var v = (WaveformView)b;
            v._drawable.Waveform = n as global::TCNet.Waveform;
            v.Invalidate();
        });

    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(WaveformView), 0.0, propertyChanged: (b, _, n) =>
        {
            var v = (WaveformView)b;
            v._drawable.Progress = (double)n;
            v.Invalidate();
        });

    public WaveformView()
    {
        Drawable = _drawable;
        HeightRequest = 48;
    }

    public global::TCNet.Waveform? Waveform { get => (global::TCNet.Waveform?)GetValue(WaveformProperty); set => SetValue(WaveformProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
}
