using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RoleAxis.Career.InterviewAssistant;

public sealed partial class RoleAxisDesktopAppForm
{
    private readonly ResumeAnalysisService _careerResumeService = new();
    private readonly JobIntelligenceService _careerJobService = new();
    private readonly CareerStorageService _careerStorageService = new();

    private ResumeAnalysisInput _resumeInputState = new();
    private ResumeAnalysisResult? _resumeAnalysisResult;
    private CancellationTokenSource? _resumeWorkCts;
    private bool _resumeBusy;
    private int _resumeSectionIndex;

    private RichTextBox? _resumeInputBox;
    private TextBox? _resumeTargetRoleBox;
    private TextBox? _resumeTargetIndustryBox;
    private TextBox? _resumeCompanyBox;
    private TextBox? _resumeJobUrlBox;
    private RichTextBox? _resumeJobDescriptionBox;
    private ComboBox? _resumeSeniorityCombo;
    private ComboBox? _resumeTypeCombo;
    private ComboBox? _resumeOutputStyleCombo;
    private Label? _resumeStatusBadge;
    private Label? _resumeSummaryLabel;
    private Label? _resumeProgressLabel;
    private TabControl? _resumeTabs;
    private ComboBox? _resumeSectionCombo;
    private readonly Dictionary<string, Label> _resumeScoreLabels = new();
    private DataGridView? _resumeMetadataGrid;
    private DataGridView? _resumeAtsGrid;
    private DataGridView? _resumeSkillsGrid;
    private DataGridView? _resumeGapGrid;
    private DataGridView? _resumeExperienceGrid;
    private DataGridView? _resumeBulletGrid;
    private DataGridView? _resumeKeywordGrid;
    private DataGridView? _resumeRiskGrid;
    private DataGridView? _resumePlanGrid;
    private RichTextBox? _resumeInputSummaryBox;
    private RichTextBox? _resumeOverviewBox;
    private RichTextBox? _resumeJobMatchBox;
    private RichTextBox? _resumeOutputBox;
    private RichTextBox? _resumeExportBox;

    private static readonly string[] ResumeSections =
    {
        "Input",
        "Overview Score",
        "ATS Analysis",
        "Skills Gap",
        "Experience Match",
        "Bullet Improvements",
        "Job Match",
        "Upgraded Resume",
        "Export"
    };

    private void ShowResumeIntelligence()
    {
        LogActivity("Resume", "Opened Resume Intelligence", "Resume analysis workspace launched.");
        ClearContent();
        SetView("resume", "RoleAxis Resume Intelligence", "Deep resume analysis, skill gap detection, ATS scoring, and targeted resume generation.");
        _contentHost.AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Navy
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildResumeInputStudio(), 0, 0);
        root.Controls.Add(BuildResumeReportStudio(), 1, 0);
        _contentHost.Controls.Add(root);

