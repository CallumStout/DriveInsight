using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DriveInsight.Controls;

public class CircularProgress : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CircularProgress, double>(nameof(Value), 0d);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<CircularProgress, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<CircularProgress, double>(nameof(StrokeThickness), 14d);

    public static readonly StyledProperty<IBrush?> ProgressBrushProperty =
        AvaloniaProperty.Register<CircularProgress, IBrush?>(nameof(ProgressBrush), Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<CircularProgress, IBrush?>(nameof(TrackBrush), Brushes.LightGray);

    public static readonly StyledProperty<double> StartAngleProperty =
        AvaloniaProperty.Register<CircularProgress, double>(nameof(StartAngle), -90d);

    static CircularProgress()
    {
        AffectsRender<CircularProgress>(
            ValueProperty,
            MaximumProperty,
            StrokeThicknessProperty,
            ProgressBrushProperty,
            TrackBrushProperty,
            StartAngleProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? ProgressBrush
    {
        get => GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double StartAngle
    {
        get => GetValue(StartAngleProperty);
        set => SetValue(StartAngleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var thickness = Math.Max(1, StrokeThickness);
        var radius = Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2d - thickness / 2d);
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2d, Bounds.Height / 2d);
        var trackPen = new Pen(TrackBrush, thickness) { LineCap = PenLineCap.Round };
        context.DrawEllipse(null, trackPen, center, radius, radius);

        var max = Math.Max(1d, Maximum);
        var ratio = Math.Clamp(Value / max, 0d, 1d);
        if (ratio <= 0d)
        {
            return;
        }

        var sweep = ratio * 360d;
        var progressPen = new Pen(ProgressBrush, thickness) { LineCap = PenLineCap.Round };

        if (sweep >= 359.99d)
        {
            context.DrawEllipse(null, progressPen, center, radius, radius);
            return;
        }

        var startRadians = DegreesToRadians(StartAngle);
        var endRadians = DegreesToRadians(StartAngle + sweep);

        var start = new Point(
            center.X + radius * Math.Cos(startRadians),
            center.Y + radius * Math.Sin(startRadians));
        var end = new Point(
            center.X + radius * Math.Cos(endRadians),
            center.Y + radius * Math.Sin(endRadians));

        var geometry = new StreamGeometry();
        using (var geoContext = geometry.Open())
        {
            geoContext.BeginFigure(start, false);
            geoContext.ArcTo(
                end,
                new Size(radius, radius),
                0,
                sweep > 180,
                SweepDirection.Clockwise);
            geoContext.EndFigure(false);
        }

        context.DrawGeometry(null, progressPen, geometry);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
