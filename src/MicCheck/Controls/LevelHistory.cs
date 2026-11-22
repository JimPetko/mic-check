using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MicCheck.Controls;

/// <summary>Scrolling area graph of recent levels in dBFS, newest on the right.</summary>
public sealed class LevelHistory : FrameworkElement
{
    private static readonly int[] GridMarks = [-12, -24, -36, -48];

    private readonly double[] _values;
    private int _next;

    public static readonly DependencyProperty FillProperty = Register(nameof(Fill), (Brush)Brushes.SteelBlue);
    public static readonly DependencyProperty GridBrushProperty = Register(nameof(GridBrush), (Brush)Brushes.DimGray);
    public static readonly DependencyProperty ForegroundProperty = Register(nameof(Foreground), (Brush)Brushes.Gray);

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public LevelHistory() : this(300) { }

    public LevelHistory(int capacity)
    {
        _values = new double[capacity];
        Clear();
    }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(LevelHistory),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    public void Push(double db)
    {
        _values[_next] = db;
        _next = (_next + 1) % _values.Length;
        InvalidateVisual();
    }

    public void Clear()
    {
        Array.Fill(_values, LevelMeter.MinDb);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        // Transparent background so the whole area is hit-testable and has a defined size.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var gridPen = new Pen(GridBrush, 1);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        foreach (var mark in GridMarks)
        {
            var y = Math.Round(height * (1 - LevelMeter.ToFraction(mark))) + 0.5;
            dc.DrawLine(gridPen, new Point(0, y), new Point(width, y));
            var text = new FormattedText($"{mark.ToString(CultureInfo.InvariantCulture)} dB", CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, Foreground, pixelsPerDip);
            dc.DrawText(text, new Point(4, y - text.Height));
        }

        var count = _values.Length;
        var step = width / (count - 1);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, height), isFilled: true, isClosed: false);
            for (var i = 0; i < count; i++)
            {
                var db = _values[(_next + i) % count];
                ctx.LineTo(new Point(i * step, height * (1 - LevelMeter.ToFraction(db))), isStroked: i > 0, isSmoothJoin: false);
            }
            ctx.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
        }
        geometry.Freeze();

        dc.PushOpacity(0.4);
        dc.DrawGeometry(Fill, null, geometry);
        dc.Pop();
        dc.DrawGeometry(null, new Pen(Fill, 1.5), geometry);
    }
}
