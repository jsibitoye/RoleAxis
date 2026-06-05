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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Packaging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using UglyToad.PdfPig;

namespace RoleAxis.Career.InterviewAssistant;

public sealed partial class RoleAxisDesktopAppForm
{
    private const int PresentationTargetSampleRate = 24000;
    private const string PresentationTranscriptionModel = "gpt-4o-mini-transcribe";
    private const string PresentationAnswerModel = "gpt-4o-mini";

    private readonly MMDeviceEnumerator _presentationDeviceEnumerator = new();
    private readonly List<MMDevice> _presentationRenderDevices = new();
    private readonly StringBuilder _presentationQaTranscriptHistory = new();
    private readonly StringBuilder _presentationLiveDeltaBuffer = new();

    private ComboBox? _presentationModeCombo;
    private ComboBox? _presentationTypeCombo;
    private ComboBox? _presentationAudienceCombo;
    private ComboBox? _presentationResponseStyleCombo;
    private ComboBox? _presentationAudioCombo;
    private TextBox? _presentationTitleBox;
    private TextBox? _presentationGoalBox;
    private TextBox? _presentationMinutesBox;
    private TextBox? _presentationNotesBox;
    private RichTextBox? _presentationOutputBox;
    private RichTextBox? _presentationQaTranscriptBox;
    private RichTextBox? _presentationQaAnswerBox;
    private CheckedListBox? _presentationKeyPointsList;
    private Label? _presentationFileStatusLabel;
    private Label? _presentationTimerLabel;
    private Label? _presentationTargetLabel;
    private Label? _presentationPaceLabel;
    private Label? _presentationNextPointLabel;
    private Label? _presentationQaStatusLabel;
    private FlowLayoutPanel? _presentationPrepActionsPanel;
    private FlowLayoutPanel? _presentationLiveActionsPanel;
    private FlowLayoutPanel? _presentationQaActionsPanel;
    private FlowLayoutPanel? _presentationDebriefActionsPanel;
    private CheckBox? _presentationAutoSuggestCheck;
    private NumericUpDown? _presentationCooldownBox;
    private NumericUpDown? _presentationMinWordsBox;

    private string _presentationLoadedFilePath = "";
    private string _presentationLoadedContent = "";
    private string _presentationLastQuestionNorm = "";
    private DateTime _presentationTimerStartedAtUtc = DateTime.MinValue;
    private DateTime _presentationLastAskUtc = DateTime.MinValue;
    private TimeSpan _presentationElapsedBeforePause = TimeSpan.Zero;
    private TimeSpan _presentationTargetDuration = TimeSpan.FromMinutes(10);
    private bool _presentationTimerPaused = true;
    private volatile bool _presentationGptInFlight;
    private long _presentationAudioChunksSent;
    private float _presentationLastRms;

    private WasapiLoopbackCapture? _presentationCapture;
    private ClientWebSocket? _presentationWs;
    private CancellationTokenSource? _presentationQaCts;
    private Channel<byte[]>? _presentationAudioChannel;

    private void ShowPresentationCopilot()
    {
        ClearContent();
        SetView("presentation", "Presentation Assistant", "Prepare, present, answer live questions, and generate follow-up notes.");
        _contentHost.AutoScroll = false;

        _presentationLoadedFilePath = "";
        _presentationLoadedContent = "";
        _presentationQaTranscriptHistory.Clear();
        _presentationLiveDeltaBuffer.Clear();
        _presentationLastQuestionNorm = "";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Navy
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy,
            Padding = new Padding(0, 0, 14, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        var setupCard = CreatePresentationCard("Presentation Setup", out var setupBody);
        var setup = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            BackColor = Surface
        };

        _presentationModeCombo = CreatePresentationCombo(new[]
        {
            "Preparation Mode",
            "Live Presentation Mode",
            "Q&A Mode",
            "Debrief Mode"
        });
        _presentationModeCombo.SelectedIndexChanged += (_, _) => SetPresentationMode(_presentationModeCombo.Text);

        _presentationTypeCombo = CreatePresentationCombo(new[]
        {
            "Project demo",
            "Executive update",
            "Client pitch",
            "Technical presentation",
            "Academic presentation",
            "Investor pitch",
            "Interview presentation",
            "Custom"
        });

        _presentationAudienceCombo = CreatePresentationCombo(new[]
        {
            "Executives",
            "Technical team",
            "Clients",
            "Investors",
            "Academic panel",
            "Interview panel",
            "General"
        });

        _presentationTitleBox = CreatePresentationInput("Board update, client demo, investor pitch...");
        _presentationMinutesBox = CreatePresentationInput("10");
        _presentationGoalBox = CreatePresentationInput("Decision, approval, buy-in, education, confidence...");
        _presentationNotesBox = CreatePresentationTextArea("Paste speaker notes, slide text, or talking points here.", 132);

        AddPresentationField(setup, "Mode", _presentationModeCombo);
        AddPresentationField(setup, "Presentation title", _presentationTitleBox);
        AddPresentationField(setup, "Presentation type", _presentationTypeCombo);
        AddPresentationField(setup, "Audience", _presentationAudienceCombo);
        AddPresentationField(setup, "Target minutes", _presentationMinutesBox);
        AddPresentationField(setup, "Presentation goal", _presentationGoalBox);
        AddPresentationField(setup, "Speaker notes / slide content", _presentationNotesBox, 160);

        var fileRow = new FlowLayoutPanel
        {
            Width = 410,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Surface,
            Margin = new Padding(0, 2, 0, 8)
        };
        var loadFileButton = CreatePresentationButton("Load Material", 136, BlackButton, Color.White);
        _presentationFileStatusLabel = CreatePresentationLabel("No file loaded.", 8.4f, SoftText);
        _presentationFileStatusLabel.Width = 246;
        _presentationFileStatusLabel.Height = 36;
        fileRow.Controls.Add(loadFileButton);
        fileRow.Controls.Add(_presentationFileStatusLabel);
        loadFileButton.Click += async (_, _) => await LoadPresentationMaterialAsync();
        setup.Controls.Add(fileRow);
        setupBody.Controls.Add(setup);

        var controlsCard = CreatePresentationCard("Mode Controls", out var controlsBody);
        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            BackColor = Surface
        };

        _presentationPrepActionsPanel = CreatePresentationActionsPanel();
        AddPrepButton("Build Presentation Brief", "brief");
        AddPrepButton("Generate Speaker Script", "script");
        AddPrepButton("Slide Talking Points", "talking_points");
        AddPrepButton("Generate Opening", "opening");
        AddPrepButton("Generate Closing", "closing");
        AddPrepButton("Likely Questions", "questions");
        AddPrepButton("Weak Point Checklist", "weak_points");