        BindResumeStateToUi();
    }

    private Control BuildResumeInputStudio()
    {
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Navy,
            Margin = new Padding(0, 0, 14, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        left.Controls.Add(BuildResumePasteCard(), 0, 0);
        left.Controls.Add(BuildResumeTargetCard(), 0, 1);
        left.Controls.Add(BuildResumeActionCard(), 0, 2);
        return left;
    }

    private Control BuildResumePasteCard()
    {
        var card = CreateCareerCard("Resume Input", out var body);
        body.Padding = new Padding(14, 36, 14, 12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        var browse = CreateCareerButton("Browse Resume", 132, BlackButton, Color.White);
        var paste = CreateCareerButton("Paste Clipboard", 126, Surface2, Ink);
        browse.Click += async (_, _) => await LoadResumeFileAsync();
        paste.Click += (_, _) =>
        {
            if (Clipboard.ContainsText())
                _resumeInputBox!.Text = Clipboard.GetText();
        };
        actions.Controls.Add(browse);
        actions.Controls.Add(paste);

        _resumeInputBox = CreateCareerRichBox(readOnly: false);
        _resumeInputBox.TextChanged += (_, _) => CaptureResumeInputState();

        var hint = CreateLabel("Supported: TXT, RTF, DOCX, PDF. Paste fallback is always available.", 8.2f, SoftText);
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.MiddleLeft;

        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(_resumeInputBox, 0, 1);
        layout.Controls.Add(hint, 0, 2);
        body.Controls.Add(layout);
        return card;
    }

    private Control BuildResumeTargetCard()
    {
        var card = CreateCareerCard("Target Role / Job", out var body);
        body.Padding = new Padding(14, 34, 14, 12);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent
        };
        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        _resumeTargetRoleBox = AddCareerTextField(fields, "Target role", _resumeInputState.TargetRole);
        _resumeTargetIndustryBox = AddCareerTextField(fields, "Target industry", _resumeInputState.TargetIndustry);
        _resumeCompanyBox = AddCareerTextField(fields, "Company name", _resumeInputState.CompanyName);
        _resumeJobUrlBox = AddCareerTextField(fields, "Job URL", _resumeInputState.JobUrl);
        _resumeSeniorityCombo = AddCareerComboField(fields, "Seniority level", new[] { "Entry", "Mid-level", "Senior", "Lead", "Manager", "Executive" }, _resumeInputState.SeniorityLevel);
        _resumeTypeCombo = AddCareerComboField(fields, "Resume type", new[] { "General", "Technical", "Executive", "Federal", "Academic CV", "Immigration/NIW profile" }, _resumeInputState.ResumeType);
        _resumeOutputStyleCombo = AddCareerComboField(fields, "Output style", new[] { "ATS optimized", "Executive", "Technical", "Concise", "Achievement-heavy" }, _resumeInputState.OutputStyle);

        var jdBlock = new Panel { Width = 372, Height = 148, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8) };
        var jdLabel = CreateLabel("Optional job description", 8.2f, SoftText, FontStyle.Bold);
        jdLabel.Location = new Point(0, 0);
        jdLabel.Width = 360;
        jdLabel.Height = 20;
        _resumeJobDescriptionBox = CreateCareerRichBox(readOnly: false);
        _resumeJobDescriptionBox.Location = new Point(0, 24);
        _resumeJobDescriptionBox.Width = 360;
        _resumeJobDescriptionBox.Height = 118;
        _resumeJobDescriptionBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _resumeJobDescriptionBox.TextChanged += (_, _) => CaptureResumeInputState();
        jdBlock.Controls.Add(jdLabel);
        jdBlock.Controls.Add(_resumeJobDescriptionBox);
        fields.Controls.Add(jdBlock);

        foreach (var input in new Control?[] { _resumeTargetRoleBox, _resumeTargetIndustryBox, _resumeCompanyBox, _resumeJobUrlBox })
            if (input is TextBox box)
                box.TextChanged += (_, _) => CaptureResumeInputState();
        foreach (var combo in new[] { _resumeSeniorityCombo, _resumeTypeCombo, _resumeOutputStyleCombo })
            if (combo != null)
                combo.SelectedIndexChanged += (_, _) => CaptureResumeInputState();

        scroll.Controls.Add(fields);
        body.Controls.Add(scroll);
        return card;
    }

    private Control BuildResumeActionCard()
    {
        var card = CreateCareerCard("Actions", out var body);
        body.Padding = new Padding(14, 34, 14, 12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        _resumeStatusBadge = CreateCareerBadge("Ready", BlackButton);
        _resumeStatusBadge.Dock = DockStyle.Left;
        _resumeStatusBadge.Width = 112;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent
        };
        var analyze = CreateCareerButton("Analyze Resume", 136, BlackButton, Color.White);
        var analyzeJob = CreateCareerButton("Analyze Against Job", 158, Cyan, Color.White);
        var upgrade = CreateCareerButton("Generate Upgraded", 154, Surface2, Ink);
        var tailor = CreateCareerButton("Generate Tailored", 150, Surface2, Ink);
        var cancel = CreateCareerButton("Cancel", 76, Surface2, Ink);
        var clear = CreateCareerButton("Clear", 76, Surface2, Ink);
        analyze.Click += async (_, _) => await AnalyzeResumeAsync(requireJob: false);
        analyzeJob.Click += async (_, _) => await AnalyzeResumeAsync(requireJob: true);
        upgrade.Click += async (_, _) => await GenerateUpgradedResumeAsync(tailored: false);
        tailor.Click += async (_, _) => await GenerateUpgradedResumeAsync(tailored: true);
        cancel.Click += (_, _) =>
        {
            _resumeWorkCts?.Cancel();
            SetResumeStatus("Cancelling", Orange, "Cancelling current resume task.");
        };
        clear.Click += (_, _) => ClearResumeModule();
        actions.Controls.Add(analyze);
        actions.Controls.Add(analyzeJob);
        actions.Controls.Add(upgrade);
        actions.Controls.Add(tailor);
        actions.Controls.Add(cancel);
        actions.Controls.Add(clear);

        _resumeProgressLabel = CreateLabel("Paste a resume or load a file to generate a full RoleAxis resume intelligence report.", 8.2f, SoftText);
        _resumeProgressLabel.Dock = DockStyle.Fill;

        layout.Controls.Add(_resumeStatusBadge, 0, 0);
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(_resumeProgressLabel, 0, 2);
        body.Controls.Add(layout);
        return card;
    }

    private Control BuildResumeReportStudio()
    {
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Navy
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

        right.Controls.Add(BuildResumeScoreBand(), 0, 0);
        right.Controls.Add(BuildResumeNavigator(), 0, 1);
        right.Controls.Add(BuildResumeTabs(), 0, 2);
        return right;
    }

    private Control BuildResumeScoreBand()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Navy,
            Margin = new Padding(0, 0, 0, 10)
        };
        for (int i = 0; i < 5; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        _resumeScoreLabels.Clear();
        grid.Controls.Add(CreateCareerScoreCard("Overall", "overall"), 0, 0);
        grid.Controls.Add(CreateCareerScoreCard("ATS", "ats"), 1, 0);
        grid.Controls.Add(CreateCareerScoreCard("Keywords", "keywords"), 2, 0);
        grid.Controls.Add(CreateCareerScoreCard("Impact", "impact"), 3, 0);
        grid.Controls.Add(CreateCareerScoreCard("Job Match", "job"), 4, 0);
        return grid;
    }

    private Control BuildResumeNavigator()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 14,
            BackColor = Surface,
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(0, 0, 0, 10)
        };

        var previous = CreateCareerButton("Previous", 98, Surface2, Ink);
        previous.Location = new Point(12, 8);
        previous.Click += (_, _) => SetResumeSection(_resumeSectionIndex - 1);

        var next = CreateCareerButton("Next", 82, BlackButton, Color.White);
        next.Location = new Point(116, 8);
        next.Click += (_, _) => SetResumeSection(_resumeSectionIndex + 1);

        _resumeSectionCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.8f),
            Location = new Point(210, 10),
            Width = 240
        };
        _resumeSectionCombo.Items.AddRange(ResumeSections.Cast<object>().ToArray());
        _resumeSectionCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_resumeSectionCombo.SelectedIndex >= 0)
                SetResumeSection(_resumeSectionCombo.SelectedIndex);
        };

        _resumeSummaryLabel = CreateLabel("Section 1 of 9", 8.6f, SoftText, FontStyle.Bold);
        _resumeSummaryLabel.Location = new Point(464, 12);
        _resumeSummaryLabel.Width = 420;
        _resumeSummaryLabel.Height = 22;
        _resumeSummaryLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        card.Controls.Add(previous);
        card.Controls.Add(next);
        card.Controls.Add(_resumeSectionCombo);
        card.Controls.Add(_resumeSummaryLabel);
        return card;
    }

    private Control BuildResumeTabs()
    {
        _resumeTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 8.8f, FontStyle.Bold),
            HotTrack = true
        };
        _resumeTabs.SelectedIndexChanged += (_, _) =>
        {
            if (_resumeTabs.SelectedIndex >= 0 && _resumeTabs.SelectedIndex != _resumeSectionIndex)
                SetResumeSection(_resumeTabs.SelectedIndex);
        };

        _resumeTabs.TabPages.Add(BuildResumeTextTab("Input", out _resumeInputSummaryBox));
        _resumeTabs.TabPages.Add(BuildResumeOverviewTab());
        _resumeTabs.TabPages.Add(BuildResumeGridTab("ATS Analysis", out _resumeAtsGrid));
        _resumeTabs.TabPages.Add(BuildResumeSkillsTab());
        _resumeTabs.TabPages.Add(BuildResumeGridTab("Experience Match", out _resumeExperienceGrid));
        _resumeTabs.TabPages.Add(BuildResumeGridTab("Bullet Improvements", out _resumeBulletGrid));
        _resumeTabs.TabPages.Add(BuildResumeJobMatchTab());
        _resumeTabs.TabPages.Add(BuildResumeOutputTab());
        _resumeTabs.TabPages.Add(BuildResumeExportTab());
        return _resumeTabs;
    }

    private TabPage BuildResumeOverviewTab()
    {
        var page = new TabPage("Overview Score") { BackColor = Navy };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Navy,
            Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));

        layout.Controls.Add(WrapCareerPanel("Executive Summary", _resumeOverviewBox = CreateCareerRichBox(readOnly: true)), 0, 0);
        layout.Controls.Add(WrapCareerPanel("Resume Metadata", _resumeMetadataGrid = CreateCareerGrid()), 1, 0);
        layout.Controls.Add(WrapCareerPanel("Recruiter Risks", _resumeRiskGrid = CreateCareerGrid()), 0, 1);
        layout.Controls.Add(WrapCareerPanel("Recommendation Plan", _resumePlanGrid = CreateCareerGrid()), 1, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildResumeSkillsTab()
    {
        var page = new TabPage("Skills Gap") { BackColor = Navy };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        layout.Controls.Add(WrapCareerPanel("Extracted Skills", _resumeSkillsGrid = CreateCareerGrid()), 0, 0);
        layout.Controls.Add(WrapCareerPanel("Skill Gap Analysis", _resumeGapGrid = CreateCareerGrid()), 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildResumeJobMatchTab()
    {
        var page = new TabPage("Job Match") { BackColor = Navy };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _resumeJobMatchBox = CreateCareerRichBox(readOnly: true);
        layout.Controls.Add(WrapCareerPanel("Job Fit Summary", _resumeJobMatchBox), 0, 0);
        layout.Controls.Add(WrapCareerPanel("Keyword Analysis", _resumeKeywordGrid = CreateCareerGrid()), 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildResumeOutputTab()
    {
        var page = new TabPage("Upgraded Resume") { BackColor = Navy };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Navy, FlowDirection = FlowDirection.LeftToRight };
        var copy = CreateCareerButton("Copy Resume", 118, BlackButton, Color.White);
        var saveTxt = CreateCareerButton("Save as TXT", 110, Surface2, Ink);
        var report = CreateCareerButton("Export Report", 120, Surface2, Ink);
        var stronger = CreateCareerButton("Regenerate Stronger", 150, Cyan, Color.White);
        var cover = CreateCareerButton("Generate Cover Letter", 168, Surface2, Ink);
        var interview = CreateCareerButton("Send to Interview Prep", 170, Surface2, Ink);
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(_resumeOutputBox?.Text)) Clipboard.SetText(_resumeOutputBox.Text); };
        saveTxt.Click += (_, _) => ExportResumeText();
        report.Click += (_, _) => ExportResumeReport();
        stronger.Click += async (_, _) => await GenerateUpgradedResumeAsync(tailored: !string.IsNullOrWhiteSpace(_resumeInputState.JobDescription));
        cover.Click += (_, _) => GenerateResumeCoverLetter();
        interview.Click += (_, _) => SendResumeToInterviewPrep();
        actions.Controls.Add(copy);
        actions.Controls.Add(saveTxt);
        actions.Controls.Add(report);
        actions.Controls.Add(stronger);
        actions.Controls.Add(cover);
        actions.Controls.Add(interview);

        _resumeOutputBox = CreateCareerRichBox(readOnly: true);
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(WrapCareerPanel("Generated Resume / Career Output", _resumeOutputBox), 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildResumeExportTab()
    {
        var page = new TabPage("Export") { BackColor = Navy };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Navy };
        var exportReport = CreateCareerButton("Save Report JSON", 140, BlackButton, Color.White);
        var exportCsv = CreateCareerButton("Export Analysis CSV", 150, Surface2, Ink);
        var exportResume = CreateCareerButton("Export Resume TXT", 144, Surface2, Ink);
        var openFolder = CreateCareerButton("Open Resume Folder", 156, Surface2, Ink);
        exportReport.Click += (_, _) => ExportResumeReport();
        exportCsv.Click += (_, _) => ExportResumeCsv();
        exportResume.Click += (_, _) => ExportResumeText();
        openFolder.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_careerStorageService.ResumeFolder) { UseShellExecute = true });
        actions.Controls.Add(exportReport);
        actions.Controls.Add(exportCsv);
        actions.Controls.Add(exportResume);
        actions.Controls.Add(openFolder);
        _resumeExportBox = CreateCareerRichBox(readOnly: true);
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(WrapCareerPanel("Export Log", _resumeExportBox), 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildResumeTextTab(string title, out RichTextBox box)
    {
        var page = new TabPage(title) { BackColor = Navy };
        box = CreateCareerRichBox(readOnly: true);
        page.Controls.Add(WrapCareerPanel(title + " Summary", box));
        return page;
    }

    private TabPage BuildResumeGridTab(string title, out DataGridView grid)
    {
        var page = new TabPage(title) { BackColor = Navy };
        grid = CreateCareerGrid();
        page.Controls.Add(WrapCareerPanel(title, grid));
        return page;
    }

    private async Task LoadResumeFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Load resume",
            Filter = "Supported resume files (*.txt;*.rtf;*.docx;*.pdf)|*.txt;*.rtf;*.docx;*.pdf|Text files (*.txt)|*.txt|Word documents (*.docx)|*.docx|PDF files (*.pdf)|*.pdf|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            SetResumeBusy(true, "Loading resume file...");
            string text = await _careerResumeService.LoadResumeFileAsync(dialog.FileName);
            _resumeInputBox!.Text = text;
            SetResumeStatus("Loaded", Mint, "Loaded resume file: " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            SetResumeStatus("Load failed", Orange, ex.Message);
        }
        finally
        {
            SetResumeBusy(false);
        }
    }

    private async Task AnalyzeResumeAsync(bool requireJob)
    {
        CaptureResumeInputState();
        if (string.IsNullOrWhiteSpace(_resumeInputState.ResumeText))
        {
            SetResumeStatus("Needs resume", Orange, "Paste or load a resume first.");
            return;
        }
        if (requireJob && string.IsNullOrWhiteSpace(_resumeInputState.JobDescription))
        {
            SetResumeStatus("Needs job", Orange, "Paste a job description before running job match analysis.");
            return;
        }

        _resumeWorkCts?.Cancel();
        _resumeWorkCts = new CancellationTokenSource();
        try
        {
            SetResumeBusy(true, requireJob ? "Analyzing resume against target job..." : "Analyzing resume...");
            _resumeAnalysisResult = await _careerResumeService.AnalyzeAsync(_resumeInputState, _resumeWorkCts.Token);
            BindResumeResultToUi();
            SetResumeStatus("Analyzed", Mint, "Resume intelligence report ready.");
            LogActivity("Resume", "Resume analyzed", DisplayOrFallback(_resumeInputState.TargetRole, "General resume analysis"));
            SetResumeSection(requireJob ? 6 : 1);
        }
        catch (OperationCanceledException)
        {
            SetResumeStatus("Cancelled", Orange, "Resume analysis cancelled.");
        }
        catch (Exception ex)
        {
            SetResumeStatus("Error", Orange, ex.Message);
        }
        finally
        {
            SetResumeBusy(false);
        }
    }

    private async Task GenerateUpgradedResumeAsync(bool tailored)
    {
        CaptureResumeInputState();
        if (string.IsNullOrWhiteSpace(_resumeInputState.ResumeText))
        {
            SetResumeStatus("Needs resume", Orange, "Paste or load a resume first.");
            return;
        }
        if (tailored && string.IsNullOrWhiteSpace(_resumeInputState.JobDescription))
        {
            SetResumeStatus("Needs job", Orange, "Add a job description before generating a tailored resume.");
            return;
        }
        if (_resumeAnalysisResult == null)
            _resumeAnalysisResult = await _careerResumeService.AnalyzeAsync(_resumeInputState);

        try
        {
            SetResumeBusy(true, tailored ? "Generating tailored resume..." : "Generating upgraded resume...");
            string output = await Task.Run(() => tailored
                ? _careerResumeService.GenerateTailoredResume(_resumeInputState, _resumeAnalysisResult)
                : _careerResumeService.GenerateUpgradedResume(_resumeInputState, _resumeAnalysisResult));
            if (tailored)
                _resumeAnalysisResult.TailoredResume = output;
            else
                _resumeAnalysisResult.UpgradedResume = output;
            _resumeOutputBox!.Text = output;
            SetResumeSection(7);
            SetResumeStatus("Generated", Mint, tailored ? "Tailored resume generated." : "Upgraded resume generated.");
            LogActivity("Resume", tailored ? "Tailored resume generated" : "Upgraded resume generated", DisplayOrFallback(_resumeInputState.TargetRole, "Resume output"));
        }
        catch (Exception ex)
        {
            SetResumeStatus("Generate failed", Orange, ex.Message);
        }
        finally
        {
            SetResumeBusy(false);
        }
    }

    private void GenerateResumeCoverLetter()
    {
        if (_resumeAnalysisResult == null)
        {
            SetResumeStatus("Analyze first", Orange, "Analyze the resume before generating a cover letter.");
            return;
        }
        var job = new SavedJob
        {
            Title = _resumeInputState.TargetRole,
            Company = _resumeInputState.CompanyName,
            JobUrl = _resumeInputState.JobUrl,
            JobDescription = _resumeInputState.JobDescription
        };
        var report = _careerJobService.AnalyzeJobMatch(_resumeInputState, job);
        _resumeAnalysisResult.CoverLetter = _careerJobService.GenerateCoverLetter(_resumeInputState, job, report);
        _resumeOutputBox!.Text = _resumeAnalysisResult.CoverLetter;
        SetResumeSection(7);
        SetResumeStatus("Cover letter", Mint, "Cover letter generated from resume and target job.");
        LogActivity("Resume", "Cover letter generated", DisplayOrFallback(_resumeInputState.CompanyName, "Target job"));
    }

    private void SendResumeToInterviewPrep()
    {
        if (_resumeAnalysisResult == null)
        {
            SetResumeStatus("Analyze first", Orange, "Analyze or generate a resume before sending context to interview prep.");
            return;
        }
        ShowInterviewAssistant(_resumeInputState.ResumeText, _resumeInputState.JobDescription);
    }

    private void ExportResumeReport()
    {
        if (_resumeAnalysisResult == null)
        {
            SetResumeStatus("No report", Orange, "Analyze a resume before exporting a report.");
            return;
        }
        string path = _careerStorageService.SaveResumeReport(_resumeAnalysisResult);
        AppendResumeExport("Report saved: " + path);
        LogActivity("Resume", "Resume report exported", Path.GetFileName(path));
    }

    private void ExportResumeCsv()
    {
        if (_resumeAnalysisResult == null)
        {
            SetResumeStatus("No report", Orange, "Analyze a resume before exporting CSV.");
            return;
        }
        string path = _careerStorageService.SaveAnalysisCsv(_resumeAnalysisResult);
        AppendResumeExport("Analysis CSV saved: " + path);
        LogActivity("Resume", "Resume analysis CSV exported", Path.GetFileName(path));
    }

    private void ExportResumeText()
    {
        string text = _resumeOutputBox?.Text.Trim() ?? _resumeAnalysisResult?.UpgradedResume ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            SetResumeStatus("No resume", Orange, "Generate an upgraded or tailored resume first.");
            return;
        }
        string path = _careerStorageService.SaveResumeText(text);
        AppendResumeExport("Resume saved: " + path);
        LogActivity("Resume", "Resume text exported", Path.GetFileName(path));
    }

    private void ClearResumeModule()
    {
        _resumeWorkCts?.Cancel();
        _resumeInputState = new ResumeAnalysisInput();
        _resumeAnalysisResult = null;
        _resumeSectionIndex = 0;
        BindResumeStateToUi();
        SetResumeStatus("Ready", BlackButton, "Paste a resume or load a file to generate a full RoleAxis resume intelligence report.");
    }

    private void CaptureResumeInputState()
    {
        _resumeInputState.ResumeText = _resumeInputBox?.Text ?? _resumeInputState.ResumeText;
        _resumeInputState.TargetRole = _resumeTargetRoleBox?.Text ?? _resumeInputState.TargetRole;
        _resumeInputState.TargetIndustry = _resumeTargetIndustryBox?.Text ?? _resumeInputState.TargetIndustry;
        _resumeInputState.CompanyName = _resumeCompanyBox?.Text ?? _resumeInputState.CompanyName;
        _resumeInputState.JobUrl = _resumeJobUrlBox?.Text ?? _resumeInputState.JobUrl;
        _resumeInputState.JobDescription = _resumeJobDescriptionBox?.Text ?? _resumeInputState.JobDescription;
        _resumeInputState.SeniorityLevel = _resumeSeniorityCombo?.Text ?? _resumeInputState.SeniorityLevel;
        _resumeInputState.ResumeType = _resumeTypeCombo?.Text ?? _resumeInputState.ResumeType;
        _resumeInputState.OutputStyle = _resumeOutputStyleCombo?.Text ?? _resumeInputState.OutputStyle;
    }

    private void BindResumeStateToUi()
    {
        if (_resumeInputBox != null) _resumeInputBox.Text = _resumeInputState.ResumeText;
        if (_resumeTargetRoleBox != null) _resumeTargetRoleBox.Text = _resumeInputState.TargetRole;
        if (_resumeTargetIndustryBox != null) _resumeTargetIndustryBox.Text = _resumeInputState.TargetIndustry;
        if (_resumeCompanyBox != null) _resumeCompanyBox.Text = _resumeInputState.CompanyName;
        if (_resumeJobUrlBox != null) _resumeJobUrlBox.Text = _resumeInputState.JobUrl;
        if (_resumeJobDescriptionBox != null) _resumeJobDescriptionBox.Text = _resumeInputState.JobDescription;
        SetComboText(_resumeSeniorityCombo, _resumeInputState.SeniorityLevel);
        SetComboText(_resumeTypeCombo, _resumeInputState.ResumeType);
        SetComboText(_resumeOutputStyleCombo, _resumeInputState.OutputStyle);
        BindResumeResultToUi();
        SetResumeSection(_resumeSectionIndex);
    }

    private void BindResumeResultToUi()
    {
        var result = _resumeAnalysisResult;
        if (result == null)
        {
            SetResumeScores(new ResumeScoreSummary());
            SetGridData(_resumeMetadataGrid, new[] { new { Field = "Status", Value = "Paste a resume or load a file to begin." } });
            SetGridData(_resumeAtsGrid, Array.Empty<ResumeFinding>());
            SetGridData(_resumeSkillsGrid, Array.Empty<SkillFinding>());
            SetGridData(_resumeGapGrid, Array.Empty<SkillGapFinding>());
            SetGridData(_resumeExperienceGrid, Array.Empty<ResumeFinding>());
            SetGridData(_resumeBulletGrid, Array.Empty<BulletImprovement>());
            SetGridData(_resumeKeywordGrid, Array.Empty<KeywordFinding>());
            SetGridData(_resumeRiskGrid, Array.Empty<ResumeFinding>());
            SetGridData(_resumePlanGrid, Array.Empty<ActionPlanItem>());
            if (_resumeOverviewBox != null)
                _resumeOverviewBox.Text = "Paste a resume or load a file to generate a full RoleAxis resume intelligence report.";
            if (_resumeInputSummaryBox != null)
                _resumeInputSummaryBox.Text = "Resume input status: no resume loaded.\r\n\r\nUse the left panel to paste a resume, browse a local file, and set target role context.";
            if (_resumeJobMatchBox != null)
                _resumeJobMatchBox.Text = "Add a job description to generate job match analysis.";
            if (_resumeOutputBox != null)
                _resumeOutputBox.Text = "Generate an upgraded or tailored resume to view it here.";
            return;
        }

        SetResumeScores(result.Scores);
        SetGridData(_resumeMetadataGrid, ToMetadataRows(result.Metadata));
        SetGridData(_resumeAtsGrid, result.AtsFindings);
        SetGridData(_resumeSkillsGrid, result.Skills);
        SetGridData(_resumeGapGrid, result.SkillGaps);
        SetGridData(_resumeExperienceGrid, result.ExperienceFindings);
        SetGridData(_resumeBulletGrid, result.WeakBullets);
        SetGridData(_resumeKeywordGrid, result.Keywords);
        SetGridData(_resumeRiskGrid, result.RecruiterRisks);
        SetGridData(_resumePlanGrid, result.RecommendationPlan);
        if (_resumeOverviewBox != null)
            _resumeOverviewBox.Text = result.ExecutiveSummary;
        if (_resumeInputSummaryBox != null)
            _resumeInputSummaryBox.Text =
                "Resume input status: " + _resumeInputState.ResumeText.Length.ToString("N0") + " characters loaded.\r\n" +
                "Target role: " + DisplayOrFallback(_resumeInputState.TargetRole, "Not set") + "\r\n" +
                "Target industry: " + DisplayOrFallback(_resumeInputState.TargetIndustry, "Not set") + "\r\n" +
                "Job description: " + (string.IsNullOrWhiteSpace(_resumeInputState.JobDescription) ? "Not provided" : _resumeInputState.JobDescription.Length.ToString("N0") + " characters") + "\r\n\r\n" +
                result.ExecutiveSummary;
        if (_resumeJobMatchBox != null)
            _resumeJobMatchBox.Text = result.JobMatchSummary;
        if (_resumeOutputBox != null)
            _resumeOutputBox.Text = !string.IsNullOrWhiteSpace(result.TailoredResume)
                ? result.TailoredResume
                : !string.IsNullOrWhiteSpace(result.UpgradedResume)
                    ? result.UpgradedResume
                    : "Generate an upgraded or tailored resume to view it here.";
    }

    private void SetResumeScores(ResumeScoreSummary scores)
    {
        SetScore("overall", scores.Overall);
        SetScore("ats", scores.Ats);
        SetScore("keywords", scores.KeywordAlignment);
        SetScore("impact", scores.Impact);
        SetScore("job", scores.JobMatch);
    }

    private void SetScore(string key, int value)
    {
        if (_resumeScoreLabels.TryGetValue(key, out var label) && !label.IsDisposed)
            label.Text = value <= 0 ? "--" : value.ToString("00");
    }

    private void SetResumeSection(int index)
    {
        index = Math.Clamp(index, 0, ResumeSections.Length - 1);
        _resumeSectionIndex = index;
        if (_resumeTabs != null && _resumeTabs.SelectedIndex != index)
            _resumeTabs.SelectedIndex = index;
        if (_resumeSectionCombo != null && _resumeSectionCombo.SelectedIndex != index)
            _resumeSectionCombo.SelectedIndex = index;
        if (_resumeSummaryLabel != null)
            _resumeSummaryLabel.Text = "Section " + (index + 1) + " of " + ResumeSections.Length + " - " + ResumeSections[index];
    }

    private void SetResumeBusy(bool busy, string message = "")
    {
        _resumeBusy = busy;
        if (busy)
            SetResumeStatus("Analyzing...", Cyan, message);
    }

    private void SetResumeStatus(string text, Color color, string message)
    {
        if (_resumeStatusBadge != null && !_resumeStatusBadge.IsDisposed)
        {
            _resumeStatusBadge.Text = text;
            _resumeStatusBadge.BackColor = color;
        }
        if (_resumeProgressLabel != null && !_resumeProgressLabel.IsDisposed)
            _resumeProgressLabel.Text = message;
    }

    private void AppendResumeExport(string message)
    {
        SetResumeStatus("Saved", Mint, message);
        if (_resumeExportBox != null)
            _resumeExportBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
    }

    private IEnumerable<ResumeMetadataRow> ToMetadataRows(ResumeMetadata metadata)
    {
        return new ResumeMetadataRow[]
        {
            new() { Field = "Candidate name", Value = DisplayOrFallback(metadata.CandidateName, "Not detected") },
            new() { Field = "Email", Value = DisplayOrFallback(metadata.Email, "Not detected") },
            new() { Field = "Phone", Value = DisplayOrFallback(metadata.Phone, "Not detected") },
            new() { Field = "Location", Value = DisplayOrFallback(metadata.Location, "Not detected") },
            new() { Field = "LinkedIn", Value = DisplayOrFallback(metadata.LinkedIn, "Not detected") },
            new() { Field = "GitHub / Portfolio", Value = DisplayOrFallback(metadata.GitHubOrPortfolio, "Not detected") },
            new() { Field = "Experience estimate", Value = metadata.YearsExperienceEstimate },
            new() { Field = "Target role alignment", Value = metadata.TargetRoleAlignment }
        };
    }

    private static void SetComboText(ComboBox? combo, string value)
    {
        if (combo == null)
            return;
        int index = combo.FindStringExact(value);
        combo.SelectedIndex = index >= 0 ? index : 0;
    }

    private TextBox AddCareerTextField(FlowLayoutPanel parent, string label, string value)
    {
        var block = new Panel { Width = 372, Height = 58, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8) };
        var labelView = CreateLabel(label, 8.2f, SoftText, FontStyle.Bold);
        labelView.Location = new Point(0, 0);
        labelView.Width = 360;
        labelView.Height = 19;
        var input = new TextBox
        {
            Text = value,
            Location = new Point(0, 22),
            Width = 360,
            Height = 28,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.8f)
        };
        block.Controls.Add(labelView);
        block.Controls.Add(input);
        parent.Controls.Add(block);
        return input;
    }

    private ComboBox AddCareerComboField(FlowLayoutPanel parent, string label, string[] values, string selected)
    {
        var block = new Panel { Width = 372, Height = 58, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 8) };
        var labelView = CreateLabel(label, 8.2f, SoftText, FontStyle.Bold);
        labelView.Location = new Point(0, 0);
        labelView.Width = 360;
        labelView.Height = 19;
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(0, 22),
            Width = 360,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.8f)
        };
        combo.Items.AddRange(values.Cast<object>().ToArray());
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(values, selected));
        block.Controls.Add(labelView);
        block.Controls.Add(combo);
        parent.Controls.Add(block);
        return combo;
    }

    private Control CreateCareerScoreCard(string title, string key)
    {
        var card = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(16),
            StartColor = Color.FromArgb(238, 249, 255),
            EndColor = Color.FromArgb(216, 237, 250)
        };
        var label = CreateLabel(title, 8.3f, SoftText, FontStyle.Bold);
        label.Location = new Point(16, 13);
        label.Width = 160;
        label.Height = 20;
        var score = CreateLabel("--", 21f, Ink, FontStyle.Bold);
        score.Location = new Point(16, 38);
        score.Width = 120;
        score.Height = 38;
        _resumeScoreLabels[key] = score;
        card.Controls.Add(label);
        card.Controls.Add(score);
        return card;
    }

    private static DataGridView CreateCareerGrid()
    {
        var grid = new DataGridView
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
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            Font = new Font("Segoe UI", 8.3f),
            ColumnHeadersHeight = 32,
            RowTemplate = { Height = 32 }
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(225, 240, 250);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.2f, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 232, 247);
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        return grid;
    }

    private static RichTextBox CreateCareerRichBox(bool readOnly)
    {
        return new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9.6f),
            ReadOnly = readOnly,
            DetectUrls = true
        };
    }

    private Panel CreateCareerCard(string title, out Panel body)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 14,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 12)
        };
        var titleLabel = CreateLabel(title, 10f, Ink, FontStyle.Bold);
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

    private static Panel WrapCareerPanel(string title, Control content)
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 14,
            Padding = new Padding(14, 36, 14, 12),
            Margin = new Padding(0, 0, 10, 10)
        };
        var label = CreateLabel(title, 9.3f, Ink, FontStyle.Bold);
        label.Location = new Point(16, 10);
        label.Width = 420;
        label.Height = 22;
        panel.Controls.Add(content);
        panel.Controls.Add(label);
        return panel;
    }

    private static Label CreateCareerBadge(string text, Color backColor)
    {
        var label = CreateLabel(text, 8.4f, Color.White, FontStyle.Bold);
        label.BackColor = backColor;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Height = 26;
        return label;
    }

    private static Button CreateCareerButton(string text, int width, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Segoe UI Semibold", 8.2f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 8)
        };
        button.FlatAppearance.BorderSize = backColor == Surface2 ? 1 : 0;
        button.FlatAppearance.BorderColor = Stroke;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.08f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.08f);
        return button;
    }

    private static void SetGridData<T>(DataGridView? grid, IEnumerable<T> rows)
    {
        if (grid == null || grid.IsDisposed)
            return;
        grid.DataSource = rows.ToList();
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.HeaderText = SplitPascal(column.HeaderText);
            column.MinimumWidth = 82;
        }
    }

    private static string SplitPascal(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        return System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
    }

    private sealed class ResumeMetadataRow
    {
        public string Field { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
