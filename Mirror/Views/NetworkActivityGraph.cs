using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mirror.Views;

public class NetworkActivityGraph : Control
{
    public static readonly StyledProperty<double[]?> SamplesProperty =
        AvaloniaProperty.Register<NetworkActivityGraph, double[]?>(nameof(Samples));

    private IBrush? _fillBrush;
    private IBrush? _idleBrush;
    private IPen? _linePen;
    private Color _cachedAccent;

    private void EnsureBrushes()
    {
        Color accent;
        if (this.TryFindResource("SystemAccentColor", this.ActualThemeVariant, out var res) && res is Color c)
            accent = c;
        else
            accent = Color.FromRgb(0, 120, 212); // fallback blue

        if (accent == _cachedAccent && _fillBrush is not null)
            return;

        _cachedAccent = accent;
        _fillBrush = new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)).ToImmutable();
        _idleBrush = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)).ToImmutable();
        _linePen = new Pen(new SolidColorBrush(Color.FromArgb(230, accent.R, accent.G, accent.B)).ToImmutable(), 1.5).ToImmutable();
    }

    public double[]? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    static NetworkActivityGraph()
    {
        AffectsRender<NetworkActivityGraph>(SamplesProperty);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        EnsureBrushes();

        var samples = Samples;
        if (samples is null || samples.Length < 2)
            return;

        double max = 0;
        foreach (var s in samples)
            if (s > max) max = s;

        if (max <= 0)
        {
            context.DrawRectangle(_idleBrush!, null, new Rect(0, height - 2, width, 2));
            return;
        }

        int n = samples.Length;

        // Evaluate smooth Catmull-Rom spline into many small segments
        const int segmentsPerSample = 4;
        int totalPts = (n - 1) * segmentsPerSample + 1;
        var curve = new Point[totalPts];
        int idx = 0;

        for (int i = 0; i < n - 1; i++)
        {
            var p0 = samples[Math.Max(i - 1, 0)];
            var p1 = samples[i];
            var p2 = samples[i + 1];
            var p3 = samples[Math.Min(i + 2, n - 1)];

            for (int s = 0; s < segmentsPerSample; s++)
            {
                double t = (double)s / segmentsPerSample;
                double val = CatmullRom(p0, p1, p2, p3, t);
                double x = ((i + t) / (n - 1)) * width;
                double y = height - (val / max) * (height - 4);
                curve[idx++] = new Point(x, Math.Max(0, Math.Min(height, y)));
            }
        }
        // Last point
        {
            double y = height - (samples[n - 1] / max) * (height - 4);
            curve[idx] = new Point(width, Math.Max(0, Math.Min(height, y)));
        }

        // Fill: thin vertical rectangles under curve
        for (int i = 0; i < totalPts - 1; i++)
        {
            double x0 = curve[i].X;
            double x1 = curve[i + 1].X;
            double topY = Math.Min(curve[i].Y, curve[i + 1].Y);
            double w = x1 - x0 + 0.5;
            if (height - topY > 0.5)
                context.DrawRectangle(_fillBrush!, null, new Rect(x0, topY, w, height - topY));
        }

        // Line: DrawLine between adjacent curve points
        for (int i = 0; i < totalPts - 1; i++)
        {
            context.DrawLine(_linePen!, curve[i], curve[i + 1]);
        }
    }

    private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
    {
        double t2 = t * t;
        double t3 = t2 * t;
        return 0.5 * (
            2 * p1 +
            (-p0 + p2) * t +
            (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
            (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
    }
}
