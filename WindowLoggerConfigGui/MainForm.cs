using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace WindowLoggerConfigGui
{
    public partial class MainForm : Form
    {
        private readonly TextBox _configPathTextBox;
        private readonly Button _openButton;
        private readonly Button _saveButton;
        private readonly Button _saveAsButton;
        private readonly Button _reloadButton;

        private readonly ListBox _applicationsList;
        private readonly TextBox _appNameText;
        private readonly TextBox _appIncludeText;
        private readonly TextBox _appExcludeText;
        private readonly Button _appAddButton;
        private readonly Button _appUpdateButton;
        private readonly Button _appRemoveButton;
        private readonly Button _appMoveUpButton;
        private readonly Button _appMoveDownButton;

        private readonly ListBox _exclusionsList;
        private readonly TextBox _exclusionIncludeText;
        private readonly Button _exclusionAddButton;
        private readonly Button _exclusionUpdateButton;
        private readonly Button _exclusionRemoveButton;

        private readonly ListBox _categoriesList;
        private readonly TextBox _categoryNameText;
        private readonly TextBox _categoryIncludeText;
        private readonly TextBox _categoryExcludeText;
        private readonly Button _categoryAddButton;
        private readonly Button _categoryUpdateButton;
        private readonly Button _categoryRemoveButton;

        private readonly ToolStripStatusLabel _statusLabel;

        private AppSettings _settings = new AppSettings();
        private string? _currentPath;
        private bool _isDirty;

        private const string DefaultRelativePath = @"..\..\..\WindowAnalyser\appsettings.json";

        public MainForm()
        {
            Text = "Window Logger Configuration";
            MinimumSize = new Size(980, 640);
            StartPosition = FormStartPosition.CenterScreen;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(mainLayout);

            var filePanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(10, 10, 10, 5)
            };
            var fileLabel = new Label
            {
                Text = "Config file:",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 6, 0, 0)
            };
            _configPathTextBox = new TextBox
            {
                Width = 520,
                ReadOnly = true
            };
            _openButton = new Button { Text = "Open..." };
            _saveButton = new Button { Text = "Save" };
            _saveAsButton = new Button { Text = "Save As..." };
            _reloadButton = new Button { Text = "Reload" };

            _openButton.Click += (_, __) => OpenConfig();
            _saveButton.Click += (_, __) => SaveConfig();
            _saveAsButton.Click += (_, __) => SaveConfigAs();
            _reloadButton.Click += (_, __) => ReloadConfig();

            filePanel.Controls.Add(fileLabel);
            filePanel.Controls.Add(_configPathTextBox);
            filePanel.Controls.Add(_openButton);
            filePanel.Controls.Add(_saveButton);
            filePanel.Controls.Add(_saveAsButton);
            filePanel.Controls.Add(_reloadButton);
            mainLayout.Controls.Add(filePanel, 0, 0);

            var tabControl = new TabControl { Dock = DockStyle.Fill };
            tabControl.TabPages.Add(CreateApplicationsTab());
            tabControl.TabPages.Add(CreateExclusionsTab());
            tabControl.TabPages.Add(CreateCategoriesTab());
            mainLayout.Controls.Add(tabControl, 0, 1);

            var statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel { Text = "Ready" };
            statusStrip.Items.Add(_statusLabel);
            mainLayout.Controls.Add(statusStrip, 0, 2);

            _applicationsList = (ListBox)tabControl.TabPages[0].Tag!;
            _appNameText = (TextBox)tabControl.TabPages[0].Controls.Find("AppNameText", true).First();
            _appIncludeText = (TextBox)tabControl.TabPages[0].Controls.Find("AppIncludeText", true).First();
            _appExcludeText = (TextBox)tabControl.TabPages[0].Controls.Find("AppExcludeText", true).First();
            _appAddButton = (Button)tabControl.TabPages[0].Controls.Find("AppAddButton", true).First();
            _appUpdateButton = (Button)tabControl.TabPages[0].Controls.Find("AppUpdateButton", true).First();
            _appRemoveButton = (Button)tabControl.TabPages[0].Controls.Find("AppRemoveButton", true).First();
            _appMoveUpButton = (Button)tabControl.TabPages[0].Controls.Find("AppMoveUpButton", true).First();
            _appMoveDownButton = (Button)tabControl.TabPages[0].Controls.Find("AppMoveDownButton", true).First();

            _exclusionsList = (ListBox)tabControl.TabPages[1].Tag!;
            _exclusionIncludeText = (TextBox)tabControl.TabPages[1].Controls.Find("ExclusionIncludeText", true).First();
            _exclusionAddButton = (Button)tabControl.TabPages[1].Controls.Find("ExclusionAddButton", true).First();
            _exclusionUpdateButton = (Button)tabControl.TabPages[1].Controls.Find("ExclusionUpdateButton", true).First();
            _exclusionRemoveButton = (Button)tabControl.TabPages[1].Controls.Find("ExclusionRemoveButton", true).First();

            _categoriesList = (ListBox)tabControl.TabPages[2].Tag!;
            _categoryNameText = (TextBox)tabControl.TabPages[2].Controls.Find("CategoryNameText", true).First();
            _categoryIncludeText = (TextBox)tabControl.TabPages[2].Controls.Find("CategoryIncludeText", true).First();
            _categoryExcludeText = (TextBox)tabControl.TabPages[2].Controls.Find("CategoryExcludeText", true).First();
            _categoryAddButton = (Button)tabControl.TabPages[2].Controls.Find("CategoryAddButton", true).First();
            _categoryUpdateButton = (Button)tabControl.TabPages[2].Controls.Find("CategoryUpdateButton", true).First();
            _categoryRemoveButton = (Button)tabControl.TabPages[2].Controls.Find("CategoryRemoveButton", true).First();
        }

        private TabPage CreateApplicationsTab()
        {
            var tab = new TabPage("Applications");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            tab.Controls.Add(layout);

            var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var list = new ListBox { Dock = DockStyle.Fill };
            leftPanel.Controls.Add(list, 0, 0);

            var movePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var moveUp = new Button { Name = "AppMoveUpButton", Text = "Move Up" };
            var moveDown = new Button { Name = "AppMoveDownButton", Text = "Move Down" };
            movePanel.Controls.Add(moveUp);
            movePanel.Controls.Add(moveDown);
            leftPanel.Controls.Add(movePanel, 0, 1);
            layout.Controls.Add(leftPanel, 0, 0);

            var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            details.Controls.Add(new Label { Text = "Name", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
            var nameText = new TextBox { Name = "AppNameText", Dock = DockStyle.Fill };
            details.Controls.Add(nameText, 1, 0);

            details.Controls.Add(new Label { Text = "Include (one per line)", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
            var includeText = CreateMultilineTextBox("AppIncludeText");
            details.Controls.Add(includeText, 1, 1);

            details.Controls.Add(new Label { Text = "Exclude (one per line)", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 2);
            var excludeText = CreateMultilineTextBox("AppExcludeText");
            details.Controls.Add(excludeText, 1, 2);

            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var add = new Button { Name = "AppAddButton", Text = "Add New" };
            var update = new Button { Name = "AppUpdateButton", Text = "Update Selected" };
            var remove = new Button { Name = "AppRemoveButton", Text = "Remove Selected" };
            buttonPanel.Controls.Add(add);
            buttonPanel.Controls.Add(update);
            buttonPanel.Controls.Add(remove);
            details.Controls.Add(buttonPanel, 1, 3);

            layout.Controls.Add(details, 1, 0);
            tab.Tag = list;
            return tab;
        }

        private TabPage CreateExclusionsTab()
        {
            var tab = new TabPage("Exclusions");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            tab.Controls.Add(layout);

            var list = new ListBox { Dock = DockStyle.Fill };
            layout.Controls.Add(list, 0, 0);

            var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            details.Controls.Add(new Label { Text = "Include (one per line)", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
            var includeText = CreateMultilineTextBox("ExclusionIncludeText");
            details.Controls.Add(includeText, 1, 0);

            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var add = new Button { Name = "ExclusionAddButton", Text = "Add New" };
            var update = new Button { Name = "ExclusionUpdateButton", Text = "Update Selected" };
            var remove = new Button { Name = "ExclusionRemoveButton", Text = "Remove Selected" };
            buttonPanel.Controls.Add(add);
            buttonPanel.Controls.Add(update);
            buttonPanel.Controls.Add(remove);
            details.Controls.Add(buttonPanel, 1, 1);

            layout.Controls.Add(details, 1, 0);
            tab.Tag = list;
            return tab;
        }

        private TabPage CreateCategoriesTab()
        {
            var tab = new TabPage("Categories");
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            tab.Controls.Add(layout);

            var list = new ListBox { Dock = DockStyle.Fill };
            layout.Controls.Add(list, 0, 0);

            var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            details.Controls.Add(new Label { Text = "Name", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
            var nameText = new TextBox { Name = "CategoryNameText", Dock = DockStyle.Fill };
            details.Controls.Add(nameText, 1, 0);

            details.Controls.Add(new Label { Text = "Include applications", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
            var includeText = CreateMultilineTextBox("CategoryIncludeText");
            details.Controls.Add(includeText, 1, 1);

            details.Controls.Add(new Label { Text = "Exclude applications", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 2);
            var excludeText = CreateMultilineTextBox("CategoryExcludeText");
            details.Controls.Add(excludeText, 1, 2);

            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var add = new Button { Name = "CategoryAddButton", Text = "Add New" };
            var update = new Button { Name = "CategoryUpdateButton", Text = "Update Selected" };
            var remove = new Button { Name = "CategoryRemoveButton", Text = "Remove Selected" };
            buttonPanel.Controls.Add(add);
            buttonPanel.Controls.Add(update);
            buttonPanel.Controls.Add(remove);
            details.Controls.Add(buttonPanel, 1, 3);

            layout.Controls.Add(details, 1, 0);
            tab.Tag = list;
            return tab;
        }

        private static TextBox CreateMultilineTextBox(string name)
        {
            return new TextBox
            {
                Name = name,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Height = 120,
                Dock = DockStyle.Fill
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            _applicationsList.SelectedIndexChanged += (_, __) => LoadSelectedApplication();
            _appAddButton.Click += (_, __) => AddApplication();
            _appUpdateButton.Click += (_, __) => UpdateApplication();
            _appRemoveButton.Click += (_, __) => RemoveApplication();
            _appMoveUpButton.Click += (_, __) => MoveApplication(-1);
            _appMoveDownButton.Click += (_, __) => MoveApplication(1);

            _exclusionsList.SelectedIndexChanged += (_, __) => LoadSelectedExclusion();
            _exclusionAddButton.Click += (_, __) => AddExclusion();
            _exclusionUpdateButton.Click += (_, __) => UpdateExclusion();
            _exclusionRemoveButton.Click += (_, __) => RemoveExclusion();

            _categoriesList.SelectedIndexChanged += (_, __) => LoadSelectedCategory();
            _categoryAddButton.Click += (_, __) => AddCategory();
            _categoryUpdateButton.Click += (_, __) => UpdateCategory();
            _categoryRemoveButton.Click += (_, __) => RemoveCategory();

            TryLoadDefaultConfig();
        }

        private void TryLoadDefaultConfig()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string localPath = Path.Combine(baseDir, "appsettings.json");
            if (File.Exists(localPath))
            {
                LoadConfig(localPath);
                return;
            }

            string candidate = Path.GetFullPath(Path.Combine(baseDir, DefaultRelativePath));
            if (File.Exists(candidate))
            {
                LoadConfig(candidate);
                return;
            }

            _settings = new AppSettings();
            _currentPath = null;
            RefreshAllLists();
            UpdatePathText();
            SetStatus("New configuration (no file loaded).");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmDiscardChanges())
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        private void OpenConfig()
        {
            if (!ConfirmDiscardChanges()) return;

            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                dialog.Title = "Open appsettings.json";
                if (!string.IsNullOrWhiteSpace(_currentPath))
                    dialog.InitialDirectory = Path.GetDirectoryName(_currentPath);

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadConfig(dialog.FileName);
            }
        }

        private void ReloadConfig()
        {
            if (string.IsNullOrWhiteSpace(_currentPath)) return;
            if (!ConfirmDiscardChanges()) return;
            LoadConfig(_currentPath);
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

                var settings = JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();
                NormalizeSettings(settings);

                _settings = settings;
                _currentPath = path;
                _isDirty = false;

                RefreshAllLists();
                UpdatePathText();
                SetStatus("Loaded " + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to load configuration:\n" + ex.Message, "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveConfig()
        {
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                SaveConfigAs();
                return;
            }
            SaveConfigToPath(_currentPath);
        }

        private void SaveConfigAs()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                dialog.Title = "Save appsettings.json";
                dialog.FileName = "appsettings.json";
                if (!string.IsNullOrWhiteSpace(_currentPath))
                    dialog.InitialDirectory = Path.GetDirectoryName(_currentPath);

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SaveConfigToPath(dialog.FileName);
            }
        }

        private void SaveConfigToPath(string path)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(path, json, new UTF8Encoding(false));

                _currentPath = path;
                _isDirty = false;
                UpdatePathText();
                SetStatus("Saved " + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to save configuration:\n" + ex.Message, "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void NormalizeSettings(AppSettings settings)
        {
            settings.Applications = settings.Applications ?? new List<ApplicationDefinition>();
            settings.Exclusions = settings.Exclusions ?? new List<ExclusionDefinition>();
            settings.Categories = settings.Categories ?? new List<CategoryDefinition>();
        }

        private void RefreshAllLists()
        {
            RefreshApplicationsList(-1);
            RefreshExclusionsList(-1);
            RefreshCategoriesList(-1);
        }

        private void RefreshApplicationsList(int selectedIndex)
        {
            _applicationsList.Items.Clear();
            foreach (var app in _settings.Applications)
            {
                string name = string.IsNullOrWhiteSpace(app.Name) ? "(unnamed)" : app.Name;
                _applicationsList.Items.Add(name);
            }

            if (_settings.Applications.Count > 0)
            {
                if (selectedIndex < 0 || selectedIndex >= _settings.Applications.Count) selectedIndex = 0;
                _applicationsList.SelectedIndex = selectedIndex;
            }
            else
            {
                _applicationsList.SelectedIndex = -1;
                ClearApplicationFields();
            }
        }

        private void LoadSelectedApplication()
        {
            int index = _applicationsList.SelectedIndex;
            if (index < 0 || index >= _settings.Applications.Count)
            {
                ClearApplicationFields();
                return;
            }
            var app = _settings.Applications[index];
            _appNameText.Text = app.Name ?? string.Empty;
            _appIncludeText.Lines = (app.Include ?? new List<string>()).ToArray();
            _appExcludeText.Lines = (app.Exclude ?? new List<string>()).ToArray();
        }

        private void AddApplication()
        {
            string name = _appNameText.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowWarning("Application name is required.");
                return;
            }
            var include = ParseLines(_appIncludeText);
            if (include.Count == 0)
            {
                ShowWarning("Include keywords are required.");
                return;
            }

            _settings.Applications.Add(new ApplicationDefinition
            {
                Name = name,
                Include = include,
                Exclude = ParseLines(_appExcludeText)
            });
            MarkDirty();
            RefreshApplicationsList(_settings.Applications.Count - 1);
        }

        private void UpdateApplication()
        {
            int index = _applicationsList.SelectedIndex;
            if (index < 0) return;

            string name = _appNameText.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowWarning("Application name is required.");
                return;
            }
            var include = ParseLines(_appIncludeText);
            if (include.Count == 0)
            {
                ShowWarning("Include keywords are required.");
                return;
            }

            var app = _settings.Applications[index];
            app.Name = name;
            app.Include = include;
            app.Exclude = ParseLines(_appExcludeText);
            MarkDirty();
            RefreshApplicationsList(index);
        }

        private void RemoveApplication()
        {
            int index = _applicationsList.SelectedIndex;
            if (index < 0) return;
            _settings.Applications.RemoveAt(index);
            MarkDirty();
            RefreshApplicationsList(Math.Max(0, index - 1));
        }

        private void MoveApplication(int direction)
        {
            int index = _applicationsList.SelectedIndex;
            if (index < 0) return;
            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= _settings.Applications.Count) return;

            var item = _settings.Applications[index];
            _settings.Applications.RemoveAt(index);
            _settings.Applications.Insert(newIndex, item);
            MarkDirty();
            RefreshApplicationsList(newIndex);
        }

        private void ClearApplicationFields()
        {
            _appNameText.Text = string.Empty;
            _appIncludeText.Text = string.Empty;
            _appExcludeText.Text = string.Empty;
        }

        private void RefreshExclusionsList(int selectedIndex)
        {
            _exclusionsList.Items.Clear();
            foreach (var ex in _settings.Exclusions)
            {
                string label = (ex.Include != null && ex.Include.Count > 0)
                    ? string.Join(" & ", ex.Include)
                    : "(empty)";
                _exclusionsList.Items.Add(label);
            }

            if (_settings.Exclusions.Count > 0)
            {
                if (selectedIndex < 0 || selectedIndex >= _settings.Exclusions.Count) selectedIndex = 0;
                _exclusionsList.SelectedIndex = selectedIndex;
            }
            else
            {
                _exclusionsList.SelectedIndex = -1;
                ClearExclusionFields();
            }
        }

        private void LoadSelectedExclusion()
        {
            int index = _exclusionsList.SelectedIndex;
            if (index < 0) return;
            var ex = _settings.Exclusions[index];
            _exclusionIncludeText.Lines = (ex.Include ?? new List<string>()).ToArray();
        }

        private void AddExclusion()
        {
            var include = ParseLines(_exclusionIncludeText);
            if (include.Count == 0)
            {
                ShowWarning("Include keywords are required.");
                return;
            }
            _settings.Exclusions.Add(new ExclusionDefinition { Include = include });
            MarkDirty();
            RefreshExclusionsList(_settings.Exclusions.Count - 1);
        }

        private void UpdateExclusion()
        {
            int index = _exclusionsList.SelectedIndex;
            if (index < 0) return;
            var include = ParseLines(_exclusionIncludeText);
            if (include.Count == 0)
            {
                ShowWarning("Include keywords are required.");
                return;
            }
            _settings.Exclusions[index].Include = include;
            MarkDirty();
            RefreshExclusionsList(index);
        }

        private void RemoveExclusion()
        {
            int index = _exclusionsList.SelectedIndex;
            if (index < 0) return;
            _settings.Exclusions.RemoveAt(index);
            MarkDirty();
            RefreshExclusionsList(Math.Max(0, index - 1));
        }

        private void ClearExclusionFields()
        {
            _exclusionIncludeText.Text = string.Empty;
        }

        private void RefreshCategoriesList(int selectedIndex)
        {
            _categoriesList.Items.Clear();
            foreach (var cat in _settings.Categories)
            {
                _categoriesList.Items.Add(cat.Name ?? "(unnamed)");
            }

            if (_settings.Categories.Count > 0)
            {
                if (selectedIndex < 0 || selectedIndex >= _settings.Categories.Count) selectedIndex = 0;
                _categoriesList.SelectedIndex = selectedIndex;
            }
            else
            {
                _categoriesList.SelectedIndex = -1;
                ClearCategoryFields();
            }
        }

        private void LoadSelectedCategory()
        {
            int index = _categoriesList.SelectedIndex;
            if (index < 0) return;
            var cat = _settings.Categories[index];
            _categoryNameText.Text = cat.Name ?? string.Empty;
            _categoryIncludeText.Lines = (cat.IncludeApplications ?? new List<string>()).ToArray();
            _categoryExcludeText.Lines = (cat.ExcludeApplications ?? new List<string>()).ToArray();
        }

        private void AddCategory()
        {
            string name = _categoryNameText.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowWarning("Category name is required.");
                return;
            }
            var include = ParseLines(_categoryIncludeText);
            if (include.Count == 0)
            {
                ShowWarning("Include applications are required.");
                return;
            }

            _settings.Categories.Add(new CategoryDefinition
            {
                Name = name,
                IncludeApplications = include,
                ExcludeApplications = ParseLines(_categoryExcludeText)
            });
            MarkDirty();
            RefreshCategoriesList(_settings.Categories.Count - 1);
        }

        private void UpdateCategory()
        {
            int index = _categoriesList.SelectedIndex;
            if (index < 0) return;
            string name = _categoryNameText.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowWarning("Category name is required.");
                return;
            }
            var include = ParseLines(_categoryIncludeText);
            if (include.Count == 0)
            {
                ShowWarning("Include applications are required.");
                return;
            }

            var cat = _settings.Categories[index];
            cat.Name = name;
            cat.IncludeApplications = include;
            cat.ExcludeApplications = ParseLines(_categoryExcludeText);
            MarkDirty();
            RefreshCategoriesList(index);
        }

        private void RemoveCategory()
        {
            int index = _categoriesList.SelectedIndex;
            if (index < 0) return;
            _settings.Categories.RemoveAt(index);
            MarkDirty();
            RefreshCategoriesList(Math.Max(0, index - 1));
        }

        private void ClearCategoryFields()
        {
            _categoryNameText.Text = string.Empty;
            _categoryIncludeText.Text = string.Empty;
            _categoryExcludeText.Text = string.Empty;
        }

        private static List<string> ParseLines(TextBox textBox)
        {
            return textBox.Lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }

        private void MarkDirty()
        {
            _isDirty = true;
            SetStatus("Unsaved changes");
        }

        private void SetStatus(string msg) => _statusLabel.Text = msg;

        private void ShowWarning(string msg) => MessageBox.Show(this, msg, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        private bool ConfirmDiscardChanges()
        {
            if (!_isDirty) return true;
            var res = MessageBox.Show(this, "You have unsaved changes. Discard them?", "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            return res == DialogResult.Yes;
        }
    }
}