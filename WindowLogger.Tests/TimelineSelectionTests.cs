using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using WindowLogger.Shared.Activity;
using WindowLogger.Shared.Configuration;
using WindowLoggerConfigGui;

namespace WindowLogger.Tests;

[Apartment(ApartmentState.STA)]
public sealed class TimelineSelectionTests
{
    private static readonly DateTime Day = new(2026, 11, 2);
    private static readonly string[] CanvasNames = ["TimelineCanvas", "CategoryTimelineCanvas", "ApplicationTimelineCanvas"];

    [TestCase(60, 180, 3, 9)]
    [TestCase(180, 60, 3, 9)]
    [TestCase(-80, 600, 0, 24)]
    [TestCase(600, -80, 0, 24)]
    public void Drag_normalizes_direction_and_clamps_to_the_visible_period(double anchor, double position, int startHour, int endHour)
    {
        TimelineSelection? selection = TimelineSelection.FromPositions(anchor, position, 480, Day, Day.AddDays(1));
        Assert.That(selection, Is.EqualTo(new TimelineSelection(Day.AddHours(startHour), Day.AddHours(endHour))));
    }

    [TestCase(1)]
    [TestCase(7)]
    [TestCase(31)]
    public void Drag_uses_the_whole_day_week_or_month_scale(int days)
    {
        TimelineSelection? selection = TimelineSelection.FromPositions(240, 720, 960, Day, Day.AddDays(days));
        Assert.That(selection, Is.EqualTo(new TimelineSelection(Day.AddDays(days * 0.25), Day.AddDays(days * 0.75))));
    }

    [Test]
    public void Clicks_tiny_drags_and_unlaid_out_canvases_do_not_create_a_selection()
    {
        Assert.That(TimelineSelection.FromPositions(40, 40, 480, Day, Day.AddDays(1)), Is.Null);
        Assert.That(TimelineSelection.FromPositions(40, 42, 480, Day, Day.AddDays(1)), Is.Null);
        Assert.That(TimelineSelection.FromPositions(0, 480, 0, Day, Day.AddDays(1)), Is.Null);
        Assert.That(TimelineSelection.FromPositions(0, 480, 480, Day, Day), Is.Null);
    }

    [TestCase("TimelineCanvas")]
    [TestCase("CategoryTimelineCanvas")]
    [TestCase("ApplicationTimelineCanvas")]
    public void Drag_on_any_track_updates_all_overlays_lists_and_clipped_totals_and_can_be_cleared(string sourceName)
    {
        var window = CreateDashboard();
        try
        {
            var source = (Canvas)window.FindName(sourceName);
            Set(window, "_timelineDragCanvas", source);
            Set(window, "_timelineDragAnchor", source.ActualWidth * 9 / 24);
            Invoke(window, "UpdateTimelineSelection", source.ActualWidth * 7 / 24);
            Invoke(window, "EndTimelineDrag");

            foreach (string name in CanvasNames)
            {
                var canvas = (Canvas)window.FindName(name);
                Rectangle overlay = canvas.Children.OfType<Rectangle>().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(Canvas.GetLeft(overlay), Is.EqualTo(canvas.ActualWidth * 7 / 24).Within(0.001));
                    Assert.That(overlay.Width, Is.EqualTo(canvas.ActualWidth * 2 / 24).Within(0.001));
                    Assert.That(overlay.Height, Is.EqualTo(canvas.ActualHeight));
                    Assert.That(overlay.IsHitTestVisible, Is.False);
                    Assert.That(canvas.Children.OfType<Border>().Count(), Is.EqualTo(5), "The full timeline stays visible for context.");
                });
            }
            Assert.Multiple(() =>
            {
                Assert.That(BarNames(window, "CategoryChartList"), Is.EquivalentTo(new[] { "Development", "Research" }));
                Assert.That(BarNames(window, "ApplicationChartList"), Is.EquivalentTo(new[] { "Editor", "Browser" }));
                Assert.That(((ListBox)window.FindName("WindowTitleList")).Items.Count, Is.EqualTo(2));
                Assert.That(((TextBlock)window.FindName("AnalyticsActiveText")).Text, Is.EqualTo("2h 0m"));
                Assert.That(((TextBlock)window.FindName("TimelineSelectionText")).Text, Does.Contain("07:00:00").And.Contain("09:00:00"));
            });

            // A refresh keeps the same selection and recreates its overlays.
            Invoke(window, "RenderAnalytics");
            Assert.That(((Canvas)window.FindName(sourceName)).Children.OfType<Rectangle>().Count(), Is.EqualTo(1));

