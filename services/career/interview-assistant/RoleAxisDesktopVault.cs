using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RoleAxis.Career.InterviewAssistant;

public sealed partial class RoleAxisDesktopAppForm
{
    private static readonly string[] VaultSupportedExtensions =
    {
        ".pdf", ".docx", ".doc", ".pptx", ".txt", ".csv", ".jpg", ".jpeg", ".png", ".zip"
    };

    private readonly List<VaultAssetItem> _vaultItems = new();
    private readonly List<string> _vaultLog = new();
    private readonly object _vaultLock = new();
    private CancellationTokenSource? _vaultScanCts;
    private Task? _vaultScanTask;
    private string _vaultSelectedFolder = "";
    private string _vaultStatus = "Idle";
    private string _vaultCurrentActivity = "Choose a local folder to build your private professional memory index.";
    private DateTime? _vaultLastScanTime;
    private long _vaultVisitedFiles;
    private long _vaultSupportedFiles;
    private long _vaultTotalBytes;
    private DateTime _vaultScanStartedAtUtc = DateTime.MinValue;
    private bool _vaultIncludeSubfolders = true;
    private bool _vaultHashFiles = true;
    private bool _vaultDetectDuplicates = true;
    private bool _vaultAutoClassify = true;
    private bool _vaultSaveLocalIndex = true;

    private Label? _vaultStatusBadge;
    private Label? _vaultFolderLabel;
    private Label? _vaultLastScanLabel;
    private Label? _vaultCurrentFileLabel;
    private Label? _vaultFilesMetric;
    private Label? _vaultAssetsMetric;
    private Label? _vaultDuplicatesMetric;
    private Label? _vaultEvidenceMetric;
    private Label? _vaultCareerMetric;
    private Label? _vaultSpeedMetric;
    private TextBox? _vaultFolderBox;
    private ProgressBar? _vaultProgress;
    private DataGridView? _vaultGrid;
    private RichTextBox? _vaultLogBox;
    private RichTextBox? _vaultInspectorBox;
    private CheckedListBox? _vaultCategoryList;
    private CheckBox? _vaultIncludeSubfoldersCheck;
    private CheckBox? _vaultHashFilesCheck;
    private CheckBox? _vaultDetectDuplicatesCheck;
    private CheckBox? _vaultAutoClassifyCheck;
    private CheckBox? _vaultSaveLocalIndexCheck;

    private void ShowVaultDashboard()
    {
        ClearContent();
        SetView("vault", "Local Vault Agent", "Privacy-first local document intelligence without uploading files.");
        _contentHost.AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Navy
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildVaultHeader(), 0, 0);
        root.Controls.Add(BuildVaultMetrics(), 0, 1);
        root.Controls.Add(BuildVaultWorkspace(), 0, 2);
        _contentHost.Controls.Add(root);

