using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Packaging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using UglyToad.PdfPig;

namespace RoleAxis.Career.InterviewAssistant;


class Config
{
    public string apiKey { get; set; } = "";
}

public enum LiveAssistMode
{
    Interview,
    Meeting
}


internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new RoleAxisDesktopAppForm());
    }
}

public sealed class AssistantForm : Form
{
    private const int TargetSampleRate = 24000;

    // Use gpt-realtime-whisper for real live deltas. This is faster-feeling than waiting for final-only turns.
    private const string RealtimeTranscriptionModel = "gpt-4o-mini-transcribe";

    // Fast answer model. You can change to gpt-4o if you want stronger answers at higher cost.
    private const string AnswerModel = "gpt-4o-mini";

    // Optional fallback for personal/local builds only.
    // Better production setup: leave this empty and use config.json beside the EXE.
    private const string HardcodedApiKey = "";

    private static readonly Color ShellBack = Color.FromArgb(228, 242, 252);
    private static readonly Color Surface = Color.FromArgb(238, 248, 255);
    private static readonly Color Surface2 = Color.FromArgb(222, 238, 250);
    private static readonly Color Stroke = Color.FromArgb(200, 219, 233);
    private static readonly Color Ink = Color.FromArgb(14, 20, 34);
    private static readonly Color SoftText = Color.FromArgb(92, 112, 132);
    private static readonly Color Accent = Color.FromArgb(14, 160, 201);
    private static readonly Color AccentDark = Color.FromArgb(7, 18, 24);
    private static readonly Color Success = Color.FromArgb(18, 184, 125);
    private static readonly Color Danger = Color.FromArgb(143, 45, 45);
    private static readonly string[] AnswerKeywordHints =
    {
        "impact", "metric", "result", "priority", "risk", "decision", "owner", "deadline", "constraint", "tradeoff",
        "customer", "revenue", "cost", "security", "compliance", "architecture", "scale", "latency", "quality",
        "stakeholder", "clarify", "recommendation", "next step", "action item", "evidence", "example", "STAR",
        "situation", "task", "action", "outcome", "timeline", "scope", "root cause", "automation", "leadership"
    };

    private readonly TextBox _resumeBox = new();
    private readonly TextBox _jdBox = new();
    private readonly TextBox _meetingTitleBox = new();
    private readonly TextBox _meetingParticipantsBox = new();
    private readonly TextBox _meetingAgendaBox = new();
    private readonly ComboBox _meetingTypeCombo = new();
    private readonly ComboBox _meetingRoleCombo = new();
    private readonly RichTextBox _transcriptBox = new();
    private readonly RichTextBox _answerBox = new();

    private readonly Label _statusLabel = new();
    private readonly Label _audioLabel = new();
    private readonly Label _footerStatusLabel = new();

    private readonly ComboBox _deviceCombo = new();
    private readonly Button _refreshDevicesButton = new();
    private readonly Button _browseResumeButton = new();
    private readonly Button _browseJdButton = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _askButton = new();
    private readonly Button _clearTranscriptButton = new();
    private readonly Button _clearAnswersButton = new();
    private readonly Button _meetingSummaryButton = new();
    private readonly Button _meetingDecisionsButton = new();
    private readonly Button _meetingActionsButton = new();
    private readonly Button _meetingEmailButton = new();
    private readonly Button _meetingSaveButton = new();

    private readonly CheckBox _autoAskCheck = new();
    private readonly CheckBox _topMostCheck = new();

    private readonly NumericUpDown _cooldownBox = new();
    private readonly NumericUpDown _minWordsBox = new();

    private readonly HttpClient _http = new();
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private readonly List<MMDevice> _renderDevices = new();

    private WasapiLoopbackCapture? _capture;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Channel<byte[]>? _audioChannel;

    private string _apiKey = "";
    private string _lastFinalTranscript = "";
    private string _lastSentNorm = "";

    private readonly StringBuilder _liveDeltaBuffer = new();
    private readonly StringBuilder _transcriptHistory = new();

    private DateTime _lastAskTimeUtc = DateTime.MinValue;

    private long _audioChunksSent = 0;
    private long _audioBytesSent = 0;
    private float _lastRms = 0;

    private volatile bool _gptInFlight = false;

    private readonly List<string> _internalLog = new();

    private readonly bool _embedded;
    private readonly LiveAssistMode _mode;
    private TableLayoutPanel? _rootLayout;
    private Control? _contextCard;
    private Control? _controlsCard;
    private RowStyle? _contextRowStyle;
    private RowStyle? _controlsRowStyle;

    public AssistantForm() : this(false)
    {
    }

    public AssistantForm(bool embedded, LiveAssistMode mode = LiveAssistMode.Interview)
    {
        _embedded = embedded;
        _mode = mode;
        Text = mode == LiveAssistMode.Meeting
            ? "RoleAxis Meeting Assistant"
            : "RoleAxis Career Interview Assistant";
        Width = 1280;
        Height = 860;
        MinimumSize = embedded ? new Size(900, 620) : new Size(1100, 740);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = !embedded;
        ShowInTaskbar = !embedded;
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;

        _apiKey = LoadApiKey();

        BuildUi();
        LoadAudioDevices();

        if (string.IsNullOrWhiteSpace(_apiKey))
            SetStatus("Missing API key. Add config.json beside the EXE or set OPENAI_API_KEY.");
        else
            SetStatus(mode == LiveAssistMode.Meeting
                ? "Ready. Add meeting context, select audio source, then Start Live Meeting."
                : "Ready. Load resume/JD, select output device, then Start Realtime.");
    }

    private static string LoadApiKey()
    {
        // Priority:
        // 1. Windows environment variable OPENAI_API_KEY
        // 2. config.json beside the published EXE
        // 3. config.json in the current working folder
        // 4. optional HardcodedApiKey constant above

        string envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        if (!string.IsNullOrWhiteSpace(envKey))
            return envKey.Trim();

        string[] possibleConfigPaths =
        {
            Path.Combine(AppContext.BaseDirectory, "config.json"),
            Path.Combine(Environment.CurrentDirectory, "config.json")
        };

        foreach (string path in possibleConfigPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                string json = File.ReadAllText(path);

                var config = JsonSerializer.Deserialize<Config>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (!string.IsNullOrWhiteSpace(config?.apiKey))
                    return config.apiKey.Trim();
            }
            catch
            {
                // Ignore malformed config and continue to fallback.
            }
        }

        if (!string.IsNullOrWhiteSpace(HardcodedApiKey))
            return HardcodedApiKey.Trim();

