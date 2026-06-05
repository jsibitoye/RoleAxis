using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RoleAxis.Career.InterviewAssistant;

public sealed partial class RoleAxisDesktopAppForm
{
    private readonly List<SavedJob> _savedJobs = new();
    private SavedJob _jobDraft = new();
    private SavedJob? _selectedJob;
    private JobMatchReport? _selectedJobReport;
    private string _jobTailoredOutput = "";
    private string _jobCoverOutput = "";
    private string _jobInterviewOutput = "";
    private bool _jobsLoaded;
    private int _jobSectionIndex;

    private TextBox? _jobTitleBox;
    private TextBox? _jobCompanyBox;
    private TextBox? _jobLocationBox;
    private TextBox? _jobSalaryBox;
    private TextBox? _jobUrlBox;
    private ComboBox? _jobRemoteCombo;
    private ComboBox? _jobSourceCombo;
    private ComboBox? _jobStatusCombo;
    private RichTextBox? _jobDescriptionBox;
    private RichTextBox? _jobNotesBox;
    private Label? _jobStatusBadge;
    private Label? _jobMessageLabel;
    private readonly Dictionary<string, Label> _jobMetricLabels = new();
    private TabControl? _jobTabs;
    private ComboBox? _jobSectionCombo;
    private Label? _jobSectionLabel;
    private DataGridView? _jobsGrid;
    private RichTextBox? _jobDetailBox;
    private RichTextBox? _jobAnalysisBox;
    private DataGridView? _jobGapGrid;
    private DataGridView? _jobRequirementGrid;
    private DataGridView? _jobRiskGrid;
    private RichTextBox? _jobTailoredResumeBox;
    private RichTextBox? _jobCoverLetterBox;
    private RichTextBox? _jobInterviewPrepBox;
    private RichTextBox? _jobTrackerBox;

    private static readonly string[] JobSections =
    {
        "Search / Add Job",
        "Saved Jobs",
        "Job Detail",
        "Match Analysis",
        "Tailored Resume",
        "Cover Letter",
        "Interview Prep",
        "Application Tracker"
    };

    private void ShowJobIntelligence()
    {
        EnsureJobsLoaded();
        LogActivity("Jobs", "Opened Job Intelligence", "Job intelligence workspace launched.");
        ClearContent();
        SetView("jobs", "RoleAxis Job Intelligence", "Save jobs, score fit, tailor applications, and track your search locally.");
        _contentHost.AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Navy
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildJobDashboardBand(), 0, 0);
        root.Controls.Add(BuildJobNavigator(), 0, 1);
        root.Controls.Add(BuildJobTabs(), 0, 2);
        _contentHost.Controls.Add(root);

