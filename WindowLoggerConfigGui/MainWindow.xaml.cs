using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WindowLoggerConfigGui;

public partial class MainWindow : Window
{
    private const string DefaultRelativePath = @"..\..\..\..\WindowAnalyser\appsettings.json";

    private readonly string? _initialPath;
    private AppSettings _settings = new();
    private string? _currentPath;
    private bool _isDirty;

    public MainWindow(string? initialPath = null)
    {
        _initialPath = initialPath;
        InitializeComponent();
        Loaded += (_, _) => TryLoadDefaultConfig();
        Closing += MainWindow_Closing;
    }

    private void TryLoadDefaultConfig()
    {
        if (!string.IsNullOrWhiteSpace(_initialPath) && File.Exists(_initialPath))
        {
            LoadConfig(_initialPath);
            return;
        }

        string baseDirectory = AppContext.BaseDirectory;
        string localPath = Path.Combine(baseDirectory, "appsettings.json");
        if (File.Exists(localPath))
        {
            LoadConfig(localPath);
            return;
        }

        string developmentPath = Path.GetFullPath(Path.Combine(baseDirectory, DefaultRelativePath));
        if (File.Exists(developmentPath))
        {
            LoadConfig(developmentPath);
            return;
        }

        _settings = new AppSettings();
        _currentPath = null;
        RefreshAllLists();
        UpdatePathText();
        SetStatus("New configuration (no file loaded).");
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Open appsettings.json"
        };

        if (!string.IsNullOrWhiteSpace(_currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_currentPath);
        }

