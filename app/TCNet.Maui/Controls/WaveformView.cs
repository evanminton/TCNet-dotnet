namespace TCNet.Maui.Controls;

/// <summary>Draws a TCNet waveform in the spec's blue look (R = G = colour byte, B = 255) with a play head.</summary>
public sealed class WaveformView : GraphicsView
{
    public static readonly BindableProperty BarsProperty =
        BindableProperty.Create(nameof(Bars), typeof(IReadOnlyList<WaveformBar>), typeof(WaveformView), null, propertyChanged: Redraw);

    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(WaveformView), 0.0, propertyChanged: Redraw);

    public WaveformView()
    {
        Drawable = new Painter(this);
        HeightRequest = 44;
    }

    public IReadOnlyList<WaveformBar>? Bars
    {
        get => (IReadOnlyList<WaveformBar>?)GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private static void Redraw(BindableObject b, object o, object n) => ((WaveformView)b).Invalidate();

    private sealed class Painter(WaveformView view) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            canvas.FillColor = Color.FromArgb("#0E1216");
            canvas.FillRectangle(rect);
            var bars = view.Bars;
            float mid = rect.Center.Y;
            if (bars is { Count: > 0 })
            {
                int cols = Math.Max(1, (int)rect.Width);
                for (int x = 0; x < cols; x++)
                {
                    int a = x * bars.Count / cols, b = Math.Max(a + 1, (x + 1) * bars.Count / cols);
                    byte level = 0, color = 0;
                    for (int i = a; i < b && i < bars.Count; i++)
                    {
                        level = Math.Max(level, bars[i].Level);
                        color = Math.Max(color, bars[i].Color);
                    }
                    var (r, g, bl) = new WaveformBar(level, color).Blue;
                    bool played = (double)x / cols < view.Progress;
                    canvas.StrokeColor = Color.FromRgba(r, g, bl, played ? (byte)110 : (byte)255);
                    canvas.StrokeSize = 1;
                    float h = level / 255f * rect.Height / 2f;
                    canvas.DrawLine(rect.X + x, mid - h, rect.X + x, mid + h);
                }
            }
            else
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 11;
                canvas.DrawString("no waveform", rect, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
            float px = rect.X + (float)(Math.Clamp(view.Progress, 0, 1) * rect.Width);
            canvas.StrokeColor = Color.FromArgb("#E5484D");
            canvas.StrokeSize = 2;
            canvas.DrawLine(px, rect.Top, px, rect.Bottom);
        }
    }
}
