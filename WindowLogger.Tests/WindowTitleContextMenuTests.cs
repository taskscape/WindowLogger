using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WindowLoggerConfigGui;

namespace WindowLogger.Tests;

// Loading compiled XAML on an STA thread catches startup failures that a successful build cannot detect.
[Apartment(ApartmentState.STA)]
public sealed class WindowTitleContextMenuTests
{
    [Test]
    public void Window_loads_and_copy_menu_targets_the_complete_clicked_line_instead_of_the_selection()
    {
        var window = new MainWindow();
        try
        {
            string[] lines =
            [
                "     12m  Active    First title [editor.exe]",
                "   1h 5m  Inactive  Zażółć — " + new string('x', 400) + " [browser.exe]"
            ];
            var list = (ListBox)window.FindName("WindowTitleList");
            list.ItemsSource = lines;
            list.SelectedIndex = 0;

            // Generate real item containers without showing the window or starting its activity tracking lifecycle.
            list.Measure(new Size(220, 120));
            list.Arrange(new Rect(0, 0, 220, 120));
            list.UpdateLayout();

            // Reusing the menu for different rows must update its text, including content beyond the visible width.
            foreach (int index in new[] { 1, 0 })
            {
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);
                ContextMenu menu = row.ContextMenu;
                menu.PlacementTarget = row;
                // Complete deferred binding attachment just as WPF does when opening the popup.
                menu.ApplyTemplate();
                menu.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var copy = (MenuItem)menu.Items[0];
                copy.GetBindingExpression(MenuItem.CommandParameterProperty)!.UpdateTarget();

                Assert.Multiple(() =>
                {
                    Assert.That(copy.Header, Is.EqualTo("Copy text"));
                    Assert.That(copy.CommandParameter, Is.EqualTo(lines[index]));
                    Assert.That(list.SelectedIndex, Is.Zero);
                });
            }
        }
        finally
        {
            window.Close();
        }
    }
}