        if (dialog.ShowDialog(this) == true)
        {
            LoadConfig(dialog.FileName);
        }
    }

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath))
        {
            SaveConfigAs();
            return;
        }

        SaveConfigToPath(_currentPath);
    }

    private void SaveConfigAs_Click(object sender, RoutedEventArgs e) => SaveConfigAs();

    private void ReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentPath) || !ConfirmDiscardChanges())
        {
            return;
        }

        LoadConfig(_currentPath);
    }

    private void SaveConfigAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Save appsettings.json",
            FileName = "appsettings.json"
        };

        if (!string.IsNullOrWhiteSpace(_currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_currentPath);
        }

        if (dialog.ShowDialog(this) == true)
        {
            SaveConfigToPath(dialog.FileName);
        }
    }

    private void LoadConfig(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();
            NormalizeSettings(settings);

            _settings = settings;
            _currentPath = path;
            _isDirty = false;

            RefreshAllLists();
            UpdatePathText();
            SetStatus($"Loaded {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load configuration:\n{ex.Message}", "Load Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveConfigToPath(string path)
    {
        try
        {
            string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            _currentPath = path;
            _isDirty = false;
            UpdatePathText();
            SetStatus($"Saved {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to save configuration:\n{ex.Message}", "Save Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void NormalizeSettings(AppSettings settings)
    {
        settings.Applications ??= [];
        settings.Exclusions ??= [];
        settings.Categories ??= [];

        foreach (ApplicationDefinition application in settings.Applications)
        {
            application.Include ??= [];
            application.Exclude ??= [];
        }

        foreach (ExclusionDefinition exclusion in settings.Exclusions)
        {
            exclusion.Include ??= [];
        }

        foreach (CategoryDefinition category in settings.Categories)
        {
            category.IncludeApplications ??= [];
            category.ExcludeApplications ??= [];
        }
    }

    private void RefreshAllLists()
    {
        RefreshApplicationsList(-1);
        RefreshExclusionsList(-1);
        RefreshCategoriesList(-1);
    }

    private void RefreshApplicationsList(int selectedIndex)
    {
        ApplicationsList.ItemsSource = null;
        ApplicationsList.ItemsSource = _settings.Applications
            .Select(application => string.IsNullOrWhiteSpace(application.Name) ? "(unnamed)" : application.Name)
            .ToList();

        if (_settings.Applications.Count == 0)
        {
            ApplicationsList.SelectedIndex = -1;
            ClearApplicationFields();
            return;
        }

        ApplicationsList.SelectedIndex = NormalizeSelection(selectedIndex, _settings.Applications.Count);
    }

    private void ApplicationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = ApplicationsList.SelectedIndex;
        if (index < 0 || index >= _settings.Applications.Count)
        {
            ClearApplicationFields();
            return;
        }

        ApplicationDefinition application = _settings.Applications[index];
        AppNameText.Text = application.Name ?? string.Empty;
        AppIncludeText.Text = JoinLines(application.Include);
        AppExcludeText.Text = JoinLines(application.Exclude);
    }

    private void AddApplication_Click(object sender, RoutedEventArgs e)
    {
        string name = AppNameText.Text.Trim();
        List<string> include = ParseLines(AppIncludeText);
        if (!ValidateNamedRule(name, include, "Application name is required.", "Include keywords are required."))
        {
            return;
        }

        _settings.Applications.Add(new ApplicationDefinition
        {
            Name = name,
            Include = include,
            Exclude = ParseLines(AppExcludeText)
        });
        MarkDirty();
        RefreshApplicationsList(_settings.Applications.Count - 1);
    }

    private void UpdateApplication_Click(object sender, RoutedEventArgs e)
    {
        int index = ApplicationsList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        string name = AppNameText.Text.Trim();
        List<string> include = ParseLines(AppIncludeText);
        if (!ValidateNamedRule(name, include, "Application name is required.", "Include keywords are required."))
        {
            return;
        }

        ApplicationDefinition application = _settings.Applications[index];
        application.Name = name;
        application.Include = include;
        application.Exclude = ParseLines(AppExcludeText);
        MarkDirty();
        RefreshApplicationsList(index);
    }

    private void RemoveApplication_Click(object sender, RoutedEventArgs e)
    {
        int index = ApplicationsList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _settings.Applications.RemoveAt(index);
        MarkDirty();
        RefreshApplicationsList(Math.Max(0, index - 1));
    }

    private void MoveApplicationUp_Click(object sender, RoutedEventArgs e) => MoveApplication(-1);

    private void MoveApplicationDown_Click(object sender, RoutedEventArgs e) => MoveApplication(1);

    private void MoveApplication(int direction)
    {
        int index = ApplicationsList.SelectedIndex;
        int newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= _settings.Applications.Count)
        {
            return;
        }

        ApplicationDefinition application = _settings.Applications[index];
        _settings.Applications.RemoveAt(index);
        _settings.Applications.Insert(newIndex, application);
        MarkDirty();
        RefreshApplicationsList(newIndex);
    }

    private void ClearApplicationFields()
    {
        AppNameText.Clear();
        AppIncludeText.Clear();
        AppExcludeText.Clear();
    }

    private void RefreshExclusionsList(int selectedIndex)
    {
        ExclusionsList.ItemsSource = null;
        ExclusionsList.ItemsSource = _settings.Exclusions
            .Select(exclusion => exclusion.Include.Count > 0 ? string.Join(" & ", exclusion.Include) : "(empty)")
            .ToList();

        if (_settings.Exclusions.Count == 0)
        {
            ExclusionsList.SelectedIndex = -1;
            ExclusionIncludeText.Clear();
            return;
        }

        ExclusionsList.SelectedIndex = NormalizeSelection(selectedIndex, _settings.Exclusions.Count);
    }

    private void ExclusionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = ExclusionsList.SelectedIndex;
        ExclusionIncludeText.Text = index >= 0 && index < _settings.Exclusions.Count
            ? JoinLines(_settings.Exclusions[index].Include)
            : string.Empty;
    }

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        List<string> include = ParseLines(ExclusionIncludeText);
        if (include.Count == 0)
        {
            ShowWarning("Include keywords are required.");
            return;
        }

        _settings.Exclusions.Add(new ExclusionDefinition { Include = include });
        MarkDirty();
        RefreshExclusionsList(_settings.Exclusions.Count - 1);
    }

    private void UpdateExclusion_Click(object sender, RoutedEventArgs e)
    {
        int index = ExclusionsList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        List<string> include = ParseLines(ExclusionIncludeText);
        if (include.Count == 0)
        {
            ShowWarning("Include keywords are required.");
            return;
        }

        _settings.Exclusions[index].Include = include;
        MarkDirty();
        RefreshExclusionsList(index);
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        int index = ExclusionsList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _settings.Exclusions.RemoveAt(index);
        MarkDirty();
        RefreshExclusionsList(Math.Max(0, index - 1));
    }

    private void RefreshCategoriesList(int selectedIndex)
    {
        CategoriesList.ItemsSource = null;
        CategoriesList.ItemsSource = _settings.Categories
            .Select(category => string.IsNullOrWhiteSpace(category.Name) ? "(unnamed)" : category.Name)
            .ToList();

        if (_settings.Categories.Count == 0)
        {
            CategoriesList.SelectedIndex = -1;
            ClearCategoryFields();
            return;
        }

        CategoriesList.SelectedIndex = NormalizeSelection(selectedIndex, _settings.Categories.Count);
    }

    private void CategoriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = CategoriesList.SelectedIndex;
        if (index < 0 || index >= _settings.Categories.Count)
        {
            ClearCategoryFields();
            return;
        }

        CategoryDefinition category = _settings.Categories[index];
        CategoryNameText.Text = category.Name ?? string.Empty;
        CategoryIncludeText.Text = JoinLines(category.IncludeApplications);
        CategoryExcludeText.Text = JoinLines(category.ExcludeApplications);
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        string name = CategoryNameText.Text.Trim();
        List<string> include = ParseLines(CategoryIncludeText);
        if (!ValidateNamedRule(name, include, "Category name is required.", "Include applications are required."))
        {
            return;
        }

        _settings.Categories.Add(new CategoryDefinition
        {
            Name = name,
            IncludeApplications = include,
            ExcludeApplications = ParseLines(CategoryExcludeText)
        });
        MarkDirty();
        RefreshCategoriesList(_settings.Categories.Count - 1);
    }

    private void UpdateCategory_Click(object sender, RoutedEventArgs e)
    {
        int index = CategoriesList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        string name = CategoryNameText.Text.Trim();
        List<string> include = ParseLines(CategoryIncludeText);
        if (!ValidateNamedRule(name, include, "Category name is required.", "Include applications are required."))
        {
            return;
        }

        CategoryDefinition category = _settings.Categories[index];
        category.Name = name;
        category.IncludeApplications = include;
        category.ExcludeApplications = ParseLines(CategoryExcludeText);
        MarkDirty();
        RefreshCategoriesList(index);
    }

    private void RemoveCategory_Click(object sender, RoutedEventArgs e)
    {
        int index = CategoriesList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _settings.Categories.RemoveAt(index);
        MarkDirty();
        RefreshCategoriesList(Math.Max(0, index - 1));
    }

    private void ClearCategoryFields()
    {
        CategoryNameText.Clear();
        CategoryIncludeText.Clear();
        CategoryExcludeText.Clear();
    }

    private static int NormalizeSelection(int selectedIndex, int count) =>
        selectedIndex < 0 || selectedIndex >= count ? 0 : selectedIndex;

    private static List<string> ParseLines(TextBox textBox) =>
        textBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    private static string JoinLines(IEnumerable<string> values) => string.Join(Environment.NewLine, values);

    private bool ValidateNamedRule(string name, List<string> include, string missingNameMessage, string missingIncludeMessage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowWarning(missingNameMessage);
            return false;
        }

        if (include.Count == 0)
        {
            ShowWarning(missingIncludeMessage);
            return false;
        }

        return true;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        SetStatus("Unsaved changes");
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void UpdatePathText() => ConfigPathTextBox.Text = _currentPath ?? "(not saved yet)";

    private void ShowWarning(string message) =>
        MessageBox.Show(this, message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);

    private bool ConfirmDiscardChanges()
    {
        if (!_isDirty)
        {
            return true;
        }

        MessageBoxResult result = MessageBox.Show(this, "You have unsaved changes. Discard them?",
            "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
        }
    }
}
