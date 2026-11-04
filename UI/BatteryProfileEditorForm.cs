using System.Text.Json;
using System.Text.Json.Serialization;
using BTChargeIndicator.Localization;
using BTChargeIndicator.Models;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.UI;

/// <summary>
/// Native editor for user-defined battery profiles. All custom rules remain declarative.
/// </summary>
internal sealed class BatteryProfileEditorForm : Form
{
    private static readonly JsonSerializerOptions ProfileJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly BatteryProfileStore _store;
    private readonly List<BatteryProfile> _profiles;
    private readonly ListBox _profileList = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _containerIds = new() { Dock = DockStyle.Fill };
    private readonly TextBox _nameContains = new() { Dock = DockStyle.Fill };
    private readonly TextBox _categories = new() { Dock = DockStyle.Fill };
    private readonly TextBox _sources = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _aggregation = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckedListBox _ruleComponents = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        Height = 90
    };
    private readonly ComboBox _ruleOperation = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _preview = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
    private bool _loading;
    private int _selectedIndex = -1;

    public BatteryProfileEditorForm(
        IReadOnlyList<BatteryProfile> profiles,
        BatteryProfileStore store)
    {
        _store = store;
        _profiles = profiles
            .Where(profile => !string.Equals(profile.Id, BatteryProfileEngine.DefaultProfileId,
                StringComparison.OrdinalIgnoreCase))
            .Select(CloneProfile)
            .ToList();

        Text = AppText.Get("ProfileEditorTitle");
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 590);
        Size = new Size(940, 700);
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = SystemFonts.MessageBoxFont;

        BuildControls();
        RefreshProfileList();
        if (_profiles.Count > 0)
        {
            _profileList.SelectedIndex = 0;
        }
        else
        {
            LoadEditor(null);
        }
    }

    private void BuildControls()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _profileList.IntegralHeight = false;
        _profileList.SelectedIndexChanged += ProfileList_SelectedIndexChanged;
        left.Controls.Add(_profileList, 0, 0);

        var profileButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 0)
        };
        profileButtons.Controls.Add(CreateButton("ProfileNew", (_, _) => AddProfile(false)));
        profileButtons.Controls.Add(CreateButton("ProfileDuplicate", (_, _) => AddProfile(true)));
        profileButtons.Controls.Add(CreateButton("ProfileDelete", (_, _) => DeleteProfile()));
        left.Controls.Add(profileButtons, 0, 1);
        root.Controls.Add(left, 0, 0);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            RowCount = 0,
            Padding = new Padding(14, 0, 0, 0)
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddEditorRow(editor, "ProfileName", _name);
        AddEditorRow(editor, "ProfileContainerIds", _containerIds);
        AddEditorRow(editor, "ProfileNameContains", _nameContains);
        AddEditorRow(editor, "ProfileCategories", _categories);

        AddEditorRow(editor, "ProfileBatterySource", _sources);

        foreach (var value in Enum.GetValues<BatteryAggregationMode>())
        {
            _aggregation.Items.Add(value);
        }
        _aggregation.SelectedItem = BatteryAggregationMode.EarbudsLowest;
        _aggregation.SelectedIndexChanged += (_, _) => UpdateRuleVisibility();
        AddEditorRow(editor, "ProfileAggregation", _aggregation);

        foreach (var value in Enum.GetValues<BatteryComponentKind>())
        {
            _ruleComponents.Items.Add(value, value is BatteryComponentKind.Left or BatteryComponentKind.Right);
        }
        AddEditorRow(editor, "ProfileRuleComponents", _ruleComponents);

        foreach (var value in Enum.GetValues<BatteryAggregationOperation>())
        {
            _ruleOperation.Items.Add(value);
        }
        _ruleOperation.SelectedItem = BatteryAggregationOperation.Minimum;
        AddEditorRow(editor, "ProfileRuleOperation", _ruleOperation);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Text = AppText.Get("ProfileMatchHelp"),
            Padding = new Padding(0, 8, 0, 8)
        };
        editor.Controls.Add(help, 0, editor.RowCount);
        editor.SetColumnSpan(help, 2);
        editor.RowCount++;
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        editor.Controls.Add(_preview, 0, editor.RowCount);
        editor.SetColumnSpan(_preview, 2);
        editor.RowCount++;
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var previewButton = CreateButton("ProfilePreview", (_, _) => UpdatePreview());
        editor.Controls.Add(previewButton, 1, editor.RowCount);
        editor.RowCount++;
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(editor, 1, 0);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0)
        };
        bottom.Controls.Add(CreateButton("ProfileSave", (_, _) => SaveProfiles()));
        bottom.Controls.Add(CreateButton("ProfileCancel", (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }));
        bottom.Controls.Add(CreateButton("ProfileImport", (_, _) => ImportProfile()));
        bottom.Controls.Add(CreateButton("ProfileExport", (_, _) => ExportProfile()));
        root.Controls.Add(bottom, 0, 1);
        root.SetColumnSpan(bottom, 2);
        UpdateRuleVisibility();
    }

    private void AddEditorRow(TableLayoutPanel panel, string labelKey, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = AppText.Get(labelKey),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 8, 0)
        };
        panel.Controls.Add(label, 0, row);
        control.Margin = new Padding(3, 4, 3, 4);
        panel.Controls.Add(control, 1, row);
    }

    private Button CreateButton(string textKey, EventHandler click)
    {
        var button = new Button
        {
            Text = AppText.Get(textKey),
            AutoSize = true,
            MinimumSize = new Size(82, 30),
            Margin = new Padding(3)
        };
        button.Click += click;
        return button;
    }

    private void RefreshProfileList(string? selectId = null)
    {
        _loading = true;
        _profileList.Items.Clear();
        foreach (var profile in _profiles)
        {
            _profileList.Items.Add(new ProfileListItem(profile.Id, profile.Name));
        }

        var index = selectId is null
            ? Math.Min(_selectedIndex, _profileList.Items.Count - 1)
            : _profiles.FindIndex(profile => string.Equals(profile.Id, selectId,
                StringComparison.OrdinalIgnoreCase));
        _profileList.SelectedIndex = index;
        _loading = false;
        _selectedIndex = index;
        LoadEditor(index >= 0 ? _profiles[index] : null);
    }

    private void ProfileList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var nextIndex = _profileList.SelectedIndex;
        if (_selectedIndex >= 0 && _selectedIndex != nextIndex && !TryCommitEditor(false))
        {
            _loading = true;
            _profileList.SelectedIndex = _selectedIndex;
            _loading = false;
            MessageBox.Show(this, AppText.Get("ProfileInvalid"), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _selectedIndex = nextIndex;
        LoadEditor(_selectedIndex >= 0 ? _profiles[_selectedIndex] : null);
    }

    private void LoadEditor(BatteryProfile? profile)
    {
        _loading = true;
        var enabled = profile is not null;
        foreach (var control in new Control[]
                 {
                     _name, _containerIds, _nameContains, _categories, _sources,
                     _aggregation, _ruleComponents, _ruleOperation
                 })
        {
            control.Enabled = enabled;
        }

        if (profile is null)
        {
            _name.Clear();
            _containerIds.Clear();
            _nameContains.Clear();
            _categories.Clear();
            _sources.Clear();
            _aggregation.SelectedItem = BatteryAggregationMode.EarbudsLowest;
            for (var i = 0; i < _ruleComponents.Items.Count; i++)
            {
                _ruleComponents.SetItemChecked(i, i < 2);
            }
            _ruleOperation.SelectedItem = BatteryAggregationOperation.Minimum;
        }
        else
        {
            _name.Text = profile.Name;
            _containerIds.Text = string.Join("; ", profile.Match.ContainerIds);
            _nameContains.Text = string.Join("; ", profile.Match.NameContains);
            _categories.Text = string.Join("; ", profile.Match.Categories);
            _sources.Text = string.Join("; ", profile.Match.Sources);
            _aggregation.SelectedItem = profile.AggregationMode;
            var components = profile.CustomRule?.Components ?? [BatteryComponentKind.Left, BatteryComponentKind.Right];
            for (var i = 0; i < _ruleComponents.Items.Count; i++)
            {
                _ruleComponents.SetItemChecked(i, components.Contains((BatteryComponentKind)_ruleComponents.Items[i]!));
            }
            _ruleOperation.SelectedItem = profile.CustomRule?.Operation ?? BatteryAggregationOperation.Minimum;
        }

        _loading = false;
        UpdateRuleVisibility();
        UpdatePreview();
    }

    private bool TryCommitEditor(bool showError)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count)
        {
            return true;
        }

        var name = _name.Text.Trim();
        var mode = _aggregation.SelectedItem is BatteryAggregationMode selectedMode
            ? selectedMode
            : BatteryAggregationMode.EarbudsLowest;
        if (name.Length is 0 or > 80)
        {
            return ShowValidationError("ProfileNameInvalid", showError);
        }

        var containerIds = SplitValues(_containerIds.Text);
        if (containerIds.Any(value => !Guid.TryParse(value, out _)))
        {
            return ShowValidationError("ProfileContainerInvalid", showError);
        }

        var profile = _profiles[_selectedIndex];
        profile.Name = name;
        var sources = new List<BatteryReadingSource>();
        foreach (var value in SplitValues(_sources.Text))
        {
            if (!Enum.TryParse<BatteryReadingSource>(value, true, out var source) ||
                !Enum.IsDefined(source))
            {
                return ShowValidationError("ProfileSourceInvalid", showError);
            }
            sources.Add(source);
        }

        profile.Match = new BatteryProfileMatch
        {
            ContainerIds = containerIds,
            NameContains = SplitValues(_nameContains.Text),
            Categories = SplitValues(_categories.Text),
            Sources = sources
        };
        profile.AggregationMode = mode;

        if (mode == BatteryAggregationMode.Custom)
        {
            var components = _ruleComponents.CheckedItems
                .Cast<BatteryComponentKind>()
                .ToList();
            if (components.Count == 0)
            {
                return ShowValidationError("ProfileRuleComponentsInvalid", showError);
            }

            profile.CustomRule = new BatteryAggregationRule
            {
                Components = components,
                Operation = _ruleOperation.SelectedItem is BatteryAggregationOperation operation
                    ? operation
                    : BatteryAggregationOperation.Minimum
            };
        }
        else
        {
            profile.CustomRule = null;
        }

        if (!BatteryProfileEngine.IsValidProfile(profile))
        {
            return ShowValidationError("ProfileInvalid", showError);
        }

        _loading = true;
        _profileList.Items.RemoveAt(_selectedIndex);
        _profileList.Items.Insert(_selectedIndex, new ProfileListItem(profile.Id, profile.Name));
        _profileList.SelectedIndex = _selectedIndex;
        _loading = false;
        return true;
    }

    private bool ShowValidationError(string key, bool showError)
    {
        if (showError)
        {
            MessageBox.Show(this, AppText.Get(key), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return false;
    }

    private void AddProfile(bool duplicate)
    {
        if (!TryCommitEditor(true))
        {
            return;
        }

        var source = duplicate && _selectedIndex >= 0 ? _profiles[_selectedIndex] : null;
        var profile = source is null
            ? new BatteryProfile()
            : CloneProfile(source);
        profile.Id = Guid.NewGuid().ToString("N");
        profile.Name = source is null
            ? AppText.Get("ProfileNewName")
            : AppText.Format("ProfileCopyName", source.Name);
        _profiles.Add(profile);
        _selectedIndex = _profiles.Count - 1;
        RefreshProfileList(profile.Id);
        _name.Focus();
        _name.SelectAll();
    }

    private void DeleteProfile()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count || !TryCommitEditor(true))
        {
            return;
        }

        var profile = _profiles[_selectedIndex];
        if (MessageBox.Show(this, AppText.Format("ProfileDeleteConfirm", profile.Name),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _profiles.RemoveAt(_selectedIndex);
        _selectedIndex = Math.Min(_selectedIndex, _profiles.Count - 1);
        RefreshProfileList();
    }

    private void SaveProfiles()
    {
        if (!TryCommitEditor(true))
        {
            return;
        }

        try
        {
            _store.Save(_profiles);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, AppText.Format("ProfileSaveError", ex.Message),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "JSON profile (*.json)|*.json|All files (*.*)|*.*",
            Title = AppText.Get("ProfileImport")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (!TryCommitEditor(true))
            {
                return;
            }

            var imported = JsonSerializer.Deserialize<BatteryProfile>(
                File.ReadAllText(dialog.FileName), ProfileJsonOptions)
                ?? throw new InvalidDataException("The profile file is empty.");
            if (!BatteryProfileEngine.IsValidProfile(imported) ||
                imported.Match is null ||
                imported.Match.ContainerIds is null ||
                imported.Match.NameContains is null ||
                imported.Match.Categories is null ||
                imported.Match.Sources is null ||
                imported.Match.ContainerIds.Any(value => !Guid.TryParse(value, out _)) ||
                imported.Match.NameContains.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 256) ||
                imported.Match.Categories.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 256))
            {
                throw new InvalidDataException(AppText.Get("ProfileInvalid"));
            }

            if (string.Equals(imported.Id, BatteryProfileEngine.DefaultProfileId, StringComparison.OrdinalIgnoreCase) ||
                _profiles.Any(profile => string.Equals(profile.Id, imported.Id, StringComparison.OrdinalIgnoreCase)))
            {
                imported.Id = Guid.NewGuid().ToString("N");
            }

            _profiles.Add(imported);
            _selectedIndex = _profiles.Count - 1;
            RefreshProfileList(imported.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, AppText.Format("ProfileImportError", ex.Message),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportProfile()
    {
        if (!TryCommitEditor(true) || _selectedIndex < 0 || _selectedIndex >= _profiles.Count)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "JSON profile (*.json)|*.json",
            FileName = $"{_profiles[_selectedIndex].Id}.json",
            Title = AppText.Get("ProfileExport")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName,
                JsonSerializer.Serialize(_profiles[_selectedIndex], ProfileJsonOptions));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, AppText.Format("ProfileExportError", ex.Message),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdatePreview()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count)
        {
            _preview.Text = AppText.Get("ProfilePreviewUnavailable");
            return;
        }

        if (!TryCommitEditor(false))
        {
            _preview.Text = AppText.Get("ProfileInvalid");
            return;
        }

        var profile = _profiles[_selectedIndex];
        var sample = new BluetoothBatteryDevice("profile-preview", "Sample earbuds", 55, true, null, null)
        {
            Components = new BatteryComponents(100, 100, 55)
        };
        var result = BatteryProfileEngine.GetBatteryPercent(sample, profile);
        _preview.Text = AppText.Format("ProfilePreviewResult",
            result.HasValue ? $"{result.Value}%" : AppText.Get("NoData"));
    }

    private void UpdateRuleVisibility()
    {
        var isCustom = _aggregation.SelectedItem is BatteryAggregationMode.Custom;
        _ruleComponents.Enabled = isCustom;
        _ruleOperation.Enabled = isCustom;
    }

    private static List<string> SplitValues(string value) =>
        value.Split([';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static BatteryProfile CloneProfile(BatteryProfile source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        AggregationMode = source.AggregationMode,
        Match = new BatteryProfileMatch
        {
            ContainerIds = [.. source.Match.ContainerIds],
            NameContains = [.. source.Match.NameContains],
            Categories = [.. source.Match.Categories],
            Sources = [.. source.Match.Sources]
        },
        CustomRule = source.CustomRule is null
            ? null
            : new BatteryAggregationRule
            {
                Components = [.. source.CustomRule.Components],
                Operation = source.CustomRule.Operation
            }
    };

    private sealed record ProfileListItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