        _presentationLiveActionsPanel = CreatePresentationActionsPanel();
        var startTimerButton = CreatePresentationButton("Start Timer", 126, BlackButton, Color.White);
        var pauseTimerButton = CreatePresentationButton("Pause / Resume", 144, Surface2, Ink);
        var stopTimerButton = CreatePresentationButton("Stop Timer", 118, Surface2, Ink);
        var continueButton = CreatePresentationButton("Help Me Continue", 164, Cyan, Color.White);
        var refreshPointsButton = CreatePresentationButton("Refresh Key Points", 166, Surface2, Ink);
        startTimerButton.Click += (_, _) => StartPresentationTimer();
        pauseTimerButton.Click += (_, _) => PausePresentationTimer();
        stopTimerButton.Click += (_, _) => StopPresentationTimer();
        continueButton.Click += async (_, _) => await GeneratePresentationLiveHelpAsync();
        refreshPointsButton.Click += (_, _) => RefreshPresentationKeyPoints();
        _presentationLiveActionsPanel.Controls.Add(startTimerButton);
        _presentationLiveActionsPanel.Controls.Add(pauseTimerButton);
        _presentationLiveActionsPanel.Controls.Add(stopTimerButton);
        _presentationLiveActionsPanel.Controls.Add(continueButton);
        _presentationLiveActionsPanel.Controls.Add(refreshPointsButton);

        _presentationQaActionsPanel = CreatePresentationActionsPanel();
        _presentationAudioCombo = CreatePresentationCombo(Array.Empty<string>(), 300);
        _presentationResponseStyleCombo = CreatePresentationCombo(new[]
        {
            "Concise",
            "Executive",
            "Technical",
            "Academic",
            "Persuasive",
            "Defensive",
            "Simple"
        }, 160);
        _presentationAutoSuggestCheck = new CheckBox
        {
            Text = "Auto-Suggest Answer",
            Checked = true,
            Width = 180,
            Height = 32,
            BackColor = Surface,
            ForeColor = Ink
        };
        _presentationCooldownBox = CreatePresentationNumberBox(2, 30, 5);
        _presentationMinWordsBox = CreatePresentationNumberBox(4, 60, 8);
        var refreshAudioButton = CreatePresentationButton("Refresh Audio", 128, Surface2, Ink);
        var startListeningButton = CreatePresentationButton("Start Listening", 142, BlackButton, Color.White);
        var stopListeningButton = CreatePresentationButton("Stop", 86, Color.FromArgb(143, 45, 45), Color.White);
        var askNowButton = CreatePresentationButton("Ask GPT Now", 126, Cyan, Color.White);
        refreshAudioButton.Click += (_, _) => LoadPresentationAudioDevices();
        startListeningButton.Click += async (_, _) => await StartPresentationQaListeningAsync();
        stopListeningButton.Click += (_, _) => StopPresentationQaListening("Question listening stopped.");
        askNowButton.Click += async (_, _) => await GeneratePresentationQaAnswerAsync(GetPresentationQuestionText(), "MANUAL");

        AddQaControl("Audio", _presentationAudioCombo);
        AddQaControl("Style", _presentationResponseStyleCombo);
        AddQaControl("Cooldown", _presentationCooldownBox);
        AddQaControl("Min words", _presentationMinWordsBox);
        _presentationQaActionsPanel.Controls.Add(_presentationAutoSuggestCheck);
        _presentationQaActionsPanel.Controls.Add(refreshAudioButton);
        _presentationQaActionsPanel.Controls.Add(startListeningButton);
        _presentationQaActionsPanel.Controls.Add(stopListeningButton);
        _presentationQaActionsPanel.Controls.Add(askNowButton);

        _presentationDebriefActionsPanel = CreatePresentationActionsPanel();
        var debriefButton = CreatePresentationButton("Generate Debrief", 146, BlackButton, Color.White);
        var emailButton = CreatePresentationButton("Follow-up Email", 136, Cyan, Color.White);
        var saveButton = CreatePresentationButton("Save Notes", 110, Surface2, Ink);
        var copyButton = CreatePresentationButton("Copy Output", 114, Surface2, Ink);
        debriefButton.Click += async (_, _) => await GeneratePresentationDebriefAsync("debrief");
        emailButton.Click += async (_, _) => await GeneratePresentationDebriefAsync("follow_up_email");
        saveButton.Click += (_, _) => SavePresentationNotes();
        copyButton.Click += (_, _) => CopyPresentationOutput();
        _presentationDebriefActionsPanel.Controls.Add(debriefButton);
        _presentationDebriefActionsPanel.Controls.Add(emailButton);
        _presentationDebriefActionsPanel.Controls.Add(saveButton);
        _presentationDebriefActionsPanel.Controls.Add(copyButton);

        controls.Controls.Add(_presentationPrepActionsPanel);
        controls.Controls.Add(_presentationLiveActionsPanel);
        controls.Controls.Add(_presentationQaActionsPanel);
        controls.Controls.Add(_presentationDebriefActionsPanel);
        _presentationQaStatusLabel = CreatePresentationLabel("Q&A listening idle.", 8.8f, SoftText);
        _presentationQaStatusLabel.Width = 410;
        _presentationQaStatusLabel.Height = 32;
        controls.Controls.Add(_presentationQaStatusLabel);
        controlsBody.Controls.Add(controls);