        BindVaultStateToUi();
    }

    private Control BuildVaultHeader()
    {
        var header = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 14,
            Padding = new Padding(22),
            Margin = new Padding(0, 0, 0, 14)
        };

        var title = CreateVaultLabel("Local Vault Agent", 18f, Ink, FontStyle.Bold);
        title.Location = new Point(24, 18);
        title.Width = 340;
        title.Height = 32;

        var subtitle = CreateVaultLabel("Privacy-first local document intelligence without uploading files.", 9.7f, SoftText);
        subtitle.Location = new Point(25, 52);
        subtitle.Width = 520;
        subtitle.Height = 24;

        _vaultStatusBadge = CreateVaultChip(_vaultStatus);
        _vaultStatusBadge.Location = new Point(24, 86);

        _vaultFolderLabel = CreateVaultLabel("Selected folder: " + DisplayOrFallback(_vaultSelectedFolder, "None"), 8.9f, SoftText);
        _vaultFolderLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _vaultFolderLabel.Width = 620;
        _vaultFolderLabel.Height = 24;
        _vaultFolderLabel.TextAlign = ContentAlignment.MiddleRight;

        _vaultLastScanLabel = CreateVaultLabel("Last scan: " + (_vaultLastScanTime?.ToString("g") ?? "Never"), 8.8f, SoftText);
        _vaultLastScanLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _vaultLastScanLabel.Width = 360;
        _vaultLastScanLabel.Height = 24;
        _vaultLastScanLabel.TextAlign = ContentAlignment.MiddleRight;

        _vaultCurrentFileLabel = CreateVaultLabel(_vaultCurrentActivity, 9.2f, Cyan, FontStyle.Bold);
        _vaultCurrentFileLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _vaultCurrentFileLabel.Width = 720;
        _vaultCurrentFileLabel.Height = 30;
        _vaultCurrentFileLabel.TextAlign = ContentAlignment.MiddleRight;

        header.Resize += (_, _) =>
        {
            _vaultFolderLabel.Left = header.Width - _vaultFolderLabel.Width - 24;
            _vaultFolderLabel.Top = 20;
            _vaultLastScanLabel.Left = header.Width - _vaultLastScanLabel.Width - 24;
            _vaultLastScanLabel.Top = 46;
            _vaultCurrentFileLabel.Left = header.Width - _vaultCurrentFileLabel.Width - 24;
            _vaultCurrentFileLabel.Top = 78;
        };

        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(_vaultStatusBadge);
        header.Controls.Add(_vaultFolderLabel);
        header.Controls.Add(_vaultLastScanLabel);
        header.Controls.Add(_vaultCurrentFileLabel);
        return header;
    }

    private Control BuildVaultMetrics()
    {
        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Navy,
            Margin = new Padding(0, 0, 0, 14)
        };

        for (int i = 0; i < 6; i++)
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666f));

        metrics.Controls.Add(BuildVaultMetricCard("Files scanned", out _vaultFilesMetric), 0, 0);
        metrics.Controls.Add(BuildVaultMetricCard("Assets discovered", out _vaultAssetsMetric), 1, 0);
        metrics.Controls.Add(BuildVaultMetricCard("Duplicates found", out _vaultDuplicatesMetric), 2, 0);
        metrics.Controls.Add(BuildVaultMetricCard("Evidence-ready", out _vaultEvidenceMetric), 3, 0);
        metrics.Controls.Add(BuildVaultMetricCard("Career-ready", out _vaultCareerMetric), 4, 0);
        metrics.Controls.Add(BuildVaultMetricCard("Scan speed", out _vaultSpeedMetric), 5, 0);
        return metrics;
    }

    private Control BuildVaultMetricCard(string title, out Label valueLabel)
    {
        var card = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(18),
            StartColor = Color.FromArgb(239, 249, 255),
            EndColor = Color.FromArgb(217, 236, 250),
            Radius = 14
        };

        var titleLabel = CreateVaultLabel(title, 8.1f, SoftText, FontStyle.Bold);
        titleLabel.Location = new Point(18, 16);
        titleLabel.Width = 180;
        titleLabel.Height = 22;

        valueLabel = CreateVaultLabel("0", 20f, Ink, FontStyle.Bold);
        valueLabel.Location = new Point(18, 40);
        valueLabel.Width = 170;
        valueLabel.Height = 40;

        card.Controls.Add(titleLabel);
        card.Controls.Add(valueLabel);
        return card;
    }

    private Control BuildVaultWorkspace()
    {
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Navy
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Navy,
            Padding = new Padding(0, 0, 14, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 206));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 226));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        left.Controls.Add(BuildVaultFolderCard(), 0, 0);
        left.Controls.Add(BuildVaultControlsCard(), 0, 1);
        left.Controls.Add(BuildVaultCategoryCard(), 0, 2);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Navy
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 166));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        right.Controls.Add(BuildVaultActivityCard(), 0, 0);
        right.Controls.Add(BuildVaultTableCard(), 0, 1);
        right.Controls.Add(BuildVaultInspectorCard(), 0, 2);

        workspace.Controls.Add(left, 0, 0);
        workspace.Controls.Add(right, 1, 0);
        return workspace;
    }

    private Control BuildVaultFolderCard()
    {
        var card = CreateVaultCard("Vault Folder", out var body);
        _vaultFolderBox = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = _vaultSelectedFolder,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9.3f),
            Margin = new Padding(0, 0, 0, 10)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Surface
        };

        var choose = CreateVaultButton("Choose Folder", 128, Surface2, Ink);
        var start = CreateVaultButton("Start Scan", 104, BlackButton, Color.White);
        var cancel = CreateVaultButton("Cancel", 86, Color.FromArgb(143, 45, 45), Color.White);
        var rescan = CreateVaultButton("Rescan", 86, Cyan, Color.White);
        var clear = CreateVaultButton("Clear Results", 118, Surface2, Ink);

        choose.Click += (_, _) => ChooseVaultFolder();
        start.Click += async (_, _) => await StartVaultScanAsync(clearExisting: true);
        cancel.Click += (_, _) => CancelVaultScan();
        rescan.Click += async (_, _) => await StartVaultScanAsync(clearExisting: true);
        clear.Click += (_, _) => ClearVaultResults();

        buttons.Controls.Add(choose);
        buttons.Controls.Add(start);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(rescan);
        buttons.Controls.Add(clear);

        var empty = CreateVaultLabel("Choose a local folder to build your private professional memory index.", 8.6f, SoftText);
        empty.Dock = DockStyle.Bottom;
        empty.Height = 36;

        body.Controls.Add(buttons);
        body.Controls.Add(empty);
        body.Controls.Add(_vaultFolderBox);
        return card;
    }

    private Control BuildVaultControlsCard()
    {
        var card = CreateVaultCard("Scan Controls", out var body);
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Surface
        };

        _vaultIncludeSubfoldersCheck = CreateVaultCheck("Include subfolders", _vaultIncludeSubfolders);
        _vaultHashFilesCheck = CreateVaultCheck("Hash files", _vaultHashFiles);
        _vaultDetectDuplicatesCheck = CreateVaultCheck("Detect duplicates", _vaultDetectDuplicates);
        _vaultAutoClassifyCheck = CreateVaultCheck("Auto-classify", _vaultAutoClassify);
        _vaultSaveLocalIndexCheck = CreateVaultCheck("Save local index", _vaultSaveLocalIndex);

        var types = CreateVaultLabel("Supported: PDF, DOCX, DOC, PPTX, TXT, CSV, JPG, PNG, ZIP", 8.2f, SoftText);
        types.Width = 330;
        types.Height = 42;

        stack.Controls.Add(_vaultIncludeSubfoldersCheck);
        stack.Controls.Add(_vaultHashFilesCheck);
        stack.Controls.Add(_vaultDetectDuplicatesCheck);
        stack.Controls.Add(_vaultAutoClassifyCheck);
        stack.Controls.Add(_vaultSaveLocalIndexCheck);
        stack.Controls.Add(types);
        body.Controls.Add(stack);
        return card;
    }

    private Control BuildVaultCategoryCard()
    {
        var card = CreateVaultCard("Category Summary", out var body);
        _vaultCategoryList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Surface,
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9f),
            SelectionMode = SelectionMode.None,
            CheckOnClick = false
        };
        body.Controls.Add(_vaultCategoryList);
        return card;
    }

    private Control BuildVaultActivityCard()
    {
        var card = CreateVaultCard("Live Scan Activity", out var body);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Surface
        };
        _vaultProgress = new ProgressBar
        {
            Width = 420,
            Height = 16,
            Style = _vaultStatus == "Scanning" ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous,
            MarqueeAnimationSpeed = _vaultStatus == "Scanning" ? 32 : 0
        };
        var privacy = CreateVaultLabel("Your files stay on this machine. RoleAxis scans metadata locally and does not upload documents.", 8.6f, SoftText);
        privacy.Width = 420;
        privacy.Height = 42;
        left.Controls.Add(_vaultProgress);
        left.Controls.Add(privacy);

        _vaultLogBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.8f)
        };

        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(_vaultLogBox, 1, 0);
        body.Controls.Add(layout);
        return card;
    }

    private Control BuildVaultTableCard()
    {
        var card = CreateVaultCard("Discovered Assets", out var body);
        _vaultGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            BackgroundColor = Color.FromArgb(250, 253, 255),
            GridColor = Stroke,
            Font = new Font("Segoe UI", 8.7f),
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(225, 240, 250),
                ForeColor = Ink,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold)
            },
            EnableHeadersVisualStyles = false
        };

        _vaultGrid.Columns.Add("Name", "File name");
        _vaultGrid.Columns.Add("Category", "Category");
        _vaultGrid.Columns.Add("Size", "Size");
        _vaultGrid.Columns.Add("Modified", "Modified date");
        _vaultGrid.Columns.Add("Duplicate", "Duplicate");
        _vaultGrid.Columns.Add("Evidence", "Evidence-ready");
        _vaultGrid.Columns.Add("Career", "Career-ready");
        _vaultGrid.Columns.Add("Hash", "Hash short");
        _vaultGrid.Columns.Add("Folder", "Folder");
        _vaultGrid.Columns.Add("Status", "Status");
        _vaultGrid.SelectionChanged += (_, _) => UpdateVaultInspectorFromSelection();
        body.Controls.Add(_vaultGrid);
        return card;
    }

    private Control BuildVaultInspectorCard()
    {
        var card = CreateVaultCard("Asset Preview / Inspector", out var body);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _vaultInspectorBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9.2f),
            Text = "Select an asset to inspect its metadata, readiness, hash, duplicate match, and suggested use."
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Surface
        };
        var exportCsv = CreateVaultButton("Export Index CSV", 136, BlackButton, Color.White);
        var exportJson = CreateVaultButton("Export Index JSON", 142, Surface2, Ink);
        var openFolder = CreateVaultButton("Open Vault Folder", 140, Surface2, Ink);
        var openSelected = CreateVaultButton("Open Selected Location", 174, Surface2, Ink);
        var markEvidence = CreateVaultButton("Mark Evidence-ready", 166, Cyan, Color.White);
        var markCareer = CreateVaultButton("Mark Career-ready", 154, Cyan, Color.White);

        exportCsv.Click += (_, _) => ExportVaultIndex("csv");
        exportJson.Click += (_, _) => ExportVaultIndex("json");
        openFolder.Click += (_, _) => OpenVaultFolder();
        openSelected.Click += (_, _) => OpenVaultSelectedLocation();
        markEvidence.Click += (_, _) => MarkVaultSelected(evidence: true, career: null);
        markCareer.Click += (_, _) => MarkVaultSelected(evidence: null, career: true);

        actions.Controls.Add(exportCsv);
        actions.Controls.Add(exportJson);
        actions.Controls.Add(openFolder);
        actions.Controls.Add(openSelected);
        actions.Controls.Add(markEvidence);
        actions.Controls.Add(markCareer);

        layout.Controls.Add(_vaultInspectorBox, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        body.Controls.Add(layout);
        return card;
    }

    private Panel CreateVaultCard(string title, out Panel body)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 12,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 12)
        };

        var titleLabel = CreateVaultLabel(title, 10f, Ink, FontStyle.Bold);
        titleLabel.Dock = DockStyle.Top;
        titleLabel.Height = 28;

        body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(0, 8, 0, 0)
        };

        card.Controls.Add(body);
        card.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        return card;
    }

    private static Label CreateVaultLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            ForeColor = color,
            Font = new Font("Segoe UI", size, style),
            BackColor = Color.Transparent
        };
    }

    private static Label CreateVaultChip(string text)
    {
        var label = CreateVaultLabel(text, 8.4f, Color.White, FontStyle.Bold);
        label.Width = 108;
        label.Height = 26;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.BackColor = VaultStatusColor(text);
        return label;
    }

    private static CheckBox CreateVaultCheck(string text, bool value)
    {
        return new CheckBox
        {
            Text = text,
            Checked = value,
            Width = 310,
            Height = 28,
            ForeColor = Ink,
            BackColor = Surface,
            Font = new Font("Segoe UI", 9f)
        };
    }

    private static Button CreateVaultButton(string text, int width, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Segoe UI Semibold", 8.4f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 8)
        };
        button.FlatAppearance.BorderSize = backColor == Surface2 ? 1 : 0;
        button.FlatAppearance.BorderColor = Stroke;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.08f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.08f);
        return button;
    }

    private void ChooseVaultFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose a local Vault folder" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _vaultSelectedFolder = dialog.SelectedPath;
        if (_vaultFolderBox != null)
            _vaultFolderBox.Text = _vaultSelectedFolder;
        AppendVaultLog("Folder selected: " + _vaultSelectedFolder);
        BindVaultStateToUi();
    }

    private async Task StartVaultScanAsync(bool clearExisting)
    {
        if (_vaultScanTask is { IsCompleted: false })
        {
            AppendVaultLog("A Vault scan is already running.");
            return;
        }

        string folder = (_vaultFolderBox?.Text ?? _vaultSelectedFolder).Trim();
        if (!Directory.Exists(folder))
        {
            _vaultStatus = "Idle";
            _vaultCurrentActivity = "Choose an existing local folder first.";
            AppendVaultLog("Scan not started: folder does not exist.");
            BindVaultStateToUi();
            return;
        }

        _vaultSelectedFolder = folder;
        _vaultIncludeSubfolders = _vaultIncludeSubfoldersCheck?.Checked ?? true;
        _vaultHashFiles = _vaultHashFilesCheck?.Checked ?? true;
        _vaultDetectDuplicates = _vaultDetectDuplicatesCheck?.Checked ?? true;
        _vaultAutoClassify = _vaultAutoClassifyCheck?.Checked ?? true;
        _vaultSaveLocalIndex = _vaultSaveLocalIndexCheck?.Checked ?? true;

        if (clearExisting)
        {
            lock (_vaultLock)
                _vaultItems.Clear();
            _vaultLog.Clear();
            _vaultVisitedFiles = 0;
            _vaultSupportedFiles = 0;
            _vaultTotalBytes = 0;
            _vaultGrid?.Rows.Clear();
            if (_vaultInspectorBox != null)
                _vaultInspectorBox.Text = "Scanning locally. You can switch modules while RoleAxis continues indexing.";
        }

        _vaultStatus = "Scanning";
        _vaultCurrentActivity = "Scanning locally. You can switch modules while RoleAxis continues indexing.";
        _vaultScanStartedAtUtc = DateTime.UtcNow;
        _vaultScanCts = new CancellationTokenSource();
        AppendVaultLog("Scan started: " + folder);
        LogActivity("Vault", "Scan started", ShortPath(folder, 72));
        BindVaultStateToUi();

        var token = _vaultScanCts.Token;
        _vaultScanTask = Task.Run(() => RunVaultScan(folder, token), token);
        await Task.CompletedTask;
    }

    private void RunVaultScan(string folder, CancellationToken token)
    {
        var hashToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        lock (_vaultLock)
        {
            foreach (var item in _vaultItems.Where(item => !string.IsNullOrWhiteSpace(item.Hash)))
                hashToPath[item.Hash] = item.Path;
        }

        try
        {
            foreach (string path in EnumerateVaultFiles(folder, _vaultIncludeSubfolders, token))
            {
                if (token.IsCancellationRequested)
                    break;

                Interlocked.Increment(ref _vaultVisitedFiles);
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (!VaultSupportedExtensions.Contains(extension))
                    continue;

                Interlocked.Increment(ref _vaultSupportedFiles);
                try
                {
                    var file = new FileInfo(path);
                    if (!file.Exists)
                        continue;

                    string category = _vaultAutoClassify ? ClassifyVaultCategory(file.Name) : "Other";
                    bool evidence = IsVaultEvidenceReady(category);
                    bool career = IsVaultCareerReady(category);
                    string hash = "";
                    bool duplicate = false;
                    string duplicateMatch = "";

                    if (_vaultHashFiles || _vaultDetectDuplicates)
                    {
                        hash = ComputeVaultSha256(path, token);
                        if (_vaultDetectDuplicates && !string.IsNullOrWhiteSpace(hash) && hashToPath.TryGetValue(hash, out var match))
                        {
                            duplicate = true;
                            duplicateMatch = match;
                        }
                        else if (!string.IsNullOrWhiteSpace(hash))
                        {
                            hashToPath[hash] = path;
                        }
                    }

                    var item = new VaultAssetItem(
                        path,
                        file.Name,
                        file.DirectoryName ?? "",
                        extension,
                        category,
                        file.Length,
                        file.LastWriteTime,
                        hash,
                        duplicate,
                        duplicateMatch,
                        evidence,
                        career,
                        "Indexed");

                    lock (_vaultLock)
                    {
                        _vaultItems.Add(item);
                        _vaultTotalBytes += file.Length;
                    }

                    _vaultCurrentActivity = "Scanning: " + file.Name;
                    AppendVaultLog("Indexed " + file.Name);
                    AddVaultItemToUi(item);
                }
                catch (UnauthorizedAccessException ex)
                {
                    AppendVaultLog("Permission denied: " + path + " (" + ex.Message + ")");
                }
                catch (IOException ex)
                {
                    AppendVaultLog("Skipped locked file: " + path + " (" + ex.Message + ")");
                }
                catch (Exception ex)
                {
                    AppendVaultLog("Skipped file: " + path + " (" + ex.Message + ")");
                }
            }

            if (token.IsCancellationRequested)
            {
                _vaultStatus = "Cancelled";
                _vaultCurrentActivity = "Scan cancelled. Discovered assets remain available.";
                AppendVaultLog("Scan cancelled by user.");
            }
            else
            {
                _vaultStatus = "Completed";
                _vaultCurrentActivity = _vaultItems.Count == 0
                    ? "No supported files found in this folder."
                    : "Scan completed. Local index is ready.";
                _vaultLastScanTime = DateTime.Now;
                AppendVaultLog("Scan completed. Assets discovered: " + _vaultItems.Count);
                LogActivity("Vault", "Scan completed", _vaultItems.Count.ToString("N0") + " assets discovered.");
                if (_vaultSaveLocalIndex)
                    SaveVaultLocalIndex();
            }
        }
        catch (OperationCanceledException)
        {
            _vaultStatus = "Cancelled";
            _vaultCurrentActivity = "Scan cancelled. Discovered assets remain available.";
            AppendVaultLog("Scan cancelled by user.");
            LogActivity("Vault", "Scan cancelled", _vaultItems.Count.ToString("N0") + " assets discovered.");
        }
        catch (Exception ex)
        {
            _vaultStatus = "Error";
            _vaultCurrentActivity = "Scan error: " + ex.Message;
            AppendVaultLog("Scan error: " + ex.Message);
            LogActivity("Vault", "Scan error", ex.Message, "Warning");
        }
        finally
        {
            BeginVaultUiUpdate();
        }
    }

    private IEnumerable<string> EnumerateVaultFiles(string folder, bool includeSubfolders, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(folder);

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string current = pending.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                AppendVaultLog("Cannot read folder: " + current + " (" + ex.Message + ")");
                continue;
            }

            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                yield return file;
            }

            if (!includeSubfolders)
                continue;

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                AppendVaultLog("Cannot list subfolders: " + current + " (" + ex.Message + ")");
                continue;
            }

            foreach (string dir in dirs)
                pending.Push(dir);
        }
    }

    private void CancelVaultScan()
    {
        if (_vaultScanTask is not { IsCompleted: false })
        {
            AppendVaultLog("No running Vault scan to cancel.");
            return;
        }

        _vaultStatus = "Cancelled";
        _vaultCurrentActivity = "Cancelling scan...";
        _vaultScanCts?.Cancel();
        BindVaultStateToUi();
    }

    private void ClearVaultResults()
    {
        if (_vaultScanTask is { IsCompleted: false })
        {
            AppendVaultLog("Cancel the running scan before clearing results.");
            return;
        }

        lock (_vaultLock)
            _vaultItems.Clear();
        _vaultLog.Clear();
        _vaultVisitedFiles = 0;
        _vaultSupportedFiles = 0;
        _vaultTotalBytes = 0;
        _vaultStatus = "Idle";
        _vaultCurrentActivity = "Choose a local folder to build your private professional memory index.";
        _vaultGrid?.Rows.Clear();
        if (_vaultInspectorBox != null)
            _vaultInspectorBox.Text = "Your files stay on this machine. RoleAxis scans metadata locally and does not upload documents.";
        BindVaultStateToUi();
    }

    private void AddVaultItemToUi(VaultAssetItem item)
    {
        if (!IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            if (_vaultGrid == null || _vaultGrid.IsDisposed)
                return;
            AddVaultGridRow(item);
            UpdateVaultMetrics();
            UpdateVaultCategorySummary();
            UpdateVaultActivityVisuals();
        });
    }

    private void AddVaultGridRow(VaultAssetItem item)
    {
        if (_vaultGrid == null)
            return;

        int row = _vaultGrid.Rows.Add(
            item.Name,
            item.Category,
            FormatVaultBytes(item.SizeBytes),
            item.ModifiedAt.ToString("g"),
            item.IsDuplicate ? "Yes" : "No",
            item.EvidenceReady ? "Yes" : "No",
            item.CareerReady ? "Yes" : "No",
            item.HashShort,
            item.Folder,
            item.Status);
        _vaultGrid.Rows[row].Tag = item.Path;
    }

    private void BindVaultStateToUi()
    {
        if (!IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            _vaultFolderBox?.SetTextIfDifferent(_vaultSelectedFolder);
            if (_vaultFolderLabel != null)
                _vaultFolderLabel.Text = "Selected folder: " + DisplayOrFallback(_vaultSelectedFolder, "None");
            if (_vaultLastScanLabel != null)
                _vaultLastScanLabel.Text = "Last scan: " + (_vaultLastScanTime?.ToString("g") ?? "Never");
            if (_vaultCurrentFileLabel != null)
                _vaultCurrentFileLabel.Text = _vaultCurrentActivity;
            if (_vaultStatusBadge != null)
            {
                _vaultStatusBadge.Text = _vaultStatus;
                _vaultStatusBadge.BackColor = VaultStatusColor(_vaultStatus);
            }

            _vaultGrid?.Rows.Clear();
            lock (_vaultLock)
            {
                foreach (var item in _vaultItems)
                    AddVaultGridRow(item);
            }

            UpdateVaultMetrics();
            UpdateVaultCategorySummary();
            UpdateVaultActivityVisuals();
            UpdateVaultInspectorFromSelection();
        });
    }

    private void BeginVaultUiUpdate()
    {
        if (!IsHandleCreated)
            return;
        BeginInvoke(() => BindVaultStateToUi());
    }

    private void UpdateVaultMetrics()
    {
        List<VaultAssetItem> items;
        lock (_vaultLock)
            items = _vaultItems.ToList();

        _vaultFilesMetric?.SetTextIfDifferent(_vaultVisitedFiles.ToString("N0"));
        _vaultAssetsMetric?.SetTextIfDifferent(items.Count.ToString("N0"));
        _vaultDuplicatesMetric?.SetTextIfDifferent(items.Count(item => item.IsDuplicate).ToString("N0"));
        _vaultEvidenceMetric?.SetTextIfDifferent(items.Count(item => item.EvidenceReady).ToString("N0"));
        _vaultCareerMetric?.SetTextIfDifferent(items.Count(item => item.CareerReady).ToString("N0"));

        double seconds = Math.Max(1, (DateTime.UtcNow - _vaultScanStartedAtUtc).TotalSeconds);
        string speed = _vaultStatus == "Scanning"
            ? $"{items.Count / seconds:0.0}/s"
            : items.Count == 0 ? "0/s" : $"{items.Count / seconds:0.0}/s";
        _vaultSpeedMetric?.SetTextIfDifferent(speed);
    }

    private void UpdateVaultCategorySummary()
    {
        if (_vaultCategoryList == null)
            return;

        List<VaultAssetItem> items;
        lock (_vaultLock)
            items = _vaultItems.ToList();

        string[] categories =
        {
            "Resume", "Degree", "Certification", "Publication", "Recommendation", "Award",
            "Project", "Employment", "Media", "Patent", "Membership", "Other"
        };

        _vaultCategoryList.Items.Clear();
        foreach (string category in categories)
        {
            int count = items.Count(item => item.Category == category);
            _vaultCategoryList.Items.Add($"{category}: {count}", count > 0);
        }
    }

    private void UpdateVaultActivityVisuals()
    {
        if (_vaultProgress != null)
        {
            _vaultProgress.Style = _vaultStatus == "Scanning" ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            _vaultProgress.MarqueeAnimationSpeed = _vaultStatus == "Scanning" ? 32 : 0;
            if (_vaultStatus != "Scanning")
                _vaultProgress.Value = _vaultStatus == "Completed" ? 100 : 0;
        }

        if (_vaultLogBox != null)
        {
            string[] logLines;
            lock (_vaultLog)
                logLines = _vaultLog.TakeLast(80).ToArray();
            _vaultLogBox.Text = string.Join(Environment.NewLine, logLines);
        }
        if (_vaultCurrentFileLabel != null)
            _vaultCurrentFileLabel.Text = _vaultCurrentActivity;
    }

    private void UpdateVaultInspectorFromSelection()
    {
        if (_vaultInspectorBox == null)
            return;

        var selected = GetSelectedVaultItem();
        if (!selected.HasValue)
        {
            if (_vaultItems.Count == 0 && _vaultStatus == "Completed")
                _vaultInspectorBox.Text = "No supported files found in this folder.";
            else if (_vaultStatus == "Scanning")
                _vaultInspectorBox.Text = "Scanning locally. You can switch modules while RoleAxis continues indexing.";
            else if (_vaultItems.Count == 0 && string.IsNullOrWhiteSpace(_vaultSelectedFolder))
                _vaultInspectorBox.Text = "Choose a local folder to build your private professional memory index.";
            else if (_vaultItems.Count == 0)
                _vaultInspectorBox.Text = "Your files stay on this machine. RoleAxis scans metadata locally and does not upload documents.";
            else
                _vaultInspectorBox.Text = "Select an asset to inspect its metadata, readiness, hash, duplicate match, and suggested use.";
            return;
        }

        var item = selected.Value;
        var builder = new StringBuilder();
        builder.AppendLine(item.Name);
        builder.AppendLine(new string('=', Math.Min(item.Name.Length, 80)));
        builder.AppendLine("Category: " + item.Category);
        builder.AppendLine("Size: " + FormatVaultBytes(item.SizeBytes));
        builder.AppendLine("Modified: " + item.ModifiedAt.ToString("F"));
        builder.AppendLine("Path: " + item.Path);
        builder.AppendLine("Hash: " + DisplayOrFallback(item.Hash, "Not calculated"));
        builder.AppendLine("Duplicate: " + (item.IsDuplicate ? "Yes" : "No"));
        if (!string.IsNullOrWhiteSpace(item.DuplicateMatch))
            builder.AppendLine("Duplicate match: " + item.DuplicateMatch);
        builder.AppendLine("Evidence-ready: " + (item.EvidenceReady ? "Yes" : "No"));
        builder.AppendLine("Career-ready: " + (item.CareerReady ? "Yes" : "No"));
        builder.AppendLine("Suggested use: " + SuggestedVaultUse(item));
        _vaultInspectorBox.Text = builder.ToString();
    }

    private VaultAssetItem? GetSelectedVaultItem()
    {
        if (_vaultGrid?.CurrentRow?.Tag is not string path)
            return null;
        lock (_vaultLock)
        {
            foreach (var item in _vaultItems)
            {
                if (item.Path == path)
                    return item;
            }
        }
        return null;
    }

    private void MarkVaultSelected(bool? evidence, bool? career)
    {
        var selected = GetSelectedVaultItem();
        if (!selected.HasValue)
            return;

        var selectedItem = selected.Value;
        lock (_vaultLock)
        {
            int index = _vaultItems.FindIndex(item => item.Path == selectedItem.Path);
            if (index >= 0)
            {
                var item = _vaultItems[index];
                _vaultItems[index] = item with
                {
                    EvidenceReady = evidence ?? item.EvidenceReady,
                    CareerReady = career ?? item.CareerReady,
                    Status = "Updated"
                };
            }
        }
        AppendVaultLog("Updated readiness: " + selectedItem.Name);
        BindVaultStateToUi();
    }

    private void ExportVaultIndex(string format)
    {
        List<VaultAssetItem> items;
        lock (_vaultLock)
            items = _vaultItems.ToList();

        if (items.Count == 0)
        {
            AppendVaultLog("Export skipped: no Vault assets available.");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Export Vault index",
            Filter = format == "csv" ? "CSV files (*.csv)|*.csv|All files (*.*)|*.*" : "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"roleaxis-vault-index-{DateTime.Now:yyyyMMdd-HHmmss}.{format}"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (format == "csv")
            File.WriteAllText(dialog.FileName, BuildVaultCsv(items));
        else
            File.WriteAllText(dialog.FileName, BuildVaultJson(items));
        AppendVaultLog("Exported Vault index: " + dialog.FileName);
        LogActivity("Vault", "Index exported", Path.GetFileName(dialog.FileName));
        BindVaultStateToUi();
    }

    private void OpenVaultFolder()
    {
        string folder = DisplayOrFallback(_vaultSelectedFolder, "");
        if (!Directory.Exists(folder))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", QuoteExplorerPath(folder)) { UseShellExecute = true });
    }

    private void OpenVaultSelectedLocation()
    {
        var selected = GetSelectedVaultItem();
        if (!selected.HasValue)
            return;

        var selectedItem = selected.Value;
        if (!File.Exists(selectedItem.Path))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", "/select," + QuoteExplorerPath(selectedItem.Path)) { UseShellExecute = true });
    }

    private void SaveVaultLocalIndex()
    {
        List<VaultAssetItem> items;
        lock (_vaultLock)
            items = _vaultItems.ToList();

        Directory.CreateDirectory(DesktopSessionState.LocalAppFolder);
        string path = Path.Combine(DesktopSessionState.LocalAppFolder, "vault-index.json");
        File.WriteAllText(path, BuildVaultJson(items));
        AppendVaultLog("Local index saved: " + path);
    }

    private void AppendVaultLog(string message)
    {
        lock (_vaultLog)
        {
            _vaultLog.Add($"{DateTime.Now:HH:mm:ss}  {message}");
            if (_vaultLog.Count > 500)
                _vaultLog.RemoveRange(0, _vaultLog.Count - 500);
        }

        if (!IsHandleCreated)
            return;
        BeginInvoke(() => UpdateVaultActivityVisuals());
    }

    private static string ComputeVaultSha256(string path, CancellationToken token)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        token.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ClassifyVaultCategory(string fileName)
    {
        string value = fileName.ToLowerInvariant();
        if (ContainsAny(value, "resume", "cv")) return "Resume";
        if (ContainsAny(value, "degree", "diploma", "transcript")) return "Degree";
        if (ContainsAny(value, "certificate", "certification", "comptia", "aws", "azure")) return "Certification";
        if (ContainsAny(value, "publication", "paper", "journal", "article")) return "Publication";
        if (ContainsAny(value, "recommendation", "reference", "letter")) return "Recommendation";
        if (ContainsAny(value, "award", "honor")) return "Award";
        if (ContainsAny(value, "project", "portfolio")) return "Project";
        if (ContainsAny(value, "employment", "offer", "promotion", "performance")) return "Employment";
        if (ContainsAny(value, "media", "press", "interview")) return "Media";
        if (ContainsAny(value, "patent")) return "Patent";
        if (ContainsAny(value, "membership", "association")) return "Membership";
        return "Other";
    }

    private static bool IsVaultEvidenceReady(string category)
    {
        return category is "Degree" or "Certification" or "Publication" or "Recommendation" or "Project" or "Employment" or "Award" or "Media" or "Patent" or "Membership";
    }

    private static bool IsVaultCareerReady(string category)
    {
        return category is "Resume" or "Certification" or "Publication" or "Project" or "Employment" or "Award";
    }

    private static string SuggestedVaultUse(VaultAssetItem item)
    {
        if (item.EvidenceReady && item.CareerReady)
            return "Evidence and Career";
        if (item.EvidenceReady)
            return "Evidence";
        if (item.CareerReady)
            return "Career";
        return "Vault only";
    }

    private static string FormatVaultBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static string BuildVaultCsv(IEnumerable<VaultAssetItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Name,Category,SizeBytes,ModifiedAt,Duplicate,EvidenceReady,CareerReady,Hash,Folder,Status,Path,DuplicateMatch");
        foreach (var item in items)
        {
            builder.AppendLine(string.Join(",",
                Csv(item.Name),
                Csv(item.Category),
                item.SizeBytes,
                Csv(item.ModifiedAt.ToString("O")),
                item.IsDuplicate,
                item.EvidenceReady,
                item.CareerReady,
                Csv(item.Hash),
                Csv(item.Folder),
                Csv(item.Status),
                Csv(item.Path),
                Csv(item.DuplicateMatch)));
        }
        return builder.ToString();
    }

    private static string BuildVaultJson(IEnumerable<VaultAssetItem> items)
    {
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    private static Color VaultStatusColor(string status)
    {
        return status switch
        {
            "Scanning" => Cyan,
            "Completed" => Color.FromArgb(18, 145, 104),
            "Cancelled" => Color.FromArgb(194, 124, 24),
            "Error" => Color.FromArgb(143, 45, 45),
            _ => Color.FromArgb(72, 94, 112)
        };
    }

    private static string QuoteExplorerPath(string path)
    {
        return "\"" + path.Replace("\"", "") + "\"";
    }
}

internal readonly record struct VaultAssetItem(
    string Path,
    string Name,
    string Folder,
    string Extension,
    string Category,
    long SizeBytes,
    DateTime ModifiedAt,
    string Hash,
    bool IsDuplicate,
    string DuplicateMatch,
    bool EvidenceReady,
    bool CareerReady,
    string Status)
{
    public string HashShort => string.IsNullOrWhiteSpace(Hash) ? "" : Hash[..Math.Min(12, Hash.Length)];
}

internal static class VaultControlExtensions
{
    public static void SetTextIfDifferent(this Control control, string value)
    {
        if (control.Text != value)
            control.Text = value;
    }
}