            var clear = (Button)window.FindName("ClearTimelineSelectionButton");
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Multiple(() =>
            {
                Assert.That(clear.IsEnabled, Is.False);
                Assert.That(BarNames(window, "CategoryChartList"), Has.Length.EqualTo(3));
                Assert.That(BarNames(window, "ApplicationChartList"), Has.Length.EqualTo(3));
                Assert.That(((ListBox)window.FindName("WindowTitleList")).Items.Count, Is.EqualTo(4));
                Assert.That(((TextBlock)window.FindName("AnalyticsActiveText")).Text, Is.EqualTo("6h 0m"));
                Assert.That(CanvasNames.SelectMany(name => ((Canvas)window.FindName(name)).Children.OfType<Rectangle>()), Is.Empty);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void An_empty_selection_clears_lists_and_lost_capture_restores_the_committed_view()
    {
        var window = CreateDashboard();
        try
        {
            var canvas = (Canvas)window.FindName("TimelineCanvas");
            var committed = new TimelineSelection(Day.AddHours(7), Day.AddHours(9));
            Set(window, "_timelineSelection", committed);
            Set(window, "_selectionBeforeDrag", committed);
            Set(window, "_timelineDragCanvas", canvas);
            Set(window, "_timelineDragAnchor", canvas.ActualWidth * 18 / 24);
            Invoke(window, "UpdateTimelineSelection", canvas.ActualWidth * 19 / 24);
            Assert.That(BarNames(window, "CategoryChartList"), Is.Empty);
            Assert.That(BarNames(window, "ApplicationChartList"), Is.Empty);
            Assert.That(((ListBox)window.FindName("WindowTitleList")).Items, Is.Empty);

            Invoke(window, "TimelineCanvas_LostMouseCapture", canvas, new MouseEventArgs(Mouse.PrimaryDevice, 0));
            Assert.That(BarNames(window, "CategoryChartList"), Has.Length.EqualTo(2));
            Assert.That(((TextBlock)window.FindName("AnalyticsActiveText")).Text, Is.EqualTo("2h 0m"));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Clearing_the_time_selection_preserves_search_and_status_filters()
    {
        var window = CreateDashboard(search: "Docs", status: "Active");
        try
        {
            Set(window, "_timelineSelection", new TimelineSelection(Day.AddHours(9), Day.AddHours(9.5)));
            Invoke(window, "RenderAnalytics");
            Assert.That(((TextBlock)window.FindName("AnalyticsActiveText")).Text, Is.EqualTo("30m"));
            ((Button)window.FindName("ClearTimelineSelectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Multiple(() =>
            {
                Assert.That(((TextBox)window.FindName("AnalyticsSearchText")).Text, Is.EqualTo("Docs"));
                Assert.That(((ComboBox)window.FindName("AnalyticsStatusFilter")).SelectedIndex, Is.EqualTo(1));
                Assert.That(BarNames(window, "ApplicationChartList"), Is.EqualTo(new[] { "Browser" }));
                Assert.That(((ListBox)window.FindName("WindowTitleList")).Items.Count, Is.EqualTo(1));
                Assert.That(((TextBlock)window.FindName("AnalyticsActiveText")).Text, Is.EqualTo("2h 0m"));
                Assert.That(((TextBlock)window.FindName("AnalyticsIdleText")).Text, Is.EqualTo("0m"));
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindow CreateDashboard(string? search = null, string? status = null)
    {
        var window = new MainWindow();
        var settings = new AppSettings
        {
            Applications =
            [
                new() { Name = "Editor", Include = ["code.exe"] },
                new() { Name = "Browser", Include = ["browser.exe"] },
                new() { Name = "Teams", Include = ["teams.exe"] }
            ],
            Categories =
            [
                new() { Name = "Development", IncludeApplications = ["Editor"] },
                new() { Name = "Research", IncludeApplications = ["Browser"] },
                new() { Name = "Communication", IncludeApplications = ["Teams"] }
            ]
        };
        ActivityRecord[] current =
        [
            ActivityRecord.Create(Day.AddHours(6), "Code [code.exe]", "Active"),
            ActivityRecord.Create(Day.AddHours(8), "Docs [browser.exe]", "Active"),
            ActivityRecord.Create(Day.AddHours(10), "Docs [browser.exe]", "Inactive"),
            ActivityRecord.Create(Day.AddHours(11), "Code [code.exe]", "Active"),
            ActivityRecord.Create(Day.AddHours(12), "", "SessionEnd"),
            ActivityRecord.Create(Day.AddHours(16), "Meeting [teams.exe]", "Active"),
            ActivityRecord.Create(Day.AddHours(17), "", "SessionEnd")
        ];
        ((TextBox)window.FindName("AnalyticsSearchText")).Text = search ?? string.Empty;
        ((ComboBox)window.FindName("AnalyticsStatusFilter")).SelectedIndex = status == "Active" ? 1 : status == "Inactive" ? 2 : 0;
        DashboardSnapshot snapshot = DashboardAnalytics.Build(current, [], settings, Day, Day.AddDays(1), Day.AddDays(-1), Day, search, status);
        Set(window, "_dashboardSnapshot", snapshot);
        Set(window, "_dashboardRangeStart", Day);
        Set(window, "_dashboardPeriodEnd", Day.AddDays(1));
        Set(window, "_previousRangeStart", Day.AddDays(-1));
        var tabs = (TabControl)window.FindName("MainTabs");
        var dashboard = (Grid)((TabItem)tabs.Items[1]).Content;
        dashboard.Measure(new Size(1040, 600));
        dashboard.Arrange(new Rect(0, 0, 1040, 600));
        Invoke(window, "RenderAnalytics");
        dashboard.UpdateLayout();
        return window;
    }

    private static string[] BarNames(MainWindow window, string name) =>
        ((ListBox)window.FindName(name)).Items.Cast<DashboardBarItem>().Select(item => item.Name).ToArray();

    private static void Set(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static void Invoke(MainWindow window, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
}
