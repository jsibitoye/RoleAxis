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


class Config
{
    public string apiKey { get; set; } = "";
}


internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new AssistantForm());
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

    private readonly TextBox _resumeBox = new();
    private readonly TextBox _jdBox = new();
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

    public AssistantForm()
    {
        Text = "RoleAxis Career Interview Assistant";
        Width = 1280;
        Height = 860;
        MinimumSize = new Size(1100, 740);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;

        _apiKey = LoadApiKey();

        BuildUi();
        LoadAudioDevices();

        if (string.IsNullOrWhiteSpace(_apiKey))
            SetStatus("Missing API key. Add config.json beside the EXE or set OPENAI_API_KEY.");
        else
            SetStatus("Ready. Load resume/JD, select output device, then Start Realtime.");
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
        BackColor = Color.FromArgb(10, 15, 28);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(10, 15, 28)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        // =========================
        // HEADER
        // =========================
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.FromArgb(10, 15, 28),
            Padding = new Padding(0, 0, 0, 8)
        };

        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };

        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 70));

        var title = new Label
        {
            Text = "RoleAxis Interview Assistant",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 19f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.BottomLeft
        };

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.TopLeft;
        _statusLabel.Font = new Font("Segoe UI", 10f);
        _statusLabel.ForeColor = Color.FromArgb(148, 163, 184);

        titleStack.Controls.Add(title, 0, 0);
        titleStack.Controls.Add(_statusLabel, 0, 1);

        var metricsStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };

        metricsStack.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        metricsStack.RowStyles.Add(new RowStyle(SizeType.Percent, 70));

        var liveBadge = new Label
        {
            Text = "LIVE MODE",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomRight,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.FromArgb(34, 197, 94)
        };

        _audioLabel.Dock = DockStyle.Fill;
        _audioLabel.TextAlign = ContentAlignment.TopRight;
        _audioLabel.Font = new Font("Consolas", 9.5f);
        _audioLabel.ForeColor = Color.FromArgb(148, 163, 184);

        metricsStack.Controls.Add(liveBadge, 0, 0);
        metricsStack.Controls.Add(_audioLabel, 0, 1);

        header.Controls.Add(titleStack, 0, 0);
        header.Controls.Add(metricsStack, 1, 0);
        root.Controls.Add(header, 0, 0);

        // =========================
        // CONTEXT CARD
        // =========================
        var contextCard = CreateCard("Context", Color.FromArgb(17, 24, 39));

        var context = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 2,
            Padding = new Padding(16, 20, 16, 16),
            BackColor = Color.FromArgb(17, 24, 39)
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
            BackColor = Color.FromArgb(17, 24, 39)
        };

        resumeHeader.Controls.Add(new Label
        {
            Text = "Resume / Candidate Context",
            AutoSize = true,
            Padding = new Padding(0, 8, 12, 0),
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.FromArgb(226, 232, 240)
        });

        StyleButton(_browseResumeButton, "Browse Resume", ButtonStyle.Secondary, 145);
        _browseResumeButton.Click += (_, _) => PickResumeFile();
        resumeHeader.Controls.Add(_browseResumeButton);

        var jdHeader = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.FromArgb(17, 24, 39)
        };

        jdHeader.Controls.Add(new Label
        {
            Text = "Job Description",
            AutoSize = true,
            Padding = new Padding(0, 8, 12, 0),
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Color.FromArgb(226, 232, 240)
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

        contextCard.Controls.Add(context);
        root.Controls.Add(contextCard, 0, 1);

        // =========================
        // CONTROLS CARD
        // =========================
        var controlsCard = CreateCard("Controls", Color.FromArgb(17, 24, 39));

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(16, 24, 16, 14),
            BackColor = Color.FromArgb(17, 24, 39)
        };

        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        controls.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var row1 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.FromArgb(17, 24, 39)
        };

        row1.Controls.Add(new Label
        {
            Text = "Audio Source",
            AutoSize = true,
            Padding = new Padding(0, 10, 10, 0),
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Color.FromArgb(203, 213, 225)
        });

        _deviceCombo.Width = 500;
        _deviceCombo.Height = 32;
        _deviceCombo.FlatStyle = FlatStyle.Flat;
        row1.Controls.Add(_deviceCombo);

        StyleButton(_refreshDevicesButton, "Refresh", ButtonStyle.Secondary, 95);
        _refreshDevicesButton.Click += (_, _) => LoadAudioDevices();
        row1.Controls.Add(_refreshDevicesButton);

        StyleButton(_startButton, "Start Realtime", ButtonStyle.Primary, 145);
        _startButton.Click += async (_, _) => await StartAsync();
        row1.Controls.Add(_startButton);

        StyleButton(_stopButton, "Stop", ButtonStyle.Danger, 90);
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => StopAll();
        row1.Controls.Add(_stopButton);

        StyleButton(_askButton, "Ask GPT Now", ButtonStyle.Accent, 135);
        _askButton.Click += async (_, _) => await AskGptForTextAsync(GetManualPromptText(), "MANUAL");
        row1.Controls.Add(_askButton);

        var row2 = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.FromArgb(17, 24, 39)
        };

        _autoAskCheck.Text = "Auto-Ask";
        _autoAskCheck.Checked = true;
        _autoAskCheck.Width = 110;
        _autoAskCheck.Height = 32;
        _autoAskCheck.ForeColor = Color.FromArgb(226, 232, 240);
        row2.Controls.Add(_autoAskCheck);

        _topMostCheck.Text = "Always on top";
        _topMostCheck.Checked = true;
        _topMostCheck.Width = 145;
        _topMostCheck.Height = 32;
        _topMostCheck.ForeColor = Color.FromArgb(226, 232, 240);
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
            BackColor = Color.FromArgb(17, 24, 39)
        };

        StyleButton(_clearTranscriptButton, "Clear Transcript", ButtonStyle.Secondary, 150);
        _clearTranscriptButton.Click += (_, _) => ClearTranscriptUi();
        row3.Controls.Add(_clearTranscriptButton);

        StyleButton(_clearAnswersButton, "Clear Answers", ButtonStyle.Secondary, 140);
        _clearAnswersButton.Click += (_, _) => _answerBox.Clear();
        row3.Controls.Add(_clearAnswersButton);

        var hint = new Label
        {
            Text = "Tip: Use Auto-Ask for clear interviewer questions, or Ask GPT Now for manual control.",
            AutoSize = true,
            Padding = new Padding(12, 9, 0, 0),
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(148, 163, 184)
        };
        row3.Controls.Add(hint);

        controls.Controls.Add(row1, 0, 0);
        controls.Controls.Add(row2, 0, 1);
        controls.Controls.Add(row3, 0, 2);

        controlsCard.Controls.Add(controls);
        root.Controls.Add(controlsCard, 0, 2);
        // =========================
        // MAIN SPLIT
        // =========================
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 1,
            ColumnCount = 2,
            BackColor = Color.FromArgb(10, 15, 28),
            Padding = new Padding(0, 8, 0, 8)
        };

        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var answerPanel = CreateCard("GPT Answer", Color.FromArgb(15, 23, 42));
        answerPanel.Padding = new Padding(12, 28, 12, 12);

        _answerBox.Dock = DockStyle.Fill;
        _answerBox.Font = new Font("Segoe UI", 20f, FontStyle.Bold);
        _answerBox.BackColor = Color.FromArgb(15, 23, 42);
        _answerBox.ForeColor = Color.White;
        _answerBox.BorderStyle = BorderStyle.None;
        _answerBox.ReadOnly = true;
        _answerBox.Margin = new Padding(0);

        answerPanel.Controls.Add(_answerBox);
        split.Controls.Add(answerPanel, 0, 0);

        var transcriptPanel = CreateCard("Live Transcript", Color.FromArgb(248, 250, 252));
        transcriptPanel.Padding = new Padding(12, 28, 12, 12);

        _transcriptBox.Dock = DockStyle.Fill;
        _transcriptBox.Font = new Font("Segoe UI", 13f);
        _transcriptBox.BackColor = Color.FromArgb(241, 245, 249);
        _transcriptBox.ForeColor = Color.FromArgb(15, 23, 42);
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
            BackColor = Color.FromArgb(15, 23, 42),
            Padding = new Padding(14, 0, 14, 0)
        };

        _footerStatusLabel.Dock = DockStyle.Fill;
        _footerStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _footerStatusLabel.Font = new Font("Segoe UI Semibold", 10f);
        _footerStatusLabel.ForeColor = Color.FromArgb(125, 211, 252);
        _footerStatusLabel.Text = "System status: waiting.";

        footer.Controls.Add(_footerStatusLabel);
        root.Controls.Add(footer, 0, 4);

        Controls.Add(root);
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
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = background,
            Padding = new Padding(14, 34, 14, 14),
            Margin = new Padding(4)
        };

        var titleLabel = new Label
        {
            Text = title,
            Height = 28,
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10.5f),
            ForeColor = background.GetBrightness() < 0.5
                ? Color.FromArgb(241, 245, 249)
                : Color.FromArgb(15, 23, 42),
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
        box.BackColor = Color.FromArgb(248, 250, 252);
        box.ForeColor = Color.FromArgb(15, 23, 42);
        box.Margin = new Padding(4);
    }

    private Label CreateSmallLabel(string text)
    {
        return new Label
        {
            Text = text + ":",
            AutoSize = true,
            Padding = new Padding(10, 9, 4, 0),
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Color.FromArgb(203, 213, 225)
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
        box.ForeColor = Color.FromArgb(15, 23, 42);
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
                button.BackColor = Color.FromArgb(37, 99, 235);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(37, 99, 235);
                break;

            case ButtonStyle.Accent:
                button.BackColor = Color.FromArgb(16, 185, 129);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(16, 185, 129);
                break;

            case ButtonStyle.Danger:
                button.BackColor = Color.FromArgb(220, 38, 38);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(220, 38, 38);
                break;

            default:
                button.BackColor = Color.FromArgb(30, 41, 59);
                button.ForeColor = Color.FromArgb(226, 232, 240);
                button.FlatAppearance.BorderColor = Color.FromArgb(51, 65, 85);
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

            SetStatus("Realtime transcription running.");
            SetFooterStatus("Listening to system audio. Waiting for speech...");
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
            _transcriptBox.SelectionColor = Color.FromArgb(37, 99, 235);
            _transcriptBox.AppendText("LIVE CAPTION\n");

            _transcriptBox.SelectionFont = new Font("Segoe UI", 13f);
            _transcriptBox.SelectionColor = Color.FromArgb(15, 23, 42);

            string existing = _transcriptHistory.ToString();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _transcriptBox.AppendText(existing.TrimEnd() + Environment.NewLine + Environment.NewLine);
            }

            _transcriptBox.SelectionColor = Color.FromArgb(37, 99, 235);
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

        BeginInvoke(() =>
        {
            _transcriptBox.Clear();

            _transcriptBox.SelectionFont = new Font("Segoe UI Semibold", 11f);
            _transcriptBox.SelectionColor = Color.FromArgb(22, 163, 74);
            _transcriptBox.AppendText("FINAL TRANSCRIPT\n");

            _transcriptBox.SelectionFont = new Font("Segoe UI", 13f);
            _transcriptBox.SelectionColor = Color.FromArgb(15, 23, 42);
            _transcriptBox.AppendText(_transcriptHistory.ToString());

            _transcriptBox.SelectionStart = _transcriptBox.TextLength;
            _transcriptBox.ScrollToCaret();
        });

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

        if (wc < 5) return false;

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

    private async Task AskGptForTextAsync(string text, string source)
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
            string resume = Clamp(_resumeBox.Text.Trim(), 9000);
            string jd = Clamp(_jdBox.Text.Trim(), 7000);

            string systemInstructions =
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
                        content =
                            "Candidate resume/context:\n" + resume +
                            "\n\nJob description:\n" + jd +
                            "\n\nRecent interviewer transcript. It may contain one or more questions:\n" + text +
                            "\n\nIdentify the most important clear question or questions in the transcript and answer them naturally as the candidate. " +
                            "If there are multiple related questions, combine them into one strong spoken answer. " +
                            "Do not repeat the question. Do not answer non-question filler. No bullets unless absolutely necessary."
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

    private void RenderQuestionHeader(string question)
    {
        _answerBox.SelectionFont = new Font("Segoe UI Semibold", 12f);
        _answerBox.SelectionColor = Color.FromArgb(96, 165, 250);
        _answerBox.AppendText("TRANSCRIPT SENT TO GPT\n");

        _answerBox.SelectionFont = new Font("Segoe UI", 13f, FontStyle.Italic);
        _answerBox.SelectionColor = Color.FromArgb(203, 213, 225);
        _answerBox.AppendText(Clamp(question, 500) + Environment.NewLine + Environment.NewLine);

        _answerBox.SelectionFont = new Font("Segoe UI Semibold", 13f);
        _answerBox.SelectionColor = Color.FromArgb(34, 197, 94);
        _answerBox.AppendText("ANSWER\n");
    }

    private void RenderAnswerBody(string answer)
    {
        _answerBox.SelectionFont = new Font("Segoe UI", 20f, FontStyle.Bold);
        _answerBox.SelectionColor = Color.White;
        _answerBox.AppendText(answer);
        _answerBox.SelectionStart = _answerBox.TextLength;
        _answerBox.ScrollToCaret();
    }

    private void AppendAnswerDelta(string delta)
    {
        if (!IsHandleCreated) return;

        BeginInvoke(() =>
        {
            _answerBox.SelectionFont = new Font("Segoe UI", 20f, FontStyle.Bold);
            _answerBox.SelectionColor = Color.White;
            _answerBox.AppendText(delta);
            _answerBox.SelectionStart = _answerBox.TextLength;
            _answerBox.ScrollToCaret();
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
                _statusLabel.Text = "Stopped.";
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