        return "";
    }

    private void BuildUi()
    {
        BackColor = ShellBack;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(18),
            BackColor = ShellBack
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, _mode == LiveAssistMode.Meeting ? 225 : 190));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, _mode == LiveAssistMode.Meeting ? 210 : 170));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        _rootLayout = root;
        _contextRowStyle = root.RowStyles[1];
        _controlsRowStyle = root.RowStyles[2];

        // =========================
        // HEADER
        // =========================
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = ShellBack,
            Padding = new Padding(0, 0, 0, 8)
        };

        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = ShellBack
        };

        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 70));

        var title = new Label
        {
            Text = _mode == LiveAssistMode.Meeting
                ? "RoleAxis Meeting Assistant"
                : "RoleAxis Interview Assistant",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 19f, FontStyle.Bold),
            ForeColor = Ink,
            TextAlign = ContentAlignment.BottomLeft
        };

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.TopLeft;
        _statusLabel.Font = new Font("Segoe UI", 10f);
        _statusLabel.ForeColor = SoftText;

        titleStack.Controls.Add(title, 0, 0);
        titleStack.Controls.Add(_statusLabel, 0, 1);

        var rightStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = ShellBack
        };
        rightStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = ShellBack
        };

        StyleButton(_askButton, "Ask GPT Now", ButtonStyle.Accent, 135);
        _askButton.Click += async (_, _) => await AskGptForTextAsync(GetManualPromptText(), "MANUAL");
        actionRow.Controls.Add(_askButton);

        StyleButton(_stopButton, "Stop", ButtonStyle.Danger, 90);
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => StopAll();
        actionRow.Controls.Add(_stopButton);

        StyleButton(_startButton, _mode == LiveAssistMode.Meeting ? "Start Live Meeting" : "Start Realtime", ButtonStyle.Primary, _mode == LiveAssistMode.Meeting ? 165 : 145);
        _startButton.Click += async (_, _) => await StartAsync();
        actionRow.Controls.Add(_startButton);

        var metricsStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = ShellBack
        };

        metricsStack.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        metricsStack.RowStyles.Add(new RowStyle(SizeType.Percent, 70));

        var liveBadge = new Label
        {
            Text = "LIVE MODE",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomRight,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Success
        };

        _audioLabel.Dock = DockStyle.Fill;
        _audioLabel.TextAlign = ContentAlignment.TopRight;
        _audioLabel.Font = new Font("Consolas", 9.5f);
        _audioLabel.ForeColor = SoftText;

        metricsStack.Controls.Add(liveBadge, 0, 0);
        metricsStack.Controls.Add(_audioLabel, 0, 1);
        rightStack.Controls.Add(actionRow, 0, 0);
        rightStack.Controls.Add(metricsStack, 0, 1);

        header.Controls.Add(titleStack, 0, 0);
        header.Controls.Add(rightStack, 1, 0);
        root.Controls.Add(header, 0, 0);

        // =========================
        // CONTEXT CARD
        // =========================
        var contextCard = CreateCard(_mode == LiveAssistMode.Meeting ? "Meeting Context" : "Context", Surface);

        Control context = _mode == LiveAssistMode.Meeting
            ? BuildMeetingContext()
            : BuildInterviewContext();

        contextCard.Controls.Add(context);
        root.Controls.Add(contextCard, 0, 1);
        _contextCard = contextCard;

        // =========================
        // CONTROLS CARD
        // =========================
        var controlsCard = CreateCard(_mode == LiveAssistMode.Meeting ? "Audio + Meeting Intelligence" : "Audio + Automation", Surface);

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = _mode == LiveAssistMode.Meeting ? 4 : 3,
            ColumnCount = 1,
            Padding = new Padding(16, 24, 16, 14),
            BackColor = Surface
        };

        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        if (_mode == LiveAssistMode.Meeting)
            controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var row1 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Surface
        };

        row1.Controls.Add(new Label
        {
            Text = "Audio Source",
            AutoSize = true,
            Padding = new Padding(0, 10, 10, 0),
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Ink
        });

        _deviceCombo.Width = 500;
        _deviceCombo.Height = 32;
        _deviceCombo.FlatStyle = FlatStyle.Flat;
        row1.Controls.Add(_deviceCombo);

        StyleButton(_refreshDevicesButton, "Refresh", ButtonStyle.Secondary, 95);
        _refreshDevicesButton.Click += (_, _) => LoadAudioDevices();
        row1.Controls.Add(_refreshDevicesButton);

        var row2 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Surface
        };

        _autoAskCheck.Text = _mode == LiveAssistMode.Meeting ? "Auto-Suggest" : "Auto-Ask";
        _autoAskCheck.Checked = true;
        _autoAskCheck.Width = 110;
        _autoAskCheck.Height = 32;
        _autoAskCheck.ForeColor = Ink;
        _autoAskCheck.BackColor = Surface;
        row2.Controls.Add(_autoAskCheck);

        _topMostCheck.Text = "Always on top";
        _topMostCheck.Checked = true;
        _topMostCheck.Width = 145;
        _topMostCheck.Height = 32;
        _topMostCheck.ForeColor = Ink;
        _topMostCheck.BackColor = Surface;
        _topMostCheck.CheckedChanged += (_, _) => TopMost = _topMostCheck.Checked;
        row2.Controls.Add(_topMostCheck);

        row2.Controls.Add(CreateSmallLabel("Cooldown"));
        StyleNumberBox(_cooldownBox, 2, 30, 5);
        row2.Controls.Add(_cooldownBox);

        row2.Controls.Add(CreateSmallLabel("Min words"));
        StyleNumberBox(_minWordsBox, 4, 40, 8);
        row2.Controls.Add(_minWordsBox);

        var row3 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Surface
        };

        StyleButton(_clearTranscriptButton, "Clear Transcript", ButtonStyle.Secondary, 150);
        _clearTranscriptButton.Click += (_, _) => ClearTranscriptUi();
        row3.Controls.Add(_clearTranscriptButton);

        StyleButton(_clearAnswersButton, "Clear Answers", ButtonStyle.Secondary, 140);
        _clearAnswersButton.Click += (_, _) => _answerBox.Clear();
        row3.Controls.Add(_clearAnswersButton);

        var hint = new Label
        {
            Text = _mode == LiveAssistMode.Meeting
                ? "Tip: Auto-Suggest watches the live transcript and proposes concise next responses."
                : "Tip: Use Auto-Ask for clear interviewer questions, or Ask GPT Now for manual control.",
            AutoSize = true,
            Padding = new Padding(12, 9, 0, 0),
            Font = new Font("Segoe UI", 9f),
            ForeColor = SoftText
        };
        row3.Controls.Add(hint);

        controls.Controls.Add(row1, 0, 0);
        controls.Controls.Add(row2, 0, 1);
        controls.Controls.Add(row3, 0, 2);
        if (_mode == LiveAssistMode.Meeting)
            controls.Controls.Add(BuildMeetingIntelligenceButtons(), 0, 3);

        controlsCard.Controls.Add(controls);
        root.Controls.Add(controlsCard, 0, 2);
        _controlsCard = controlsCard;
        // =========================
        // MAIN SPLIT
        // =========================
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            BackColor = ShellBack,
            Padding = new Padding(0, 8, 0, 8)
        };

        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var answerPanel = CreateCard(_mode == LiveAssistMode.Meeting ? "Suggested Response" : "GPT Answer", Surface);
        answerPanel.Padding = new Padding(12, 28, 12, 12);

        _answerBox.Dock = DockStyle.Fill;
        _answerBox.Font = new Font("Segoe UI", _mode == LiveAssistMode.Meeting ? 13.5f : 17f, FontStyle.Regular);
        _answerBox.BackColor = Color.FromArgb(250, 253, 255);
        _answerBox.ForeColor = Ink;
        _answerBox.BorderStyle = BorderStyle.None;
        _answerBox.ReadOnly = true;
        _answerBox.Margin = new Padding(0);

        answerPanel.Controls.Add(_answerBox);
        split.Controls.Add(answerPanel, 0, 0);

        var transcriptPanel = CreateCard("Live Transcript", Surface);
        transcriptPanel.Padding = new Padding(12, 28, 12, 12);

        _transcriptBox.Dock = DockStyle.Fill;
        _transcriptBox.Font = new Font("Segoe UI", 13f);
        _transcriptBox.BackColor = Color.FromArgb(250, 253, 255);
        _transcriptBox.ForeColor = Ink;
        _transcriptBox.BorderStyle = BorderStyle.None;
        _transcriptBox.ReadOnly = true;
        _transcriptBox.Margin = new Padding(0);

        transcriptPanel.Controls.Add(_transcriptBox);
        split.Controls.Add(transcriptPanel, 1, 0);

        root.Controls.Add(split, 0, 3);

        // =========================
        // FOOTER STATUS
        // =========================
        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface2,
            Padding = new Padding(14, 0, 14, 0)
        };

        _footerStatusLabel.Dock = DockStyle.Fill;
        _footerStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _footerStatusLabel.Font = new Font("Segoe UI Semibold", 10f);
        _footerStatusLabel.ForeColor = Accent;
        _footerStatusLabel.Text = "System status: waiting.";

        footer.Controls.Add(_footerStatusLabel);
        root.Controls.Add(footer, 0, 4);

        Controls.Add(root);
    }

    private void SetPreparationVisible(bool visible)
    {
        if (_rootLayout == null || _contextCard == null || _controlsCard == null || _contextRowStyle == null || _controlsRowStyle == null)
            return;

        int contextHeight = _mode == LiveAssistMode.Meeting ? 225 : 190;
        int controlsHeight = _mode == LiveAssistMode.Meeting ? 210 : 170;

        _rootLayout.SuspendLayout();
        _contextCard.Visible = visible;
        _controlsCard.Visible = visible;
        _contextRowStyle.SizeType = SizeType.Absolute;
        _controlsRowStyle.SizeType = SizeType.Absolute;
        _contextRowStyle.Height = visible ? contextHeight : 0;
        _controlsRowStyle.Height = visible ? controlsHeight : 0;
        _rootLayout.ResumeLayout(true);
    }

    private Control BuildInterviewContext()
    {
        var context = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 2,
            Padding = new Padding(16, 20, 16, 16),
            BackColor = Surface
        };

        context.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        context.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        context.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        context.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var resumeHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Surface
        };

        resumeHeader.Controls.Add(new Label
        {
            Text = "Resume / Candidate Context",
            AutoSize = true,
            Padding = new Padding(0, 8, 12, 0),
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Ink
        });

        StyleButton(_browseResumeButton, "Browse Resume", ButtonStyle.Secondary, 145);
        _browseResumeButton.Click += (_, _) => PickResumeFile();
        resumeHeader.Controls.Add(_browseResumeButton);

        var jdHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Surface
        };

        jdHeader.Controls.Add(new Label
        {
            Text = "Job Description",
            AutoSize = true,
            Padding = new Padding(0, 8, 12, 0),
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Ink
        });

        StyleButton(_browseJdButton, "Browse JD", ButtonStyle.Secondary, 120);
        _browseJdButton.Click += (_, _) => PickJdFile();
        jdHeader.Controls.Add(_browseJdButton);

        StyleTextBox(_resumeBox);
        StyleTextBox(_jdBox);

        context.Controls.Add(resumeHeader, 0, 0);
        context.Controls.Add(jdHeader, 1, 0);
        context.Controls.Add(_resumeBox, 0, 1);
        context.Controls.Add(_jdBox, 1, 1);
        return context;
    }

    private Control BuildMeetingContext()
    {
        var context = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 4,
            Padding = new Padding(16, 24, 16, 16),
            BackColor = Surface
        };

        context.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        context.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        context.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        context.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        context.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        context.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        context.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        context.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        StyleSingleLineTextBox(_meetingTitleBox);
        StyleSingleLineTextBox(_meetingParticipantsBox);
        StyleTextBox(_meetingAgendaBox);

        _meetingTypeCombo.Items.Clear();
        _meetingTypeCombo.Items.AddRange(new object[]
        {
            "Team meeting",
            "Client meeting",
            "Project review",
            "Sales call",
            "Technical discussion",
            "Executive update",
            "Interview debrief",
            "Custom"
        });
        _meetingTypeCombo.SelectedIndex = 0;
        StyleComboBox(_meetingTypeCombo);

        _meetingRoleCombo.Items.Clear();
        _meetingRoleCombo.Items.AddRange(new object[]
        {
            "Presenter",
            "Participant",
            "Project owner",
            "Sales rep",
            "Interviewer",
            "Candidate",
            "Manager",
            "Custom"
        });
        _meetingRoleCombo.SelectedIndex = 1;
        StyleComboBox(_meetingRoleCombo);

        context.Controls.Add(CreateMeetingFieldLabel("Meeting title"), 0, 0);
        context.Controls.Add(CreateMeetingFieldLabel("Meeting type"), 1, 0);
        context.Controls.Add(CreateMeetingFieldLabel("User role"), 2, 0);
        context.Controls.Add(CreateMeetingFieldLabel("Participants"), 3, 0);

        context.Controls.Add(_meetingTitleBox, 0, 1);
        context.Controls.Add(_meetingTypeCombo, 1, 1);
        context.Controls.Add(_meetingRoleCombo, 2, 1);
        context.Controls.Add(_meetingParticipantsBox, 3, 1);

        context.Controls.Add(CreateMeetingFieldLabel("Agenda"), 0, 2);
        context.SetColumnSpan(_meetingAgendaBox, 4);
        context.Controls.Add(_meetingAgendaBox, 0, 3);
        return context;
    }

    private Control BuildMeetingIntelligenceButtons()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Surface
        };

        StyleButton(_meetingSummaryButton, "Generate Summary", ButtonStyle.Secondary, 150);
        _meetingSummaryButton.Click += async (_, _) => await AskGptForTextAsync(BuildFullTranscriptForGpt(""), "SUMMARY", "summary");
        row.Controls.Add(_meetingSummaryButton);

        StyleButton(_meetingDecisionsButton, "Extract Decisions", ButtonStyle.Secondary, 150);
        _meetingDecisionsButton.Click += async (_, _) => await AskGptForTextAsync(BuildFullTranscriptForGpt(""), "DECISIONS", "decisions");
        row.Controls.Add(_meetingDecisionsButton);

        StyleButton(_meetingActionsButton, "Extract Action Items", ButtonStyle.Secondary, 160);
        _meetingActionsButton.Click += async (_, _) => await AskGptForTextAsync(BuildFullTranscriptForGpt(""), "ACTIONS", "action_items");
        row.Controls.Add(_meetingActionsButton);

        StyleButton(_meetingEmailButton, "Follow-up Email", ButtonStyle.Secondary, 145);
        _meetingEmailButton.Click += async (_, _) => await AskGptForTextAsync(BuildFullTranscriptForGpt(""), "EMAIL", "follow_up_email");
        row.Controls.Add(_meetingEmailButton);

        StyleButton(_meetingSaveButton, "Save Meeting Notes", ButtonStyle.Accent, 165);
        _meetingSaveButton.Click += (_, _) => SaveMeetingNotes();
        row.Controls.Add(_meetingSaveButton);

        return row;
    }

    private static Label CreateMeetingFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI Semibold", 9.2f),
            ForeColor = Ink
        };
    }

    private enum ButtonStyle
    {
        Primary,
        Secondary,
        Accent,
        Danger
    }

    private Panel CreateCard(string title, Color background)
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = background,
            Padding = new Padding(14, 34, 14, 14),
            Margin = new Padding(4),
            Radius = 12
        };

        var titleLabel = new Label
        {
            Text = title,
            Height = 28,
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10.5f),
            ForeColor = Ink,
            BackColor = background,
            Padding = new Padding(2, 0, 0, 0)
        };

        panel.Controls.Add(titleLabel);
        titleLabel.BringToFront();

        return panel;
    }

    private void StyleTextBox(TextBox box)
    {
        box.Multiline = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.Dock = DockStyle.Fill;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 10f);
        box.BackColor = Color.FromArgb(250, 253, 255);
        box.ForeColor = Ink;
        box.Margin = new Padding(4);
    }

    private void StyleSingleLineTextBox(TextBox box)
    {
        box.Multiline = false;
        box.Dock = DockStyle.Fill;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 10f);
        box.BackColor = Color.FromArgb(250, 253, 255);
        box.ForeColor = Ink;
        box.Margin = new Padding(4);
    }

    private void StyleComboBox(ComboBox combo)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Font = new Font("Segoe UI", 10f);
        combo.BackColor = Color.FromArgb(250, 253, 255);
        combo.ForeColor = Ink;
        combo.Margin = new Padding(4);
    }

    private Label CreateSmallLabel(string text)
    {
        return new Label
        {
            Text = text + ":",
            AutoSize = true,
            Padding = new Padding(10, 9, 4, 0),
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Ink
        };
    }

    private void StyleNumberBox(NumericUpDown box, int min, int max, int value)
    {
        box.Minimum = min;
        box.Maximum = max;
        box.Value = value;
        box.Width = 64;
        box.Height = 30;
        box.BackColor = Color.White;
        box.ForeColor = Ink;
    }

    private void StyleButton(Button button, string text, ButtonStyle style, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 32;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI Semibold", 9.2f);
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(4);

        switch (style)
        {
            case ButtonStyle.Primary:
                button.BackColor = AccentDark;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = AccentDark;
                break;

            case ButtonStyle.Accent:
                button.BackColor = Accent;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Accent;
                break;

            case ButtonStyle.Danger:
                button.BackColor = Danger;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Danger;
                break;

            default:
                button.BackColor = Color.FromArgb(246, 251, 255);
                button.ForeColor = Ink;
                button.FlatAppearance.BorderColor = Stroke;
                break;
        }

        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(button.BackColor, 0.12f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(button.BackColor, 0.12f);
    }

    private void LoadAudioDevices()
    {
        try
        {
            _renderDevices.Clear();
            _deviceCombo.Items.Clear();

            var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

            foreach (var dev in devices)
            {
                _renderDevices.Add(dev);
                _deviceCombo.Items.Add(dev.FriendlyName);
            }

            if (_deviceCombo.Items.Count > 0)
            {
                _deviceCombo.SelectedIndex = 0;
            }

            Log($"Loaded {_deviceCombo.Items.Count} render devices.");
            SetFooterStatus($"Loaded {_deviceCombo.Items.Count} audio output devices.");
        }
        catch (Exception ex)
        {
            SetStatus("Device load error: " + ex.Message);
            SetFooterStatus("Device load error: " + ex.Message);
            Log("Device load error: " + ex);
        }
    }

    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            SetStatus("Missing API key.");
            SetFooterStatus("Missing API key. Add config.json beside the EXE or set OPENAI_API_KEY.");
            MessageBox.Show(
                "Missing OpenAI API key. Create config.json beside the EXE with: { \"apiKey\": \"YOUR_KEY\" }",
                "Missing API Key",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return;
        }

        if (_deviceCombo.SelectedIndex < 0 || _deviceCombo.SelectedIndex >= _renderDevices.Count)
        {
            SetStatus("Select a valid capture device.");
            SetFooterStatus("Select a valid capture device.");
            return;
        }

        try
        {
            _cts = new CancellationTokenSource();
            _audioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(30)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            await ConnectRealtimeAsync(_cts.Token);

            _ = Task.Run(() => AudioSenderLoopAsync(_cts.Token));
            StartLoopbackCapture(_renderDevices[_deviceCombo.SelectedIndex]);

            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _deviceCombo.Enabled = false;
            SetPreparationVisible(false);

            SetStatus(_mode == LiveAssistMode.Meeting ? "Live meeting listening is running." : "Realtime transcription running.");
            SetFooterStatus(_mode == LiveAssistMode.Meeting ? "Listening to meeting audio. Waiting for speakers..." : "Listening to system audio. Waiting for speech...");
            Log("Realtime started.");
        }
        catch (Exception ex)
        {
            SetStatus("Start error: " + ex.Message);
            SetFooterStatus("Start error: " + ex.Message);
            Log("Start error: " + ex);
            StopAll();
        }
    }

    private async Task ConnectRealtimeAsync(CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

        var uri = new Uri("wss://api.openai.com/v1/realtime?intent=transcription");
        await _ws.ConnectAsync(uri, ct);

        var sessionUpdate = new
        {
            type = "session.update",
            session = new
            {
                type = "transcription",
                audio = new
                {
                    input = new
                    {
                        format = new
                        {
                            type = "audio/pcm",
                            rate = TargetSampleRate
                        },
                        transcription = new
                        {
                            model = RealtimeTranscriptionModel,
                            language = "en"
                        },
                        turn_detection = new
                        {
                            type = "server_vad",
                            threshold = 0.5,
                            prefix_padding_ms = 300,
                            silence_duration_ms = 500
                        },
                        noise_reduction = new
                        {
                            type = "near_field"
                        }
                    }
                }
            }
        };

        await SendJsonAsync(sessionUpdate, ct);
        _ = Task.Run(() => ReceiveLoopAsync(ct), ct);
    }

    private void StartLoopbackCapture(MMDevice device)
    {
        _capture = new WasapiLoopbackCapture(device);

        _capture.DataAvailable += (_, e) =>
        {
            try
            {
                if (_audioChannel == null || _cts == null || _cts.IsCancellationRequested)
                    return;

                byte[] pcm24k = ConvertToPcm16Mono24k(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
                if (pcm24k.Length == 0) return;

                _audioChunksSent++;
                _audioBytesSent += pcm24k.Length;
                _audioChannel.Writer.TryWrite(pcm24k);
            }
            catch (Exception ex)
            {
                Log("Audio callback error: " + ex.Message);
            }
        };

        _capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null)
                Log("Capture stopped with error: " + e.Exception.Message);
        };

        _capture.StartRecording();
        Log($"Capturing: {device.FriendlyName}");
        Log($"Input format: {_capture.WaveFormat}");
    }

    private async Task AudioSenderLoopAsync(CancellationToken ct)
    {
        if (_audioChannel == null) return;

        try
        {
            await foreach (byte[] pcm in _audioChannel.Reader.ReadAllAsync(ct))
            {
                if (_ws == null || _ws.State != WebSocketState.Open) continue;

                string b64 = Convert.ToBase64String(pcm);
                var evt = new
                {
                    type = "input_audio_buffer.append",
                    audio = b64
                };

                await SendJsonAsync(evt, ct);
                UpdateAudioLabel();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log("Audio sender error: " + ex.Message);
            SetFooterStatus("Audio sender error: " + ex.Message);
        }
    }

    private byte[] ConvertToPcm16Mono24k(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        float[] mono = ToMonoFloat(buffer, bytesRecorded, format);
        if (mono.Length == 0) return Array.Empty<byte>();

        _lastRms = ComputeRms(mono);

        float[] resampled = ResampleLinear(mono, format.SampleRate, TargetSampleRate);
        if (resampled.Length == 0) return Array.Empty<byte>();

        byte[] pcm = ArrayPool<byte>.Shared.Rent(resampled.Length * 2);
        int offset = 0;

        foreach (float sample in resampled)
        {
            float clamped = Math.Clamp(sample, -1.0f, 1.0f);
            short s = (short)(clamped * short.MaxValue);
            pcm[offset++] = (byte)(s & 0xff);
            pcm[offset++] = (byte)((s >> 8) & 0xff);
        }

        byte[] exact = new byte[offset];
        Buffer.BlockCopy(pcm, 0, exact, 0, offset);
        ArrayPool<byte>.Shared.Return(pcm);

        return exact;
    }

    private static float[] ToMonoFloat(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        int channels = Math.Max(1, format.Channels);

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            int totalSamples = bytesRecorded / 4;
            int frames = totalSamples / channels;
            float[] mono = new float[frames];

            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleIndex = frame * channels + ch;
                    sum += BitConverter.ToSingle(buffer, sampleIndex * 4);
                }
                mono[frame] = sum / channels;
            }

            return mono;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            int totalSamples = bytesRecorded / 2;
            int frames = totalSamples / channels;
            float[] mono = new float[frames];

            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleIndex = frame * channels + ch;
                    short s = BitConverter.ToInt16(buffer, sampleIndex * 2);
                    sum += s / 32768f;
                }
                mono[frame] = sum / channels;
            }

            return mono;
        }

        return Array.Empty<float>();
    }

    private static float[] ResampleLinear(float[] input, int sourceRate, int targetRate)
    {
        if (input.Length == 0) return Array.Empty<float>();
        if (sourceRate == targetRate) return input;

        int outputLength = (int)((long)input.Length * targetRate / sourceRate);
        if (outputLength <= 0) return Array.Empty<float>();

        float[] output = new float[outputLength];
        double ratio = (double)(input.Length - 1) / Math.Max(1, outputLength - 1);

        for (int i = 0; i < outputLength; i++)
        {
            double pos = i * ratio;
            int left = (int)pos;
            int right = Math.Min(left + 1, input.Length - 1);
            double frac = pos - left;
            output[i] = (float)(input[left] * (1 - frac) + input[right] * frac);
        }

        return output;
    }

    private static float ComputeRms(float[] samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (float s in samples)
            sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;

        string json = JsonSerializer.Serialize(payload);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            ct
        );
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[1024 * 64];

        while (!ct.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
        {
            try
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        SetStatus("Realtime socket closed.");
                        SetFooterStatus("Realtime socket closed.");
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string json = Encoding.UTF8.GetString(ms.ToArray());
                HandleRealtimeEvent(json);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                SetStatus("Receive error: " + ex.Message);
                SetFooterStatus("Receive error: " + ex.Message);
                Log("Receive error: " + ex);
                return;
            }
        }
    }

    private void HandleRealtimeEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string type = root.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString() ?? ""
                : "";

            switch (type)
            {
                case "session.created":
                    Log("Session created.");
                    SetFooterStatus("Realtime session created.");
                    break;

                case "session.updated":
                    Log("Session updated.");
                    SetFooterStatus("Realtime session configured.");
                    break;

                case "input_audio_buffer.speech_started":
                    _liveDeltaBuffer.Clear();
                    SetStatus("Speech detected...");
                    SetFooterStatus("Speech detected. Listening...");
                    break;

                case "input_audio_buffer.speech_stopped":
                    SetStatus("Speech stopped. Finalizing transcript...");
                    SetFooterStatus("Speech stopped. Finalizing transcript...");
                    break;

                case "conversation.item.input_audio_transcription.delta":
                    if (root.TryGetProperty("delta", out var deltaEl))
                    {
                        string delta = deltaEl.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(delta))
                        {
                            _liveDeltaBuffer.Append(delta);
                            ShowLiveTranscript(_liveDeltaBuffer.ToString());
                            SetFooterStatus("Live caption streaming...");
                        }
                    }
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    if (root.TryGetProperty("transcript", out var transcriptEl))
                    {
                        string text = transcriptEl.GetString() ?? "";
                        HandleFinalTranscript(text);
                    }
                    break;

                case "error":
                {
                    string errorMessage = json;

                    try
                    {
                        if (root.TryGetProperty("error", out var err))
                        {
                            if (err.TryGetProperty("message", out var msg))
                                errorMessage = msg.GetString() ?? json;
                        }
                    }
                    catch { }

                    Log("OpenAI error: " + json);
                    SetStatus("OpenAI realtime error: " + errorMessage);
                    SetFooterStatus("OpenAI realtime error: " + errorMessage);
                    MessageBox.Show(errorMessage, "OpenAI Realtime Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
                }

                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            Log("Event parse error: " + ex.Message);
        }
    }

    private void ShowLiveTranscript(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;

        BeginInvoke(() =>
        {
            _transcriptBox.Clear();

            _transcriptBox.SelectionFont = new Font("Segoe UI Semibold", 11f);
            _transcriptBox.SelectionColor = Accent;
            _transcriptBox.AppendText("LIVE CAPTION\n");

            _transcriptBox.SelectionFont = new Font("Segoe UI", 13f);
            _transcriptBox.SelectionColor = Ink;

            string existing = _transcriptHistory.ToString();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _transcriptBox.AppendText(existing.TrimEnd() + Environment.NewLine + Environment.NewLine);
            }

            _transcriptBox.SelectionColor = Accent;
            _transcriptBox.AppendText(text);

            _transcriptBox.SelectionStart = _transcriptBox.TextLength;
            _transcriptBox.ScrollToCaret();
        });
    }

    private void HandleFinalTranscript(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length < 2) return;

        _lastFinalTranscript = text;
        _liveDeltaBuffer.Clear();

        bool shouldAsk = _autoAskCheck.Checked && ShouldAskGpt(text);

        if (_mode == LiveAssistMode.Meeting)
        {
            if (_transcriptHistory.Length > 0)
                _transcriptHistory.AppendLine();

            _transcriptHistory.AppendLine(text);
            RenderFinalTranscript("LIVE MEETING TRANSCRIPT");

            if (shouldAsk)
            {
                _ = AskGptForTextAsync(BuildFullTranscriptForGpt(""), "AUTO", "live_suggestion");
                SetStatus("Meeting transcript captured. Auto-suggest is preparing a response.");
                SetFooterStatus("Auto-suggest triggered from live meeting transcript.");
                return;
            }

            SetStatus("Meeting transcript received.");
            SetFooterStatus("Meeting transcript received. Waiting for enough signal before suggesting.");
            return;
        }

        if (shouldAsk)
        {
            string fullPromptText = BuildFullTranscriptForGpt(text);

            _transcriptHistory.Clear();

            BeginInvoke(() =>
            {
                _transcriptBox.Clear();
            });

            _ = AskGptForTextAsync(fullPromptText, "AUTO");
            SetStatus("Question detected. Sending full transcript context to GPT...");
            SetFooterStatus("Clear question detected. Full transcript sent to GPT.");
            return;
        }

        if (_transcriptHistory.Length > 0)
            _transcriptHistory.AppendLine();

        _transcriptHistory.AppendLine(text);

        RenderFinalTranscript("FINAL TRANSCRIPT");

        SetStatus("Transcript received.");
        SetFooterStatus("Transcript received. No clear question detected, GPT not triggered.");
    }

    private bool ShouldAskGpt(string text)
    {
        if (_gptInFlight) return false;

        text = (text ?? "").Trim();
        if (text.Length < 10) return false;

        string norm = NormalizeForDedupe(text);
        if (string.IsNullOrWhiteSpace(norm)) return false;
        if (norm == _lastSentNorm) return false;

        int cooldown = (int)_cooldownBox.Value;
        if ((DateTime.UtcNow - _lastAskTimeUtc).TotalSeconds < cooldown)
            return false;

        string lower = " " + text.ToLowerInvariant() + " ";
        int wc = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        int minWords = Math.Max(4, (int)_minWordsBox.Value);
        if (wc < minWords) return false;

        if (_mode == LiveAssistMode.Meeting)
        {
            _lastSentNorm = norm;
            _lastAskTimeUtc = DateTime.UtcNow;
            return true;
        }

        // Hard reject common non-question coaching/instruction phrases.
        string[] rejectPhrases =
        {
            " you should ",
            " you need to ",
            " you have to ",
            " try to ",
            " make sure ",
            " remember to ",
            " let's ",
            " we are going to ",
            " we're going to ",
            " i want you to ",
            " you can ",
            " you could ",
            " it should ",
            " this should "
        };

        foreach (string phrase in rejectPhrases)
        {
            if (lower.Contains(phrase))
                return false;
        }

        bool hasQuestionMark = text.Contains('?');

        bool startsWithDirectQuestion = StartsWithAny(
            text.Trim().ToLowerInvariant(),
            "what ",
            "why ",
            "how ",
            "when ",
            "where ",
            "who ",
            "which ",
            "can you ",
            "could you ",
            "would you ",
            "do you ",
            "did you ",
            "have you ",
            "are you ",
            "is there ",
            "tell me about ",
            "walk me through ",
            "explain ",
            "describe ",
            "give me an example of ",
            "what",
            "talk about "
        );

        bool containsStrongQuestionPhrase =
            lower.Contains(" can you tell me ") ||
            lower.Contains(" could you tell me ") ||
            lower.Contains(" tell me about ") ||
            lower.Contains(" tell us about ") ||
            lower.Contains(" walk me through ") ||
            lower.Contains(" walk us through ") ||
            lower.Contains(" explain how ") ||
            lower.Contains(" are you able to ") ||
            lower.Contains(" explain what ") ||
            lower.Contains(" what do you understand ") ||
            lower.Contains(" what is your experience ") ||
            lower.Contains(" why should we ") ||
            lower.Contains(" why do you want ") ||
            lower.Contains(" how would you ") ||
            lower.Contains(" what can you ") ||
            lower.Contains(" how do you ") ||
            lower.Contains(" describe a time ") ||
            lower.Contains(" give me an example ");

        bool strongQuestion = hasQuestionMark || startsWithDirectQuestion || containsStrongQuestionPhrase;

        if (!strongQuestion)
            return false;

        _lastSentNorm = norm;
        _lastAskTimeUtc = DateTime.UtcNow;
        return true;
    }

    private async Task AskGptForTextAsync(string text, string source, string meetingTask = "live_suggestion")
    {
        text = (text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            RenderAnswer("No transcript available.", "");
            return;
        }

        if (_gptInFlight)
        {
            SetFooterStatus("GPT is already answering.");
            return;
        }

        _gptInFlight = true;

        BeginInvoke(() =>
        {
            _answerBox.Clear();
            RenderQuestionHeader(text);
            RenderAnswerBody("Thinking...");
        });

        SetStatus("Asking GPT...");
        SetFooterStatus("Asking GPT. Waiting for first answer tokens...");

        try
        {
            string systemInstructions = _mode == LiveAssistMode.Meeting
                ? BuildMeetingSystemInstructions(meetingTask)
                : BuildInterviewSystemInstructions();
            string userContent = _mode == LiveAssistMode.Meeting
                ? BuildMeetingUserPrompt(text, meetingTask)
                : BuildInterviewUserPrompt(text);

            var body = new
            {
                model = AnswerModel,
                temperature = 0.45,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = systemInstructions
                    },
                    new
                    {
                        role = "user",
                        content = userContent
                    }
                },
                stream = true
            };

            string requestJson = JsonSerializer.Serialize(body);

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            string full = "";

            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync();
                RenderAnswer(text, "GPT error: " + err);
                return;
            }

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            BeginInvoke(() =>
            {
                _answerBox.Clear();
                RenderQuestionHeader(text);
                RenderAnswerBody("");
            });

            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync();
                if (line == null) continue;
                if (!line.StartsWith("data:")) continue;

                string data = line.Substring("data:".Length).Trim();
                if (data == "[DONE]") break;
                if (string.IsNullOrWhiteSpace(data)) continue;

                try
                {
                    using var chunkDoc = JsonDocument.Parse(data);
                    var choice = chunkDoc.RootElement.GetProperty("choices")[0];

                    if (choice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var contentEl))
                    {
                        string deltaText = contentEl.GetString() ?? "";
                        if (!string.IsNullOrEmpty(deltaText))
                        {
                            full += deltaText;
                            AppendAnswerDelta(deltaText);
                            SetFooterStatus("GPT answer streaming...");
                        }
                    }
                }
                catch
                {
                    // Ignore malformed SSE fragments.
                }
            }

            SetStatus("GPT answer ready.");
            SetFooterStatus("Answer ready.");
            if (_mode == LiveAssistMode.Meeting && meetingTask == "summary")
                ActivityLogService.Add("Meeting", "Meeting summary generated", "Generated from live transcript.");
            else if (_mode == LiveAssistMode.Meeting && meetingTask == "follow_up_email")
                ActivityLogService.Add("Meeting", "Follow-up email generated", "Generated from live transcript.");
            BeginInvoke(() => HighlightAnswerKeywords());
        }
        catch (Exception ex)
        {
            RenderAnswer(text, "GPT exception: " + ex.Message);
            SetFooterStatus("GPT exception: " + ex.Message);
        }
        finally
        {
            _gptInFlight = false;
        }
    }

    private string BuildFullTranscriptForGpt(string latestText)
    {
        latestText = (latestText ?? "").Trim();

        var parts = new List<string>();

        string history = _transcriptHistory.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(history))
            parts.Add(history);

        if (!string.IsNullOrWhiteSpace(latestText))
            parts.Add(latestText);

        return string.Join(Environment.NewLine, parts)
            .Trim();
    }

    private string BuildInterviewSystemInstructions()
    {
        return
            "You are a professional interview assistant. Generate the exact interview answer I should say, in first person, as if I am speaking directly to the interviewer. " +
            "Do not give advice, do not explain how I should answer, and do not frame the response as coaching. " +
            "Do not use phrases like 'I would say', 'If asked', 'You can say', or anything that sounds like guidance. " +
            "Just produce the final spoken answer in my voice.\n\n" +
            "The answer must sound natural, confident, convincing, and human, like a strong real interview candidate speaking out loud. " +
            "Do not sound robotic, scripted, overly formal, or overly polished like AI-generated text. " +
            "Avoid filler, meta-commentary, disclaimers, and unnecessary setup.\n\n" +
            "Keep the language clear and easy to follow. Do not sound overly technical, overly complex, or full of jargon unless the interviewer clearly asks for technical depth. " +
            "By default, explain things in a simple, professional, conversational way that still sounds smart and credible. " +
            "If the question is technical, answer clearly first, then add only the most useful technical detail needed to sound strong and competent.\n\n" +
            "This is an initial recruiter conversation, not a deep technical interview, unless the interviewer clearly asks a technical question. " +
            "Return only the final answer. Do not repeat the question. Do not include labels like Q: or A:. Maximum 45 words.";
    }

    private string BuildInterviewUserPrompt(string text)
    {
        string resume = Clamp(_resumeBox.Text.Trim(), 9000);
        string jd = Clamp(_jdBox.Text.Trim(), 7000);

        return
            "Candidate resume/context:\n" + resume +
            "\n\nJob description:\n" + jd +
            "\n\nRecent interviewer transcript. It may contain one or more questions:\n" + text +
            "\n\nIdentify the most important clear question or questions in the transcript and answer them naturally as the candidate. " +
            "If there are multiple related questions, combine them into one strong spoken answer. " +
            "Do not repeat the question. Do not answer non-question filler. No bullets unless absolutely necessary.";
    }

    private string BuildMeetingSystemInstructions(string meetingTask)
    {
        if (meetingTask == "summary")
            return "You are a meeting intelligence assistant. Create a concise executive meeting summary with decisions, unresolved questions, risks, and next steps. Use clear headings.";

        if (meetingTask == "decisions")
            return "You are a meeting intelligence assistant. Extract only explicit or strongly implied decisions from the meeting transcript. Include owner or context when available. Use bullets.";

        if (meetingTask == "action_items")
            return "You are a meeting intelligence assistant. Extract action items from the meeting transcript. Include task, owner, deadline, and dependency when available. Use bullets.";

        if (meetingTask == "follow_up_email")
            return "You are a meeting intelligence assistant. Draft a concise professional follow-up email from the meeting transcript. Include summary, decisions, action items, and next steps.";

        return
            "You are a live meeting strategist. Help the user know what to say next during a real meeting. " +
            "Be concise, direct, and useful live. Consider the user's role, the meeting type, participants, agenda, and latest transcript. " +
            "Return exactly these sections:\n\n" +
            "Suggested Response:\n" +
            "Clarifying Question:\n" +
            "Key Point:\n" +
            "Risk or Concern:\n" +
            "Follow-up:\n\n" +
            "Keep each section short enough to glance at during a live call.";
    }

    private string BuildMeetingUserPrompt(string text, string meetingTask)
    {
        string transcript = Clamp(text, meetingTask == "live_suggestion" ? 7000 : 14000);
        string taskInstruction = meetingTask switch
        {
            "summary" => "Generate Summary.",
            "decisions" => "Extract Decisions.",
            "action_items" => "Extract Action Items.",
            "follow_up_email" => "Generate Follow-up Email.",
            _ => "Suggest what I should say next live."
        };

        return
            "Meeting title:\n" + _meetingTitleBox.Text.Trim() +
            "\n\nMeeting type:\n" + (_meetingTypeCombo.SelectedItem?.ToString() ?? "") +
            "\n\nParticipants:\n" + _meetingParticipantsBox.Text.Trim() +
            "\n\nAgenda:\n" + _meetingAgendaBox.Text.Trim() +
            "\n\nUser role in meeting:\n" + (_meetingRoleCombo.SelectedItem?.ToString() ?? "") +
            "\n\nTask:\n" + taskInstruction +
            "\n\nTranscript:\n" + transcript;
    }

    private void RenderFinalTranscript(string heading)
    {
        if (!IsHandleCreated) return;

        BeginInvoke(() =>
        {
            _transcriptBox.Clear();

            _transcriptBox.SelectionFont = new Font("Segoe UI Semibold", 11f);
            _transcriptBox.SelectionColor = Success;
            _transcriptBox.AppendText(heading + "\n");

            _transcriptBox.SelectionFont = new Font("Segoe UI", 13f);
            _transcriptBox.SelectionColor = Ink;
            _transcriptBox.AppendText(_transcriptHistory.ToString());

            _transcriptBox.SelectionStart = _transcriptBox.TextLength;
            _transcriptBox.ScrollToCaret();
        });
    }

    private string GetManualPromptText()
    {
        string selected = _transcriptBox.SelectedText?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(selected))
            return selected;

        if (!string.IsNullOrWhiteSpace(_lastFinalTranscript))
            return _lastFinalTranscript;

        return _transcriptBox.Text.Trim();
    }

    private void PickResumeFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select Resume",
            Filter = "Supported Files (*.txt;*.pdf;*.docx)|*.txt;*.pdf;*.docx|Text Files (*.txt)|*.txt|PDF Files (*.pdf)|*.pdf|Word Documents (*.docx)|*.docx|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _resumeBox.Text = ReadDocumentFile(dlg.FileName);
            SetStatus("Resume loaded: " + Path.GetFileName(dlg.FileName));
            SetFooterStatus("Resume loaded successfully.");
        }
        catch (Exception ex)
        {
            SetStatus("Resume load failed: " + ex.Message);
            SetFooterStatus("Resume load failed.");
            Log("Resume load failed: " + ex);
        }
    }

    private void PickJdFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select Job Description",
            Filter = "Supported Files (*.txt;*.pdf;*.docx)|*.txt;*.pdf;*.docx|Text Files (*.txt)|*.txt|PDF Files (*.pdf)|*.pdf|Word Documents (*.docx)|*.docx|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            _jdBox.Text = ReadDocumentFile(dlg.FileName);
            SetStatus("Job description loaded: " + Path.GetFileName(dlg.FileName));
            SetFooterStatus("Job description loaded successfully.");
        }
        catch (Exception ex)
        {
            SetStatus("JD load failed: " + ex.Message);
            SetFooterStatus("JD load failed.");
            Log("JD load failed: " + ex);
        }
    }

    private static string ReadDocumentFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".txt")
            return File.ReadAllText(path);

        if (ext == ".pdf")
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(path);
            foreach (var page in pdf.GetPages())
                sb.AppendLine(page.Text);
            return sb.ToString();
        }

        if (ext == ".docx")
        {
            using var doc = WordprocessingDocument.Open(path, false);
            return doc.MainDocumentPart?.Document?.Body?.InnerText ?? "";
        }

        throw new NotSupportedException("Unsupported file type: " + ext);
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
    {
        foreach (string p in prefixes)
        {
            if (value.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string NormalizeForDedupe(string text)
    {
        var chars = text
            .Trim()
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '?')
            .ToArray();

        return new string(chars);
    }

    private static string Clamp(string text, int maxChars)
    {
        text ??= "";
        if (text.Length <= maxChars) return text;
        return text.Substring(0, maxChars);
    }

    private void ClearTranscriptUi()
    {
        _transcriptBox.Clear();
        _lastFinalTranscript = "";
        _liveDeltaBuffer.Clear();
        _transcriptHistory.Clear();
    }

    private void SaveMeetingNotes()
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RoleAxis",
                "meetings");
            Directory.CreateDirectory(folder);
            string safeTitle = string.Join(
                "-",
                (_meetingTitleBox.Text.Trim().Length == 0 ? "meeting" : _meetingTitleBox.Text.Trim())
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            string path = Path.Combine(folder, $"{safeTitle}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            var builder = new StringBuilder();
            builder.AppendLine("ROLEAXIS MEETING NOTES");
            builder.AppendLine("======================");
            builder.AppendLine("Title: " + _meetingTitleBox.Text.Trim());
            builder.AppendLine("Type: " + (_meetingTypeCombo.SelectedItem?.ToString() ?? ""));
            builder.AppendLine("Role: " + (_meetingRoleCombo.SelectedItem?.ToString() ?? ""));
            builder.AppendLine("Participants: " + _meetingParticipantsBox.Text.Trim());
            builder.AppendLine();
            builder.AppendLine("AGENDA");
            builder.AppendLine(_meetingAgendaBox.Text.Trim());
            builder.AppendLine();
            builder.AppendLine("FULL TRANSCRIPT");
            builder.AppendLine(_transcriptHistory.ToString().Trim());
            builder.AppendLine();
            builder.AppendLine("LATEST SUGGESTED RESPONSE / INTELLIGENCE");
            builder.AppendLine(_answerBox.Text.Trim());

            File.WriteAllText(path, builder.ToString());
            SetFooterStatus("Meeting notes saved: " + path);
            RenderAnswer("", "Meeting notes saved:\n" + path);
        }
        catch (Exception ex)
        {
            SetFooterStatus("Meeting notes save failed: " + ex.Message);
            RenderAnswer("", "Meeting notes save failed: " + ex.Message);
        }
    }

    private void RenderQuestionHeader(string question)
    {
        _answerBox.SelectionFont = new Font("Segoe UI Semibold", 12f);
        _answerBox.SelectionColor = Accent;
        _answerBox.AppendText(_mode == LiveAssistMode.Meeting ? "MEETING TRANSCRIPT SENT TO GPT\n" : "TRANSCRIPT SENT TO GPT\n");

        _answerBox.SelectionFont = new Font("Segoe UI", 13f, FontStyle.Italic);
        _answerBox.SelectionColor = SoftText;
        _answerBox.AppendText(Clamp(question, 500) + Environment.NewLine + Environment.NewLine);

        _answerBox.SelectionFont = new Font("Segoe UI Semibold", 13f);
        _answerBox.SelectionColor = Success;
        _answerBox.AppendText(_mode == LiveAssistMode.Meeting ? "SUGGESTED RESPONSE\n" : "ANSWER\n");
    }

    private void RenderAnswerBody(string answer)
    {
        _answerBox.SelectionFont = new Font("Segoe UI", _mode == LiveAssistMode.Meeting ? 13.5f : 17f, FontStyle.Regular);
        _answerBox.SelectionColor = Ink;
        _answerBox.AppendText(answer);
        if (!string.IsNullOrWhiteSpace(answer) && !answer.Equals("Thinking...", StringComparison.OrdinalIgnoreCase))
            HighlightAnswerKeywords();
        _answerBox.SelectionStart = _answerBox.TextLength;
        _answerBox.ScrollToCaret();
    }

    private void AppendAnswerDelta(string delta)
    {
        if (!IsHandleCreated) return;

        BeginInvoke(() =>
        {
            _answerBox.SelectionFont = new Font("Segoe UI", _mode == LiveAssistMode.Meeting ? 13.5f : 17f, FontStyle.Regular);
            _answerBox.SelectionColor = Ink;
            _answerBox.AppendText(delta);
            _answerBox.SelectionStart = _answerBox.TextLength;
            _answerBox.ScrollToCaret();
        });
    }

    private void HighlightAnswerKeywords()
    {
        if (_answerBox.IsDisposed)
            return;

        string text = _answerBox.Text;
        if (text.Length < 12 || text.Length > 24000)
            return;

        int originalStart = _answerBox.SelectionStart;
        int originalLength = _answerBox.SelectionLength;
        var ranges = BuildAnswerKeywordRanges(text).Take(90).ToList();
        if (ranges.Count == 0)
            return;

        _answerBox.SuspendLayout();
        try
        {
            var font = new Font("Segoe UI Semibold", _mode == LiveAssistMode.Meeting ? 13.5f : 17f, FontStyle.Bold);
            foreach (var range in ranges)
            {
                if (range.Start < 0 || range.Length <= 0 || range.Start + range.Length > text.Length)
                    continue;
                _answerBox.Select(range.Start, range.Length);
                _answerBox.SelectionFont = font;
                _answerBox.SelectionColor = Danger;
            }
        }
        finally
        {
            _answerBox.Select(Math.Min(originalStart, _answerBox.TextLength), Math.Min(originalLength, Math.Max(0, _answerBox.TextLength - originalStart)));
            _answerBox.ResumeLayout();
        }
    }

    private static IEnumerable<(int Start, int Length)> BuildAnswerKeywordRanges(string text)
    {
        var terms = new HashSet<string>(AnswerKeywordHints, StringComparer.OrdinalIgnoreCase);
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "answer", "response", "suggested", "meeting", "transcript", "sent", "gpt", "this", "that", "there", "when", "what",
            "with", "from", "your", "have", "will", "about", "because"
        };

        foreach (Match match in Regex.Matches(text, @"\b(?:[A-Z]{2,}|[A-Z][a-zA-Z0-9+/#.-]{4,}|\d+(?:[%x+]|\.\d+)?)\b"))
        {
            string value = match.Value.Trim();
            if (value.Length >= 3 && !blocked.Contains(value))
                terms.Add(value);
            if (terms.Count >= 48)
                break;
        }

        var ranges = new List<(int Start, int Length)>();
        foreach (string term in terms.Where(term => term.Length >= 3).OrderByDescending(term => term.Length))
        {
            string pattern = Regex.Escape(term).Replace("\\ ", "\\s+");
            foreach (Match match in Regex.Matches(text, @"(?<!\w)" + pattern + @"(?!\w)", RegexOptions.IgnoreCase))
                ranges.Add((match.Index, match.Length));
        }

        return ranges
            .OrderBy(range => range.Start)
            .Aggregate(new List<(int Start, int Length)>(), (merged, range) =>
            {
                if (merged.Count == 0 || range.Start >= merged[^1].Start + merged[^1].Length)
                    merged.Add(range);
                return merged;
            });
    }

    private void RenderAnswer(string question, string answer)
    {
        if (!IsHandleCreated) return;

        BeginInvoke(() =>
        {
            _answerBox.Clear();
            if (!string.IsNullOrWhiteSpace(question))
                RenderQuestionHeader(question);
            RenderAnswerBody(answer);
        });
    }

    private void UpdateAudioLabel()
    {
        if (!IsHandleCreated) return;

        BeginInvoke(() =>
        {
            _audioLabel.Text =
                $"RMS:{_lastRms:0.0000} | chunks:{_audioChunksSent} | sent:{_audioBytesSent / 1024}KB";
        });
    }

    private void SetStatus(string text)
    {
        if (!IsHandleCreated) return;

        BeginInvoke(() =>
        {
            _statusLabel.Text = text;
        });
    }

    private void SetFooterStatus(string text)
    {
        if (!IsHandleCreated) return;

        BeginInvoke(() =>
        {
            _footerStatusLabel.Text = "System status: " + text;
        });
    }

    private void Log(string text)
    {
        string item = $"[{DateTime.Now:HH:mm:ss}] {text}";
        _internalLog.Add(item);
        if (_internalLog.Count > 300)
            _internalLog.RemoveAt(0);
    }

    public void EndSession()
    {
        StopAll();
    }

    public void LoadInterviewContext(string resume, string jobDescription)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => LoadInterviewContext(resume, jobDescription));
            return;
        }

        if (_mode != LiveAssistMode.Interview)
            return;

        _resumeBox.Text = resume ?? "";
        _jdBox.Text = jobDescription ?? "";
        SetStatus("Interview context loaded from RoleAxis Resume Intelligence.");
        SetFooterStatus("Resume and job context loaded.");
    }

    private void StopAll()
    {
        try
        {
            _cts?.Cancel();

            try
            {
                _audioChannel?.Writer.TryComplete();
            }
            catch { }

            if (_capture != null)
            {
                try { _capture.StopRecording(); } catch { }
                try { _capture.Dispose(); } catch { }
                _capture = null;
            }

            if (_ws != null)
            {
                try
                {
                    if (_ws.State == WebSocketState.Open)
                    {
                        _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None)
                           .Wait(1000);
                    }
                }
                catch { }

                try { _ws.Dispose(); } catch { }
                _ws = null;
            }

            _cts?.Dispose();
            _cts = null;
        }
        catch { }

        if (IsHandleCreated)
        {
            BeginInvoke(() =>
            {
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
                _deviceCombo.Enabled = true;
                SetPreparationVisible(true);
                _statusLabel.Text = _mode == LiveAssistMode.Meeting ? "Meeting listening stopped." : "Stopped.";
                _footerStatusLabel.Text = "System status: stopped.";
            });
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopAll();
        base.OnFormClosing(e);
    }
}
