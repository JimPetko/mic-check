using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MicCheck.Controls;

/// <summary>Horizontal dBFS meter: solid RMS bar, translucent peak bar, peak-hold tick and a scale.</summary>
public sealed class LevelMeter : FrameworkElement
{
    public const double MinDb = -60;
    private const double BarHeight = 28;
    private const double ScaleHeight = 18;
    private static readonly int[] ScaleMarks = [-60, -48, -36, -24, -18, -12, -6, -3, 0];

    public static readonly DependencyProperty RmsDbProperty = Register(nameof(RmsDb), MinDb);
    public static readonly DependencyProperty PeakDbProperty = Register(nameof(PeakDb), MinDb);
    public static readonly DependencyProperty PeakHoldDbProperty = Register(nameof(PeakHoldDb), MinDb);
    public static readonly DependencyProperty TrackBrushProperty = Register(nameof(TrackBrush), (Brush)Brushes.DimGray);
    public static readonly DependencyProperty ForegroundProperty = Register(nameof(Foreground), (Brush)Brushes.Gray);

    public double RmsDb { get => (double)GetValue(RmsDbProperty); set => SetValue(RmsDbProperty, value); }
    public double PeakDb { get => (double)GetValue(PeakDbProperty); set => SetValue(PeakDbProperty, value); }
    public double PeakHoldDb { get => (double)GetValue(PeakHoldDbProperty); set => SetValue(PeakHoldDbProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(LevelMeter),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static double ToFraction(double db) => Math.Clamp((db - MinDb) / -MinDb, 0, 1);

    protected override Size MeasureOverride(Size availableSize) => new(0, BarHeight + ScaleHeight);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var bar = new Rect(0, 0, width, BarHeight);
        var radius = 4.0;

        dc.DrawRoundedRectangle(TrackBrush, null, bar, radius, radius);

        var gradient = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(width, 0),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x2E, 0xC2, 0x6B), 0),
                new GradientStop(Color.FromRgb(0x2E, 0xC2, 0x6B), ToFraction(-18)),
                new GradientStop(Color.FromRgb(0xF2, 0xC9, 0x3A), ToFraction(-9)),
                new GradientStop(Color.FromRgb(0xF2, 0x8C, 0x28), ToFraction(-4)),
                new GradientStop(Color.FromRgb(0xE5, 0x3E, 0x3E), ToFraction(-1)),
            },
        };

        var clip = new RectangleGeometry(bar, radius, radius);
        dc.PushClip(clip);

        // Peak extends past RMS as a faded region; RMS is the solid "body" of the signal.
        dc.PushOpacity(0.35);
        dc.DrawRectangle(gradient, null, new Rect(0, 0, width * ToFraction(PeakDb), BarHeight));
        dc.Pop();
        dc.DrawRectangle(gradient, null, new Rect(0, 0, width * ToFraction(RmsDb), BarHeight));

        if (PeakHoldDb > MinDb)
        {
            var x = Math.Min(width * ToFraction(PeakHoldDb), width - 2);
            dc.DrawRectangle(Foreground, null, new Rect(x, 0, 2, BarHeight));
        }

        dc.Pop();

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        foreach (var mark in ScaleMarks)
        {
            var x = width * ToFraction(mark);
            var text = new FormattedText(mark.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10.5, Foreground, pixelsPerDip);
            var tx = Math.Clamp(x - text.Width / 2, 0, width - text.Width);
            dc.DrawRectangle(Foreground, null, new Rect(Math.Min(x, width - 1), BarHeight + 1, 1, 3));
            dc.DrawText(text, new Point(tx, BarHeight + 4));
        }
    }
}
