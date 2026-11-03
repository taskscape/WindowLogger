using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WindowLogger.Shared.Activity;

namespace WindowLoggerConfigGui;

public enum TimelineGrouping
{
    Activity,
    Category,
    Application
}

public static class TimelineRenderer
{
    public const double RowHeight = 76;
    private const double BarTop = 24;
    private const double BarHeight = 42;
    private static readonly Brush ActiveBrush = CreateBrush(67, 133, 208);
    private static readonly Brush IdleBrush = CreateBrush(224, 162, 58);
    private static readonly Brush[] Palette =
    [
        CreateBrush(79, 102, 173), CreateBrush(45, 125, 109),
        CreateBrush(151, 78, 158), CreateBrush(177, 94, 51),
        CreateBrush(43, 119, 158), CreateBrush(132, 111, 40),
        CreateBrush(174, 68, 101), CreateBrush(96, 113, 66),
        CreateBrush(107, 86, 163), CreateBrush(58, 119, 130),
        CreateBrush(153, 91, 77), CreateBrush(88, 105, 125)
    ];

    public static void Render(
        Canvas canvas,
        IReadOnlyList<TimelineBlock> blocks,
        DateTime rangeStart,
        DateTime rangeEnd,
        IReadOnlyList<KeyValuePair<DateTime, string>> ticks,
        TimelineGrouping grouping,
        TimelineSelection? selection = null)
    {
        canvas.Children.Clear();
        double width = canvas.ActualWidth;
        if (width <= 0 || rangeEnd <= rangeStart)
        {
            return;
        }

        double periodTicks = (rangeEnd - rangeStart).Ticks;
        foreach (KeyValuePair<DateTime, string> tick in ticks)
        {
            double x = (tick.Key - rangeStart).Ticks / periodTicks * width;
            canvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = RowHeight,
                Stroke = Brushes.LightGray, StrokeThickness = 1
            });
            var label = new TextBlock { Text = tick.Value, FontSize = 10, Foreground = Brushes.DimGray };
            Canvas.SetLeft(label, Math.Min(x + 2, Math.Max(0, width - 44)));
            Canvas.SetTop(label, 2);
            canvas.Children.Add(label);
        }

        foreach (TimelineBlock block in blocks)
        {
            DateTime start = block.Start < rangeStart ? rangeStart : block.Start;
            DateTime end = block.End > rangeEnd ? rangeEnd : block.End;
            if (end <= start)
            {
                continue;
            }

            double left = (start - rangeStart).Ticks / periodTicks * width;
            double blockWidth = Math.Min(width - left, Math.Max(2, (end - start).Ticks / periodTicks * width));
            string[] names = grouping switch
            {
                TimelineGrouping.Category => block.Categories.DefaultIfEmpty("Uncategorized").ToArray(),
                TimelineGrouping.Application => [block.Application],
                _ => [block.Status]
            };
            var segments = new Grid();
            foreach (string name in names)
            {
                int row = segments.RowDefinitions.Count;
                segments.RowDefinitions.Add(new RowDefinition());
                var segment = new Border
                {
                    Background = grouping == TimelineGrouping.Activity
                        ? block.Status == "Active" ? ActiveBrush : IdleBrush
                        : GetNameBrush(name)
                };
                if (grouping != TimelineGrouping.Activity && blockWidth >= 44)
                {
                    segment.Child = new TextBlock
                    {
                        Text = name, Foreground = Brushes.White, FontSize = 11,
                        Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                }
                Grid.SetRow(segment, row);
                segments.Children.Add(segment);
            }

            var bar = new Border
            {
                Width = blockWidth, Height = BarHeight, Child = segments, ClipToBounds = true,
                ToolTip = $"{block.Start:g} – {block.End:t}\n{block.Status} • {block.Application}\nCategories: {string.Join(", ", block.Categories)}\n{block.WindowTitle}"
            };
            Canvas.SetLeft(bar, left);
            Canvas.SetTop(bar, BarTop);
            canvas.Children.Add(bar);
        }
        RenderSelection(canvas, rangeStart, rangeEnd, selection);
    }

    public static void RenderSelection(Canvas canvas, DateTime rangeStart, DateTime rangeEnd, TimelineSelection? selection)
    {
        foreach (Rectangle overlay in canvas.Children.OfType<Rectangle>().ToArray())
        {
            canvas.Children.Remove(overlay);
        }
        if (selection is null || canvas.ActualWidth <= 0 || rangeEnd <= rangeStart)
        {
            return;
        }
        double ticks = (rangeEnd - rangeStart).Ticks;
        double left = Math.Clamp((selection.Start - rangeStart).Ticks / ticks, 0, 1) * canvas.ActualWidth;
        double right = Math.Clamp((selection.End - rangeStart).Ticks / ticks, 0, 1) * canvas.ActualWidth;
        if (right <= left)
        {
            return;
        }
        var rectangle = new Rectangle
        {
            Width = right - left, Height = canvas.ActualHeight,
            Fill = new SolidColorBrush(Color.FromArgb(55, 35, 98, 191)),
            Stroke = new SolidColorBrush(Color.FromRgb(35, 98, 191)), StrokeThickness = 1.5,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, 0);
        Panel.SetZIndex(rectangle, 1);
        canvas.Children.Add(rectangle);
    }

    private static Brush GetNameBrush(string name)
    {
        // A fixed hash keeps colors stable across refreshes, filters, periods, and restarts.
        uint hash = 2166136261;
        foreach (char character in name.ToUpperInvariant())
        {
            hash = unchecked((hash ^ character) * 16777619);
        }
        return Palette[hash % (uint)Palette.Length];
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
