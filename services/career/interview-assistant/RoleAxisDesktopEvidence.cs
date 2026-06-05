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
    private static readonly string[] EvidenceSupportedExtensions =
    {
        ".pdf", ".docx", ".doc", ".pptx", ".txt", ".csv", ".jpg", ".jpeg", ".png", ".zip"
    };

    private static readonly EvidenceCategoryRule[] EvidenceCategoryRules =
    {
        new("Degrees", "Degrees", 80, new[] { "degree", "diploma", "transcript" }),
        new("Certifications", "Certifications", 75, new[] { "certificate", "certification", "comptia", "aws", "azure" }),
        new("Publications", "Publications", 90, new[] { "publication", "paper", "journal", "article" }),
        new("Citations", "Citations", 80, new[] { "citation", "cited", "scholar" }),
        new("Awards", "Awards", 85, new[] { "award", "honor", "recognition" }),
        new("Recommendation Letters", "Recommendation_Letters", 90, new[] { "recommendation", "reference", "letter" }),
        new("Employment Evidence", "Employment_Evidence", 75, new[] { "employment", "offer", "promotion", "performance", "payslip", "paystub", "contract" }),
        new("Projects", "Projects", 70, new[] { "project", "portfolio", "github", "system", "software" }),
        new("Media Mentions", "Media_Mentions", 80, new[] { "media", "press", "interview", "news" }),
        new("Memberships", "Memberships", 65, new[] { "membership", "association", "society", "fellow" }),
        new("Patents", "Patents", 85, new[] { "patent" }),
        new("Speaking / Conference", "Speaking_Conference", 75, new[] { "conference", "speaking", "presentation", "keynote", "invite" }),
        new("Other Supporting Evidence", "Other_Supporting_Evidence", 40, Array.Empty<string>())
    };

    private readonly List<EvidenceCandidateItem> _evidenceCandidates = new();
    private readonly List<string> _evidenceLog = new();
    private readonly List<string> _evidenceErrors = new();
    private readonly object _evidenceLock = new();
    private readonly ManualResetEventSlim _evidencePauseGate = new(true);

    private CancellationTokenSource? _evidenceScanCts;
    private Task? _evidenceScanTask;
    private bool _evidenceStopRequested;
    private bool _evidenceIsPaused;
    private string _evidenceSelectedFolder = "";
    private string _evidenceStatus = "Idle";
    private string _evidenceCurrentFile = "Choose a local folder to discover evidence without uploading files.";
    private string _evidenceLastExportFolder = "";
    private DateTime? _evidenceStartedAt;
    private DateTime? _evidenceEndedAt;
    private DateTime? _evidenceLastScanAt;
    private long _evidenceFilesVisited;
    private long _evidenceFilesScanned;
    private long _evidenceUnsupportedSkipped;
    private long _evidencePermissionErrors;

    private bool _evidenceIncludeSubfolders = true;
    private bool _evidenceHashFiles = true;
    private bool _evidenceDetectDuplicates = true;
    private bool _evidenceAutoClassify = true;
    private bool _evidenceAutoScore = true;
    private bool _evidenceSaveLocalIndex = true;
    private readonly HashSet<string> _evidenceEnabledExtensions = new(EvidenceSupportedExtensions, StringComparer.OrdinalIgnoreCase);

    private Label? _evidenceStatusBadge;
    private Label? _evidenceFolderLabel;
    private Label? _evidenceLastScanLabel;
    private Label? _evidenceCurrentFileLabel;
    private Label? _evidenceSetupSummaryLabel;
    private Label? _evidenceSetupFolderSummaryLabel;
    private Label? _evidenceProfileSummaryLabel;
    private Label? _evidenceFilesMetric;
    private Label? _evidenceCandidatesMetric;
    private Label? _evidenceApprovedMetric;
    private Label? _evidenceRejectedMetric;
    private Label? _evidenceDuplicatesMetric;
    private Label? _evidenceHighValueMetric;
    private Label? _evidenceExportReadyMetric;
    private TextBox? _evidenceFolderBox;
    private ProgressBar? _evidenceProgress;
    private RichTextBox? _evidenceLogBox;
    private DataGridView? _evidenceGrid;
    private ListView? _evidenceCategoryList;
    private RichTextBox? _evidenceInspectorReadout;
    private TextBox? _evidenceTitleEditor;
    private ComboBox? _evidenceCategoryEditor;
    private NumericUpDown? _evidenceValueEditor;
    private RichTextBox? _evidenceNotesEditor;
    private Guid? _evidenceInspectorCandidateId;
    private CheckBox? _evidenceIncludeSubfoldersCheck;
    private CheckBox? _evidenceHashFilesCheck;
    private CheckBox? _evidenceDetectDuplicatesCheck;
    private CheckBox? _evidenceAutoClassifyCheck;
    private CheckBox? _evidenceAutoScoreCheck;
    private CheckBox? _evidenceSaveLocalIndexCheck;
    private Button? _evidenceSetupButton;
    private Button? _evidencePauseButton;
    private Button? _evidenceStopButton;
    private Button? _evidenceCancelButton;
    private Button? _evidenceSetupCardButton;

    private void ShowEvidenceDashboard()
    {
        ClearContent();
        SetView("evidence", "Evidence Scanner", "Local evidence discovery, classification, scoring, and attorney-ready package export.");
        _contentHost.AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Navy
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        root.Controls.Add(BuildEvidenceHeader(), 0, 0);
        root.Controls.Add(BuildEvidenceMetrics(), 0, 1);
        root.Controls.Add(BuildEvidenceWorkspace(), 0, 2);
        root.Controls.Add(BuildEvidenceExportBar(), 0, 3);
        _contentHost.Controls.Add(root);

        BindEvidenceStateToUi(rebuildGrid: true);
    }

    private Control BuildEvidenceHeader()
    {
        var header = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 14,
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 0, 8)
        };

        _evidenceStatusBadge = CreateEvidenceChip(_evidenceStatus);
        _evidenceStatusBadge.Location = new Point(16, 17);

        _evidenceCurrentFileLabel = CreateEvidenceLabel(_evidenceCurrentFile, 9.2f, Cyan, FontStyle.Bold);
        _evidenceCurrentFileLabel.Location = new Point(124, 9);
        _evidenceCurrentFileLabel.Width = 680;
        _evidenceCurrentFileLabel.Height = 22;
        _evidenceCurrentFileLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _evidenceFolderLabel = CreateEvidenceLabel("Folder: " + DisplayOrFallback(_evidenceSelectedFolder, "None"), 8.3f, SoftText);
        _evidenceFolderLabel.Location = new Point(124, 31);
        _evidenceFolderLabel.Width = 420;
        _evidenceFolderLabel.Height = 18;
        _evidenceFolderLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _evidenceLastScanLabel = CreateEvidenceLabel("Last scan: " + (_evidenceLastScanAt?.ToString("g") ?? "Never"), 8.3f, SoftText);
        _evidenceLastScanLabel.Location = new Point(548, 31);
        _evidenceLastScanLabel.Width = 260;
        _evidenceLastScanLabel.Height = 18;
        _evidenceLastScanLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _evidenceLastScanLabel.TextAlign = ContentAlignment.MiddleRight;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 496,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 0)
        };

        _evidenceCancelButton = CreateEvidenceButton("Cancel", 82, Color.FromArgb(148, 51, 57), Color.White);
        _evidenceStopButton = CreateEvidenceButton("Stop", 74, BlackButton, Color.White);
        _evidencePauseButton = CreateEvidenceButton("Pause", 86, Surface2, Ink);
        _evidenceSetupButton = CreateEvidenceButton("Setup Scan", 122, Cyan, Color.White);

        _evidenceCancelButton.Click += (_, _) => CancelEvidenceScan();
        _evidenceStopButton.Click += (_, _) => StopEvidenceScan();
        _evidencePauseButton.Click += (_, _) => ToggleEvidencePause();
        _evidenceSetupButton.Click += (_, _) => ShowEvidenceSetupDialog();

        actions.Controls.Add(_evidenceCancelButton);
        actions.Controls.Add(_evidenceStopButton);
        actions.Controls.Add(_evidencePauseButton);
        actions.Controls.Add(_evidenceSetupButton);

        header.Resize += (_, _) =>
        {
            int reserved = actions.Width + 152;
            int available = Math.Max(280, header.Width - reserved);
            _evidenceCurrentFileLabel.Width = available;
            _evidenceFolderLabel.Width = Math.Max(240, available - 270);
            _evidenceLastScanLabel.Left = Math.Max(360, header.Width - actions.Width - _evidenceLastScanLabel.Width - 18);
        };

        header.Controls.Add(_evidenceStatusBadge);
        header.Controls.Add(_evidenceCurrentFileLabel);
        header.Controls.Add(_evidenceFolderLabel);
        header.Controls.Add(_evidenceLastScanLabel);
        header.Controls.Add(actions);
        return header;
    }

    private Control BuildEvidenceMetrics()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            BackColor = Navy,
            Margin = new Padding(0, 0, 0, 12)
        };
        for (int i = 0; i < 7; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7f));

        grid.Controls.Add(CreateEvidenceMetricCard("Files scanned", out _evidenceFilesMetric), 0, 0);
        grid.Controls.Add(CreateEvidenceMetricCard("Candidates found", out _evidenceCandidatesMetric), 1, 0);
        grid.Controls.Add(CreateEvidenceMetricCard("Approved", out _evidenceApprovedMetric), 2, 0);
        grid.Controls.Add(CreateEvidenceMetricCard("Rejected", out _evidenceRejectedMetric), 3, 0);
        grid.Controls.Add(CreateEvidenceMetricCard("Duplicates", out _evidenceDuplicatesMetric), 4, 0);
        grid.Controls.Add(CreateEvidenceMetricCard("High-value evidence", out _evidenceHighValueMetric), 5, 0);
        grid.Controls.Add(CreateEvidenceMetricCard("Export-ready items", out _evidenceExportReadyMetric), 6, 0);
        return grid;
    }

    private Control CreateEvidenceMetricCard(string title, out Label valueLabel)
    {
        var card = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
            Padding = new Padding(12),
            StartColor = Color.FromArgb(238, 249, 255),
            EndColor = Color.FromArgb(216, 237, 250)
        };

        var titleLabel = CreateEvidenceLabel(title, 7.6f, SoftText, FontStyle.Bold);
        titleLabel.Location = new Point(14, 10);
        titleLabel.Width = 180;
        titleLabel.Height = 18;

        valueLabel = CreateEvidenceLabel("0", 16.5f, Ink, FontStyle.Bold);
        valueLabel.Location = new Point(14, 31);
        valueLabel.Width = 170;
        valueLabel.Height = 28;

        card.Controls.Add(titleLabel);
        card.Controls.Add(valueLabel);
        return card;
    }

    private Control BuildEvidenceWorkspace()
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

        workspace.Controls.Add(BuildEvidenceLeftColumn(), 0, 0);
        workspace.Controls.Add(BuildEvidenceMainArea(), 1, 0);
        return workspace;
    }

    private Control BuildEvidenceLeftColumn()
    {
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Navy,
            Margin = new Padding(0, 0, 12, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        left.Controls.Add(BuildEvidenceFolderCard(), 0, 0);
        left.Controls.Add(BuildEvidenceOptionsCard(), 0, 1);
        left.Controls.Add(BuildEvidenceCategoryCard(), 0, 2);
        return left;
    }

    private Control BuildEvidenceFolderCard()
    {
        _evidenceFolderBox = null;
        var card = CreateEvidenceCard("Scan Setup", out var body);
        body.Padding = new Padding(16, 34, 16, 12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _evidenceSetupFolderSummaryLabel = CreateEvidenceLabel("Folder: " + DisplayOrFallback(_evidenceSelectedFolder, "None"), 8.4f, SoftText, FontStyle.Bold);
        _evidenceSetupFolderSummaryLabel.Dock = DockStyle.Fill;

        _evidenceSetupSummaryLabel = CreateEvidenceLabel("Open setup to choose a folder, file types, and scan options.", 8.2f, SoftText);
        _evidenceSetupSummaryLabel.Dock = DockStyle.Fill;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0)
        };

        _evidenceSetupCardButton = CreateEvidenceButton("Setup Scan", 122, Cyan, Color.White);
        var clear = CreateEvidenceButton("Clear Results", 116, Surface2, Ink);

        _evidenceSetupCardButton.Click += (_, _) => ShowEvidenceSetupDialog();
        clear.Click += (_, _) => ClearEvidenceResults();

        actions.Controls.Add(_evidenceSetupCardButton);
        actions.Controls.Add(clear);

        layout.Controls.Add(_evidenceSetupFolderSummaryLabel, 0, 0);
        layout.Controls.Add(_evidenceSetupSummaryLabel, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        body.Controls.Add(layout);
        return card;
    }

    private Control BuildEvidenceOptionsCard()
    {
        var card = CreateEvidenceCard("Scan Profile", out var body);
        body.Padding = new Padding(16, 34, 16, 12);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent
        };

        _evidenceProfileSummaryLabel = CreateEvidenceLabel(DescribeEvidenceScanProfile(), 8.3f, SoftText);
        _evidenceProfileSummaryLabel.Location = new Point(0, 0);
        _evidenceProfileSummaryLabel.Width = 330;
        _evidenceProfileSummaryLabel.Height = 132;

        scroll.Controls.Add(_evidenceProfileSummaryLabel);
        scroll.Resize += (_, _) => _evidenceProfileSummaryLabel.Width = Math.Max(280, scroll.ClientSize.Width - 18);

        body.Controls.Add(scroll);
        return card;
    }

    private Control BuildEvidenceCategoryCard()
    {
        var card = CreateEvidenceCard("Evidence Category Summary", out var body);
        body.Padding = new Padding(12, 40, 12, 12);

        _evidenceCategoryList = new ListView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Surface,
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.4f),
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        _evidenceCategoryList.Columns.Add("Category", 214);
        _evidenceCategoryList.Columns.Add("All", 52, HorizontalAlignment.Right);
        _evidenceCategoryList.Columns.Add("Approved", 82, HorizontalAlignment.Right);

        body.Controls.Add(_evidenceCategoryList);
        return card;
    }

    private Control BuildEvidenceMainArea()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Navy
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 178));

        main.Controls.Add(BuildEvidenceActivityCard(), 0, 0);
        main.Controls.Add(BuildEvidenceCandidatesCard(), 0, 1);
        main.Controls.Add(BuildEvidenceInspectorCard(), 0, 2);
        return main;
    }

    private Control BuildEvidenceActivityCard()
    {
        var card = CreateEvidenceCard("Live Scan Activity", out var body);
        body.Padding = new Padding(16, 34, 16, 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _evidenceProgress = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 18,
            Style = ProgressBarStyle.Blocks
        };

        _evidenceLogBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Color.FromArgb(54, 76, 96),
            Font = new Font("Segoe UI", 8.4f),
            ReadOnly = true
        };

        var hint = CreateEvidenceLabel("During scan: files appear live, errors are logged, and you can switch modules safely.", 8.5f, SoftText);
        hint.Dock = DockStyle.Fill;

        layout.Controls.Add(_evidenceProgress, 0, 0);
        layout.Controls.Add(hint, 0, 1);
        layout.Controls.Add(_evidenceLogBox, 0, 2);
        body.Controls.Add(layout);
        return card;
    }

    private Control BuildEvidenceCandidatesCard()
    {
        var card = CreateEvidenceCard("Evidence Candidates", out var body);
        body.Padding = new Padding(12, 34, 12, 10);

        _evidenceGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            EnableHeadersVisualStyles = false,
            GridColor = Stroke,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Segoe UI", 8.2f),
            ColumnHeadersHeight = 34,
            RowTemplate = { Height = 34 }
        };
        _evidenceGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(225, 240, 250);
        _evidenceGrid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        _evidenceGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.2f, FontStyle.Bold);
        _evidenceGrid.DefaultCellStyle.BackColor = Surface;
        _evidenceGrid.DefaultCellStyle.ForeColor = Ink;
        _evidenceGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 232, 247);
        _evidenceGrid.DefaultCellStyle.SelectionForeColor = Ink;

        AddEvidenceTextColumn("Status", "Status", 74, 7);
        AddEvidenceTextColumn("Title", "Suggested Title", 180, 17);
        AddEvidenceTextColumn("Category", "Suggested Category", 132, 12);
        AddEvidenceTextColumn("Confidence", "Confidence", 72, 6);
        AddEvidenceTextColumn("Value", "Evidence Value", 86, 7);
        AddEvidenceTextColumn("FileName", "File Name", 160, 14);
        AddEvidenceTextColumn("Size", "Size", 66, 6);
        AddEvidenceTextColumn("Modified", "Modified Date", 112, 9);
        AddEvidenceTextColumn("Duplicate", "Duplicate", 70, 6);
        AddEvidenceTextColumn("Folder", "Folder", 150, 12);
        AddEvidenceButtonColumn("Approve", "Approve", 74);
        AddEvidenceButtonColumn("Reject", "Reject", 64);
        AddEvidenceButtonColumn("Edit", "Edit", 54);
        AddEvidenceButtonColumn("Open", "Open", 58);

        _evidenceGrid.SelectionChanged += (_, _) => UpdateEvidenceInspectorFromSelection();
        _evidenceGrid.CellContentClick += (_, e) => HandleEvidenceGridAction(e);

        body.Controls.Add(_evidenceGrid);
        return card;
    }

    private void AddEvidenceTextColumn(string name, string header, int minimumWidth, float fillWeight)
    {
        if (_evidenceGrid == null)
            return;
        _evidenceGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            MinimumWidth = minimumWidth,
            FillWeight = fillWeight
        });
    }

    private void AddEvidenceButtonColumn(string name, string text, int width)
    {
        if (_evidenceGrid == null)
            return;
        _evidenceGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = name,
            HeaderText = "",
            Text = text,
            UseColumnTextForButtonValue = true,
            MinimumWidth = width,
            FillWeight = 5
        });
    }

    private Control BuildEvidenceInspectorCard()
    {
        var card = CreateEvidenceCard("Candidate Inspector", out var body);
        body.Padding = new Padding(16, 34, 16, 10);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));

        _evidenceInspectorReadout = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.5f),
            ReadOnly = true,
            Text = "Before scan: RoleAxis scans locally, classifies evidence candidates, and keeps original files untouched."
        };

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            BackColor = Color.Transparent,
            Padding = new Padding(14, 0, 0, 0)
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        _evidenceTitleEditor = CreateEvidenceTextBox();
        _evidenceCategoryEditor = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.6f)
        };
        _evidenceCategoryEditor.Items.AddRange(EvidenceCategoryRules.Select(rule => rule.Category).Cast<object>().ToArray());
        _evidenceValueEditor = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.6f)
        };
        _evidenceNotesEditor = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.5f)
        };

        var save = CreateEvidenceButton("Save Candidate Edits", 152, BlackButton, Color.White);
        save.Dock = DockStyle.Right;
        save.Click += (_, _) => SaveEvidenceCandidateEdits();

        AddEvidenceEditorRow(editor, "Title", _evidenceTitleEditor, 0);
        AddEvidenceEditorRow(editor, "Category", _evidenceCategoryEditor, 1);
        AddEvidenceEditorRow(editor, "Value", _evidenceValueEditor, 2);
        AddEvidenceEditorRow(editor, "Notes", _evidenceNotesEditor, 3);
        editor.Controls.Add(save, 1, 4);

        layout.Controls.Add(_evidenceInspectorReadout, 0, 0);
        layout.Controls.Add(editor, 1, 0);
        body.Controls.Add(layout);
        return card;
    }

    private static TextBox CreateEvidenceTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Color.FromArgb(14, 20, 34),
            Font = new Font("Segoe UI", 8.6f)
        };
    }

    private void AddEvidenceEditorRow(TableLayoutPanel editor, string label, Control control, int row)
    {
        var labelView = CreateEvidenceLabel(label, 8.2f, SoftText, FontStyle.Bold);
        labelView.Dock = DockStyle.Fill;
        labelView.TextAlign = ContentAlignment.MiddleLeft;
        editor.Controls.Add(labelView, 0, row);
        editor.Controls.Add(control, 1, row);
    }

    private Control BuildEvidenceExportBar()
    {
        var bar = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 14,
            Padding = new Padding(16, 12, 16, 10),
            Margin = new Padding(0, 12, 0, 0)
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 780,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        var open = CreateEvidenceButton("Open Export Folder", 150, Surface2, Ink);
        var json = CreateEvidenceButton("Export Evidence Index JSON", 190, Surface2, Ink);
        var csv = CreateEvidenceButton("Export Evidence Index CSV", 184, Surface2, Ink);
        var package = CreateEvidenceButton("Export Approved Package", 184, BlackButton, Color.White);
        open.Click += (_, _) => OpenEvidenceExportFolder();
        json.Click += (_, _) => ExportEvidenceIndex("json");
        csv.Click += (_, _) => ExportEvidenceIndex("csv");
        package.Click += (_, _) => ExportEvidenceApprovedPackage();

        actions.Controls.Add(open);
        actions.Controls.Add(json);
        actions.Controls.Add(csv);
        actions.Controls.Add(package);

        var hint = CreateEvidenceLabel("Approved candidates are ready for local package export.", 9f, SoftText, FontStyle.Bold);
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;

        bar.Controls.Add(actions);
        bar.Controls.Add(hint);
        return bar;
    }

    private Panel CreateEvidenceCard(string title, out Panel body)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 14,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 12)
        };

        var titleLabel = CreateEvidenceLabel(title, 10f, Ink, FontStyle.Bold);
        titleLabel.Location = new Point(16, 10);
        titleLabel.Width = 360;
        titleLabel.Height = 22;

        body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(14, 34, 14, 12)
        };

        card.Controls.Add(body);
        card.Controls.Add(titleLabel);
        return card;
    }

    private static Label CreateEvidenceLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent
        };
    }

    private static Label CreateEvidenceChip(string text)
    {
        var label = CreateEvidenceLabel(text, 8.4f, Color.White, FontStyle.Bold);
        label.Width = 94;
        label.Height = 24;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.BackColor = EvidenceStatusColor(text);
        return label;
    }

    private static CheckBox CreateEvidenceCheck(string text, bool value)
    {
        return new CheckBox
        {
            Text = text,
            Checked = value,
            AutoSize = false,
            Width = 260,
            Height = 26,
            ForeColor = Color.FromArgb(54, 76, 96),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.5f)
        };
    }

    private static Button CreateEvidenceButton(string text, int width, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            Margin = new Padding(4),
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Segoe UI Semibold", 8.4f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private void ShowEvidenceSetupDialog()
    {
        if (IsEvidenceLiveStatus(_evidenceStatus))
        {
            AppendEvidenceLog("Stop or cancel the active scan before changing setup.");
            return;
        }

        using var dialog = new Form
        {
            Text = "Evidence Scan Setup",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(760, 620),
            MinimumSize = new Size(700, 560),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Navy,
            Font = new Font("Segoe UI", 9f),
            ShowInTaskbar = false
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Navy,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 164));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var intro = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var title = CreateEvidenceLabel("Evidence Scan Setup", 16.5f, Ink, FontStyle.Bold);
        title.Location = new Point(0, 3);
        title.Width = 420;
        title.Height = 30;
        var subtitle = CreateEvidenceLabel("Choose the local folder, evidence file types, and scan behavior before discovery starts.", 9f, SoftText);
        subtitle.Location = new Point(1, 36);
        subtitle.Width = 660;
        subtitle.Height = 20;
        intro.Controls.Add(title);
        intro.Controls.Add(subtitle);

        var folderCard = CreateEvidenceCard("Local Folder", out var folderBody);
        folderCard.Margin = new Padding(0, 0, 0, 10);
        folderBody.Padding = new Padding(16, 34, 16, 12);
        var folderGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        folderGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));

        var folderBox = CreateEvidenceTextBox();
        folderBox.Text = _evidenceSelectedFolder;
        var browse = CreateEvidenceButton("Browse", 96, Surface2, Ink);
        browse.Dock = DockStyle.Fill;
        browse.Click += (_, _) =>
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Choose a local evidence folder"
            };
            if (folderDialog.ShowDialog(dialog) == DialogResult.OK)
                folderBox.Text = folderDialog.SelectedPath;
        };
        folderGrid.Controls.Add(folderBox, 0, 0);
        folderGrid.Controls.Add(browse, 1, 0);
        folderBody.Controls.Add(folderGrid);

        var typesCard = CreateEvidenceCard("File Types", out var typesBody);
        typesCard.Margin = new Padding(0, 0, 0, 10);
        typesBody.Padding = new Padding(16, 34, 16, 12);
        var typeList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.8f),
            MultiColumn = true,
            ColumnWidth = 92
        };
        foreach (string extension in EvidenceSupportedExtensions)
            typeList.Items.Add(extension.ToUpperInvariant(), _evidenceEnabledExtensions.Contains(extension));
        typesBody.Controls.Add(typeList);

        var optionsCard = CreateEvidenceCard("Scan Options", out var optionsBody);
        optionsCard.Margin = new Padding(0, 0, 0, 10);
        optionsBody.Padding = new Padding(16, 34, 16, 12);
        var optionScroll = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent
        };
        var includeSubfolders = CreateEvidenceCheck("Include subfolders", _evidenceIncludeSubfolders);
        var hashFiles = CreateEvidenceCheck("Hash files for traceability", _evidenceHashFiles);
        var detectDuplicates = CreateEvidenceCheck("Detect duplicate evidence", _evidenceDetectDuplicates);
        var autoClassify = CreateEvidenceCheck("Auto-classify evidence", _evidenceAutoClassify);
        var autoScore = CreateEvidenceCheck("Auto-score evidence value", _evidenceAutoScore);
        var saveLocalIndex = CreateEvidenceCheck("Save local scan index", _evidenceSaveLocalIndex);
        foreach (var check in new[] { includeSubfolders, hashFiles, detectDuplicates, autoClassify, autoScore, saveLocalIndex })
        {
            check.Width = 650;
            optionScroll.Controls.Add(check);
        }
        optionsBody.Controls.Add(optionScroll);

        var message = CreateEvidenceLabel("RoleAxis scans metadata locally. Original files stay untouched and are not uploaded by the scanner.", 8.6f, SoftText);
        message.Dock = DockStyle.Fill;
        message.TextAlign = ContentAlignment.MiddleLeft;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        var start = CreateEvidenceButton("Start Scan", 122, BlackButton, Color.White);
        var cancel = CreateEvidenceButton("Cancel", 94, Surface2, Ink);
        var error = CreateEvidenceLabel("", 8.6f, Orange, FontStyle.Bold);
        error.Width = 360;
        error.Height = 34;
        error.TextAlign = ContentAlignment.MiddleRight;

        cancel.Click += (_, _) => dialog.Close();
        start.Click += (_, _) =>
        {
            string folder = folderBox.Text.Trim();
            if (!Directory.Exists(folder))
            {
                error.Text = "Choose an existing local folder.";
                return;
            }

            var selectedTypes = typeList.CheckedItems
                .Cast<object>()
                .Select(item => item.ToString()?.Trim().ToLowerInvariant() ?? "")
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
            if (selectedTypes.Count == 0)
            {
                error.Text = "Choose at least one file type.";
                return;
            }

            _evidenceSelectedFolder = folder;
            _evidenceEnabledExtensions.Clear();
            foreach (string extension in selectedTypes)
                _evidenceEnabledExtensions.Add(extension);
            _evidenceIncludeSubfolders = includeSubfolders.Checked;
            _evidenceHashFiles = hashFiles.Checked;
            _evidenceDetectDuplicates = detectDuplicates.Checked;
            _evidenceAutoClassify = autoClassify.Checked;
            _evidenceAutoScore = autoScore.Checked;
            _evidenceSaveLocalIndex = saveLocalIndex.Checked;
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };

        actions.Controls.Add(start);
        actions.Controls.Add(cancel);
        actions.Controls.Add(error);

        root.Controls.Add(intro, 0, 0);
        root.Controls.Add(folderCard, 0, 1);
        root.Controls.Add(typesCard, 0, 2);
        root.Controls.Add(optionsCard, 0, 3);
        root.Controls.Add(message, 0, 4);
        root.Controls.Add(actions, 0, 5);
        dialog.Controls.Add(root);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _evidenceCurrentFile = "Before scan: RoleAxis scans locally, classifies evidence candidates, and keeps original files untouched.";
            AppendEvidenceLog("Evidence scan setup configured.");
            BindEvidenceStateToUi(rebuildGrid: false);
            StartEvidenceScan(clearExisting: true);
        }
    }

    private bool IsEvidenceLiveStatus(string status)
    {
        return status == "Scanning" || status == "Paused";
    }

    private string DescribeEvidenceScanProfile()
    {
        var options = new List<string>();
        if (_evidenceIncludeSubfolders)
            options.Add("subfolders");
        if (_evidenceHashFiles)
            options.Add("hashing");
        if (_evidenceDetectDuplicates)
            options.Add("duplicates");
        if (_evidenceAutoClassify)
            options.Add("classification");
        if (_evidenceAutoScore)
            options.Add("scoring");
        if (_evidenceSaveLocalIndex)
            options.Add("local index");

        string extensions = _evidenceEnabledExtensions.Count == EvidenceSupportedExtensions.Length
            ? "all supported types"
            : string.Join(", ", _evidenceEnabledExtensions.OrderBy(item => item).Select(item => item.TrimStart('.').ToUpperInvariant()));

        return "File types: " + extensions + Environment.NewLine +
            "Options: " + (options.Count == 0 ? "manual review only" : string.Join(", ", options)) + Environment.NewLine +
            "Privacy: local discovery, no original-file upload.";
    }

    private void ChooseEvidenceFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a local evidence folder"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _evidenceSelectedFolder = dialog.SelectedPath;
        if (_evidenceFolderBox != null)
            _evidenceFolderBox.Text = _evidenceSelectedFolder;
        _evidenceCurrentFile = "Before scan: RoleAxis scans locally, classifies evidence candidates, and keeps original files untouched.";
        AppendEvidenceLog("Selected evidence folder: " + _evidenceSelectedFolder);
        BindEvidenceStateToUi(rebuildGrid: false);
    }

    private void StartEvidenceScan(bool clearExisting)
    {
        if (IsEvidenceLiveStatus(_evidenceStatus) || _evidenceScanTask is { IsCompleted: false })
        {
            AppendEvidenceLog("Scan already running. You can switch modules while discovery continues.");
            return;
        }

        string folder = _evidenceFolderBox is { IsDisposed: false }
            ? _evidenceFolderBox.Text.Trim()
            : _evidenceSelectedFolder;
        if (!Directory.Exists(folder))
        {
            _evidenceCurrentFile = "Choose a local folder to discover evidence without uploading files.";
            AppendEvidenceError("Choose an existing local folder before scanning.");
            BindEvidenceStateToUi(rebuildGrid: false);
            return;
        }

        _evidenceIncludeSubfolders = _evidenceIncludeSubfoldersCheck is { IsDisposed: false } ? _evidenceIncludeSubfoldersCheck.Checked : _evidenceIncludeSubfolders;
        _evidenceHashFiles = _evidenceHashFilesCheck is { IsDisposed: false } ? _evidenceHashFilesCheck.Checked : _evidenceHashFiles;
        _evidenceDetectDuplicates = _evidenceDetectDuplicatesCheck is { IsDisposed: false } ? _evidenceDetectDuplicatesCheck.Checked : _evidenceDetectDuplicates;
        _evidenceAutoClassify = _evidenceAutoClassifyCheck is { IsDisposed: false } ? _evidenceAutoClassifyCheck.Checked : _evidenceAutoClassify;
        _evidenceAutoScore = _evidenceAutoScoreCheck is { IsDisposed: false } ? _evidenceAutoScoreCheck.Checked : _evidenceAutoScore;
        _evidenceSaveLocalIndex = _evidenceSaveLocalIndexCheck is { IsDisposed: false } ? _evidenceSaveLocalIndexCheck.Checked : _evidenceSaveLocalIndex;

        if (clearExisting)
            ResetEvidenceScanState(clearLogs: true);

        _evidenceSelectedFolder = folder;
        _evidenceStopRequested = false;
        _evidenceIsPaused = false;
        _evidencePauseGate.Set();
        _evidenceScanCts?.Dispose();
        _evidenceScanCts = new CancellationTokenSource();
        _evidenceStartedAt = DateTime.Now;
        _evidenceEndedAt = null;
        _evidenceStatus = "Scanning";
        _evidenceCurrentFile = "Scanning locally. You can switch modules while RoleAxis continues discovery.";
        AppendEvidenceLog("Started local evidence scan.");
        LogActivity("Evidence", "Scan started", ShortPath(folder, 72));
        BindEvidenceStateToUi(rebuildGrid: true);

        var token = _evidenceScanCts.Token;
        _evidenceScanTask = Task.Run(() => RunEvidenceScan(folder, token), token);
    }

    private void RunEvidenceScan(string folder, CancellationToken token)
    {
        var hashToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string path in EnumerateEvidenceFiles(folder, _evidenceIncludeSubfolders, token))
            {
                WaitForEvidenceResume(token);
                if (token.IsCancellationRequested)
                    break;

                FileInfo file;
                try
                {
                    file = new FileInfo(path);
                }
                catch (Exception ex)
                {
                    RegisterEvidenceError("Could not inspect file: " + path + " - " + ex.Message);
                    continue;
                }

                lock (_evidenceLock)
                {
                    _evidenceFilesVisited++;
                    _evidenceCurrentFile = file.Name;
                }

                if (!file.Exists || !_evidenceEnabledExtensions.Contains(file.Extension.ToLowerInvariant()))
                {
                    lock (_evidenceLock)
                        _evidenceUnsupportedSkipped++;
                    if (_evidenceFilesVisited % 15 == 0)
                        BeginEvidenceUiUpdate(rebuildGrid: false);
                    continue;
                }

                string hash = "";
                bool duplicate = false;
                string duplicateMatch = "";
                if (_evidenceHashFiles || _evidenceDetectDuplicates)
                {
                    WaitForEvidenceResume(token);
                    try
                    {
                        hash = ComputeEvidenceHash(file.FullName, token);
                        if (_evidenceDetectDuplicates && !string.IsNullOrWhiteSpace(hash))
                        {
                            if (hashToPath.TryGetValue(hash, out duplicateMatch))
                            {
                                duplicate = true;
                            }
                            else
                            {
                                hashToPath[hash] = file.FullName;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        RegisterEvidenceError("Hash skipped: " + file.Name + " - " + ex.Message);
                    }
                }

                var candidate = BuildEvidenceCandidate(file, folder, hash, duplicate, duplicateMatch);
                lock (_evidenceLock)
                {
                    _evidenceFilesScanned++;
                    _evidenceCandidates.Add(candidate);
                }

                AppendEvidenceLog("Found " + candidate.Category + ": " + candidate.FileName);
                BeginEvidenceCandidateDiscovered(candidate);
            }

            lock (_evidenceLock)
            {
                _evidenceEndedAt = DateTime.Now;
                _evidenceLastScanAt = _evidenceEndedAt;
                _evidenceStatus = token.IsCancellationRequested
                    ? (_evidenceStopRequested ? "Stopped" : "Cancelled")
                    : "Completed";
                _evidenceCurrentFile = token.IsCancellationRequested
                    ? (_evidenceStopRequested
                        ? "Scan stopped. Results remain visible until you clear them."
                        : "Scan cancelled. Results remain visible until you clear them.")
                    : (_evidenceCandidates.Count == 0
                        ? "No evidence candidates found. Try another folder or enable broader file types."
                        : "Completed. Approved candidates are ready for local package export.");
            }

            AppendEvidenceLog(token.IsCancellationRequested
                ? (_evidenceStopRequested ? "Scan stopped." : "Scan cancelled.")
                : "Scan completed.");
            LogActivity(
                "Evidence",
                token.IsCancellationRequested ? (_evidenceStopRequested ? "Scan stopped" : "Scan cancelled") : "Scan completed",
                _evidenceFilesScanned.ToString("N0") + " files scanned; " + _evidenceCandidates.Count.ToString("N0") + " candidates.");
            if (_evidenceSaveLocalIndex)
                SaveEvidenceLocalIndex();
        }
        catch (OperationCanceledException)
        {
            lock (_evidenceLock)
            {
                _evidenceEndedAt = DateTime.Now;
                _evidenceLastScanAt = _evidenceEndedAt;
                _evidenceStatus = _evidenceStopRequested ? "Stopped" : "Cancelled";
                _evidenceCurrentFile = _evidenceStopRequested
                    ? "Scan stopped. Results remain visible until you clear them."
                    : "Scan cancelled. Results remain visible until you clear them.";
            }
            AppendEvidenceLog(_evidenceStopRequested ? "Scan stopped." : "Scan cancelled.");
            LogActivity("Evidence", _evidenceStopRequested ? "Scan stopped" : "Scan cancelled", _evidenceFilesScanned.ToString("N0") + " files scanned.");
        }
        catch (Exception ex)
        {
            lock (_evidenceLock)
            {
                _evidenceEndedAt = DateTime.Now;
                _evidenceLastScanAt = _evidenceEndedAt;
                _evidenceStatus = "Error";
                _evidenceCurrentFile = "Scan stopped because an unexpected error occurred.";
            }
            RegisterEvidenceError(ex.Message);
            LogActivity("Evidence", "Scan error", ex.Message, "Warning");
        }
        finally
        {
            _evidenceIsPaused = false;
            _evidencePauseGate.Set();
            _evidenceScanTask = null;
            BeginEvidenceUiUpdate(rebuildGrid: true);
        }
    }

    private void WaitForEvidenceResume(CancellationToken token)
    {
        _evidencePauseGate.Wait(token);
    }

    private IEnumerable<string> EnumerateEvidenceFiles(string root, bool includeSubfolders, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string folder = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (Exception ex)
            {
                RegisterEvidenceError("Folder skipped: " + folder + " - " + ex.Message);
                continue;
            }

            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                yield return file;
            }

            if (!includeSubfolders)
                continue;

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(folder);
            }
            catch (Exception ex)
            {
                RegisterEvidenceError("Subfolders skipped: " + folder + " - " + ex.Message);
                continue;
            }

            foreach (string child in folders)
                pending.Push(child);
        }
    }

    private EvidenceCandidateItem BuildEvidenceCandidate(FileInfo file, string rootFolder, string hash, bool duplicate, string duplicateMatch)
    {
        var classification = ClassifyEvidenceFile(file, rootFolder);
        string title = BuildEvidenceTitle(file);
        return new EvidenceCandidateItem
        {
            Id = Guid.NewGuid(),
            SourcePath = file.FullName,
            FileName = file.Name,
            Folder = file.DirectoryName ?? "",
            Extension = file.Extension.ToLowerInvariant(),
            SizeBytes = file.Length,
            ModifiedAt = file.LastWriteTime,
            Status = "Pending",
            SuggestedTitle = title,
            Category = classification.Category,
            ConfidenceScore = classification.Confidence,
            EvidenceValueScore = classification.EvidenceValue,
            ClassificationReason = classification.Reason,
            Hash = hash,
            IsDuplicate = duplicate,
            DuplicateMatch = duplicateMatch,
            RecommendedUsage = BuildEvidenceRecommendedUsage(classification.Category),
            Notes = "",
            DiscoveredAt = DateTime.Now
        };
    }

    private EvidenceClassification ClassifyEvidenceFile(FileInfo file, string rootFolder)
    {
        if (!_evidenceAutoClassify)
            return new EvidenceClassification("Other Supporting Evidence", 20, _evidenceAutoScore ? 40 : 0, "Auto-classification disabled.");

        string name = NormalizeEvidenceText(Path.GetFileNameWithoutExtension(file.Name));
        string relativeFolder = "";
        try
        {
            relativeFolder = Path.GetRelativePath(rootFolder, file.DirectoryName ?? rootFolder);
        }
        catch
        {
            relativeFolder = file.DirectoryName ?? "";
        }
        string folderText = NormalizeEvidenceText(relativeFolder);
        bool supported = EvidenceSupportedExtensions.Contains(file.Extension.ToLowerInvariant());
        bool meaningfulName = HasMeaningfulEvidenceName(name);
        bool recent = file.LastWriteTime >= DateTime.Now.AddYears(-2);
        bool generic = IsGenericEvidenceName(name);

        EvidenceCategoryRule bestRule = EvidenceCategoryRules[^1];
        int bestKeywordScore = -1;
        var bestReasons = new List<string>();

        foreach (var rule in EvidenceCategoryRules.Where(rule => rule.Keywords.Length > 0))
        {
            bool fileMatch = rule.Keywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            bool folderMatch = rule.Keywords.Any(keyword => folderText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            int keywordScore = (fileMatch ? 45 : 0) + (folderMatch ? 25 : 0);
            if (keywordScore <= bestKeywordScore)
                continue;

            bestRule = rule;
            bestKeywordScore = keywordScore;
            bestReasons = new List<string>();
            if (fileMatch)
                bestReasons.Add("strong keyword match in filename");
            if (folderMatch)
                bestReasons.Add("keyword match in folder path");
        }

        int confidence = Math.Max(bestKeywordScore, 0);
        if (supported)
        {
            confidence += 15;
            bestReasons.Add("supported evidence file type");
        }
        if (meaningfulName || recent)
        {
            confidence += 5;
            bestReasons.Add(meaningfulName ? "meaningful filename" : "recently modified");
        }
        if (generic)
        {
            confidence -= 20;
            bestReasons.Add("generic filename lowered confidence");
        }

        if (bestKeywordScore <= 0)
        {
            bestRule = EvidenceCategoryRules[^1];
            confidence = supported ? 35 : 20;
            if (meaningfulName)
                confidence += 5;
            bestReasons.Add("no strong category keyword found");
        }

        confidence = Math.Clamp(confidence, 0, 100);
        int value = _evidenceAutoScore
            ? Math.Clamp(bestRule.BaseValue + (confidence - 70) / 4, 0, 100)
            : bestRule.BaseValue;
        string reason = string.Join("; ", bestReasons.Distinct());
        return new EvidenceClassification(bestRule.Category, confidence, value, reason);
    }

    private static string NormalizeEvidenceText(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char ch in value.ToLowerInvariant())
            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return builder.ToString();
    }

    private static bool HasMeaningfulEvidenceName(string normalizedName)
    {
        return normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(part => part.Length >= 5);
    }

    private static bool IsGenericEvidenceName(string normalizedName)
    {
        string compact = normalizedName.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Length < 5)
            return true;
        string[] generic = { "scan", "scanned", "document", "doc", "file", "image", "img", "photo", "untitled", "newfile", "copy" };
        return generic.Any(word => compact.Equals(word, StringComparison.OrdinalIgnoreCase) || compact.StartsWith(word, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildEvidenceTitle(FileInfo file)
    {
        string raw = Path.GetFileNameWithoutExtension(file.Name)
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Replace('.', ' ');
        var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length <= 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word[1..]);
        string title = string.Join(" ", words);
        return string.IsNullOrWhiteSpace(title) ? file.Name : title;
    }

    private static string BuildEvidenceRecommendedUsage(string category)
    {
        return category switch
        {
            "Recommendation Letters" => "Use as attorney-ready character, expertise, or professional credibility support.",
            "Publications" => "Use to establish subject-matter authority and external recognition.",
            "Awards" => "Use as high-value recognition evidence with exhibit treatment.",
            "Patents" => "Use as innovation, authorship, and technical achievement evidence.",
            "Degrees" => "Use as education and credential evidence.",
            "Certifications" => "Use as verified professional skills evidence.",
            "Employment Evidence" => "Use to support role history, compensation, promotions, or performance claims.",
            "Media Mentions" => "Use as public visibility and external validation evidence.",
            "Speaking / Conference" => "Use as thought-leadership and industry participation evidence.",
            "Projects" => "Use as practical implementation and portfolio evidence.",
            "Memberships" => "Use as professional affiliation support.",
            "Citations" => "Use to support impact and third-party reliance on work.",
            _ => "Review manually and decide whether this supports the case narrative."
        };
    }

    private static string ComputeEvidenceHash(string path, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sha = SHA256.Create();
        byte[] buffer = new byte[1024 * 64];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            sha.TransformBlock(buffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
    }

    private void CancelEvidenceScan()
    {
        if (!IsEvidenceLiveStatus(_evidenceStatus))
            return;
        _evidenceStopRequested = false;
        _evidenceIsPaused = false;
        _evidencePauseGate.Set();
        _evidenceCurrentFile = "Cancellation requested. RoleAxis will stop after the current file.";
        AppendEvidenceLog("Cancellation requested.");
        _evidenceScanCts?.Cancel();
        BeginEvidenceUiUpdate(rebuildGrid: false);
    }

    private void StopEvidenceScan()
    {
        if (!IsEvidenceLiveStatus(_evidenceStatus))
            return;
        _evidenceStopRequested = true;
        _evidenceIsPaused = false;
        _evidencePauseGate.Set();
        _evidenceCurrentFile = "Stop requested. RoleAxis will preserve discovered candidates.";
        AppendEvidenceLog("Stop requested.");
        _evidenceScanCts?.Cancel();
        BeginEvidenceUiUpdate(rebuildGrid: false);
    }

    private void ToggleEvidencePause()
    {
        if (_evidenceStatus == "Scanning")
        {
            _evidenceIsPaused = true;
            _evidencePauseGate.Reset();
            lock (_evidenceLock)
            {
                _evidenceStatus = "Paused";
                _evidenceCurrentFile = "Scan paused. Resume when you are ready to continue discovery.";
            }
            AppendEvidenceLog("Scan paused.");
            BeginEvidenceUiUpdate(rebuildGrid: false);
            return;
        }

        if (_evidenceStatus != "Paused")
            return;

        _evidenceIsPaused = false;
        _evidencePauseGate.Set();
        lock (_evidenceLock)
        {
            _evidenceStatus = "Scanning";
            _evidenceCurrentFile = "Scan resumed. RoleAxis is continuing local discovery.";
        }
        AppendEvidenceLog("Scan resumed.");
        BeginEvidenceUiUpdate(rebuildGrid: false);
    }

    private void CancelEvidenceScanForShutdown()
    {
        try
        {
            _evidenceIsPaused = false;
            _evidencePauseGate.Set();
            _evidenceScanCts?.Cancel();
        }
        catch
        {
            // App shutdown should never be blocked by scanner cancellation.
        }
    }

    private void ClearEvidenceResults()
    {
        if (IsEvidenceLiveStatus(_evidenceStatus))
        {
            _evidenceIsPaused = false;
            _evidencePauseGate.Set();
            _evidenceScanCts?.Cancel();
        }

        ResetEvidenceScanState(clearLogs: true);
        _evidenceStatus = "Idle";
        _evidenceCurrentFile = "Choose a local folder to discover evidence without uploading files.";
        BindEvidenceStateToUi(rebuildGrid: true);
    }

    private void ResetEvidenceScanState(bool clearLogs)
    {
        lock (_evidenceLock)
        {
            _evidenceCandidates.Clear();
            _evidenceFilesVisited = 0;
            _evidenceFilesScanned = 0;
            _evidenceUnsupportedSkipped = 0;
            _evidencePermissionErrors = 0;
            _evidenceStartedAt = null;
            _evidenceEndedAt = null;
            if (clearLogs)
            {
                _evidenceLog.Clear();
                _evidenceErrors.Clear();
            }
        }
        _evidenceInspectorCandidateId = null;
    }

    private void AppendEvidenceLog(string message)
    {
        lock (_evidenceLock)
        {
            _evidenceLog.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            if (_evidenceLog.Count > 300)
                _evidenceLog.RemoveRange(0, _evidenceLog.Count - 300);
        }
        BeginEvidenceUiUpdate(rebuildGrid: false);
    }

    private void AppendEvidenceError(string message)
    {
        RegisterEvidenceError(message);
        BeginEvidenceUiUpdate(rebuildGrid: false);
    }

    private void RegisterEvidenceError(string message)
    {
        lock (_evidenceLock)
        {
            _evidencePermissionErrors++;
            _evidenceErrors.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            _evidenceLog.Add(DateTime.Now.ToString("HH:mm:ss") + "  Warning: " + message);
            if (_evidenceErrors.Count > 100)
                _evidenceErrors.RemoveRange(0, _evidenceErrors.Count - 100);
            if (_evidenceLog.Count > 300)
                _evidenceLog.RemoveRange(0, _evidenceLog.Count - 300);
        }
    }

    private void BeginEvidenceCandidateDiscovered(EvidenceCandidateItem candidate)
    {
        if (!IsHandleCreated || IsDisposed)
            return;

        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                if (IsDisposed)
                    return;
                AddEvidenceGridRow(candidate);
                RefreshEvidenceSummaryControls();
            }));
        }
        catch
        {
            // UI may be closing while a background scan is finishing.
        }
    }

    private void BeginEvidenceUiUpdate(bool rebuildGrid)
    {
        if (!IsHandleCreated || IsDisposed)
            return;

        try
        {
            BeginInvoke((MethodInvoker)(() =>
            {
                if (!IsDisposed)
                    BindEvidenceStateToUi(rebuildGrid);
            }));
        }
        catch
        {
            // Ignore updates during shutdown.
        }
    }

    private void BindEvidenceStateToUi(bool rebuildGrid)
    {
        if (InvokeRequired)
        {
            BeginEvidenceUiUpdate(rebuildGrid);
            return;
        }

        if (_evidenceFolderBox != null && !_evidenceFolderBox.IsDisposed && _evidenceFolderBox.Text != _evidenceSelectedFolder)
            _evidenceFolderBox.Text = _evidenceSelectedFolder;

        if (rebuildGrid && _evidenceGrid != null && !_evidenceGrid.IsDisposed)
        {
            Guid? selectedId = GetSelectedEvidenceCandidateId();
            _evidenceGrid.Rows.Clear();
            foreach (var candidate in SnapshotEvidenceCandidates())
                AddEvidenceGridRow(candidate);
            if (selectedId.HasValue)
                SelectEvidenceGridRow(selectedId.Value);
        }

        RefreshEvidenceSummaryControls();
        UpdateEvidenceInspectorFromSelection();
    }

    private void RefreshEvidenceSummaryControls()
    {
        var candidates = SnapshotEvidenceCandidates();
        long filesVisited;
        long filesScanned;
        long unsupported;
        long errors;
        string status;
        string currentFile;
        DateTime? lastScan;
        DateTime? startedAt;
        DateTime? endedAt;
        lock (_evidenceLock)
        {
            filesVisited = _evidenceFilesVisited;
            filesScanned = _evidenceFilesScanned;
            unsupported = _evidenceUnsupportedSkipped;
            errors = _evidencePermissionErrors;
            status = _evidenceStatus;
            currentFile = _evidenceCurrentFile;
            lastScan = _evidenceLastScanAt;
            startedAt = _evidenceStartedAt;
            endedAt = _evidenceEndedAt;
        }

        if (_evidenceStatusBadge != null && !_evidenceStatusBadge.IsDisposed)
        {
            _evidenceStatusBadge.Text = status;
            _evidenceStatusBadge.BackColor = EvidenceStatusColor(status);
        }
        if (_evidenceFolderLabel != null && !_evidenceFolderLabel.IsDisposed)
            _evidenceFolderLabel.Text = "Folder: " + DisplayOrFallback(_evidenceSelectedFolder, "None");
        if (_evidenceLastScanLabel != null && !_evidenceLastScanLabel.IsDisposed)
        {
            _evidenceLastScanLabel.Text = IsEvidenceLiveStatus(status) && startedAt.HasValue
                ? "Started: " + startedAt.Value.ToString("g")
                : endedAt.HasValue
                    ? "Last finished: " + endedAt.Value.ToString("g")
                    : "Last scan: " + (lastScan?.ToString("g") ?? "Never");
        }
        if (_evidenceCurrentFileLabel != null && !_evidenceCurrentFileLabel.IsDisposed)
            _evidenceCurrentFileLabel.Text = currentFile;

        bool live = IsEvidenceLiveStatus(status);
        if (_evidenceSetupButton != null && !_evidenceSetupButton.IsDisposed)
            _evidenceSetupButton.Visible = !live;
        if (_evidenceSetupCardButton != null && !_evidenceSetupCardButton.IsDisposed)
            _evidenceSetupCardButton.Visible = !live;
        if (_evidencePauseButton != null && !_evidencePauseButton.IsDisposed)
        {
            _evidencePauseButton.Visible = live;
            _evidencePauseButton.Text = status == "Paused" ? "Resume" : "Pause";
        }
        if (_evidenceStopButton != null && !_evidenceStopButton.IsDisposed)
            _evidenceStopButton.Visible = live;
        if (_evidenceCancelButton != null && !_evidenceCancelButton.IsDisposed)
            _evidenceCancelButton.Visible = live;

        if (_evidenceSetupFolderSummaryLabel != null && !_evidenceSetupFolderSummaryLabel.IsDisposed)
            _evidenceSetupFolderSummaryLabel.Text = "Folder: " + DisplayOrFallback(_evidenceSelectedFolder, "None");
        if (_evidenceSetupSummaryLabel != null && !_evidenceSetupSummaryLabel.IsDisposed)
        {
            _evidenceSetupSummaryLabel.Text = live
                ? "Setup is tucked away during live scanning. Use the command strip above."
                : "Open setup to choose a folder, file types, and scan options.";
        }
        if (_evidenceProfileSummaryLabel != null && !_evidenceProfileSummaryLabel.IsDisposed)
            _evidenceProfileSummaryLabel.Text = DescribeEvidenceScanProfile();

        _evidenceFilesMetric?.SetTextIfDifferent(filesVisited.ToString("N0"));
        _evidenceCandidatesMetric?.SetTextIfDifferent(candidates.Count.ToString("N0"));
        _evidenceApprovedMetric?.SetTextIfDifferent(candidates.Count(item => item.Status == "Approved").ToString("N0"));
        _evidenceRejectedMetric?.SetTextIfDifferent(candidates.Count(item => item.Status == "Rejected").ToString("N0"));
        _evidenceDuplicatesMetric?.SetTextIfDifferent(candidates.Count(item => item.IsDuplicate).ToString("N0"));
        _evidenceHighValueMetric?.SetTextIfDifferent(candidates.Count(item => item.EvidenceValueScore >= 80 && item.Status != "Rejected").ToString("N0"));
        _evidenceExportReadyMetric?.SetTextIfDifferent(candidates.Count(item => item.Status == "Approved").ToString("N0"));

        if (_evidenceProgress != null && !_evidenceProgress.IsDisposed)
        {
            if (status == "Scanning")
            {
                _evidenceProgress.Style = ProgressBarStyle.Marquee;
                _evidenceProgress.MarqueeAnimationSpeed = 28;
            }
            else if (status == "Paused")
            {
                _evidenceProgress.Style = ProgressBarStyle.Blocks;
                _evidenceProgress.MarqueeAnimationSpeed = 0;
                _evidenceProgress.Value = 0;
            }
            else
            {
                _evidenceProgress.Style = ProgressBarStyle.Blocks;
                _evidenceProgress.MarqueeAnimationSpeed = 0;
                _evidenceProgress.Value = status == "Completed" ? 100 : 0;
            }
        }

        if (_evidenceLogBox != null && !_evidenceLogBox.IsDisposed)
        {
            List<string> lines;
            lock (_evidenceLock)
            {
                lines = _evidenceLog.TakeLast(90).ToList();
                if (errors > 0)
                    lines.Add("Warnings/errors: " + errors.ToString("N0") + "   Unsupported skipped: " + unsupported.ToString("N0") + "   Supported scanned: " + filesScanned.ToString("N0"));
            }
            if (lines.Count == 0)
                lines.Add(string.IsNullOrWhiteSpace(_evidenceSelectedFolder)
                    ? "Choose a local folder to discover evidence without uploading files."
                    : "Before scan: RoleAxis scans locally, classifies evidence candidates, and keeps original files untouched.");
            _evidenceLogBox.Text = string.Join(Environment.NewLine, lines);
        }

        if (_evidenceCategoryList != null && !_evidenceCategoryList.IsDisposed)
        {
            _evidenceCategoryList.BeginUpdate();
            _evidenceCategoryList.Items.Clear();
            foreach (var rule in EvidenceCategoryRules)
            {
                int total = candidates.Count(item => item.Category == rule.Category);
                int approved = candidates.Count(item => item.Category == rule.Category && item.Status == "Approved");
                var row = new ListViewItem(rule.Category);
                row.SubItems.Add(total.ToString("N0"));
                row.SubItems.Add(approved.ToString("N0"));
                if (approved > 0)
                    row.ForeColor = Mint;
                _evidenceCategoryList.Items.Add(row);
            }
            _evidenceCategoryList.EndUpdate();
        }
    }

    private List<EvidenceCandidateItem> SnapshotEvidenceCandidates()
    {
        lock (_evidenceLock)
            return _evidenceCandidates.ToList();
    }

    private void AddEvidenceGridRow(EvidenceCandidateItem candidate)
    {
        if (_evidenceGrid == null || _evidenceGrid.IsDisposed)
            return;

        foreach (DataGridViewRow existing in _evidenceGrid.Rows)
        {
            if (existing.Tag is Guid id && id == candidate.Id)
            {
                RefreshEvidenceGridRow(existing, candidate);
                return;
            }
        }

        int rowIndex = _evidenceGrid.Rows.Add();
        var row = _evidenceGrid.Rows[rowIndex];
        row.Tag = candidate.Id;
        RefreshEvidenceGridRow(row, candidate);
    }

    private void RefreshEvidenceGridRow(DataGridViewRow row, EvidenceCandidateItem candidate)
    {
        row.Cells["Status"].Value = candidate.Status;
        row.Cells["Title"].Value = candidate.SuggestedTitle;
        row.Cells["Category"].Value = candidate.Category;
        row.Cells["Confidence"].Value = candidate.ConfidenceScore.ToString("N0");
        row.Cells["Value"].Value = candidate.EvidenceValueScore.ToString("N0");
        row.Cells["FileName"].Value = candidate.FileName;
        row.Cells["Size"].Value = FormatEvidenceBytes(candidate.SizeBytes);
        row.Cells["Modified"].Value = candidate.ModifiedAt.ToString("g");
        row.Cells["Duplicate"].Value = candidate.IsDuplicate ? "Yes" : "No";
        row.Cells["Folder"].Value = candidate.Folder;

        row.DefaultCellStyle.BackColor = candidate.Status switch
        {
            "Approved" => Color.FromArgb(226, 246, 238),
            "Rejected" => Color.FromArgb(247, 232, 232),
            _ when candidate.EvidenceValueScore >= 85 => Color.FromArgb(239, 250, 255),
            _ => Surface
        };
    }

    private void HandleEvidenceGridAction(DataGridViewCellEventArgs e)
    {
        if (_evidenceGrid == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;
        if (_evidenceGrid.Rows[e.RowIndex].Tag is not Guid id)
            return;

        string column = _evidenceGrid.Columns[e.ColumnIndex].Name;
        switch (column)
        {
            case "Approve":
                SetEvidenceCandidateStatus(id, "Approved");
                break;
            case "Reject":
                SetEvidenceCandidateStatus(id, "Rejected");
                break;
            case "Edit":
                SelectEvidenceGridRow(id);
                _evidenceTitleEditor?.Focus();
                break;
            case "Open":
                OpenEvidenceCandidateLocation(id);
                break;
        }
    }

    private Guid? GetSelectedEvidenceCandidateId()
    {
        if (_evidenceGrid == null || _evidenceGrid.IsDisposed)
            return null;
        if (_evidenceGrid.CurrentRow?.Tag is Guid id)
            return id;
        if (_evidenceGrid.SelectedRows.Count > 0 && _evidenceGrid.SelectedRows[0].Tag is Guid selectedId)
            return selectedId;
        return null;
    }

    private EvidenceCandidateItem? FindEvidenceCandidate(Guid id)
    {
        lock (_evidenceLock)
            return _evidenceCandidates.FirstOrDefault(item => item.Id == id);
    }

    private void SelectEvidenceGridRow(Guid id)
    {
        if (_evidenceGrid == null || _evidenceGrid.IsDisposed)
            return;

        foreach (DataGridViewRow row in _evidenceGrid.Rows)
        {
            if (row.Tag is Guid rowId && rowId == id)
            {
                row.Selected = true;
                _evidenceGrid.CurrentCell = row.Cells[0];
                return;
            }
        }
    }

    private void UpdateEvidenceInspectorFromSelection()
    {
        if (_evidenceInspectorReadout == null || _evidenceInspectorReadout.IsDisposed)
            return;

        var selectedId = GetSelectedEvidenceCandidateId();
        if (!selectedId.HasValue)
        {
            _evidenceInspectorCandidateId = null;
            _evidenceInspectorReadout.Text = EvidenceEmptyInspectorText();
            FillEvidenceEditor(null);
            return;
        }

        var item = FindEvidenceCandidate(selectedId.Value);
        if (item == null)
        {
            _evidenceInspectorCandidateId = null;
            _evidenceInspectorReadout.Text = EvidenceEmptyInspectorText();
            FillEvidenceEditor(null);
            return;
        }

        _evidenceInspectorCandidateId = item.Id;
        var builder = new StringBuilder();
        builder.AppendLine(item.SuggestedTitle);
        builder.AppendLine(new string('=', Math.Min(item.SuggestedTitle.Length, 80)));
        builder.AppendLine("Status: " + item.Status);
        builder.AppendLine("Category: " + item.Category);
        builder.AppendLine("Confidence score: " + item.ConfidenceScore);
        builder.AppendLine("Evidence value score: " + item.EvidenceValueScore);
        builder.AppendLine("Classification reason: " + item.ClassificationReason);
        builder.AppendLine("Recommended usage: " + item.RecommendedUsage);
        builder.AppendLine("Duplicate: " + (item.IsDuplicate ? "Yes" : "No"));
        if (!string.IsNullOrWhiteSpace(item.DuplicateMatch))
            builder.AppendLine("Duplicate match: " + item.DuplicateMatch);
        builder.AppendLine("File path: " + item.SourcePath);
        _evidenceInspectorReadout.Text = builder.ToString();
        FillEvidenceEditor(item);
    }

    private string EvidenceEmptyInspectorText()
    {
        if (_evidenceStatus == "Scanning")
            return "Scanning locally. You can switch modules while RoleAxis continues discovery.";
        if (_evidenceCandidates.Count == 0 && string.IsNullOrWhiteSpace(_evidenceSelectedFolder))
            return "Choose a local folder to discover evidence without uploading files.";
        if (_evidenceCandidates.Count == 0 && _evidenceStatus == "Completed")
            return "No evidence candidates found. Try another folder or enable broader file types.";
        if (_evidenceCandidates.Count == 0)
            return "Before scan: RoleAxis scans locally, classifies evidence candidates, and keeps original files untouched.";
        return "Select a candidate to inspect classification reason, confidence, value score, duplicate status, and recommended usage.";
    }

    private void FillEvidenceEditor(EvidenceCandidateItem? item)
    {
        if (_evidenceTitleEditor == null || _evidenceCategoryEditor == null || _evidenceValueEditor == null || _evidenceNotesEditor == null)
            return;

        bool enabled = item != null;
        _evidenceTitleEditor.Enabled = enabled;
        _evidenceCategoryEditor.Enabled = enabled;
        _evidenceValueEditor.Enabled = enabled;
        _evidenceNotesEditor.Enabled = enabled;
        _evidenceTitleEditor.Text = item?.SuggestedTitle ?? "";
        _evidenceCategoryEditor.SelectedItem = item?.Category ?? EvidenceCategoryRules[^1].Category;
        _evidenceValueEditor.Value = item == null ? 0 : Math.Clamp(item.EvidenceValueScore, 0, 100);
        _evidenceNotesEditor.Text = item?.Notes ?? "";
    }

    private void SaveEvidenceCandidateEdits()
    {
        if (!_evidenceInspectorCandidateId.HasValue)
            return;

        var item = FindEvidenceCandidate(_evidenceInspectorCandidateId.Value);
        if (item == null)
            return;

        lock (_evidenceLock)
        {
            item.SuggestedTitle = DisplayOrFallback(_evidenceTitleEditor?.Text ?? "", item.SuggestedTitle);
            item.Category = DisplayOrFallback(_evidenceCategoryEditor?.Text ?? "", item.Category);
            item.EvidenceValueScore = (int)(_evidenceValueEditor?.Value ?? item.EvidenceValueScore);
            item.Notes = _evidenceNotesEditor?.Text.Trim() ?? "";
        }

        AppendEvidenceLog("Updated candidate: " + item.SuggestedTitle);
        if (_evidenceSaveLocalIndex)
            SaveEvidenceLocalIndex();
        BindEvidenceStateToUi(rebuildGrid: true);
    }

    private void SetEvidenceCandidateStatus(Guid id, string status)
    {
        var item = FindEvidenceCandidate(id);
        if (item == null)
            return;
        lock (_evidenceLock)
            item.Status = status;

        AppendEvidenceLog(status + ": " + item.SuggestedTitle);
        if (_evidenceSaveLocalIndex)
            SaveEvidenceLocalIndex();
        BindEvidenceStateToUi(rebuildGrid: true);
    }

    private void OpenEvidenceCandidateLocation(Guid id)
    {
        var item = FindEvidenceCandidate(id);
        if (item == null || !File.Exists(item.SourcePath))
            return;
        Process.Start(new ProcessStartInfo("explorer.exe", "/select," + QuoteEvidencePath(item.SourcePath)) { UseShellExecute = true });
    }

    private void ExportEvidenceApprovedPackage()
    {
        var approved = SnapshotEvidenceCandidates()
            .Where(item => item.Status == "Approved")
            .OrderByDescending(item => item.EvidenceValueScore)
            .ThenByDescending(item => item.ConfidenceScore)
            .ToList();

        if (approved.Count == 0)
        {
            AppendEvidenceLog("Export skipped: approve at least one evidence candidate first.");
            return;
        }

        string exportRoot = Path.Combine(
            DesktopSessionState.LocalAppFolder,
            "EvidenceExports",
            "RoleAxis_Evidence_Package_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(exportRoot);
        foreach (var rule in EvidenceCategoryRules)
            Directory.CreateDirectory(Path.Combine(exportRoot, rule.FolderName));

        int exhibit = 1;
        foreach (var item in approved)
        {
            if (!File.Exists(item.SourcePath))
            {
                RegisterEvidenceError("Package copy skipped, source file missing: " + item.SourcePath);
                continue;
            }

            string exhibitNumber = "EXHIBIT_A" + exhibit.ToString("000");
            string safeTitle = SafeEvidenceFileName(item.SuggestedTitle);
            string exportedName = exhibitNumber + "_" + safeTitle + item.Extension;
            string categoryFolder = Path.Combine(exportRoot, EvidenceFolderName(item.Category));
            Directory.CreateDirectory(categoryFolder);
            string destination = UniqueEvidencePath(Path.Combine(categoryFolder, exportedName));

            try
            {
                File.Copy(item.SourcePath, destination, overwrite: false);
                lock (_evidenceLock)
                {
                    item.ExhibitNumber = exhibitNumber;
                    item.ExportedFileName = Path.GetRelativePath(exportRoot, destination);
                }
                exhibit++;
            }
            catch (Exception ex)
            {
                RegisterEvidenceError("Package copy failed: " + item.FileName + " - " + ex.Message);
            }
        }

        var packageItems = SnapshotEvidenceCandidates().Where(item => item.Status == "Approved").ToList();
        File.WriteAllText(Path.Combine(exportRoot, "evidence_index.csv"), BuildEvidenceIndexCsv(packageItems));
        File.WriteAllText(Path.Combine(exportRoot, "evidence_index.json"), BuildEvidenceIndexJson(packageItems));
        File.WriteAllText(Path.Combine(exportRoot, "evidence_summary.txt"), BuildEvidenceSummary(packageItems));
        _evidenceLastExportFolder = exportRoot;
        AppendEvidenceLog("Exported approved evidence package: " + exportRoot);
        LogActivity("Evidence", "Package exported", approved.Count.ToString("N0") + " approved candidates.");
        BindEvidenceStateToUi(rebuildGrid: true);
    }

    private void ExportEvidenceIndex(string format)
    {
        var candidates = SnapshotEvidenceCandidates();
        if (candidates.Count == 0)
        {
            AppendEvidenceLog("Export skipped: no evidence candidates available.");
            return;
        }

        string folder = Path.Combine(DesktopSessionState.LocalAppFolder, "EvidenceExports");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "evidence-index-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + format);
        if (format == "json")
            File.WriteAllText(path, BuildEvidenceIndexJson(candidates));
        else
            File.WriteAllText(path, BuildEvidenceIndexCsv(candidates));
        _evidenceLastExportFolder = folder;
        AppendEvidenceLog("Exported evidence index: " + path);
        LogActivity("Evidence", "Index exported", Path.GetFileName(path));
    }

    private void OpenEvidenceExportFolder()
    {
        string folder = _evidenceLastExportFolder;
        if (string.IsNullOrWhiteSpace(folder))
            folder = Path.Combine(DesktopSessionState.LocalAppFolder, "EvidenceExports");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", QuoteEvidencePath(folder)) { UseShellExecute = true });
    }

    private void SaveEvidenceLocalIndex()
    {
        try
        {
            Directory.CreateDirectory(DesktopSessionState.LocalAppFolder);
            string path = Path.Combine(DesktopSessionState.LocalAppFolder, "evidence-scan-index.json");
            File.WriteAllText(path, BuildEvidenceIndexJson(SnapshotEvidenceCandidates()));
        }
        catch (Exception ex)
        {
            RegisterEvidenceError("Local index save failed: " + ex.Message);
        }
    }

    private string BuildEvidenceIndexCsv(IEnumerable<EvidenceCandidateItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("exhibit_number,title,category,confidence_score,evidence_value_score,original_filename,source_path,exported_filename,status,notes");
        foreach (var item in items)
        {
            builder.AppendLine(string.Join(",",
                Csv(item.ExhibitNumber),
                Csv(item.SuggestedTitle),
                Csv(item.Category),
                item.ConfidenceScore,
                item.EvidenceValueScore,
                Csv(item.FileName),
                Csv(item.SourcePath),
                Csv(item.ExportedFileName),
                Csv(item.Status),
                Csv(item.Notes)));
        }
        return builder.ToString();
    }

    private static string BuildEvidenceIndexJson(IEnumerable<EvidenceCandidateItem> items)
    {
        var rows = items.Select(item => new EvidenceExportRow
        {
            ExhibitNumber = item.ExhibitNumber,
            Title = item.SuggestedTitle,
            Category = item.Category,
            ConfidenceScore = item.ConfidenceScore,
            EvidenceValueScore = item.EvidenceValueScore,
            OriginalFilename = item.FileName,
            SourcePath = item.SourcePath,
            ExportedFilename = item.ExportedFileName,
            Status = item.Status,
            Notes = item.Notes,
            Duplicate = item.IsDuplicate,
            ClassificationReason = item.ClassificationReason,
            RecommendedUsage = item.RecommendedUsage
        }).ToList();
        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }

    private string BuildEvidenceSummary(List<EvidenceCandidateItem> packageItems)
    {
        var all = SnapshotEvidenceCandidates();
        var builder = new StringBuilder();
        builder.AppendLine("RoleAxis Evidence Package Summary");
        builder.AppendLine("=================================");
        builder.AppendLine("Scan folder: " + DisplayOrFallback(_evidenceSelectedFolder, "Not selected"));
        builder.AppendLine("Scan date: " + (_evidenceLastScanAt?.ToString("F") ?? DateTime.Now.ToString("F")));
        builder.AppendLine("Total files scanned: " + _evidenceFilesVisited.ToString("N0"));
        builder.AppendLine("Candidates found: " + all.Count.ToString("N0"));
        builder.AppendLine("Approved count: " + packageItems.Count.ToString("N0"));
        builder.AppendLine("Rejected count: " + all.Count(item => item.Status == "Rejected").ToString("N0"));
        builder.AppendLine();
        builder.AppendLine("Category counts:");
        foreach (var group in packageItems.GroupBy(item => item.Category).OrderByDescending(group => group.Count()))
            builder.AppendLine("- " + group.Key + ": " + group.Count().ToString("N0"));
        builder.AppendLine();
        builder.AppendLine("High-value evidence:");
        foreach (var item in packageItems.Where(item => item.EvidenceValueScore >= 80).OrderByDescending(item => item.EvidenceValueScore).Take(20))
            builder.AppendLine("- " + DisplayOrFallback(item.ExhibitNumber, "[pending]") + "  " + item.SuggestedTitle + " (" + item.Category + ", value " + item.EvidenceValueScore + ")");
        return builder.ToString();
    }

    private static string EvidenceFolderName(string category)
    {
        var rule = EvidenceCategoryRules.FirstOrDefault(rule => rule.Category == category);
        return string.IsNullOrWhiteSpace(rule.FolderName) ? "Other_Supporting_Evidence" : rule.FolderName;
    }

    private static string SafeEvidenceFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder();
        foreach (char ch in DisplayOrFallback(value, "Evidence"))
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        string safe = string.Join("_", builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (safe.Length > 80)
            safe = safe[..80];
        return string.IsNullOrWhiteSpace(safe) ? "Evidence" : safe;
    }

    private static string UniqueEvidencePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string folder = Path.GetDirectoryName(path) ?? "";
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 2; i < 1000; i++)
        {
            string candidate = Path.Combine(folder, name + "_" + i + extension);
            if (!File.Exists(candidate))
                return candidate;
        }
        return Path.Combine(folder, name + "_" + Guid.NewGuid().ToString("N")[..8] + extension);
    }

    private static string FormatEvidenceBytes(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";
        double value = bytes / 1024d;
        if (value < 1024)
            return value.ToString("0.0") + " KB";
        value /= 1024d;
        if (value < 1024)
            return value.ToString("0.0") + " MB";
        return (value / 1024d).ToString("0.0") + " GB";
    }

    private static Color EvidenceStatusColor(string status)
    {
        return status switch
        {
            "Scanning" => Color.FromArgb(14, 160, 201),
            "Paused" => Color.FromArgb(237, 91, 0),
            "Completed" => Color.FromArgb(18, 154, 99),
            "Stopped" => Color.FromArgb(86, 105, 120),
            "Cancelled" => Color.FromArgb(122, 134, 148),
            "Error" => Color.FromArgb(148, 51, 57),
            _ => Color.FromArgb(7, 18, 24)
        };
    }

    private static string QuoteEvidencePath(string path)
    {
        return "\"" + path.Replace("\"", "") + "\"";
    }
}

