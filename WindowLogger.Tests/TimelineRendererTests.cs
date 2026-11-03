using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WindowLogger.Shared.Activity;
using WindowLoggerConfigGui;

namespace WindowLogger.Tests;

[Apartment(ApartmentState.STA)]
public sealed class TimelineRendererTests
{
    private static readonly DateTime Day = new(2026, 11, 2);
    private static readonly KeyValuePair<DateTime, string>[] Ticks =
        Enumerable.Range(0, 4).Select(hour => new KeyValuePair<DateTime, string>(
            Day.AddHours(hour * 6), $"{hour * 6:00}:00")).ToArray();

    [TestCase(240)]
    [TestCase(960)]
    public void All_tracks_share_time_positions_dimensions_and_ticks(double width)
    {
        TimelineBlock[] blocks =
        [
            new(Day.AddHours(6), Day.AddHours(9), "Active", "Editor", "Code", ["Work", "Development"]),
            new(Day.AddHours(12), Day.AddHours(15), "Inactive", "Browser", "Docs", ["Research"])
        ];
        Canvas[] canvases = Enum.GetValues<TimelineGrouping>().Select(grouping =>
            Render(blocks, grouping, width)).ToArray();

        foreach (Canvas canvas in canvases)
        {
            Border[] bars = canvas.Children.OfType<Border>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(bars, Has.Length.EqualTo(2));
                Assert.That(bars.Select(Canvas.GetLeft), Is.EqualTo(new[] { width / 4, width / 2 }));
                Assert.That(bars.Select(bar => bar.Width), Is.All.EqualTo(width / 8));
                Assert.That(bars.Select(bar => bar.Height), Is.All.EqualTo(42));
                Assert.That(bars.Select(Canvas.GetTop), Is.All.EqualTo(24));
                Assert.That(canvas.Children.OfType<Line>().Select(line => line.X1),
                    Is.EqualTo(new[] { 0, width / 4, width / 2, width * 3 / 4 }));
                Assert.That(canvas.Children.OfType<TextBlock>().Select(label => label.Text),
                    Is.EqualTo(Ticks.Select(tick => tick.Value)));
            });
        }

        Grid categorySegments = (Grid)canvases[1].Children.OfType<Border>().First().Child;
        Assert.That(categorySegments.RowDefinitions, Has.Count.EqualTo(2),
            "Multiple matching categories must fit inside the same 42px bar.");
    }

    [Test]
    public void Group_colors_remain_stable_when_filtering_and_empty_results_clear_old_bars()
    {
        TimelineBlock editor = new(Day, Day.AddHours(6), "Active", "Editor", "Code", ["Development"]);
        TimelineBlock idleEditor = editor with { Start = Day.AddHours(9), End = Day.AddHours(12), Status = "Inactive" };
        foreach (TimelineGrouping grouping in new[] { TimelineGrouping.Category, TimelineGrouping.Application })
        {
            Canvas canvas = Render([editor, idleEditor], grouping, 960);
            Color[] colors = canvas.Children.OfType<Border>().Select(GetFirstSegmentColor).ToArray();
            TimelineRenderer.Render(canvas, [idleEditor], Day, Day.AddDays(1), Ticks, grouping);
            Assert.That(GetFirstSegmentColor(canvas.Children.OfType<Border>().Single()), Is.EqualTo(colors[0]));
            Assert.That(colors[1], Is.EqualTo(colors[0]));

            TimelineRenderer.Render(canvas, [], Day, Day.AddDays(1), Ticks, grouping);
            Assert.That(canvas.Children.OfType<Border>(), Is.Empty);
            Assert.That(canvas.Children.OfType<Line>().Count(), Is.EqualTo(4));
        }
    }

    [Test]
    public void Range_boundaries_clip_every_track_and_preserve_gaps()
    {
        TimelineBlock[] blocks =
        [
            new(Day.AddHours(-2), Day.AddHours(6), "Active", "Editor", "Code", []),
            new(Day.AddHours(23), Day.AddHours(26), "Inactive", "Browser", "Docs", ["Research"]),
            new(Day.AddDays(2), Day.AddDays(3), "Active", "Editor", "Outside range", [])
        ];
        foreach (TimelineGrouping grouping in Enum.GetValues<TimelineGrouping>())
        {
            Canvas canvas = Render(blocks, grouping, 240);
            Border[] bars = canvas.Children.OfType<Border>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(bars, Has.Length.EqualTo(2));
                Assert.That(bars.Select(Canvas.GetLeft), Is.EqualTo(new[] { 0d, 230d }));
                Assert.That(bars.Select(bar => bar.Width), Is.EqualTo(new[] { 60d, 10d }));
            });
        }
    }

    [Test]
    public void Dashboard_layout_stacks_three_equal_tracks_in_the_requested_order()
    {
        var window = new MainWindow();
        try
        {
            var tabs = (TabControl)window.FindName("MainTabs");
            var dashboard = (Grid)((TabItem)tabs.Items[1]).Content;
            dashboard.Measure(new Size(1040, 600));
            dashboard.Arrange(new Rect(0, 0, 1040, 600));
            dashboard.UpdateLayout();
            Canvas[] canvases = new[] { "TimelineCanvas", "CategoryTimelineCanvas", "ApplicationTimelineCanvas" }
                .Select(name => (Canvas)window.FindName(name)).ToArray();
            Border[] borders = canvases.Select(canvas => (Border)canvas.Parent).ToArray();
            double[] tops = canvases.Select(canvas => canvas.TranslatePoint(new Point(0, 0), dashboard).Y).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(borders.Select(border => border.ActualHeight), Is.All.EqualTo(76));
                Assert.That(canvases[0].ActualWidth, Is.GreaterThan(0));
                Assert.That(canvases.Select(canvas => canvas.ActualWidth), Is.All.EqualTo(canvases[0].ActualWidth));
                Assert.That(tops[1], Is.GreaterThan(tops[0] + canvases[0].ActualHeight));
                Assert.That(tops[2], Is.GreaterThan(tops[1] + canvases[1].ActualHeight));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Selection_overlay_resizes_and_clears_without_replacing_activity_bars()
    {
        var block = new TimelineBlock(Day.AddHours(6), Day.AddHours(12), "Active", "Editor", "Code", ["Development"]);
        Canvas canvas = Render([block], TimelineGrouping.Activity, 240);
        Border originalBar = canvas.Children.OfType<Border>().Single();
        var selection = new TimelineSelection(Day.AddHours(6), Day.AddHours(12));
        TimelineRenderer.RenderSelection(canvas, Day, Day.AddDays(1), selection);
        Assert.That(Canvas.GetLeft(canvas.Children.OfType<Rectangle>().Single()), Is.EqualTo(60));

        canvas.Width = 960;
        canvas.Measure(new Size(960, canvas.Height));
        canvas.Arrange(new Rect(0, 0, 960, canvas.Height));
        TimelineRenderer.RenderSelection(canvas, Day, Day.AddDays(1), selection);
        Rectangle overlay = canvas.Children.OfType<Rectangle>().Single();
        Assert.That(Canvas.GetLeft(overlay), Is.EqualTo(240));
        Assert.That(overlay.Width, Is.EqualTo(240));
        Assert.That(canvas.Children.OfType<Border>().Single(), Is.SameAs(originalBar));

        TimelineRenderer.RenderSelection(canvas, Day, Day.AddDays(1), null);
        Assert.That(canvas.Children.OfType<Rectangle>(), Is.Empty);
        Assert.That(canvas.Children.OfType<Border>().Single(), Is.SameAs(originalBar));
    }

    private static Canvas Render(IReadOnlyList<TimelineBlock> blocks, TimelineGrouping grouping, double width)
    {
        var canvas = new Canvas { Width = width, Height = TimelineRenderer.RowHeight - 2 };
        canvas.Measure(new Size(width, canvas.Height));
        canvas.Arrange(new Rect(0, 0, width, canvas.Height));
        TimelineRenderer.Render(canvas, blocks, Day, Day.AddDays(1), Ticks, grouping);
        return canvas;
    }

    private static Color GetFirstSegmentColor(Border bar) =>
        ((SolidColorBrush)((Grid)bar.Child).Children.OfType<Border>().First().Background).Color;
}