        left.Controls.Add(setupCard, 0, 0);
        left.Controls.Add(controlsCard, 0, 1);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Navy
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 24));

        var outputCard = CreatePresentationCard("Output Intelligence", out var outputBody);
        _presentationOutputBox = CreatePresentationRichBox(readOnly: true);
        _presentationOutputBox.Text = BuildPresentationEmptyState("Preparation Mode");
        outputBody.Controls.Add(_presentationOutputBox);

        var qaCard = CreatePresentationCard("Q&A Answer", out var qaBody);
        var qaGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Surface
        };
        qaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        qaGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        _presentationQaTranscriptBox = CreatePresentationRichBox(readOnly: false);
        _presentationQaTranscriptBox.Text = "Live Audience Question Transcript\r\n\r\nPaste a question here, or start listening in Q&A Mode.";
        _presentationQaAnswerBox = CreatePresentationRichBox(readOnly: true);
        _presentationQaAnswerBox.Text = BuildPresentationQaEmptyState();
        qaGrid.Controls.Add(WrapPresentationMiniPanel("Live Audience Question Transcript", _presentationQaTranscriptBox), 0, 0);
        qaGrid.Controls.Add(WrapPresentationMiniPanel("Suggested Answer / Support / Risk / Follow-up", _presentationQaAnswerBox), 1, 0);
        qaBody.Controls.Add(qaGrid);

        var timerCard = CreatePresentationCard("Timer / Pace", out var timerBody);
        var timerGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Surface
        };
        timerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        timerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        var timerStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Surface
        };
        _presentationTimerLabel = CreatePresentationLabel("Elapsed: 00:00", 16f, Ink, FontStyle.Bold);
        _presentationTimerLabel.Width = 300;
        _presentationTimerLabel.Height = 34;
        _presentationTargetLabel = CreatePresentationLabel("Target: 10:00", 9.2f, SoftText);
        _presentationTargetLabel.Width = 300;
        _presentationTargetLabel.Height = 24;
        _presentationPaceLabel = CreatePresentationLabel("Pace: Ready", 10f, Cyan, FontStyle.Bold);
        _presentationPaceLabel.Width = 300;
        _presentationPaceLabel.Height = 26;
        _presentationNextPointLabel = CreatePresentationLabel("Next point: Add notes to generate guidance.", 9f, SoftText);
        _presentationNextPointLabel.Width = 330;
        _presentationNextPointLabel.Height = 46;
        timerStack.Controls.Add(_presentationTimerLabel);
        timerStack.Controls.Add(_presentationTargetLabel);
        timerStack.Controls.Add(_presentationPaceLabel);
        timerStack.Controls.Add(_presentationNextPointLabel);

        _presentationKeyPointsList = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            CheckOnClick = true,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9.2f)
        };
        _presentationKeyPointsList.ItemCheck += (_, _) => BeginInvoke(() => UpdatePresentationTimerUi());
        timerGrid.Controls.Add(timerStack, 0, 0);
        timerGrid.Controls.Add(_presentationKeyPointsList, 1, 0);
        timerBody.Controls.Add(timerGrid);

        right.Controls.Add(outputCard, 0, 0);
        right.Controls.Add(qaCard, 0, 1);
        right.Controls.Add(timerCard, 0, 2);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        _contentHost.Controls.Add(root);

        LoadPresentationAudioDevices();
        RefreshPresentationKeyPoints();
        SetPresentationMode("Preparation Mode");

        void AddPrepButton(string text, string task)
        {
            var button = CreatePresentationButton(text, 186, task == "brief" ? BlackButton : Surface2, task == "brief" ? Color.White : Ink);
            button.Click += async (_, _) => await GeneratePresentationPreparationAsync(task);
            _presentationPrepActionsPanel.Controls.Add(button);
        }

        void AddQaControl(string label, Control control)
        {
            var block = new FlowLayoutPanel
            {
                Width = 420,
                Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Surface,
                Margin = new Padding(0, 0, 0, 6)
            };
            var labelView = CreatePresentationLabel(label, 8.6f, SoftText, FontStyle.Bold);
            labelView.Width = 74;
            labelView.Height = 30;
            control.Margin = new Padding(0, 0, 10, 0);
            block.Controls.Add(labelView);
            block.Controls.Add(control);
            _presentationQaActionsPanel.Controls.Add(block);
        }
    }

    private void SetPresentationMode(string mode)
    {
        _presentationPrepActionsPanel?.Hide();
        _presentationLiveActionsPanel?.Hide();
        _presentationQaActionsPanel?.Hide();
        _presentationDebriefActionsPanel?.Hide();

        if (mode.StartsWith("Live", StringComparison.OrdinalIgnoreCase))
        {
            _presentationLiveActionsPanel?.Show();
            RefreshPresentationKeyPoints();
            RenderPresentationOutputIfEmpty(BuildPresentationEmptyState("Live Presentation Mode"));
        }
        else if (mode.StartsWith("Q&A", StringComparison.OrdinalIgnoreCase))
        {
            _presentationQaActionsPanel?.Show();
            RenderPresentationOutputIfEmpty(BuildPresentationEmptyState("Q&A Mode"));
        }
        else if (mode.StartsWith("Debrief", StringComparison.OrdinalIgnoreCase))
        {
            _presentationDebriefActionsPanel?.Show();
            RenderPresentationOutputIfEmpty(BuildPresentationEmptyState("Debrief Mode"));
        }
        else
        {
            _presentationPrepActionsPanel?.Show();
            RenderPresentationOutputIfEmpty(BuildPresentationEmptyState("Preparation Mode"));
        }
    }

    private Panel CreatePresentationCard(string title, out Panel body)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Radius = 12,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 0, 14)
        };

        var titleLabel = CreatePresentationLabel(title, 10.4f, Ink, FontStyle.Bold);
        titleLabel.Dock = DockStyle.Top;
        titleLabel.Height = 30;

        body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(0, 40, 0, 0)
        };

        card.Controls.Add(body);
        card.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        return card;
    }

    private FlowLayoutPanel CreatePresentationActionsPanel()
    {
        return new FlowLayoutPanel
        {
            Width = 430,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Surface,
            Margin = new Padding(0, 0, 0, 12)
        };
    }

    private static Label CreatePresentationLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            ForeColor = color,
            Font = new Font("Segoe UI", size, style),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
    }

    private static TextBox CreatePresentationInput(string value)
    {
        return new TextBox
        {
            Text = value,
            Width = 390,
            Height = 34,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9.7f)
        };
    }

    private static TextBox CreatePresentationTextArea(string value, int height)
    {
        return new TextBox
        {
            Text = value,
            Width = 390,
            Height = height,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9.7f)
        };
    }

    private static ComboBox CreatePresentationCombo(IEnumerable<string> items, int width = 390)
    {
        var combo = new ComboBox
        {
            Width = width,
            Height = 34,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 9.4f)
        };
        foreach (string item in items)
            combo.Items.Add(item);
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
        return combo;
    }

    private static NumericUpDown CreatePresentationNumberBox(int min, int max, int value)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Width = 72,
            Height = 30,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink
        };
    }

    private static Button CreatePresentationButton(string text, int width, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Segoe UI Semibold", 8.6f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 8)
        };
        button.FlatAppearance.BorderSize = backColor == Surface2 ? 1 : 0;
        button.FlatAppearance.BorderColor = Stroke;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.08f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.08f);
        return button;
    }

    private static RichTextBox CreatePresentationRichBox(bool readOnly)
    {
        return new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(250, 253, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 10.2f),
            ReadOnly = readOnly,
            DetectUrls = true
        };
    }

    private static Panel WrapPresentationMiniPanel(string title, Control content)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(250, 253, 255),
            Padding = new Padding(10, 34, 10, 10),
            Margin = new Padding(4)
        };
        var label = CreatePresentationLabel(title, 8.5f, Color.FromArgb(70, 91, 109), FontStyle.Bold);
        label.Dock = DockStyle.Top;
        label.Height = 24;
        panel.Controls.Add(content);
        panel.Controls.Add(label);
        label.BringToFront();
        return panel;
    }

    private static void AddPresentationField(FlowLayoutPanel parent, string label, Control control, int blockHeight = 68)
    {
        var block = new Panel
        {
            Width = 410,
            Height = blockHeight,
            BackColor = Surface,
            Margin = new Padding(0, 0, 0, 6)
        };
        var labelView = CreatePresentationLabel(label, 8.5f, Color.FromArgb(70, 91, 109), FontStyle.Bold);
        labelView.Location = new Point(0, 0);
        labelView.Width = 390;
        labelView.Height = 22;
        control.Location = new Point(0, 26);
        block.Controls.Add(labelView);
        block.Controls.Add(control);
        parent.Controls.Add(block);
    }

    private string BuildPresentationContext()
    {
        string notes = _presentationNotesBox?.Text.Trim() ?? "";
        string loaded = _presentationLoadedContent;
        var builder = new StringBuilder();
        builder.AppendLine("PRESENTATION PROFILE");
        builder.AppendLine("Title: " + DisplayOrFallback(_presentationTitleBox?.Text ?? "", "Untitled presentation"));
        builder.AppendLine("Type: " + DisplayOrFallback(_presentationTypeCombo?.Text ?? "", "General"));
        builder.AppendLine("Audience: " + DisplayOrFallback(_presentationAudienceCombo?.Text ?? "", "General"));
        builder.AppendLine("Target minutes: " + DisplayOrFallback(_presentationMinutesBox?.Text ?? "", "10"));
        builder.AppendLine("Goal: " + DisplayOrFallback(_presentationGoalBox?.Text ?? "", "Not specified"));
        builder.AppendLine();
        builder.AppendLine("SPEAKER NOTES / SLIDE CONTENT");
        builder.AppendLine(PresentationClamp(notes, 10000));
        if (!string.IsNullOrWhiteSpace(_presentationLoadedFilePath))
        {
            builder.AppendLine();
            builder.AppendLine("LOADED FILE");
            builder.AppendLine(_presentationLoadedFilePath);
            builder.AppendLine(PresentationClamp(loaded, 12000));
        }
        return builder.ToString();
    }

    private async Task GeneratePresentationPreparationAsync(string task)
    {
        if (_presentationOutputBox == null)
            return;

        RefreshPresentationKeyPoints();
        _presentationOutputBox.Text = "Generating presentation intelligence...";
        string prompt = BuildPreparationPrompt(task);
        string local = BuildLocalPreparationOutput(task);
        string result = await GeneratePresentationTextAsync(prompt, local);
        _presentationOutputBox.Text = result;
        LogActivity("Presentation", "Preparation generated", task.Replace('_', ' '));
    }

    private string BuildPreparationPrompt(string task)
    {
        string action = task switch
        {
            "script" => "Generate a polished speaker script with timing guidance.",
            "talking_points" => "Generate slide-by-slide or section-by-section talking points.",
            "opening" => "Generate three strong opening options and recommend the best one.",
            "closing" => "Generate three strong closing options with a clear final ask.",
            "questions" => "Generate likely audience questions with suggested answers.",
            "weak_points" => "Generate a weak point checklist with preparation fixes.",
            _ => "Build a complete presentation brief."
        };

        return
            "You are RoleAxis Presentation Copilot. Create premium, practical presentation guidance.\n\n" +
            action + "\n\n" +
            "Return clearly labeled sections: Executive Summary, Recommended Structure, Speaker Script, Slide Talking Points, Likely Audience Questions, Suggested Answers, Weak Areas To Prepare, Delivery Checklist.\n\n" +
            BuildPresentationContext();
    }

    private async Task GeneratePresentationLiveHelpAsync()
    {
        if (_presentationOutputBox == null)
            return;

        string nextPoint = GetNextPresentationPoint();
        _presentationOutputBox.Text = "Preparing a continuation suggestion...";
        string prompt =
            "You are a live presentation coach. The speaker is mid-presentation and needs a concise continuation.\n" +
            "Give them the next 30-60 seconds to say, a transition line, and one reminder about tone.\n\n" +
            "Next point: " + nextPoint + "\n\n" +
            BuildPresentationContext();
        string local = "HELP ME CONTINUE\r\n\r\n" +
            "Say next:\r\n" +
            "- " + nextPoint + "\r\n\r\n" +
            "Transition:\r\n" +
            "- The reason this matters now is simple: it changes the decision we can make today.\r\n\r\n" +
            "Tone reminder:\r\n" +
            "- Slow down, make the point once, then move forward.";
        _presentationOutputBox.Text = await GeneratePresentationTextAsync(prompt, local);
        LogActivity("Presentation", "Live help generated", ShortPath(nextPoint, 72));
    }

    private async Task GeneratePresentationDebriefAsync(string task)
    {
        if (_presentationOutputBox == null)
            return;

        _presentationOutputBox.Text = "Generating debrief...";
        string transcript = _presentationQaTranscriptBox?.Text.Trim() ?? "";
        string qaAnswer = _presentationQaAnswerBox?.Text.Trim() ?? "";
        string instruction = task == "follow_up_email"
            ? "Generate a concise follow-up email after this presentation. Include thanks, recap, answers to questions, and next steps."
            : "Generate a debrief with presentation summary, questions asked, improvement notes, strong points, weak points, and next steps.";

        string prompt =
            "You are RoleAxis Presentation Copilot. " + instruction + "\n\n" +
            BuildPresentationContext() + "\n\n" +
            "Q&A TRANSCRIPT\n" + PresentationClamp(transcript, 9000) + "\n\n" +
            "LATEST Q&A ANSWER\n" + PresentationClamp(qaAnswer, 6000);

        string local = BuildLocalDebriefOutput(task);
        _presentationOutputBox.Text = await GeneratePresentationTextAsync(prompt, local);
        LogActivity("Presentation", task == "follow_up_email" ? "Follow-up email generated" : "Debrief generated", DisplayOrFallback(_presentationTitleBox?.Text ?? "", "Presentation"));
    }

    private async Task GeneratePresentationQaAnswerAsync(string question, string source)
    {
        question = (question ?? "").Trim();
        if (_presentationQaAnswerBox == null)
            return;

        if (string.IsNullOrWhiteSpace(question))
        {
            _presentationQaAnswerBox.Text = BuildPresentationQaEmptyState();
            UpdatePresentationQaStatus("Add or capture an audience question first.");
            return;
        }

        if (_presentationGptInFlight)
        {
            UpdatePresentationQaStatus("A GPT answer is already in progress.");
            return;
        }

        try
        {
            _presentationGptInFlight = true;
            _presentationQaAnswerBox.Text = "Generating suggested answer...";
            UpdatePresentationQaStatus(source == "AUTO" ? "Auto-suggest is generating an answer." : "Asking GPT from latest question.");

            string style = _presentationResponseStyleCombo?.Text ?? "Concise";
            string prompt =
                "You are RoleAxis Presentation Q&A Strategist. Answer the audience question using the presentation context.\n" +
                "Response style: " + style + "\n\n" +
                "Return exactly these labeled sections:\n" +
                "Suggested Answer:\nSupporting Point:\nClarifying Question:\nRisk/Caution:\nFollow-up:\n\n" +
                BuildPresentationContext() + "\n\n" +
                "AUDIENCE QUESTION\n" + PresentationClamp(question, 5000);

            string local = BuildLocalQaAnswer(question, style);
            _presentationQaAnswerBox.Text = await GeneratePresentationTextAsync(prompt, local);
            UpdatePresentationQaStatus("Suggested answer ready.");
            LogActivity("Presentation", "Q&A answer generated", ShortPath(question, 72));
        }
        finally
        {
            _presentationGptInFlight = false;
        }
    }

    private async Task<string> GeneratePresentationTextAsync(string prompt, string localFallback)
    {
        string apiKey = LoadPresentationApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return localFallback +
                "\r\n\r\nManual step: add OPENAI_API_KEY or config.json beside the EXE to enable GPT-generated presentation intelligence.";
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var body = new
            {
                model = PresentationAnswerModel,
                temperature = 0.45,
                messages = new[]
                {
                    new { role = "system", content = "You are RoleAxis Presentation Copilot for high-stakes business presentations. Be concise, structured, practical, and executive-grade." },
                    new { role = "user", content = prompt }
                }
            };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return localFallback + "\r\n\r\nGPT request failed: " + PresentationClamp(json, 900);

            using var doc = JsonDocument.Parse(json);
            string content = "";
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                content = ReadString(choices[0], "message", "content");

            return string.IsNullOrWhiteSpace(content) ? localFallback : content.Trim();
        }
        catch (Exception ex)
        {
            return localFallback + "\r\n\r\nGPT request failed: " + ex.Message;
        }
    }

    private static string LoadPresentationApiKey()
    {
        string env = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        string path = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (!File.Exists(path))
            return "";

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return ReadString(document.RootElement, "apiKey").Trim();
        }
        catch
        {
            return "";
        }
    }

    private async Task LoadPresentationMaterialAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Load presentation material",
            Filter = "Supported Files (*.txt;*.docx;*.pdf;*.pptx)|*.txt;*.docx;*.pdf;*.pptx|Text Files (*.txt)|*.txt|Word Documents (*.docx)|*.docx|PDF Files (*.pdf)|*.pdf|PowerPoint Files (*.pptx)|*.pptx|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _presentationLoadedFilePath = dialog.FileName;
        _presentationLoadedContent = "";
        if (_presentationFileStatusLabel != null)
            _presentationFileStatusLabel.Text = "Loading " + Path.GetFileName(dialog.FileName) + "...";

        try
        {
            string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            _presentationLoadedContent = ext switch
            {
                ".txt" => await File.ReadAllTextAsync(dialog.FileName),
                ".docx" => await Task.Run(() => ReadPresentationDocx(dialog.FileName)),
                ".pdf" => await Task.Run(() => ReadPresentationPdf(dialog.FileName)),
                ".pptx" => await Task.Run(() => ReadPresentationPptx(dialog.FileName)),
                _ => ""
            };

            if (_presentationFileStatusLabel != null)
            {
                string status = string.IsNullOrWhiteSpace(_presentationLoadedContent)
                    ? "Loaded path only. Paste content if extraction is empty."
                    : $"Loaded {SplitWords(_presentationLoadedContent).Count} words.";
                _presentationFileStatusLabel.Text = status;
            }

            if (!string.IsNullOrWhiteSpace(_presentationLoadedContent) && _presentationNotesBox != null && _presentationNotesBox.Text.Contains("Paste speaker notes", StringComparison.OrdinalIgnoreCase))
                _presentationNotesBox.Text = PresentationClamp(_presentationLoadedContent, 5000);

            RefreshPresentationKeyPoints();
        }
        catch (Exception ex)
        {
            if (_presentationFileStatusLabel != null)
                _presentationFileStatusLabel.Text = "Load failed: " + ex.Message;
        }
    }

    private static string ReadPresentationDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        return doc.MainDocumentPart?.Document?.Body?.InnerText ?? "";
    }

    private static string ReadPresentationPdf(string path)
    {
        var builder = new StringBuilder();
        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages().Take(80))
            builder.AppendLine(page.Text);
        return builder.ToString();
    }

    private static string ReadPresentationPptx(string path)
    {
        var builder = new StringBuilder();
        using var doc = PresentationDocument.Open(path, false);
        var slides = doc.PresentationPart?.SlideParts ?? Enumerable.Empty<SlidePart>();
        foreach (var slide in slides.Take(80))
        {
            foreach (var text in slide.Slide?.Descendants<DocumentFormat.OpenXml.Drawing.Text>() ?? Enumerable.Empty<DocumentFormat.OpenXml.Drawing.Text>())
                if (!string.IsNullOrWhiteSpace(text.Text))
                    builder.AppendLine(text.Text.Trim());
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private void RefreshPresentationKeyPoints()
    {
        if (_presentationKeyPointsList == null)
            return;

        var checkedValues = _presentationKeyPointsList.CheckedItems.Cast<object>().Select(item => item.ToString() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var points = ExtractPresentationKeyPoints();
        _presentationKeyPointsList.Items.Clear();
        foreach (string point in points)
            _presentationKeyPointsList.Items.Add(point, checkedValues.Contains(point));
        UpdatePresentationTimerUi();
    }

    private List<string> ExtractPresentationKeyPoints()
    {
        string source = (_presentationNotesBox?.Text ?? "") + "\n" + _presentationLoadedContent;
        var lines = source.SplitLines()
            .Select(line => line.Trim().Trim('-', '*', ' ', '\t'))
            .Where(line => line.Length >= 18)
            .Take(14)
            .ToList();

        if (lines.Count > 0)
            return lines;

        return new List<string>
        {
            "Open with the audience problem.",
            "State the main recommendation.",
            "Prove the recommendation with evidence.",
            "Address the highest-risk objection.",
            "Close with the specific ask."
        };
    }

    private void StartPresentationTimer()
    {
        RefreshPresentationKeyPoints();
        int minutes = Math.Clamp(ParseInt(_presentationMinutesBox?.Text ?? "10", 10), 1, 240);
        _presentationTargetDuration = TimeSpan.FromMinutes(minutes);
        _presentationElapsedBeforePause = TimeSpan.Zero;
        _presentationTimerStartedAtUtc = DateTime.UtcNow;
        _presentationTimerPaused = false;

        _presentationTimer?.Stop();
        _presentationTimer?.Dispose();
        _presentationTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _presentationTimer.Tick += (_, _) => UpdatePresentationTimerUi();
        _presentationTimer.Start();
        UpdatePresentationTimerUi();
    }

    private void PausePresentationTimer()
    {
        if (_presentationTimer == null)
            return;

        if (_presentationTimerPaused)
        {
            _presentationTimerStartedAtUtc = DateTime.UtcNow;
            _presentationTimerPaused = false;
            _presentationTimer.Start();
        }
        else
        {
            _presentationElapsedBeforePause = GetPresentationElapsed();
            _presentationTimerPaused = true;
            _presentationTimer.Stop();
        }

        UpdatePresentationTimerUi();
    }

    private void StopPresentationTimer()
    {
        _presentationTimer?.Stop();
        _presentationTimerPaused = true;
        _presentationElapsedBeforePause = TimeSpan.Zero;
        UpdatePresentationTimerUi();
    }

    private TimeSpan GetPresentationElapsed()
    {
        if (_presentationTimerStartedAtUtc == DateTime.MinValue)
            return _presentationElapsedBeforePause;
        return _presentationTimerPaused
            ? _presentationElapsedBeforePause
            : _presentationElapsedBeforePause + (DateTime.UtcNow - _presentationTimerStartedAtUtc);
    }

    private void UpdatePresentationTimerUi()
    {
        if (_presentationTimerLabel == null || _presentationTargetLabel == null || _presentationPaceLabel == null || _presentationNextPointLabel == null)
            return;

        TimeSpan elapsed = GetPresentationElapsed();
        _presentationTimerLabel.Text = $"Elapsed: {elapsed:mm\\:ss}";
        _presentationTargetLabel.Text = $"Target: {_presentationTargetDuration:mm\\:ss}";

        string pace = CalculatePresentationPace(elapsed);
        _presentationPaceLabel.Text = "Pace: " + pace;
        _presentationPaceLabel.ForeColor = pace == "On pace" ? Color.FromArgb(18, 145, 104) : pace == "Too fast" ? Color.FromArgb(194, 124, 24) : Color.FromArgb(143, 45, 45);
        _presentationNextPointLabel.Text = "Next point: " + GetNextPresentationPoint();
    }

    private string CalculatePresentationPace(TimeSpan elapsed)
    {
        if (_presentationTargetDuration.TotalSeconds <= 0)
            return "On pace";

        int total = _presentationKeyPointsList?.Items.Count ?? 0;
        int completed = _presentationKeyPointsList?.CheckedItems.Count ?? 0;
        if (total <= 0)
            return elapsed <= _presentationTargetDuration ? "On pace" : "Too slow";

        double expected = Math.Clamp(elapsed.TotalSeconds / _presentationTargetDuration.TotalSeconds, 0, 1.4);
        double actual = Math.Clamp((double)completed / total, 0, 1);
        if (actual + 0.14 < expected)
            return "Too slow";
        if (actual - 0.18 > expected)
            return "Too fast";
        return "On pace";
    }

    private string GetNextPresentationPoint()
    {
        if (_presentationKeyPointsList == null || _presentationKeyPointsList.Items.Count == 0)
            return "Add notes to generate guidance.";

        for (int i = 0; i < _presentationKeyPointsList.Items.Count; i++)
            if (!_presentationKeyPointsList.GetItemChecked(i))
                return _presentationKeyPointsList.Items[i]?.ToString() ?? "Continue to the next key point.";

        return "All key points are checked. Move into closing and final ask.";
    }

    private void LoadPresentationAudioDevices()
    {
        if (_presentationAudioCombo == null)
            return;

        try
        {
            _presentationRenderDevices.Clear();
            _presentationAudioCombo.Items.Clear();
            var devices = _presentationDeviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            foreach (var device in devices)
            {
                _presentationRenderDevices.Add(device);
                _presentationAudioCombo.Items.Add(device.FriendlyName);
            }
            if (_presentationAudioCombo.Items.Count > 0)
                _presentationAudioCombo.SelectedIndex = 0;
            UpdatePresentationQaStatus($"Loaded {_presentationAudioCombo.Items.Count} audio output devices.");
        }
        catch (Exception ex)
        {
            UpdatePresentationQaStatus("Audio device load failed: " + ex.Message);
        }
    }

    private async Task StartPresentationQaListeningAsync()
    {
        string apiKey = LoadPresentationApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            UpdatePresentationQaStatus("Missing OpenAI API key for live Q&A listening.");
            return;
        }

        if (_presentationAudioCombo == null || _presentationAudioCombo.SelectedIndex < 0 || _presentationAudioCombo.SelectedIndex >= _presentationRenderDevices.Count)
        {
            UpdatePresentationQaStatus("Select a valid audio source.");
            return;
        }

        try
        {
            StopPresentationQaListening("");
            _presentationQaCts = new CancellationTokenSource();
            _presentationAudioChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(30)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            await ConnectPresentationRealtimeAsync(apiKey, _presentationQaCts.Token);
            _ = Task.Run(() => PresentationAudioSenderLoopAsync(_presentationQaCts.Token));
            StartPresentationLoopbackCapture(_presentationRenderDevices[_presentationAudioCombo.SelectedIndex]);
            UpdatePresentationQaStatus("Listening for audience questions...");
        }
        catch (Exception ex)
        {
            StopPresentationQaListening("");
            UpdatePresentationQaStatus("Q&A listening failed: " + ex.Message);
        }
    }

    private async Task ConnectPresentationRealtimeAsync(string apiKey, CancellationToken ct)
    {
        _presentationWs = new ClientWebSocket();
        _presentationWs.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        await _presentationWs.ConnectAsync(new Uri("wss://api.openai.com/v1/realtime?intent=transcription"), ct);

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
                        format = new { type = "audio/pcm", rate = PresentationTargetSampleRate },
                        transcription = new { model = PresentationTranscriptionModel, language = "en" },
                        turn_detection = new
                        {
                            type = "server_vad",
                            threshold = 0.5,
                            prefix_padding_ms = 300,
                            silence_duration_ms = 500
                        },
                        noise_reduction = new { type = "near_field" }
                    }
                }
            }
        };

        await SendPresentationJsonAsync(sessionUpdate, ct);
        _ = Task.Run(() => PresentationReceiveLoopAsync(ct), ct);
    }

    private void StartPresentationLoopbackCapture(MMDevice device)
    {
        _presentationCapture = new WasapiLoopbackCapture(device);
        _presentationCapture.DataAvailable += (_, e) =>
        {
            try
            {
                if (_presentationAudioChannel == null || _presentationQaCts == null || _presentationQaCts.IsCancellationRequested)
                    return;

                byte[] pcm = ConvertPresentationToPcm16Mono24k(e.Buffer, e.BytesRecorded, _presentationCapture.WaveFormat);
                if (pcm.Length == 0)
                    return;

                _presentationAudioChunksSent++;
                _presentationAudioChannel.Writer.TryWrite(pcm);
            }
            catch
            {
            }
        };
        _presentationCapture.StartRecording();
    }

    private async Task PresentationAudioSenderLoopAsync(CancellationToken ct)
    {
        if (_presentationAudioChannel == null)
            return;

        try
        {
            await foreach (byte[] pcm in _presentationAudioChannel.Reader.ReadAllAsync(ct))
            {
                if (_presentationWs == null || _presentationWs.State != WebSocketState.Open)
                    continue;

                await SendPresentationJsonAsync(new
                {
                    type = "input_audio_buffer.append",
                    audio = Convert.ToBase64String(pcm)
                }, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            UpdatePresentationQaStatus("Audio sender failed: " + ex.Message);
        }
    }

    private async Task PresentationReceiveLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[1024 * 64];
        while (!ct.IsCancellationRequested && _presentationWs != null && _presentationWs.State == WebSocketState.Open)
        {
            try
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _presentationWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        UpdatePresentationQaStatus("Realtime Q&A socket closed.");
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                HandlePresentationRealtimeEvent(Encoding.UTF8.GetString(ms.ToArray()));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                UpdatePresentationQaStatus("Realtime receive failed: " + ex.Message);
                return;
            }
        }
    }

    private void HandlePresentationRealtimeEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
            switch (type)
            {
                case "input_audio_buffer.speech_started":
                    _presentationLiveDeltaBuffer.Clear();
                    UpdatePresentationQaStatus("Audience speech detected...");
                    break;

                case "conversation.item.input_audio_transcription.delta":
                    if (root.TryGetProperty("delta", out var deltaEl))
                    {
                        string delta = deltaEl.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(delta))
                        {
                            _presentationLiveDeltaBuffer.Append(delta);
                            ShowPresentationLiveQuestion(_presentationLiveDeltaBuffer.ToString(), partial: true);
                        }
                    }
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    if (root.TryGetProperty("transcript", out var transcriptEl))
                        HandlePresentationFinalQuestion(transcriptEl.GetString() ?? "");
                    break;

                case "error":
                    UpdatePresentationQaStatus("OpenAI realtime error.");
                    break;
            }
        }
        catch
        {
        }
    }

    private void HandlePresentationFinalQuestion(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0)
            return;

        _presentationLiveDeltaBuffer.Clear();
        if (_presentationQaTranscriptHistory.Length > 0)
            _presentationQaTranscriptHistory.AppendLine().AppendLine();
        _presentationQaTranscriptHistory.AppendLine(text);
        ShowPresentationLiveQuestion(_presentationQaTranscriptHistory.ToString(), partial: false);
        UpdatePresentationQaStatus("Audience question captured.");

        if (ShouldAutoSuggestPresentationAnswer(text))
            _ = GeneratePresentationQaAnswerAsync(text, "AUTO");
    }

    private bool ShouldAutoSuggestPresentationAnswer(string text)
    {
        if (_presentationAutoSuggestCheck?.Checked != true)
            return false;

        int minWords = (int)(_presentationMinWordsBox?.Value ?? 8);
        if (SplitWords(text).Count < minWords)
            return false;

        int cooldown = (int)(_presentationCooldownBox?.Value ?? 5);
        if ((DateTime.UtcNow - _presentationLastAskUtc).TotalSeconds < cooldown)
            return false;

        string norm = NormalizePresentationText(text);
        if (norm == _presentationLastQuestionNorm)
            return false;

        _presentationLastQuestionNorm = norm;
        _presentationLastAskUtc = DateTime.UtcNow;
        return true;
    }

    private void ShowPresentationLiveQuestion(string text, bool partial)
    {
        if (_presentationQaTranscriptBox == null || !IsHandleCreated)
            return;

        BeginInvoke(() =>
        {
            _presentationQaTranscriptBox.Text = partial
                ? _presentationQaTranscriptHistory + "\r\n\r\nLIVE: " + text.Trim()
                : text.Trim();
            _presentationQaTranscriptBox.SelectionStart = _presentationQaTranscriptBox.TextLength;
            _presentationQaTranscriptBox.ScrollToCaret();
        });
    }

    private async Task SendPresentationJsonAsync(object payload, CancellationToken ct)
    {
        if (_presentationWs == null || _presentationWs.State != WebSocketState.Open)
            return;

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await _presentationWs.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private byte[] ConvertPresentationToPcm16Mono24k(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        float[] mono = PresentationToMonoFloat(buffer, bytesRecorded, format);
        if (mono.Length == 0)
            return Array.Empty<byte>();

        _presentationLastRms = ComputePresentationRms(mono);
        float[] resampled = PresentationResampleLinear(mono, format.SampleRate, PresentationTargetSampleRate);
        byte[] rented = ArrayPool<byte>.Shared.Rent(resampled.Length * 2);
        int offset = 0;
        foreach (float sample in resampled)
        {
            short s = (short)(Math.Clamp(sample, -1.0f, 1.0f) * short.MaxValue);
            rented[offset++] = (byte)(s & 0xff);
            rented[offset++] = (byte)((s >> 8) & 0xff);
        }
        byte[] exact = new byte[offset];
        Buffer.BlockCopy(rented, 0, exact, 0, offset);
        ArrayPool<byte>.Shared.Return(rented);
        return exact;
    }

    private static float[] PresentationToMonoFloat(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        int channels = Math.Max(1, format.Channels);
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            int frames = bytesRecorded / 4 / channels;
            var mono = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                    sum += BitConverter.ToSingle(buffer, (frame * channels + ch) * 4);
                mono[frame] = sum / channels;
            }
            return mono;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            int frames = bytesRecorded / 2 / channels;
            var mono = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                    sum += BitConverter.ToInt16(buffer, (frame * channels + ch) * 2) / 32768f;
                mono[frame] = sum / channels;
            }
            return mono;
        }

        return Array.Empty<float>();
    }

    private static float[] PresentationResampleLinear(float[] input, int sourceRate, int targetRate)
    {
        if (input.Length == 0 || sourceRate == targetRate)
            return input;

        int outputLength = (int)((long)input.Length * targetRate / sourceRate);
        if (outputLength <= 0)
            return Array.Empty<float>();

        var output = new float[outputLength];
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

    private static float ComputePresentationRms(float[] samples)
    {
        if (samples.Length == 0)
            return 0;
        double sum = 0;
        foreach (float sample in samples)
            sum += sample * sample;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    private void StopPresentationQaListening(string status)
    {
        try { _presentationQaCts?.Cancel(); } catch { }
        try { _presentationAudioChannel?.Writer.TryComplete(); } catch { }
        try { _presentationCapture?.StopRecording(); } catch { }
        try { _presentationCapture?.Dispose(); } catch { }
        _presentationCapture = null;

        if (_presentationWs != null)
        {
            try
            {
                if (_presentationWs.State == WebSocketState.Open)
                    _presentationWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None).Wait(600);
            }
            catch { }
            try { _presentationWs.Dispose(); } catch { }
            _presentationWs = null;
        }

        try { _presentationQaCts?.Dispose(); } catch { }
        _presentationQaCts = null;
        _presentationAudioChannel = null;

        if (!string.IsNullOrWhiteSpace(status))
            UpdatePresentationQaStatus(status);
    }

    private void StopPresentationCopilot()
    {
        StopPresentationQaListening("");
        _presentationTimer?.Stop();
    }

    private void UpdatePresentationQaStatus(string message)
    {
        if (_presentationQaStatusLabel == null)
            return;

        if (IsHandleCreated)
            BeginInvoke(() => _presentationQaStatusLabel.Text = message);
        else
            _presentationQaStatusLabel.Text = message;
    }

    private string GetPresentationQuestionText()
    {
        string selected = _presentationQaTranscriptBox?.SelectedText?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(selected))
            return selected;

        string text = _presentationQaTranscriptBox?.Text.Trim() ?? "";
        if (text.StartsWith("Live Audience Question Transcript", StringComparison.OrdinalIgnoreCase))
            return "";
        return text;
    }

    private string BuildLocalPreparationOutput(string task)
    {
        string title = DisplayOrFallback(_presentationTitleBox?.Text ?? "", "Untitled presentation");
        int minutes = Math.Clamp(ParseInt(_presentationMinutesBox?.Text ?? "10", 10), 1, 240);
        var points = ExtractPresentationKeyPoints();
        var questions = BuildLikelyPresentationQuestions(points);
        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine(new string('=', Math.Min(title.Length, 80)));
        builder.AppendLine();
        builder.AppendLine("Executive Summary");
        builder.AppendLine("- Goal: " + DisplayOrFallback(_presentationGoalBox?.Text ?? "", "Make the audience confident in the recommendation."));
        builder.AppendLine("- Audience: " + DisplayOrFallback(_presentationAudienceCombo?.Text ?? "", "General"));
        builder.AppendLine("- Target time: " + minutes + " minutes");
        builder.AppendLine();
        builder.AppendLine("Recommended Structure");
        builder.AppendLine("1. Open with the audience problem and the decision needed.");
        builder.AppendLine("2. State the recommendation in one sentence.");
        builder.AppendLine("3. Prove it with the strongest three points.");
        builder.AppendLine("4. Address risk before the audience has to raise it.");
        builder.AppendLine("5. Close with the exact next step.");
        builder.AppendLine();
        builder.AppendLine("Speaker Script");
        builder.AppendLine("Thank you for the time. The main point I want to make is that this presentation is about moving from information to decision. I will keep the structure tight, show the proof, and close with the action I recommend.");
        builder.AppendLine();
        builder.AppendLine("Slide Talking Points");
        foreach (string point in points.Take(8))
            builder.AppendLine("- " + point);
        builder.AppendLine();
        builder.AppendLine("Likely Audience Questions");
        foreach (string question in questions)
            builder.AppendLine("- " + question);
        builder.AppendLine();
        builder.AppendLine("Suggested Answers");
        builder.AppendLine("- Anchor every answer in the goal, the evidence, and the next step.");
        builder.AppendLine();
        builder.AppendLine("Weak Areas To Prepare");
        builder.AppendLine("- Any claim without a metric, example, customer signal, or implementation detail.");
        builder.AppendLine("- Any transition that does not explain why the next point matters.");
        builder.AppendLine();
        builder.AppendLine("Delivery Checklist");
        builder.AppendLine("- First 30 seconds memorized.");
        builder.AppendLine("- One message per slide or section.");
        builder.AppendLine("- Clear final ask.");
        return builder.ToString();
    }

    private string BuildLocalDebriefOutput(string task)
    {
        string title = DisplayOrFallback(_presentationTitleBox?.Text ?? "", "the presentation");
        if (task == "follow_up_email")
        {
            return "Subject: Follow-up on " + title + "\r\n\r\n" +
                "Hi,\r\n\r\nThank you for the time today. I appreciated the discussion and the questions raised during the presentation. The key takeaway is that the next step should be clear, owned, and time-bound.\r\n\r\n" +
                "Summary:\r\n- Main recommendation: [add recommendation]\r\n- Questions discussed: [add questions]\r\n- Next step: [add next step]\r\n\r\nBest,\r\n";
        }

        return "Presentation Summary\r\n- " + title + "\r\n\r\n" +
            "Questions Asked\r\n" + PresentationClamp(_presentationQaTranscriptBox?.Text ?? "No questions captured.", 1200) + "\r\n\r\n" +
            "Strong Points\r\n- Clear setup, structured points, and practical close.\r\n\r\n" +
            "Weak Points\r\n- Add stronger evidence where the audience may challenge assumptions.\r\n\r\n" +
            "Improvement Notes\r\n- Tighten transitions and rehearse the first and final minute.\r\n\r\n" +
            "Next Steps\r\n- Send follow-up, answer open questions, and confirm ownership.";
    }

    private string BuildLocalQaAnswer(string question, string style)
    {
        string support = GetNextPresentationPoint();
        return "Suggested Answer:\r\n" +
            "The short answer is yes, and the reason is tied to the main point of the presentation: " + support + "\r\n\r\n" +
            "Supporting Point:\r\n" + support + "\r\n\r\n" +
            "Clarifying Question:\r\nWould it help if I answered from the implementation, cost, or risk angle?\r\n\r\n" +
            "Risk/Caution:\r\nAvoid over-committing beyond the evidence in the deck. If the question asks for a number not provided, state the assumption clearly.\r\n\r\n" +
            "Follow-up:\r\nI can send the supporting detail after this session and include it in the follow-up notes.";
    }

    private static List<string> BuildLikelyPresentationQuestions(List<string> points)
    {
        var questions = new List<string>
        {
            "What is the strongest evidence behind this recommendation?",
            "What are the main risks or tradeoffs?",
            "What would change your recommendation?",
            "What is the timeline and owner for the next step?",
            "What do you need from us today?"
        };
        foreach (string point in points.Take(3))
            questions.Add("How does this point hold up under scrutiny: " + point + "?");
        return questions.Take(8).ToList();
    }

    private string BuildPresentationQaEmptyState()
    {
        return "Suggested Answer:\r\nWaiting for an audience question.\r\n\r\n" +
            "Supporting Point From Presentation:\r\nLoad or paste presentation material first.\r\n\r\n" +
            "Clarifying Question:\r\n\r\nRisk/Caution:\r\n\r\nFollow-up Offer:\r\n";
    }

    private string BuildPresentationEmptyState(string mode)
    {
        return mode + "\r\n" + new string('=', mode.Length) + "\r\n\r\n" +
            "Use the controls on the left to generate working presentation intelligence.\r\n\r\n" +
            "Preparation: brief, script, talking points, openings, closings, likely questions, weak points.\r\n" +
            "Live: timer, pace, key points, next-point guidance.\r\n" +
            "Q&A: live transcript and suggested audience answers.\r\n" +
            "Debrief: summary, follow-up email, improvement notes, and saved notes.";
    }

    private void RenderPresentationOutputIfEmpty(string value)
    {
        if (_presentationOutputBox == null)
            return;
        if (string.IsNullOrWhiteSpace(_presentationOutputBox.Text) || _presentationOutputBox.Text.Contains("Use the controls on the left", StringComparison.OrdinalIgnoreCase))
            _presentationOutputBox.Text = value;
    }

    private void SavePresentationNotes()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save presentation notes",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = "RoleAxis-Presentation-Notes.txt"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var builder = new StringBuilder();
        builder.AppendLine(BuildPresentationContext());
        builder.AppendLine();
        builder.AppendLine("OUTPUT INTELLIGENCE");
        builder.AppendLine(_presentationOutputBox?.Text ?? "");
        builder.AppendLine();
        builder.AppendLine("Q&A TRANSCRIPT");
        builder.AppendLine(_presentationQaTranscriptBox?.Text ?? "");
        builder.AppendLine();
        builder.AppendLine("Q&A ANSWER");
        builder.AppendLine(_presentationQaAnswerBox?.Text ?? "");
        File.WriteAllText(dialog.FileName, builder.ToString());
        if (_presentationOutputBox != null)
            _presentationOutputBox.Text = "Presentation notes saved:\r\n" + dialog.FileName;
    }

    private void CopyPresentationOutput()
    {
        string text = (_presentationOutputBox?.Text ?? "") + "\r\n\r\n" + (_presentationQaAnswerBox?.Text ?? "");
        if (!string.IsNullOrWhiteSpace(text))
            Clipboard.SetText(text);
    }

    private static string PresentationClamp(string value, int max)
    {
        value ??= "";
        return value.Length <= max ? value : value[..max] + "\r\n[trimmed]";
    }

    private static string NormalizePresentationText(string value)
    {
        return string.Join(" ", (value ?? "").ToLowerInvariant().Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