internal readonly record struct EvidenceCategoryRule(string Category, string FolderName, int BaseValue, string[] Keywords);

internal readonly record struct EvidenceClassification(string Category, int Confidence, int EvidenceValue, string Reason);

internal sealed class EvidenceCandidateItem
{
    public Guid Id { get; set; }
    public string SourcePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Extension { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string SuggestedTitle { get; set; } = "";
    public string Category { get; set; } = "Other Supporting Evidence";
    public int ConfidenceScore { get; set; }
    public int EvidenceValueScore { get; set; }
    public string ClassificationReason { get; set; } = "";
    public string Hash { get; set; } = "";
    public bool IsDuplicate { get; set; }
    public string DuplicateMatch { get; set; } = "";
    public string RecommendedUsage { get; set; } = "";
    public string Notes { get; set; } = "";
    public string ExhibitNumber { get; set; } = "";
    public string ExportedFileName { get; set; } = "";
    public DateTime DiscoveredAt { get; set; }
}

internal sealed class EvidenceExportRow
{
    public string ExhibitNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public int ConfidenceScore { get; set; }
    public int EvidenceValueScore { get; set; }
    public string OriginalFilename { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string ExportedFilename { get; set; } = "";
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Duplicate { get; set; }
    public string ClassificationReason { get; set; } = "";
    public string RecommendedUsage { get; set; } = "";
}