        BindJobsToUi();
        SetJobSection(_jobSectionIndex);
    }

    private Control BuildJobDashboardBand()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Navy,
            Margin = new Padding(0, 0, 0, 10)
        };
        for (int i = 0; i < 6; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6f));

        _jobMetricLabels.Clear();
        grid.Controls.Add(CreateJobMetricCard("Saved Jobs", "saved"), 0, 0);
        grid.Controls.Add(CreateJobMetricCard("Avg Match", "average"), 1, 0);
        grid.Controls.Add(CreateJobMetricCard("Best Match", "best"), 2, 0);
        grid.Controls.Add(CreateJobMetricCard("Applied", "applied"), 3, 0);
        grid.Controls.Add(CreateJobMetricCard("Interviewing", "interviewing"), 4, 0);
        grid.Controls.Add(CreateJobMetricCard("Offers", "offers"), 5, 0);
        return grid;
    }

    private Control BuildJobNavigator()
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 14,
            Padding = new Padding(12, 8, 12, 8),
            Margin = new Padding(0, 0, 0, 10)
        };
        var previous = CreateCareerButton("Previous", 98, Surface2, Ink);
        previous.Location = new Point(12, 8);
        previous.Click += (_, _) => SetJobSection(_jobSectionIndex - 1);
        var next = CreateCareerButton("Next", 82, BlackButton, Color.White);
        next.Location = new Point(116, 8);
        next.Click += (_, _) => SetJobSection(_jobSectionIndex + 1);

        _jobSectionCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 8.8f),
            Location = new Point(210, 10),
            Width = 250
        };
        _jobSectionCombo.Items.AddRange(JobSections.Cast<object>().ToArray());
        _jobSectionCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_jobSectionCombo.SelectedIndex >= 0)
                SetJobSection(_jobSectionCombo.SelectedIndex);
        };

        _jobSectionLabel = CreateLabel("Section 1 of 8", 8.6f, SoftText, FontStyle.Bold);
        _jobSectionLabel.Location = new Point(474, 12);
        _jobSectionLabel.Width = 520;
        _jobSectionLabel.Height = 22;

        _jobStatusBadge = CreateCareerBadge("Ready", BlackButton);
        _jobStatusBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _jobStatusBadge.Location = new Point(card.Width - 132, 10);
        _jobStatusBadge.Width = 112;
        card.Resize += (_, _) => _jobStatusBadge.Left = card.Width - 132;

        card.Controls.Add(previous);
        card.Controls.Add(next);
        card.Controls.Add(_jobSectionCombo);
        card.Controls.Add(_jobSectionLabel);
        card.Controls.Add(_jobStatusBadge);
        return card;
    }

    private Control BuildJobTabs()
    {
        _jobTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 8.8f, FontStyle.Bold),
            HotTrack = true
        };
        _jobTabs.SelectedIndexChanged += (_, _) =>
        {
            if (_jobTabs.SelectedIndex >= 0 && _jobTabs.SelectedIndex != _jobSectionIndex)
                SetJobSection(_jobTabs.SelectedIndex);
        };

        _jobTabs.TabPages.Add(BuildJobAddTab());
        _jobTabs.TabPages.Add(BuildSavedJobsTab());
        _jobTabs.TabPages.Add(BuildJobDetailTab());
        _jobTabs.TabPages.Add(BuildJobAnalysisTab());
        _jobTabs.TabPages.Add(BuildJobOutputTab("Tailored Resume", out _jobTailoredResumeBox));
        _jobTabs.TabPages.Add(BuildJobOutputTab("Cover Letter", out _jobCoverLetterBox));
        _jobTabs.TabPages.Add(BuildJobOutputTab("Interview Prep", out _jobInterviewPrepBox));
        _jobTabs.TabPages.Add(BuildJobTrackerTab());
        return _jobTabs;
    }

    private TabPage BuildJobAddTab()
    {
        var page = new TabPage("Search / Add Job") { BackColor = Navy };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Navy,
            Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var inputCard = CreateCareerCard("Manual Job Capture", out var inputBody);
        inputBody.Padding = new Padding(14, 36, 14, 12);
        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent
        };
        _jobTitleBox = AddCareerTextField(fields, "Job title / keyword", "");
        _jobCompanyBox = AddCareerTextField(fields, "Company name", "");
        _jobLocationBox = AddCareerTextField(fields, "Location", "");
        _jobSalaryBox = AddCareerTextField(fields, "Salary target / range", "");
        _jobUrlBox = AddCareerTextField(fields, "Job URL", "");
        _jobRemoteCombo = AddCareerComboField(fields, "Remote preference", new[] { "Remote only", "Hybrid", "Onsite", "Any" }, "Any");
        _jobSourceCombo = AddCareerComboField(fields, "Job source", new[] { "Manual", "LinkedIn URL", "Indeed URL", "Company site URL", "Remote job board", "Other" }, "Manual");
        _jobStatusCombo = AddCareerComboField(fields, "Application status", JobStatuses, "Saved");
        inputBody.Controls.Add(fields);

        var descriptionCard = CreateCareerCard("Job Description / Notes", out var descriptionBody);
        descriptionBody.Padding = new Padding(14, 36, 14, 12);
        var descLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent
        };
        descLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        descLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        descLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        _jobDescriptionBox = CreateCareerRichBox(readOnly: false);
        _jobNotesBox = CreateCareerRichBox(readOnly: false);
        WireJobDraftEvents();
        descLayout.Controls.Add(WrapCareerPanel("Pasted Job Description", _jobDescriptionBox), 0, 0);
        descLayout.Controls.Add(WrapCareerPanel("Notes", _jobNotesBox), 0, 1);
        descLayout.Controls.Add(BuildJobActionStrip(), 0, 2);
        descriptionBody.Controls.Add(descLayout);

        layout.Controls.Add(inputCard, 0, 0);
        layout.Controls.Add(descriptionCard, 1, 0);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildJobActionStrip()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 14,
            Padding = new Padding(14)
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        var paste = CreateCareerButton("Import Clipboard", 132, Surface2, Ink);
        var fresh = CreateCareerButton("New Job", 86, Surface2, Ink);
        var save = CreateCareerButton("Save Job", 94, BlackButton, Color.White);
        var analyze = CreateCareerButton("Analyze Fit", 106, Cyan, Color.White);
        var resume = CreateCareerButton("Generate Resume", 138, Surface2, Ink);
        var cover = CreateCareerButton("Cover Letter", 112, Surface2, Ink);
        var prep = CreateCareerButton("Prepare Interview", 142, Surface2, Ink);
        var applied = CreateCareerButton("Mark Applied", 116, Surface2, Ink);
        paste.Click += (_, _) => { if (Clipboard.ContainsText()) _jobDescriptionBox!.Text = Clipboard.GetText(); };
        fresh.Click += (_, _) => ClearJobInputFields();
        save.Click += (_, _) => SaveJobFromInput(selectAfterSave: true);
        analyze.Click += async (_, _) => await AnalyzeSelectedJobAsync(ensureSaved: true);
        resume.Click += async (_, _) => await GenerateSelectedJobResumeAsync();
        cover.Click += (_, _) => GenerateSelectedJobCoverLetter();
        prep.Click += (_, _) => GenerateSelectedJobInterviewPrep();
        applied.Click += (_, _) => MarkSelectedJobApplied();
        actions.Controls.Add(paste);
        actions.Controls.Add(fresh);
        actions.Controls.Add(save);
        actions.Controls.Add(analyze);
        actions.Controls.Add(resume);
        actions.Controls.Add(cover);
        actions.Controls.Add(prep);
        actions.Controls.Add(applied);

        _jobMessageLabel = CreateLabel("Paste a job description or add a job link to start building a targeted application strategy.", 8.3f, SoftText);
        _jobMessageLabel.Dock = DockStyle.Bottom;
        _jobMessageLabel.Height = 22;
        panel.Controls.Add(actions);
        panel.Controls.Add(_jobMessageLabel);
        return panel;
    }

    private TabPage BuildSavedJobsTab()
    {
        var page = new TabPage("Saved Jobs") { BackColor = Navy };
        _jobsGrid = CreateCareerGrid();
        _jobsGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
                SelectJobFromGrid(e.RowIndex, openDetail: true);
        };
        _jobsGrid.SelectionChanged += (_, _) =>
        {
            if (_jobsGrid.SelectedRows.Count > 0)
                SelectJobFromGrid(_jobsGrid.SelectedRows[0].Index, openDetail: false);
        };
        page.Controls.Add(WrapCareerPanel("Saved Job Pipeline", _jobsGrid));
        return page;
    }

    private TabPage BuildJobDetailTab()
    {
        var page = new TabPage("Job Detail") { BackColor = Navy };
        _jobDetailBox = CreateCareerRichBox(readOnly: true);
        page.Controls.Add(WrapCareerPanel("Selected Job Detail", _jobDetailBox));
        return page;
    }

    private TabPage BuildJobAnalysisTab()
    {
        var page = new TabPage("Match Analysis") { BackColor = Navy };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Navy,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        _jobAnalysisBox = CreateCareerRichBox(readOnly: true);
        layout.Controls.Add(WrapCareerPanel("Application Strategy", _jobAnalysisBox), 0, 0);
        layout.Controls.Add(WrapCareerPanel("Required Skill Match", _jobGapGrid = CreateCareerGrid()), 0, 1);
        layout.Controls.Add(WrapCareerPanel("Requirement Evidence", _jobRequirementGrid = CreateCareerGrid()), 0, 2);
        layout.Controls.Add(WrapCareerPanel("Rejection Risks", _jobRiskGrid = CreateCareerGrid()), 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildJobOutputTab(string title, out RichTextBox box)
    {
        var page = new TabPage(title) { BackColor = Navy };
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
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Navy };
        box = CreateCareerRichBox(readOnly: true);
        var output = box;
        var copy = CreateCareerButton("Copy", 80, BlackButton, Color.White);
        var save = CreateCareerButton("Save TXT", 96, Surface2, Ink);
        var regenerate = CreateCareerButton(title == "Cover Letter" ? "More Direct" : "Regenerate", 122, Cyan, Color.White);
        copy.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(output.Text)) Clipboard.SetText(output.Text); };
        save.Click += (_, _) => SaveSelectedJobOutput(title, output.Text);
        regenerate.Click += async (_, _) =>
        {
            if (title == "Tailored Resume") await GenerateSelectedJobResumeAsync();
            else if (title == "Cover Letter") GenerateSelectedJobCoverLetter();
            else GenerateSelectedJobInterviewPrep();
        };
        actions.Controls.Add(copy);
        actions.Controls.Add(save);
        actions.Controls.Add(regenerate);
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(WrapCareerPanel(title, output), 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildJobTrackerTab()
    {
        var page = new TabPage("Application Tracker") { BackColor = Navy };
        _jobTrackerBox = CreateCareerRichBox(readOnly: true);
        page.Controls.Add(WrapCareerPanel("Pipeline Counts / Selected Job Status", _jobTrackerBox));
        return page;
    }

    private void EnsureJobsLoaded()
    {
        if (_jobsLoaded)
            return;
        _savedJobs.Clear();
        _savedJobs.AddRange(_careerStorageService.LoadJobs());
        _jobsLoaded = true;
    }

    private SavedJob? SaveJobFromInput(bool selectAfterSave)
    {
        var job = _selectedJob ?? _jobDraft;
        job.Title = _jobTitleBox?.Text.Trim() ?? "";
        job.Company = _jobCompanyBox?.Text.Trim() ?? "";
        job.Location = _jobLocationBox?.Text.Trim() ?? "";
        job.SalaryRange = _jobSalaryBox?.Text.Trim() ?? "";
        job.JobUrl = _jobUrlBox?.Text.Trim() ?? "";
        job.RemoteType = _jobRemoteCombo?.Text ?? "Any";
        job.Source = _jobSourceCombo?.Text ?? "Manual";
        job.Status = _jobStatusCombo?.Text ?? "Saved";
        job.JobDescription = _jobDescriptionBox?.Text.Trim() ?? "";
        job.Notes = _jobNotesBox?.Text.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(job.Title) && string.IsNullOrWhiteSpace(job.JobDescription))
        {
            SetJobStatus("Needs job", Orange, "Add a title, URL, or job description before saving.");
            return null;
        }

        if (!_savedJobs.Any(item => item.Id == job.Id))
            _savedJobs.Add(job);

        _careerStorageService.SaveJobs(_savedJobs);
        if (selectAfterSave)
            _selectedJob = job;
        _jobDraft = new SavedJob();
        BindJobsToUi();
        SetJobStatus("Saved", Mint, "Job saved locally.");
        LogActivity("Jobs", "Job saved", DisplayOrFallback(job.Title, DisplayOrFallback(job.Company, "Saved job")));
        return job;
    }

    private async Task AnalyzeSelectedJobAsync(bool ensureSaved)
    {
        var job = ensureSaved ? SaveJobFromInput(selectAfterSave: true) : _selectedJob;
        if (job == null)
            return;

        try
        {
            SetJobStatus("Analyzing...", Cyan, "Scoring job fit against the current Resume Intelligence context.");
            CaptureResumeInputState();
            _selectedJobReport = await Task.Run(() => _careerJobService.AnalyzeJobMatch(_resumeInputState, job));
            job.MatchScore = _selectedJobReport.OverallFitScore;
            _careerStorageService.SaveJobs(_savedJobs);
            BindJobAnalysisToUi();
            BindJobsToUi();
            SetJobSection(3);
            SetJobStatus("Analyzed", Mint, "Job fit analysis ready.");
            LogActivity("Jobs", "Job fit analyzed", DisplayOrFallback(job.Title, "Saved job") + " - " + job.MatchScore + "/100");
        }
        catch (Exception ex)
        {
            SetJobStatus("Error", Orange, ex.Message);
        }
    }

    private async Task GenerateSelectedJobResumeAsync()
    {
        var job = _selectedJob ?? SaveJobFromInput(selectAfterSave: true);
        if (job == null)
            return;
        CaptureResumeInputState();
        if (string.IsNullOrWhiteSpace(_resumeInputState.ResumeText))
        {
            SetJobStatus("Needs resume", Orange, "Open Resume Intelligence and paste or load a resume first.");
            return;
        }

        SetJobStatus("Generating...", Cyan, "Generating a job-specific tailored resume.");
        var analysis = await _careerResumeService.AnalyzeAsync(new ResumeAnalysisInput
        {
            ResumeText = _resumeInputState.ResumeText,
            TargetRole = job.Title,
            CompanyName = job.Company,
            JobUrl = job.JobUrl,
            JobDescription = job.JobDescription,
            OutputStyle = _resumeInputState.OutputStyle,
            ResumeType = _resumeInputState.ResumeType,
            SeniorityLevel = _resumeInputState.SeniorityLevel,
            TargetIndustry = _resumeInputState.TargetIndustry
        });
        string tailored = _careerResumeService.GenerateTailoredResume(_resumeInputState, analysis, job);
        job.ResumeVersionPath = _careerStorageService.SaveTailoredResume(job.Id, tailored);
        _careerStorageService.SaveJobs(_savedJobs);
        if (_jobTailoredResumeBox != null)
        {
            _jobTailoredOutput = tailored + "\r\n\r\nSaved to:\r\n" + job.ResumeVersionPath;
            _jobTailoredResumeBox.Text = _jobTailoredOutput;
        }
        SetJobSection(4);
        SetJobStatus("Generated", Mint, "Tailored resume generated and saved locally.");
        LogActivity("Jobs", "Tailored job resume generated", DisplayOrFallback(job.Title, "Saved job"));
    }

    private void GenerateSelectedJobCoverLetter()
    {
        var job = _selectedJob ?? SaveJobFromInput(selectAfterSave: true);
        if (job == null)
            return;
        CaptureResumeInputState();
        if (_selectedJobReport == null)
            _selectedJobReport = _careerJobService.AnalyzeJobMatch(_resumeInputState, job);
        string cover = _careerJobService.GenerateCoverLetter(_resumeInputState, job, _selectedJobReport);
        job.CoverLetterPath = _careerStorageService.SaveCoverLetter(job.Id, cover);
        _careerStorageService.SaveJobs(_savedJobs);
        if (_jobCoverLetterBox != null)
        {
            _jobCoverOutput = cover + "\r\n\r\nSaved to:\r\n" + job.CoverLetterPath;
            _jobCoverLetterBox.Text = _jobCoverOutput;
        }
        SetJobSection(5);
        SetJobStatus("Cover letter", Mint, "Cover letter generated and saved locally.");
        LogActivity("Jobs", "Cover letter generated", DisplayOrFallback(job.Title, "Saved job"));
    }

    private void GenerateSelectedJobInterviewPrep()
    {
        var job = _selectedJob ?? SaveJobFromInput(selectAfterSave: true);
        if (job == null)
            return;
        CaptureResumeInputState();
        if (_selectedJobReport == null)
            _selectedJobReport = _careerJobService.AnalyzeJobMatch(_resumeInputState, job);
        string prep = _careerJobService.GenerateInterviewPrep(_resumeInputState, job, _selectedJobReport);
        string contextPath = _careerStorageService.SaveInterviewContext(job, _resumeAnalysisResult?.ExecutiveSummary ?? "", prep);
        if (_jobInterviewPrepBox != null)
        {
            _jobInterviewOutput = prep + "\r\n\r\nInterview context JSON saved to:\r\n" + contextPath;
            _jobInterviewPrepBox.Text = _jobInterviewOutput;
        }
        SetJobSection(6);
        SetJobStatus("Interview prep", Mint, "Interview prep generated and context exported.");
        LogActivity("Jobs", "Interview prep generated", DisplayOrFallback(job.Title, "Saved job"));
    }

    private void SaveSelectedJobOutput(string title, string text)
    {
        if (_selectedJob == null || string.IsNullOrWhiteSpace(text))
        {
            SetJobStatus("Nothing to save", Orange, "Select a job and generate output first.");
            return;
        }
        string path = title switch
        {
            "Tailored Resume" => _careerStorageService.SaveTailoredResume(_selectedJob.Id, text),
            "Cover Letter" => _careerStorageService.SaveCoverLetter(_selectedJob.Id, text),
            _ => _careerStorageService.SaveInterviewContext(_selectedJob, _resumeAnalysisResult?.ExecutiveSummary ?? "", text)
        };
        SetJobStatus("Saved", Mint, title + " saved: " + path);
        LogActivity("Jobs", title + " saved", DisplayOrFallback(_selectedJob.Title, "Saved job"));
    }

    private void MarkSelectedJobApplied()
    {
        var job = _selectedJob ?? SaveJobFromInput(selectAfterSave: true);
        if (job == null)
            return;
        job.Status = "Applied";
        job.DateApplied ??= DateTime.Now;
        if (_jobStatusCombo != null)
            _jobStatusCombo.Text = "Applied";
        _careerStorageService.SaveJobs(_savedJobs);
        BindJobsToUi();
        SetJobStatus("Applied", Mint, "Job marked as applied.");
        LogActivity("Jobs", "Job marked applied", DisplayOrFallback(job.Title, "Saved job"));
    }

    private void ClearJobInputFields()
    {
        _selectedJob = null;
        _selectedJobReport = null;
        _jobDraft = new SavedJob();
        if (_jobTitleBox != null) _jobTitleBox.Text = "";
        if (_jobCompanyBox != null) _jobCompanyBox.Text = "";
        if (_jobLocationBox != null) _jobLocationBox.Text = "";
        if (_jobSalaryBox != null) _jobSalaryBox.Text = "";
        if (_jobUrlBox != null) _jobUrlBox.Text = "";
        if (_jobRemoteCombo != null) _jobRemoteCombo.Text = "Any";
        if (_jobSourceCombo != null) _jobSourceCombo.Text = "Manual";
        if (_jobStatusCombo != null) _jobStatusCombo.Text = "Saved";
        if (_jobDescriptionBox != null) _jobDescriptionBox.Text = "";
        if (_jobNotesBox != null) _jobNotesBox.Text = "";
        BindJobDetailToUi();
        BindJobAnalysisToUi();
        SetJobStatus("Ready", BlackButton, "New job entry ready.");
    }

    private void WireJobDraftEvents()
    {
        foreach (var box in new[] { _jobTitleBox, _jobCompanyBox, _jobLocationBox, _jobSalaryBox, _jobUrlBox })
            if (box != null)
                box.TextChanged += (_, _) => CaptureJobDraftState();
        foreach (var combo in new[] { _jobRemoteCombo, _jobSourceCombo, _jobStatusCombo })
            if (combo != null)
                combo.SelectedIndexChanged += (_, _) => CaptureJobDraftState();
        if (_jobDescriptionBox != null)
            _jobDescriptionBox.TextChanged += (_, _) => CaptureJobDraftState();
        if (_jobNotesBox != null)
            _jobNotesBox.TextChanged += (_, _) => CaptureJobDraftState();
    }

    private void CaptureJobDraftState()
    {
        var target = _selectedJob ?? _jobDraft;
        target.Title = _jobTitleBox?.Text.Trim() ?? "";
        target.Company = _jobCompanyBox?.Text.Trim() ?? "";
        target.Location = _jobLocationBox?.Text.Trim() ?? "";
        target.SalaryRange = _jobSalaryBox?.Text.Trim() ?? "";
        target.JobUrl = _jobUrlBox?.Text.Trim() ?? "";
        target.RemoteType = _jobRemoteCombo?.Text ?? "Any";
        target.Source = _jobSourceCombo?.Text ?? "Manual";
        target.Status = _jobStatusCombo?.Text ?? "Saved";
        target.JobDescription = _jobDescriptionBox?.Text.Trim() ?? "";
        target.Notes = _jobNotesBox?.Text.Trim() ?? "";
    }

    private void SelectJobFromGrid(int rowIndex, bool openDetail)
    {
        if (_jobsGrid == null || rowIndex < 0 || rowIndex >= _jobsGrid.Rows.Count)
            return;
        if (_jobsGrid.Rows[rowIndex].Tag is not Guid id)
            return;
        _selectedJob = _savedJobs.FirstOrDefault(job => job.Id == id);
        _selectedJobReport = null;
        BindSelectedJobToFields();
        BindJobDetailToUi();
        if (openDetail)
            SetJobSection(2);
    }

    private void BindJobsToUi()
    {
        var rows = _savedJobs
            .OrderByDescending(job => job.DateSaved)
            .Select(job => new JobGridRow
            {
                Id = job.Id,
                Status = job.Status,
                MatchScore = job.MatchScore == 0 ? "--" : job.MatchScore.ToString("00"),
                JobTitle = job.Title,
                Company = job.Company,
                Location = job.Location,
                Remote = job.RemoteType,
                Salary = job.SalaryRange,
                Source = job.Source,
                SavedDate = job.DateSaved.ToString("g")
            })
            .ToList();
        SetGridData(_jobsGrid, rows);
        if (_jobsGrid != null)
        {
            foreach (DataGridViewRow row in _jobsGrid.Rows)
            {
                if (row.DataBoundItem is JobGridRow jobRow)
                    row.Tag = jobRow.Id;
            }
            if (_jobsGrid.Columns.Contains("Id"))
                _jobsGrid.Columns["Id"].Visible = false;
        }
        BindJobMetrics();
        BindJobDetailToUi();
        BindTrackerToUi();
        if (_selectedJob == null)
            BindJobDraftToFields();
        if (_jobTailoredResumeBox != null && !string.IsNullOrWhiteSpace(_jobTailoredOutput))
            _jobTailoredResumeBox.Text = _jobTailoredOutput;
        if (_jobCoverLetterBox != null && !string.IsNullOrWhiteSpace(_jobCoverOutput))
            _jobCoverLetterBox.Text = _jobCoverOutput;
        if (_jobInterviewPrepBox != null && !string.IsNullOrWhiteSpace(_jobInterviewOutput))
            _jobInterviewPrepBox.Text = _jobInterviewOutput;
    }

    private void BindJobDraftToFields()
    {
        if (_jobTitleBox != null) _jobTitleBox.Text = _jobDraft.Title;
        if (_jobCompanyBox != null) _jobCompanyBox.Text = _jobDraft.Company;
        if (_jobLocationBox != null) _jobLocationBox.Text = _jobDraft.Location;
        if (_jobSalaryBox != null) _jobSalaryBox.Text = _jobDraft.SalaryRange;
        if (_jobUrlBox != null) _jobUrlBox.Text = _jobDraft.JobUrl;
        if (_jobRemoteCombo != null) _jobRemoteCombo.Text = string.IsNullOrWhiteSpace(_jobDraft.RemoteType) ? "Any" : _jobDraft.RemoteType;
        if (_jobSourceCombo != null) _jobSourceCombo.Text = string.IsNullOrWhiteSpace(_jobDraft.Source) ? "Manual" : _jobDraft.Source;
        if (_jobStatusCombo != null) _jobStatusCombo.Text = string.IsNullOrWhiteSpace(_jobDraft.Status) ? "Saved" : _jobDraft.Status;
        if (_jobDescriptionBox != null) _jobDescriptionBox.Text = _jobDraft.JobDescription;
        if (_jobNotesBox != null) _jobNotesBox.Text = _jobDraft.Notes;
    }

    private void BindJobMetrics()
    {
        SetJobMetric("saved", _savedJobs.Count.ToString("N0"));
        var scored = _savedJobs.Where(job => job.MatchScore > 0).ToList();
        SetJobMetric("average", scored.Count == 0 ? "--" : ((int)scored.Average(job => job.MatchScore)).ToString("00"));
        var best = scored.OrderByDescending(job => job.MatchScore).FirstOrDefault();
        SetJobMetric("best", best == null ? "--" : best.MatchScore.ToString("00"));
        SetJobMetric("applied", _savedJobs.Count(job => job.Status == "Applied").ToString("N0"));
        SetJobMetric("interviewing", _savedJobs.Count(job => job.Status == "Interviewing").ToString("N0"));
        SetJobMetric("offers", _savedJobs.Count(job => job.Status == "Offer").ToString("N0"));
    }

    private void BindSelectedJobToFields()
    {
        var job = _selectedJob;
        if (job == null)
            return;
        if (_jobTitleBox != null) _jobTitleBox.Text = job.Title;
        if (_jobCompanyBox != null) _jobCompanyBox.Text = job.Company;
        if (_jobLocationBox != null) _jobLocationBox.Text = job.Location;
        if (_jobSalaryBox != null) _jobSalaryBox.Text = job.SalaryRange;
        if (_jobUrlBox != null) _jobUrlBox.Text = job.JobUrl;
        if (_jobRemoteCombo != null) _jobRemoteCombo.Text = job.RemoteType;
        if (_jobSourceCombo != null) _jobSourceCombo.Text = job.Source;
        if (_jobStatusCombo != null) _jobStatusCombo.Text = job.Status;
        if (_jobDescriptionBox != null) _jobDescriptionBox.Text = job.JobDescription;
        if (_jobNotesBox != null) _jobNotesBox.Text = job.Notes;
    }

    private void BindJobDetailToUi()
    {
        if (_jobDetailBox == null)
            return;
        var job = _selectedJob;
        if (job == null)
        {
            _jobDetailBox.Text = "Paste a job description or add a job link to start building a targeted application strategy.";
            return;
        }
        _jobDetailBox.Text =
            "JOB DETAIL\r\n" +
            "Title: " + job.Title + "\r\n" +
            "Company: " + job.Company + "\r\n" +
            "Location: " + job.Location + "\r\n" +
            "Remote: " + job.RemoteType + "\r\n" +
            "Salary: " + job.SalaryRange + "\r\n" +
            "Source: " + job.Source + "\r\n" +
            "URL: " + job.JobUrl + "\r\n" +
            "Status: " + job.Status + "\r\n" +
            "Match score: " + (job.MatchScore == 0 ? "Not analyzed" : job.MatchScore + "/100") + "\r\n" +
            "Saved: " + job.DateSaved.ToString("g") + "\r\n" +
            "Applied: " + (job.DateApplied?.ToString("g") ?? "Not applied") + "\r\n\r\n" +
            "Generated resume path:\r\n" + DisplayOrFallback(job.ResumeVersionPath, "Not generated") + "\r\n\r\n" +
            "Cover letter path:\r\n" + DisplayOrFallback(job.CoverLetterPath, "Not generated") + "\r\n\r\n" +
            "NOTES\r\n" + job.Notes + "\r\n\r\nJOB DESCRIPTION\r\n" + job.JobDescription;
    }

    private void BindJobAnalysisToUi()
    {
        var report = _selectedJobReport;
        if (report == null)
        {
            if (_jobAnalysisBox != null)
                _jobAnalysisBox.Text = "Select or save a job, then run Analyze Fit.";
            SetGridData(_jobGapGrid, Array.Empty<SkillGapFinding>());
            SetGridData(_jobRequirementGrid, Array.Empty<ResumeFinding>());
            SetGridData(_jobRiskGrid, Array.Empty<ResumeFinding>());
            return;
        }
        if (_jobAnalysisBox != null)
        {
            _jobAnalysisBox.Text =
                "Overall fit score: " + report.OverallFitScore + "/100\r\n" +
                "Skills match: " + report.SkillsMatch + "\r\n" +
                "Experience match: " + report.ExperienceMatch + "\r\n" +
                "Keyword match: " + report.KeywordMatch + "\r\n" +
                "Seniority match: " + report.SeniorityMatch + "\r\n" +
                "Domain match: " + report.DomainMatch + "\r\n\r\n" +
                "Recommended application strategy:\r\n" + report.Strategy;
        }
        SetGridData(_jobGapGrid, report.SkillGaps);
        SetGridData(_jobRequirementGrid, report.RequirementFindings);
        SetGridData(_jobRiskGrid, report.Risks);
    }

    private void BindTrackerToUi()
    {
        if (_jobTrackerBox == null)
            return;
        _jobTrackerBox.Text =
            "APPLICATION PIPELINE\r\n" +
            "Saved: " + _savedJobs.Count(job => job.Status == "Saved").ToString("N0") + "\r\n" +
            "Interested: " + _savedJobs.Count(job => job.Status == "Interested").ToString("N0") + "\r\n" +
            "Applied: " + _savedJobs.Count(job => job.Status == "Applied").ToString("N0") + "\r\n" +
            "Interviewing: " + _savedJobs.Count(job => job.Status == "Interviewing").ToString("N0") + "\r\n" +
            "Offer: " + _savedJobs.Count(job => job.Status == "Offer").ToString("N0") + "\r\n" +
            "Rejected: " + _savedJobs.Count(job => job.Status == "Rejected").ToString("N0") + "\r\n" +
            "Archived: " + _savedJobs.Count(job => job.Status == "Archived").ToString("N0") + "\r\n\r\n" +
            "Select a saved job to update notes/status from the Search / Add Job section, then Save Job.";
    }

    private void SetJobSection(int index)
    {
        index = Math.Clamp(index, 0, JobSections.Length - 1);
        _jobSectionIndex = index;
        if (_jobTabs != null && _jobTabs.SelectedIndex != index)
            _jobTabs.SelectedIndex = index;
        if (_jobSectionCombo != null && _jobSectionCombo.SelectedIndex != index)
            _jobSectionCombo.SelectedIndex = index;
        if (_jobSectionLabel != null)
            _jobSectionLabel.Text = "Section " + (index + 1) + " of " + JobSections.Length + " - " + JobSections[index];
    }

    private void SetJobMetric(string key, string value)
    {
        if (_jobMetricLabels.TryGetValue(key, out var label) && !label.IsDisposed)
            label.Text = value;
    }

    private void SetJobStatus(string text, Color color, string message)
    {
        if (_jobStatusBadge != null)
        {
            _jobStatusBadge.Text = text;
            _jobStatusBadge.BackColor = color;
        }
        if (_jobMessageLabel != null)
            _jobMessageLabel.Text = message;
    }

    private Control CreateJobMetricCard(string title, string key)
    {
        var card = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            StartColor = Color.FromArgb(238, 249, 255),
            EndColor = Color.FromArgb(216, 237, 250)
        };
        var titleLabel = CreateLabel(title, 8.1f, SoftText, FontStyle.Bold);
        titleLabel.Location = new Point(14, 12);
        titleLabel.Width = 150;
        titleLabel.Height = 18;
        var value = CreateLabel("--", 17.5f, Ink, FontStyle.Bold);
        value.Location = new Point(14, 35);
        value.Width = 140;
        value.Height = 34;
        _jobMetricLabels[key] = value;
        card.Controls.Add(titleLabel);
        card.Controls.Add(value);
        return card;
    }

    private static readonly string[] JobStatuses =
    {
        "Saved",
        "Interested",
        "Applied",
        "Interviewing",
        "Offer",
        "Rejected",
        "Archived"
    };

    private sealed class JobGridRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public string MatchScore { get; set; } = "";
        public string JobTitle { get; set; } = "";
        public string Company { get; set; } = "";
        public string Location { get; set; } = "";
        public string Remote { get; set; } = "";
        public string Salary { get; set; } = "";
        public string Source { get; set; } = "";
        public string SavedDate { get; set; } = "";
    }
}
