using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RoleAxis.Career.InterviewAssistant;

public sealed partial class RoleAxisDesktopAppForm : Form
{
    private static readonly Color Midnight = Color.FromArgb(218, 235, 246);
    private static readonly Color Navy = Color.FromArgb(228, 242, 252);
    private static readonly Color Navy2 = Color.FromArgb(205, 226, 241);
    private static readonly Color Surface = Color.FromArgb(238, 248, 255);
    private static readonly Color Surface2 = Color.FromArgb(222, 238, 250);
    private static readonly Color Stroke = Color.FromArgb(200, 219, 233);
    private static readonly Color Ink = Color.FromArgb(14, 20, 34);
    private static readonly Color Gold = Color.FromArgb(12, 159, 194);
    private static readonly Color Cyan = Color.FromArgb(14, 160, 201);
    private static readonly Color Mint = Color.FromArgb(18, 184, 125);
    private static readonly Color Orange = Color.FromArgb(237, 91, 0);
    private static readonly Color SoftText = Color.FromArgb(92, 112, 132);
    private static readonly Color BlackButton = Color.FromArgb(7, 18, 24);

    private readonly HttpClient _http = new();
    private readonly ToolTip _toolTip = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private readonly DesktopSessionState _state = DesktopSessionState.Load();
    private readonly Panel _contentHost = new();
    private readonly Label _pageTitle = new();
    private readonly Label _pageSubtitle = new();
    private readonly Label _licensePill = new();
    private readonly Label _accountLabel = new();
    private readonly Button _manageAccountButton = new();
    private readonly Button _notificationButton = new();
    private readonly Button _accountMenuButton = new();
    private readonly ContextMenuStrip _accountMenu = new();
    private readonly PictureBox _avatarImage = new();

    private AssistantForm? _embeddedAssistant;
    private System.Windows.Forms.Timer? _presentationTimer;
    private System.Windows.Forms.Timer? _desktopHeartbeatTimer;
    private bool _desktopHeartbeatInFlight;

    public RoleAxisDesktopAppForm()
    {
        Text = "RoleAxis Desktop";
        Width = 1360;
        Height = 860;
        MinimumSize = new Size(1180, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Navy;
        DoubleBuffered = true;

        BuildShell();
        if (_state.HasToken)
        {
            ShowLauncherView();
            Shown += async (_, _) => await ValidateSavedSessionAsync();
        }
        else
        {
            ShowLauncherView();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        EndEmbeddedAssistant();
        StopPresentationCopilot();
        CancelEvidenceScanForShutdown();
        StopDesktopHeartbeatLoop();
        _presentationTimer?.Stop();
        _presentationTimer?.Dispose();
        _desktopHeartbeatTimer?.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildShell()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Navy
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildNavigation(), 0, 0);
        root.Controls.Add(BuildMainShell(), 1, 0);
        Controls.Add(root);
    }

    private Control BuildNavigation()
    {
        var nav = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Midnight,
            Padding = new Padding(20, 24, 20, 24),
            Radius = 20
        };

        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Midnight
        };

        var logo = new RaisedLogoPanel
        {
            Width = 94,
            Height = 94,
            Margin = new Padding(18, 0, 0, 28)
        };
        stack.Controls.Add(logo);

        stack.Controls.Add(CreateNavButton("launcher", "\uE80F", "Dashboard", "Dashboard", (_, _) => ShowLauncherView()));
        stack.Controls.Add(CreateNavButton("interview", "\uE720", "Interview", "Interview Assistant", (_, _) => RequireLicenseThen(ShowInterviewAssistant)));
        stack.Controls.Add(CreateNavButton("meeting", "\uE716", "Meeting", "Meeting Assistant", (_, _) => RequireLicenseThen(ShowMeetingAssistant)));
        stack.Controls.Add(CreateNavButton("presentation", "\uE7F4", "Presentation", "Presentation Assistant", (_, _) => RequireLicenseThen(ShowPresentationAssistant)));
        stack.Controls.Add(CreateNavButton("resume", "\uE8D4", "Resume", "Resume Intelligence", (_, _) => RequireLicenseThen(ShowResumeIntelligence)));
        stack.Controls.Add(CreateNavButton("jobs", "\uE821", "Jobs", "Job Intelligence", (_, _) => RequireLicenseThen(ShowJobIntelligence)));
        stack.Controls.Add(CreateNavButton("vault", "\uE8B7", "Vault", "Local Vault Agent", (_, _) => RequireLicenseThen(ShowVaultAgent)));
        stack.Controls.Add(CreateNavButton("evidence", "\uE8A5", "Evidence", "Evidence Scanner", (_, _) => RequireLicenseThen(ShowEvidenceScanner)));
        stack.Controls.Add(CreateNavButton("login", "\uE13D", "My Account", "Account / License", (_, _) => ShowLoginView()));

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 120,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Midnight
        };
        bottom.Controls.Add(new Panel { Width = 120, Height = 1, BackColor = Stroke, Margin = new Padding(0, 0, 0, 18) });
        bottom.Controls.Add(CreateNavButton("settings", "\uE713", "Settings", "Settings", (_, _) => ShowSettingsView()));

        nav.Controls.Add(stack);
        nav.Controls.Add(bottom);
        return nav;
    }

    private Control BuildMainShell()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Navy,
            Padding = new Padding(26, 10, 26, 10)
        };

        _pageTitle.AutoSize = false;
        _pageTitle.Left = 26;
        _pageTitle.Top = 18;
        _pageTitle.Width = 620;
        _pageTitle.Height = 28;
        _pageTitle.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
        _pageTitle.ForeColor = Ink;

        _pageSubtitle.AutoSize = false;
        _pageSubtitle.Left = 27;
        _pageSubtitle.Top = 42;
        _pageSubtitle.Width = 720;
        _pageSubtitle.Height = 22;
        _pageSubtitle.Font = new Font("Segoe UI", 8.8f);
        _pageSubtitle.ForeColor = Color.FromArgb(91, 103, 122);

        _licensePill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _licensePill.Width = 0;
        _licensePill.Height = 0;
        _licensePill.Visible = false;
        _licensePill.TextAlign = ContentAlignment.MiddleCenter;
        _licensePill.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        _licensePill.BackColor = _state.HasToken ? Color.FromArgb(226, 246, 238) : Color.FromArgb(245, 232, 232);
        _licensePill.ForeColor = _state.HasToken ? Color.FromArgb(20, 118, 82) : Color.FromArgb(142, 60, 60);

        _accountLabel.Visible = false;

        _notificationButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _notificationButton.Width = 34;
        _notificationButton.Height = 34;
        _notificationButton.Top = 16;
        _notificationButton.FlatStyle = FlatStyle.Flat;
        _notificationButton.BackColor = Surface;
        _notificationButton.ForeColor = Ink;
        _notificationButton.Text = "\uE7F4";
        _notificationButton.Font = new Font("Segoe MDL2 Assets", 10f);
        _notificationButton.FlatAppearance.BorderSize = 0;
        _notificationButton.Paint += PaintNotificationButton;
        _notificationButton.Click += (_, _) => MessageBox.Show(CurrentLicenseSummary(), "RoleAxis Notifications", MessageBoxButtons.OK, MessageBoxIcon.Information);

        _manageAccountButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _manageAccountButton.Width = 148;
        _manageAccountButton.Height = 34;
        _manageAccountButton.Top = 16;
        _manageAccountButton.FlatStyle = FlatStyle.Flat;
        _manageAccountButton.BackColor = Surface;
        _manageAccountButton.ForeColor = Ink;
        _manageAccountButton.Text = "  Manage account";
        _manageAccountButton.Font = new Font("Segoe UI", 8.6f);
        _manageAccountButton.FlatAppearance.BorderSize = 0;
        _manageAccountButton.Paint += PaintManageButton;
        _manageAccountButton.Click += (_, _) => ShowLoginView();

        _accountMenu.BackColor = Surface;
        _accountMenu.ForeColor = Ink;
        _accountMenu.Font = new Font("Segoe UI", 9f);
        _accountMenu.ShowImageMargin = false;
        _accountMenu.Items.Add("Refresh License", null, async (_, _) => await CheckLicenseAsync(new Label()));
        _accountMenu.Items.Add("Log Out", null, async (_, _) => await LogoutAsync(new Label()));

        _accountMenuButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _accountMenuButton.Width = 34;
        _accountMenuButton.Height = 34;
        _accountMenuButton.Top = 16;
        _accountMenuButton.FlatStyle = FlatStyle.Flat;
        _accountMenuButton.BackColor = Surface;
        _accountMenuButton.ForeColor = Ink;
        _accountMenuButton.Text = "\uE712";
        _accountMenuButton.Font = new Font("Segoe MDL2 Assets", 10f);
        _accountMenuButton.FlatAppearance.BorderSize = 0;
        _accountMenuButton.Paint += PaintMenuButton;
        _accountMenuButton.Click += (_, _) => _accountMenu.Show(_accountMenuButton, new Point(-98, _accountMenuButton.Height + 4));
        _toolTip.SetToolTip(_accountMenuButton, "License actions");

        _avatarImage.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _avatarImage.Width = 36;
        _avatarImage.Height = 36;
        _avatarImage.Top = 15;
        _avatarImage.SizeMode = PictureBoxSizeMode.StretchImage;

        topBar.Resize += (_, _) =>
        {
            _avatarImage.Left = topBar.Width - 64;
            _accountMenuButton.Left = topBar.Width - 108;
            _manageAccountButton.Left = topBar.Width - 268;
            _notificationButton.Left = topBar.Width - 314;
        };

        topBar.Controls.Add(_pageTitle);
        topBar.Controls.Add(_pageSubtitle);
        topBar.Controls.Add(_licensePill);
        topBar.Controls.Add(_accountLabel);
        topBar.Controls.Add(_notificationButton);
        topBar.Controls.Add(_manageAccountButton);
        topBar.Controls.Add(_accountMenuButton);
        topBar.Controls.Add(_avatarImage);

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = Navy;
        _contentHost.Padding = new Padding(28, 18, 28, 24);
        _contentHost.AutoScroll = true;

        shell.Controls.Add(topBar, 0, 0);
        shell.Controls.Add(_contentHost, 0, 1);
        return shell;
    }

    private Button CreateNavButton(string key, string icon, string label, string tip, EventHandler onClick)
    {
        var button = new Button
        {
            Name = key,
            Text = "",
            Width = 128,
            Height = 36,
            Margin = new Padding(0, 0, 0, 10),
            Font = new Font("Segoe UI", 8.6f),
            FlatStyle = FlatStyle.Flat,
            BackColor = Midnight,
            ForeColor = SoftText,
            Cursor = Cursors.Hand,
            Tag = (icon, label)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(211, 233, 247);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(196, 223, 240);
        button.Paint += PaintNavButton;
        button.Click += onClick;
        _toolTip.SetToolTip(button, tip);
        _navButtons[key] = button;
        return button;
    }

    private void SetView(string activeKey, string title, string subtitle)
    {
        _pageTitle.Text = title;
        _pageSubtitle.Text = subtitle;
        _licensePill.Text = _state.HasToken ? $"{_state.PlanName} licensed" : "License required";
        _licensePill.BackColor = _state.HasToken ? Color.FromArgb(226, 246, 238) : Color.FromArgb(245, 232, 232);
        _licensePill.ForeColor = _state.HasToken ? Color.FromArgb(20, 118, 82) : Color.FromArgb(142, 60, 60);
        _accountLabel.Text = string.IsNullOrWhiteSpace(_state.UserDisplay) ? Environment.UserName : _state.UserDisplay;
        _manageAccountButton.Text = "  Manage account";
        _avatarImage.Image?.Dispose();
        _avatarImage.Image = CreateAvatarImage(DisplayOrFallback(_state.UserDisplay, Environment.UserName), _state.UserEmail, 36);
        _notificationButton.Invalidate();
        _manageAccountButton.Invalidate();
        _accountMenuButton.Invalidate();

        foreach (var item in _navButtons)
        {
            bool active = item.Key == activeKey;
            item.Value.BackColor = active ? Cyan : Midnight;
            item.Value.ForeColor = active ? Color.White : SoftText;
            item.Value.Invalidate();
        }
    }

    private void PaintNavButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        bool active = button.ForeColor == Color.White;
        var bounds = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
        using var bg = new SolidBrush(active ? Cyan : button.BackColor);
        using var path = RoundedRect(bounds, 18);
        e.Graphics.FillPath(bg, path);

        var (icon, label) = ((string icon, string label))button.Tag!;
        using var iconFont = new Font("Segoe MDL2 Assets", 10.8f);
        using var textFont = new Font("Segoe UI", 8.5f, active ? FontStyle.Bold : FontStyle.Regular);
        using var brush = new SolidBrush(active ? Color.White : Color.FromArgb(82, 101, 119));
        e.Graphics.DrawString(icon, iconFont, brush, 13, 10);
        e.Graphics.DrawString(label, textFont, brush, 36, 10);
    }

    private void ClearContent(bool stopAssistant = true)
    {
        if (stopAssistant)
            EndEmbeddedAssistant();
        StopPresentationCopilot();
        _presentationTimer?.Stop();
        _contentHost.Controls.Clear();
        _contentHost.AutoScroll = true;
    }

    private void RequireLicenseThen(Action action)
    {
        if (!_state.HasToken)
        {
            ShowLoginView("Sign in to RoleAxis Cloud before launching licensed desktop services.", forceForm: true);
            return;
        }

        if (!_state.HasActiveLicense)
        {
            ShowAccountDashboard("Your subscription is inactive. Protected modules are locked until access is restored.");
            return;
        }

        action();
    }

    private void ShowLauncherView()
    {
        ClearContent();
        SetView("launcher", "Dashboard", "");

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Navy
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 102,
            BackColor = Navy
        };
        var date = CreateLabel(DateTime.Now.ToString("dd MMMM, yyyy"), 8.3f, Cyan, FontStyle.Bold);
        date.Location = new Point(0, 20);
        date.Width = 240;
        date.Height = 20;
        string firstName = DisplayOrFallback(_state.UserDisplay, Environment.UserName).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Environment.UserName;
        var greeting = CreateLabel($"Good Morning, {firstName}!", 19.5f, Ink, FontStyle.Bold);
        greeting.Location = new Point(0, 42);
        greeting.Width = 520;
        greeting.Height = 34;
        var sub = CreateLabel("Explore your local RoleAxis services here.", 9.2f, SoftText);
        sub.Location = new Point(1, 73);
        sub.Width = 420;
        sub.Height = 22;

        var create = CreateBlackPillButton("+  Start New Session", 190);
        create.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        create.Top = 40;
        create.Left = header.Width - 205;
        create.Click += (_, _) => RequireLicenseThen(ShowMeetingAssistant);
        header.Resize += (_, _) => create.Left = header.Width - 205;

        header.Controls.Add(date);
        header.Controls.Add(greeting);
        header.Controls.Add(sub);
        header.Controls.Add(create);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 116,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Navy,
            Margin = new Padding(0, 0, 0, 22)
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        metrics.Controls.Add(CreateDashboardMetricCard("Licensed Modules", "Available on this device", _state.HasActiveLicense ? "07" : "00", Color.FromArgb(3, 150, 190), Color.FromArgb(3, 102, 145), "Open Account"), 0, 0);
        metrics.Controls.Add(CreateDashboardMetricCard("Device Usage", "Registered devices", DeviceUsageText(), Color.FromArgb(21, 163, 151), Color.FromArgb(2, 112, 128), "Manage Device"), 1, 0);
        metrics.Controls.Add(CreateDashboardMetricCard("Active Sessions", "Desktop sessions", SessionUsageText(), Color.FromArgb(36, 102, 142), Color.FromArgb(9, 48, 74), "View License"), 2, 0);

        var section = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Navy
        };
        var sectionTitle = CreateLabel("Your Services", 10.2f, Ink, FontStyle.Bold);
        sectionTitle.Location = new Point(0, 4);
        sectionTitle.Width = 180;
        var viewAll = CreateLabel("View All >", 8.5f, SoftText);
        viewAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        viewAll.Width = 80;
        viewAll.Left = section.Width - 82;
        viewAll.Top = 6;
        section.Resize += (_, _) => viewAll.Left = section.Width - 82;
        section.Controls.Add(sectionTitle);
        section.Controls.Add(viewAll);

        var rows = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Navy
        };
        rows.Controls.Add(CreateServiceRow("READY", "Interview Assistant", "Realtime interview transcript and spoken answer support.", ShowInterviewAssistant));
        rows.Controls.Add(CreateServiceRow("READY", "Meeting Assistant", "Live meeting transcript, suggestions, decisions, and follow-up.", ShowMeetingAssistant));
        rows.Controls.Add(CreateServiceRow("LOCAL", "Presentation Assistant", "Preparation, live pace, Q&A answers, and follow-up notes.", ShowPresentationAssistant));
        rows.Controls.Add(CreateServiceRow("READY", "Resume Intelligence", "ATS scoring, skills gap detection, bullet rewrites, and targeted resumes.", ShowResumeIntelligence));
        rows.Controls.Add(CreateServiceRow("READY", "Job Intelligence", "Save jobs, score fit, tailor applications, and track your pipeline.", ShowJobIntelligence));
        rows.Controls.Add(CreateServiceRow("LOCAL", "Local Vault Agent", "Local metadata index without uploading documents.", ShowVaultAgent));
        rows.Controls.Add(CreateServiceRow("LOCAL", "Evidence Scanner", "Discover, classify, approve, and package local evidence.", ShowEvidenceScanner));

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(metrics, 0, 1);
        layout.Controls.Add(section, 0, 2);
        layout.Controls.Add(rows, 0, 3);
        layout.Controls.Add(BuildDesktopCommandCenter(), 0, 4);
        _contentHost.Controls.Add(layout);
    }

    private Control BuildDesktopCommandCenter()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 150,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Navy,
            Margin = new Padding(0, 18, 0, 0)
        };
        for (int i = 0; i < 4; i++)
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        panel.Controls.Add(CreateCommandCenterTile("Cloud Sync", _state.HasToken ? "Connected" : "Offline", "Last heartbeat: " + DisplayOrFallback(_state.LastHeartbeatAt, "Not sent yet"), "Refresh", async () =>
        {
            await SendHeartbeatAsync();
            ShowLauncherView();
        }), 0, 0);
        panel.Controls.Add(CreateCommandCenterTile("Privacy Mode", "Local-first", "Audio and files stay on this workstation by default.", "Local Data", OpenLocalRoleAxisFolder), 1, 0);
        panel.Controls.Add(CreateCommandCenterTile("Evidence Packages", "Attorney-ready", "Approved evidence exports into local exhibit folders.", "Evidence", ShowEvidenceScanner), 2, 0);
        panel.Controls.Add(CreateCommandCenterTile("Cloud Controls", "Device audit", "Manage licensed devices, active sessions, and installer access.", "Devices", OpenCloudDevicesPage), 3, 0);
        return panel;
    }

    private Control CreateCommandCenterTile(string title, string value, string detail, string actionText, Action action)
    {
        var card = CreateDarkCard();
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(6);
        card.Padding = new Padding(16);

        var eyebrow = CreateLabel(title, 8f, Cyan, FontStyle.Bold);
        eyebrow.Location = new Point(16, 14);
        eyebrow.Width = 220;
        eyebrow.Height = 20;

        var valueLabel = CreateLabel(value, 15f, Ink, FontStyle.Bold);
        valueLabel.Location = new Point(16, 40);
        valueLabel.Width = 230;
        valueLabel.Height = 28;

        var detailLabel = CreateLabel(detail, 8.2f, SoftText);
        detailLabel.Location = new Point(16, 74);
        detailLabel.Width = 250;
        detailLabel.Height = 38;

        var button = CreateBlackPillButton(actionText, 112);
        button.Location = new Point(16, 112);
        button.Height = 28;
        button.Click += (_, _) => action();

        card.Controls.Add(eyebrow);
        card.Controls.Add(valueLabel);
        card.Controls.Add(detailLabel);
        card.Controls.Add(button);
        return card;
    }

    private Control CreateHeroDeviceArt()
    {
        var art = new Panel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Width = 340,
            Height = 150,
            Left = 820,
            Top = 20,
            BackColor = Color.Transparent
        };
        art.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var laptopBrush = new SolidBrush(Color.White);
            using var screenBrush = new SolidBrush(Color.FromArgb(24, 42, 88));
            using var linePen = new Pen(Color.FromArgb(200, 213, 232), 2);
            e.Graphics.FillRectangle(laptopBrush, 92, 18, 162, 92);
            e.Graphics.DrawRectangle(linePen, 92, 18, 162, 92);
            e.Graphics.FillRectangle(screenBrush, 106, 32, 134, 64);
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(223, 238, 255)), 72, 112, 204, 18);
            e.Graphics.FillEllipse(new SolidBrush(Color.FromArgb(49, 196, 141)), 258, 16, 62, 62);
            e.Graphics.DrawString("RA", new Font("Georgia", 18f, FontStyle.Bold), new SolidBrush(Color.FromArgb(245, 239, 225)), 270, 30);
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(33, 57, 114)), 38, 48, 38, 70);
            e.Graphics.DrawRectangle(linePen, 38, 48, 38, 70);
        };
        return art;
    }

    private Control CreateDashboardMetricCard(string title, string subtitle, string value, Color start, Color end, string actionText)
    {
        var card = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 14, 0),
            StartColor = start,
            EndColor = end,
            Padding = new Padding(20)
        };
        var titleLabel = CreateLabel(title, 10f, Color.White, FontStyle.Bold);
        titleLabel.Location = new Point(24, 22);
        titleLabel.Width = 190;
        titleLabel.Height = 22;
        var sub = CreateLabel(subtitle, 8.4f, Color.FromArgb(230, 248, 255));
        sub.Location = new Point(24, 44);
        sub.Width = 190;
        sub.Height = 18;
        var valueLabel = CreateLabel(value, 21f, Color.White, FontStyle.Bold);
        valueLabel.TextAlign = ContentAlignment.TopRight;
        valueLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        valueLabel.Location = new Point(card.Width - 92, 25);
        valueLabel.Width = 70;
        valueLabel.Height = 36;
        card.Resize += (_, _) => valueLabel.Left = card.Width - 92;

        var button = CreateBlackPillButton(actionText, 220);
        button.Location = new Point(24, 72);
        button.Click += (_, _) => ShowLoginView();

        card.Controls.Add(titleLabel);
        card.Controls.Add(sub);
        card.Controls.Add(valueLabel);
        card.Controls.Add(button);
        return card;
    }

    private Control CreateServiceRow(string status, string title, string subtitle, Action action)
    {
        var row = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 78,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Surface,
            Radius = 12,
            Padding = new Padding(18, 12, 18, 12)
        };

        var statusColor = status == "READY" ? Color.FromArgb(31, 185, 131) : Color.FromArgb(242, 165, 62);
        var statusLabel = CreateLabel(status, 7.2f, statusColor, FontStyle.Bold);
        statusLabel.Location = new Point(18, 12);
        statusLabel.Width = 90;
        statusLabel.Height = 16;

        var titleLabel = CreateLabel(title, 9.8f, Ink, FontStyle.Bold);
        titleLabel.Location = new Point(18, 30);
        titleLabel.Width = 360;
        titleLabel.Height = 20;

        var sub = CreateLabel(subtitle, 8.2f, SoftText);
        sub.Location = new Point(18, 50);
        sub.Width = 640;
        sub.Height = 18;

        var button = CreateBlackPillButton("View", 110);
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.Top = 22;
        button.Left = row.Width - 132;
        row.Resize += (_, _) => button.Left = row.Width - 132;
        button.Click += (_, _) => RequireLicenseThen(action);

        row.Controls.Add(statusLabel);
        row.Controls.Add(titleLabel);
        row.Controls.Add(sub);
        row.Controls.Add(button);
        return row;
    }

    private Control CreateModuleCard(string title, string description, string icon, Color accent, Action action)
    {
        var card = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8),
            Padding = new Padding(18),
            StartColor = Color.FromArgb(26, 54, 111),
            EndColor = Color.FromArgb(15, 35, 77),
            Cursor = Cursors.Hand
        };

        var iconLabel = CreateLabel(icon, 28f, accent);
        iconLabel.Font = new Font("Segoe MDL2 Assets", 29f);
        iconLabel.Location = new Point(18, 18);
        iconLabel.Width = 60;
        iconLabel.Height = 50;

        var titleLabel = CreateLabel(title, 13f, Color.White, FontStyle.Bold);
        titleLabel.Location = new Point(18, 84);
        titleLabel.Width = 280;
        titleLabel.Height = 28;

        var descLabel = CreateLabel(description, 9.5f, Color.FromArgb(191, 203, 226));
        descLabel.Location = new Point(18, 116);
        descLabel.Width = 300;
        descLabel.Height = 44;

        card.Controls.Add(iconLabel);
        card.Controls.Add(titleLabel);
        card.Controls.Add(descLabel);
        WireClick(card, (_, _) => action());
        return card;
    }

    private void ShowLoginView(string message = "", bool forceForm = false)
    {
        if (_state.HasToken && !forceForm)
        {
            ShowAccountDashboard(message);
            return;
        }

        ClearContent();
        SetView("login", "My Account", "");

        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 560,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Navy
        };
        wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        wrapper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        var copy = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 18, 0),
            Padding = new Padding(30),
            StartColor = Color.FromArgb(238, 249, 255),
            EndColor = Color.FromArgb(216, 237, 250)
        };
        var eyebrow = CreateLabel("ROLEAXIS DESKTOP", 8.5f, Cyan, FontStyle.Bold);
        eyebrow.Location = new Point(30, 34);
        var title = CreateLabel("One secure license for every local assistant.", 27f, Ink, FontStyle.Bold);
        title.Location = new Point(30, 70);
        title.Width = 430;
        title.Height = 100;
        var body = CreateLabel("Sign in against your local RoleAxis web app. This device receives a protected desktop session and unlocks local assistants when your subscription is active.", 11f, SoftText);
        body.Location = new Point(32, 180);
        body.Width = 430;
        body.Height = 116;
        var device = CreateLabel($"Device fingerprint: {ShortFingerprint(_state.DeviceFingerprint)}", 9.5f, Cyan);
        device.Location = new Point(32, 320);
        device.Width = 430;
        device.Height = 28;
        copy.Controls.Add(eyebrow);
        copy.Controls.Add(title);
        copy.Controls.Add(body);
        copy.Controls.Add(device);

        var form = CreateDarkCard();
        form.Dock = DockStyle.Fill;
        form.Padding = new Padding(26);

        var formLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 11,
            BackColor = Surface
        };
        for (int i = 0; i < 11; i++)
            formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, i is 0 or 10 ? 42 : 46));
        formLayout.RowStyles[10] = new RowStyle(SizeType.Percent, 100);

        var status = CreateLabel(string.IsNullOrWhiteSpace(message) ? CurrentLicenseSummary() : message, 9.5f, string.IsNullOrWhiteSpace(message) ? Mint : Gold);
        status.Dock = DockStyle.Fill;

        var apiBox = CreateInput(_state.ApiBaseUrl);
        var loginBox = CreateInput("");
        var passwordBox = CreateInput("");
        passwordBox.UseSystemPasswordChar = true;
        var deviceNameBox = CreateInput(_state.DeviceName);

        formLayout.Controls.Add(CreateFieldLabel("Desktop API URL"), 0, 0);
        formLayout.Controls.Add(apiBox, 0, 1);
        formLayout.Controls.Add(CreateFieldLabel("Email or username"), 0, 2);
        formLayout.Controls.Add(loginBox, 0, 3);
        formLayout.Controls.Add(CreateFieldLabel("Password"), 0, 4);
        formLayout.Controls.Add(passwordBox, 0, 5);
        formLayout.Controls.Add(CreateFieldLabel("Device name"), 0, 6);
        formLayout.Controls.Add(deviceNameBox, 0, 7);

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        var loginButton = CreateBlackPillButton("Log In", 128);
        var checkButton = CreateBlackPillButton("Check License", 142);
        var logoutButton = CreateBlackPillButton("Log Out", 112);
        actions.Controls.Add(loginButton);
        actions.Controls.Add(checkButton);
        actions.Controls.Add(logoutButton);

        loginButton.Click += async (_, _) =>
        {
            _state.ApiBaseUrl = apiBox.Text.Trim();
            _state.DeviceName = deviceNameBox.Text.Trim();
            _state.Save();
            await LoginAsync(loginBox.Text.Trim(), passwordBox.Text, status);
        };
        checkButton.Click += async (_, _) =>
        {
            _state.ApiBaseUrl = apiBox.Text.Trim();
            _state.Save();
            await CheckLicenseAsync(status);
        };
        logoutButton.Click += async (_, _) => await LogoutAsync(status);

        formLayout.Controls.Add(actions, 0, 8);
        formLayout.Controls.Add(status, 0, 9);
        form.Controls.Add(formLayout);

        wrapper.Controls.Add(copy, 0, 0);
        wrapper.Controls.Add(form, 1, 0);
        _contentHost.Controls.Add(wrapper);
    }

    private async Task LoginAsync(string login, string password, Label status)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            status.Text = "Enter your account and password.";
            status.ForeColor = Gold;
            return;
        }

        status.Text = "Checking license...";
        status.ForeColor = SoftText;

        var payload = new
        {
            login,
            password,
            device_name = _state.DeviceName,
            device_fingerprint = _state.DeviceFingerprint,
            platform = Environment.OSVersion.VersionString,
            app_version = Application.ProductVersion
        };

        var response = await PostJsonAsync("/api/desktop/login", payload);
        if (!response.IsSuccess)
        {
            status.Text = FriendlyApiError(response.Body, "License login failed.");
            status.ForeColor = Color.FromArgb(255, 156, 146);
            return;
        }

        ApplyLicensePayload(response.Body);
        StartDesktopHeartbeatLoop();
        LogActivity("Account", "Login success", "Desktop license session started.");
        ShowAccountDashboard("License login succeeded.");
    }

    private async Task CheckLicenseAsync(Label status)
    {
        if (!_state.HasToken)
        {
            status.Text = "Sign in first. No desktop session token is active.";
            status.ForeColor = Gold;
            return;
        }

        status.Text = "Checking current license...";
        var response = await GetLicenseAsync();
        if (!response.IsSuccess)
        {
            string message = FriendlyApiError(response.Body, "License check failed.");
            _state.ClearLicense();
            _state.Save();
            ShowLoginView(message, forceForm: true);
            return;
        }

        ApplyLicensePayload(response.Body);
        await SendHeartbeatAsync();
        StartDesktopHeartbeatLoop();
        LogActivity("Account", "License refreshed", "Desktop license checked against RoleAxis Cloud.");
        ShowAccountDashboard("License refreshed.");
    }

    private async Task LogoutAsync(Label status)
    {
        if (_state.HasToken)
            await PostJsonAsync("/api/desktop/logout", new { session_token = _state.SessionToken });

        LogActivity("Account", "Logout", "Local desktop session ended.");
        _state.ClearLicense();
        _state.Save();
        StopDesktopHeartbeatLoop();
        ShowLoginView("Logged out locally. Sign in again to launch licensed desktop services.", forceForm: true);
    }

    private async Task ValidateSavedSessionAsync()
    {
        if (!_state.HasToken)
            return;

        var response = await GetLicenseAsync();
        if (!response.IsSuccess)
        {
            string message = FriendlyApiError(response.Body, "Saved desktop session expired or was revoked.");
            _state.ClearLicense();
            _state.Save();
            ShowLoginView(message, forceForm: true);
            return;
        }

        ApplyLicensePayload(response.Body);
        await SendHeartbeatAsync();
        StartDesktopHeartbeatLoop();
        LogActivity("Account", "Login restored", "Saved desktop session restored.");
        ShowLauncherView();
    }

    private async Task SendHeartbeatAsync()
    {
        if (!_state.HasToken)
            return;
        if (_desktopHeartbeatInFlight)
            return;

        _desktopHeartbeatInFlight = true;
        try
        {
            var response = await PostJsonAsync(
                "/api/desktop/heartbeat",
                new
                {
                    session_token = _state.SessionToken,
                    device_fingerprint = _state.DeviceFingerprint,
                    app_version = Application.ProductVersion,
                    capabilities = DesktopCapabilitiesPayload(),
                    local_state = DesktopLocalStatePayload()
                });

            if (response.IsSuccess)
            {
                _state.LastHeartbeatAt = DateTime.Now.ToString("g");
                ApplyLicensePayload(response.Body);
            }
            else if (response.StatusCode is 401 or 403)
            {
                string message = FriendlyApiError(response.Body, "Desktop session was revoked or expired.");
                _state.ClearLicense();
                _state.Save();
                StopDesktopHeartbeatLoop();
                ShowLoginView(message, forceForm: true);
            }
        }
        finally
        {
            _desktopHeartbeatInFlight = false;
        }
    }

    private void StartDesktopHeartbeatLoop()
    {
        if (!_state.HasToken)
            return;
        if (_desktopHeartbeatTimer != null)
        {
            _desktopHeartbeatTimer.Start();
            return;
        }

        _desktopHeartbeatTimer = new System.Windows.Forms.Timer { Interval = 120_000 };
        _desktopHeartbeatTimer.Tick += async (_, _) => await SendHeartbeatAsync();
        _desktopHeartbeatTimer.Start();
    }

    private void StopDesktopHeartbeatLoop()
    {
        _desktopHeartbeatTimer?.Stop();
    }

    private static string[] DesktopCapabilitiesPayload()
    {
        return new[]
        {
            "interview_assistant",
            "meeting_assistant",
            "presentation_assistant",
            "resume_intelligence",
            "job_intelligence",
            "local_vault_agent",
            "evidence_scanner",
            "evidence_package_export",
            "local_index_export",
            "desktop_command_center"
        };
    }

    private object DesktopLocalStatePayload()
    {
        string selectedView = _navButtons.FirstOrDefault(pair => pair.Value.ForeColor == Color.White).Key ?? "";
        string evidenceStatus;
        string evidenceFolder;
        int evidenceCandidates;
        int evidenceApproved;
        lock (_evidenceLock)
        {
            evidenceStatus = _evidenceStatus;
            evidenceFolder = _evidenceSelectedFolder;
            evidenceCandidates = _evidenceCandidates.Count;
            evidenceApproved = _evidenceCandidates.Count(item => item.Status == "Approved");
        }

        string vaultStatus;
        string vaultFolder;
        int vaultAssets;
        lock (_vaultLock)
        {
            vaultStatus = _vaultStatus;
            vaultFolder = _vaultSelectedFolder;
            vaultAssets = _vaultItems.Count;
        }

        return new
        {
            selected_view = selectedView,
            evidence = new
            {
                status = evidenceStatus,
                selected_folder = evidenceFolder,
                candidates = evidenceCandidates,
                approved = evidenceApproved
            },
            vault = new
            {
                status = vaultStatus,
                selected_folder = vaultFolder,
                assets = vaultAssets
            }
        };
    }

    private void ShowAccountDashboard(string message = "")
    {
        ClearContent();
        SetView("login", "My Account", "RoleAxis Desktop Account Command Center");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Navy
        };

        root.Controls.Add(BuildAccountIdentityHero(message), 0, 0);
        root.Controls.Add(BuildAccountOverviewGrid(), 0, 1);
        root.Controls.Add(BuildAccountModuleAccessSection(), 0, 2);
        root.Controls.Add(BuildAccountUsageAndActivitySection(), 0, 3);
        root.Controls.Add(BuildAccountControlsSection(), 0, 4);
        _contentHost.Controls.Add(root);
    }

    private readonly record struct AccountModuleSpec(string Name, string ModuleKey, string Icon, string Description, Action Open);

    private Control BuildAccountIdentityHero(string message)
    {
        var hero = new AccountHeroPanel
        {
            Height = 218,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 16),
            Padding = new Padding(34, 24, 34, 24)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));

        var avatar = new PictureBox
        {
            Width = 92,
            Height = 92,
            Margin = new Padding(8, 46, 16, 0),
            SizeMode = PictureBoxSizeMode.StretchImage,
            Image = CreateAvatarImage(DisplayOrFallback(_state.UserDisplay, Environment.UserName), _state.UserEmail, 92)
        };

        var identity = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 48, 0, 0)
        };
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var name = CreateLabel(DisplayOrFallback(_state.UserFullName, "RoleAxis User"), 20f, Ink, FontStyle.Bold);
        name.Dock = DockStyle.Fill;
        var email = CreateLabel(DisplayOrFallback(_state.UserEmail, "Email not returned by API yet"), 9.4f, SoftText);
        email.Dock = DockStyle.Fill;
        var bannerText = string.IsNullOrWhiteSpace(message)
            ? (_state.HasActiveLicense ? "Licensed workstation connected to RoleAxis Cloud." : "Subscription inactive. Protected modules are locked.")
            : message;
        var banner = CreateLabel(bannerText, 9.2f, _state.HasActiveLicense ? Mint : Orange, FontStyle.Bold);
        banner.Dock = DockStyle.Fill;

        var badges = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0)
        };
        badges.Controls.Add(CreateAccountBadge(DisplayOrFallback(_state.PlanName, "Plan pending"), Color.FromArgb(224, 245, 252), Cyan, 116));
        badges.Controls.Add(CreateAccountBadge(DisplayOrFallback(_state.LicenseStatus, "License pending"), _state.HasActiveLicense ? Color.FromArgb(222, 246, 235) : Color.FromArgb(252, 236, 226), _state.HasActiveLicense ? Mint : Orange, 132));
        badges.Controls.Add(CreateAccountBadge(DisplayOrFallback(_state.DeviceStatus, "Device synced"), Color.FromArgb(236, 244, 250), SoftText, 132));

        identity.Controls.Add(name, 0, 0);
        identity.Controls.Add(email, 0, 1);
        identity.Controls.Add(banner, 0, 2);
        identity.Controls.Add(badges, 0, 3);

        var facts = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 32, 0, 20)
        };
        for (int i = 0; i < 4; i++)
            facts.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        facts.Controls.Add(CreateHeroFact("Device", DisplayOrFallback(_state.DeviceName, Environment.MachineName)), 0, 0);
        facts.Controls.Add(CreateHeroFact("Last check", DisplayOrFallback(_state.LastLicenseCheckAt, "Not checked yet")), 0, 1);
        facts.Controls.Add(CreateHeroFact("Heartbeat", DisplayOrFallback(_state.LastHeartbeatAt, "Not sent yet")), 0, 2);
        facts.Controls.Add(CreateHeroFact("Workspace", ShortPath(DesktopSessionState.LocalAppFolder, 36)), 0, 3);

        layout.Controls.Add(avatar, 0, 0);
        layout.Controls.Add(identity, 1, 0);
        layout.Controls.Add(facts, 2, 0);
        hero.Controls.Add(layout);
        return hero;
    }

    private Control BuildAccountOverviewGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 176,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Navy,
            Margin = new Padding(0, 0, 0, 16)
        };
        for (int i = 0; i < 4; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        grid.Controls.Add(CreateAccountInfoCard("Plan", DisplayOrFallback(_state.PlanName, "Not returned yet")), 0, 0);
        grid.Controls.Add(CreateAccountInfoCard("License", DisplayOrFallback(_state.LicenseStatus, "Not checked yet")), 1, 0);
        grid.Controls.Add(CreateAccountInfoCard("Devices", DeviceUsageText()), 2, 0);
        grid.Controls.Add(CreateAccountInfoCard("Active Sessions", SessionUsageText()), 3, 0);
        grid.Controls.Add(CreateAccountInfoCard("Last License Check", DisplayOrFallback(_state.LastLicenseCheckAt, "Not checked yet")), 0, 1);
        grid.Controls.Add(CreateAccountInfoCard("App Version", DisplayOrFallback(Application.ProductVersion, "Local build")), 1, 1);
        grid.Controls.Add(CreateAccountInfoCard("API Connection", ShortPath(BuildApiUrl("/"), 34)), 2, 1);
        grid.Controls.Add(CreateAccountInfoCard("Local Workspace", ShortPath(DesktopSessionState.LocalAppFolder, 34)), 3, 1);
        return grid;
    }

    private Control BuildAccountModuleAccessSection()
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 346,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy,
            Margin = new Padding(0, 0, 0, 16)
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Navy };
        var title = CreateLabel("Module Access", 11.8f, Ink, FontStyle.Bold);
        title.Location = new Point(0, 4);
        title.Width = 220;
        title.Height = 24;
        var subtitle = CreateLabel("Enabled desktop services for this licensed workstation.", 8.8f, SoftText);
        subtitle.Location = new Point(0, 26);
        subtitle.Width = 520;
        subtitle.Height = 18;
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Navy
        };
        for (int i = 0; i < 4; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        int index = 0;
        foreach (var module in AccountModuleSpecs())
        {
            grid.Controls.Add(CreateModuleAccessCard(module, IsModuleEnabled(module.Name)), index % 4, index / 4);
            index++;
        }

        section.Controls.Add(header, 0, 0);
        section.Controls.Add(grid, 0, 1);
        return section;
    }

    private Control BuildAccountUsageAndActivitySection()
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 282,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Navy,
            Margin = new Padding(0, 0, 0, 16)
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        section.Controls.Add(BuildUsageSnapshotCard(), 0, 0);
        section.Controls.Add(BuildRecentActivityCard(), 1, 0);
        return section;
    }

    private Control BuildAccountControlsSection()
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 228,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Navy
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));

        section.Controls.Add(BuildDeviceSessionCard(), 0, 0);
        section.Controls.Add(BuildSecurityPrivacyCard(), 1, 0);
        section.Controls.Add(BuildPlanPathCard(), 2, 0);
        return section;
    }

    private Control BuildUsageSnapshotCard()
    {
        var card = CreateAccountSectionCard("Usage Snapshot", "Local-first signals from this workstation.", out var body);
        card.Margin = new Padding(0, 0, 10, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 4; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        grid.Controls.Add(CreateUsageMetric("Active sessions", SessionUsageText()), 0, 0);
        grid.Controls.Add(CreateUsageMetric("Device usage", DeviceUsageText()), 1, 0);
        grid.Controls.Add(CreateUsageMetric("Enabled features", EnabledModuleCount().ToString("N0")), 0, 1);
        grid.Controls.Add(CreateUsageMetric("Local scans completed", TrackedCountOrPending(ActivityLogService.Count("Evidence", "Scan completed"))), 1, 1);
        grid.Controls.Add(CreateUsageMetric("Meetings assisted", TrackedCountOrPending(ActivityLogService.Count("Meeting", "Opened"))), 0, 2);
        grid.Controls.Add(CreateUsageMetric("Resumes analyzed", TrackedCountOrPending(ActivityLogService.Count("Resume", "Resume analyzed"))), 1, 2);
        grid.Controls.Add(CreateUsageMetric("Jobs saved", TrackedCountOrPending(LoadSavedJobCount())), 0, 3);
        grid.Controls.Add(CreateUsageMetric("Evidence packages", TrackedCountOrPending(ActivityLogService.Count("Evidence", "Package exported"))), 1, 3);
        body.Controls.Add(grid);
        return card;
    }

    private Control BuildRecentActivityCard()
    {
        var card = CreateAccountSectionCard("Recent Activity", "Private local desktop history.", out var body);
        card.Margin = new Padding(10, 0, 0, 0);

        var feed = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Color.Transparent
        };
        var items = ActivityLogService.LoadRecent(7);
        if (items.Count == 0)
        {
            feed.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var empty = CreateLabel("No local activity yet. Open a module or refresh the license to begin building this feed.", 9f, SoftText);
            empty.Dock = DockStyle.Fill;
            empty.TextAlign = ContentAlignment.MiddleLeft;
            feed.Controls.Add(empty, 0, 0);
        }
        else
        {
            int row = 0;
            foreach (var item in items)
            {
                feed.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                feed.Controls.Add(CreateActivityFeedRow(item), 0, row++);
            }
            feed.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }

        body.Controls.Add(feed);
        return card;
    }

    private Control BuildDeviceSessionCard()
    {
        var card = CreateAccountSectionCard("Device + Session", "Current licensed workstation.", out var body);
        card.Margin = new Padding(0, 0, 10, 0);

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Color.Transparent
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34f));
        AddAccountPair(details, 0, "Fingerprint", ShortFingerprint(_state.DeviceFingerprint));
        AddAccountPair(details, 1, "Session", DisplayOrFallback(_state.ActiveSessionStatus, _state.HasToken ? "Active" : "Not signed in"));
        AddAccountPair(details, 2, "Heartbeat", DisplayOrFallback(_state.LastHeartbeatAt, "Not sent yet"));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 76,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 10, 0, 0)
        };
        var refresh = CreateAccountCommandButton("Refresh License", 138);
        var logout = CreateAccountCommandButton("Logout", 92);
        var switchAccount = CreateAccountCommandButton("Switch Account", 132);
        refresh.Click += async (_, _) => await CheckLicenseAsync(new Label());
        logout.Click += async (_, _) => await LogoutAsync(new Label());
        switchAccount.Click += (_, _) => SwitchAccount();
        actions.Controls.Add(refresh);
        actions.Controls.Add(logout);
        actions.Controls.Add(switchAccount);

        body.Controls.Add(details);
        body.Controls.Add(actions);
        return card;
    }

    private Control BuildSecurityPrivacyCard()
    {
        var card = CreateAccountSectionCard("Security + Privacy", "Cloud controls stay one click away.", out var body);
        card.Margin = new Padding(5, 0, 5, 0);

        var copy = CreateLabel("RoleAxis Desktop keeps local files and transcripts on this device unless a cloud endpoint is explicitly used.", 8.8f, SoftText);
        copy.Location = new Point(0, 0);
        copy.Width = 360;
        copy.Height = 52;
        copy.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 102,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        var web = CreateAccountCommandButton("Open Web Account", 148);
        var devices = CreateAccountCommandButton("Manage Devices", 136);
        var subscription = CreateAccountCommandButton("View Subscription", 148);
        var clear = CreateAccountCommandButton("Clear Local Session", 152);
        web.Click += (_, _) => OpenWebAccountPage();
        devices.Click += (_, _) => OpenCloudDevicesPage();
        subscription.Click += (_, _) => OpenSubscriptionPage();
        clear.Click += (_, _) => SwitchAccount();
        actions.Controls.Add(web);
        actions.Controls.Add(devices);
        actions.Controls.Add(subscription);
        actions.Controls.Add(clear);

        body.Controls.Add(copy);
        body.Controls.Add(actions);
        return card;
    }

    private Control BuildPlanPathCard()
    {
        var card = CreateAccountSectionCard("Plan Path", "Upgrade readiness and installer access.", out var body);
        card.Margin = new Padding(10, 0, 0, 0);

        var plan = CreateLabel(DisplayOrFallback(_state.PlanName, "Plan not returned yet"), 17f, Ink, FontStyle.Bold);
        plan.Location = new Point(0, 0);
        plan.Width = 320;
        plan.Height = 34;
        plan.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var features = CreateLabel(EnabledFeaturesText(), 8.6f, SoftText);
        features.Location = new Point(0, 38);
        features.Width = 360;
        features.Height = 54;
        features.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 70,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        var upgrade = CreateAccountCommandButton("Upgrade Plan", 120);
        var installer = CreateAccountCommandButton("Installer", 98);
        var localData = CreateAccountCommandButton("Local Data", 106);
        upgrade.Click += (_, _) => OpenSubscriptionPage();
        installer.Click += (_, _) => Process.Start(new ProcessStartInfo(BuildApiUrl("/downloads")) { UseShellExecute = true });
        localData.Click += (_, _) => OpenLocalRoleAxisFolder();
        actions.Controls.Add(upgrade);
        actions.Controls.Add(installer);
        actions.Controls.Add(localData);

        body.Controls.Add(plan);
        body.Controls.Add(features);
        body.Controls.Add(actions);
        return card;
    }

    private AccountModuleSpec[] AccountModuleSpecs()
    {
        return new[]
        {
            new AccountModuleSpec("Interview Assistant", "Interview", "\uE720", "Realtime transcript and spoken answer support.", () => RequireLicenseThen(ShowInterviewAssistant)),
            new AccountModuleSpec("Meeting Assistant", "Meeting", "\uE716", "Live transcript, decisions, actions, and follow-up.", () => RequireLicenseThen(ShowMeetingAssistant)),
            new AccountModuleSpec("Presentation Assistant", "Presentation", "\uE7F4", "Preparation, live pace, Q&A, and debrief notes.", () => RequireLicenseThen(ShowPresentationAssistant)),
            new AccountModuleSpec("Resume Intelligence", "Resume", "\uE8D4", "ATS scoring, gaps, rewrites, and targeted resumes.", () => RequireLicenseThen(ShowResumeIntelligence)),
            new AccountModuleSpec("Job Intelligence", "Jobs", "\uE821", "Save jobs, score fit, tailor applications, track pipeline.", () => RequireLicenseThen(ShowJobIntelligence)),
            new AccountModuleSpec("Local Vault", "Vault", "\uE8B7", "Private local metadata index and workspace inventory.", () => RequireLicenseThen(ShowVaultAgent)),
            new AccountModuleSpec("Evidence Scanner", "Evidence", "\uE8A5", "Local evidence discovery, classification, approval, export.", () => RequireLicenseThen(ShowEvidenceScanner))
        };
    }

    private RoundedPanel CreateAccountSectionCard(string title, string subtitle, out Panel body)
    {
        var card = (RoundedPanel)CreateDarkCard();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(18);

        var titleLabel = CreateLabel(title, 10.6f, Ink, FontStyle.Bold);
        titleLabel.Location = new Point(18, 16);
        titleLabel.Width = 320;
        titleLabel.Height = 22;

        var subtitleLabel = CreateLabel(subtitle, 8.4f, SoftText);
        subtitleLabel.Location = new Point(18, 38);
        subtitleLabel.Width = 420;
        subtitleLabel.Height = 18;
        subtitleLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var bodyPanel = new Panel
        {
            Location = new Point(18, 64),
            Width = 380,
            Height = 148,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        card.Resize += (_, _) =>
        {
            bodyPanel.Width = Math.Max(10, card.ClientSize.Width - 36);
            bodyPanel.Height = Math.Max(10, card.ClientSize.Height - 82);
            subtitleLabel.Width = Math.Max(10, card.ClientSize.Width - 36);
        };

        card.Controls.Add(titleLabel);
        card.Controls.Add(subtitleLabel);
        card.Controls.Add(bodyPanel);
        body = bodyPanel;
        return card;
    }

    private Control CreateHeroFact(string label, string value)
    {
        var item = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var labelView = CreateLabel(label, 7.6f, Cyan, FontStyle.Bold);
        labelView.Location = new Point(0, 0);
        labelView.Width = 110;
        labelView.Height = 18;
        var valueView = CreateLabel(value, 8.5f, Ink, FontStyle.Bold);
        valueView.Location = new Point(118, 0);
        valueView.Width = 210;
        valueView.Height = 18;
        valueView.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        item.Controls.Add(labelView);
        item.Controls.Add(valueView);
        return item;
    }

    private Control CreateAccountBadge(string text, Color backColor, Color foreColor, int width)
    {
        var badge = new RoundedPanel
        {
            Width = width,
            Height = 28,
            Radius = 14,
            BackColor = backColor,
            Margin = new Padding(0, 0, 8, 8)
        };
        var label = CreateLabel(text, 8.1f, foreColor, FontStyle.Bold);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleCenter;
        badge.Controls.Add(label);
        return badge;
    }

    private Control CreateUsageMetric(string label, string value)
    {
        var metric = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 8),
            Radius = 10,
            BackColor = Color.FromArgb(246, 252, 255)
        };
        var labelView = CreateLabel(label, 7.7f, SoftText, FontStyle.Bold);
        labelView.Location = new Point(12, 9);
        labelView.Width = 180;
        labelView.Height = 18;
        var valueView = CreateLabel(value, value == "Not tracked yet" ? 8.8f : 13.5f, Ink, FontStyle.Bold);
        valueView.Location = new Point(12, 30);
        valueView.Width = 190;
        valueView.Height = 28;
        valueView.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        metric.Controls.Add(labelView);
        metric.Controls.Add(valueView);
        return metric;
    }

    private Control CreateActivityFeedRow(ActivityLogItem item)
    {
        var row = new Panel { Dock = DockStyle.Fill, Height = 28, BackColor = Color.Transparent };
        var dot = new Panel
        {
            Width = 8,
            Height = 8,
            Left = 2,
            Top = 10,
            BackColor = item.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? Orange : Cyan
        };
        var title = CreateLabel(item.Module + "  " + item.Action, 8.5f, Ink, FontStyle.Bold);
        title.Location = new Point(18, 2);
        title.Width = 340;
        title.Height = 18;
        title.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var time = CreateLabel(item.Timestamp.ToString("g"), 7.6f, SoftText);
        time.TextAlign = ContentAlignment.TopRight;
        time.Location = new Point(row.Width - 128, 4);
        time.Width = 120;
        time.Height = 18;
        time.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        row.Controls.Add(dot);
        row.Controls.Add(title);
        row.Controls.Add(time);
        return row;
    }

    private static void AddAccountPair(TableLayoutPanel table, int row, string label, string value)
    {
        var labelView = CreateLabel(label, 8.2f, SoftText, FontStyle.Bold);
        labelView.Dock = DockStyle.Fill;
        labelView.TextAlign = ContentAlignment.MiddleLeft;
        var valueView = CreateLabel(value, 8.4f, Ink, FontStyle.Bold);
        valueView.Dock = DockStyle.Fill;
        valueView.TextAlign = ContentAlignment.MiddleLeft;
        table.Controls.Add(labelView, 0, row);
        table.Controls.Add(valueView, 1, row);
    }

    private static Button CreateAccountCommandButton(string text, int width)
    {
        var button = CreateBlackPillButton(text, width);
        button.Height = 30;
        button.Margin = new Padding(0, 0, 8, 8);
        return button;
    }

    private bool IsModuleEnabled(string moduleName)
    {
        if (!_state.HasActiveLicense)
            return false;
        if (_state.Features.Count == 0)
            return true;

        string normalized = NormalizeFeature(moduleName);
        return _state.Features.Any(feature =>
        {
            string current = NormalizeFeature(feature);
            return current.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains(current, StringComparison.OrdinalIgnoreCase) ||
                   current.Contains("desktopcommandcenter", StringComparison.OrdinalIgnoreCase);
        });
    }

    private int EnabledModuleCount()
    {
        return AccountModuleSpecs().Count(module => IsModuleEnabled(module.Name));
    }

    private int LoadSavedJobCount()
    {
        try
        {
            return _careerStorageService.LoadJobs().Count;
        }
        catch
        {
            return 0;
        }
    }

    private string EnabledFeaturesText()
    {
        if (_state.Features.Count == 0)
            return "Feature list not returned by API yet. Local modules use active-license fallback until the cloud contract is expanded.";

        var text = string.Join("  |  ", _state.Features.Take(8));
        return _state.Features.Count <= 8 ? text : text + "  |  +" + (_state.Features.Count - 8).ToString("N0") + " more";
    }

    private static string TrackedCountOrPending(int count)
    {
        return count <= 0 ? "Not tracked yet" : count.ToString("N0");
    }

    private static string NormalizeFeature(string value)
    {
        var builder = new StringBuilder();
        foreach (char ch in value ?? "")
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        return builder.ToString();
    }

    private static string ShortPath(string value, int max)
    {
        value = DisplayOrFallback(value, "Not available");
        if (value.Length <= max)
            return value;
        if (max < 12)
            return value[..max];
        return value[..Math.Max(4, max / 2 - 2)] + "..." + value[^Math.Max(4, max / 2)..];
    }

    private void ApplyLicensePayload(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        _state.SessionToken = ReadString(root, "session_token");
        if (string.IsNullOrWhiteSpace(_state.SessionToken))
            _state.SessionToken = _state.SessionTokenBackup;
        _state.SessionTokenBackup = _state.SessionToken;
        string userFullName = ReadString(root, "user", "full_name");
        if (!string.IsNullOrWhiteSpace(userFullName))
            _state.UserFullName = userFullName;
        string userEmail = ReadString(root, "user", "email");
        if (!string.IsNullOrWhiteSpace(userEmail))
            _state.UserEmail = userEmail;
        _state.UserDisplay = _state.UserFullName;
        if (string.IsNullOrWhiteSpace(_state.UserDisplay))
            _state.UserDisplay = _state.UserEmail;
        string planName = ReadString(root, "subscription", "plan", "name");
        if (!string.IsNullOrWhiteSpace(planName))
            _state.PlanName = planName;
        _state.LicenseStatus = ReadBool(root, "subscription", "is_active") ? "Active" : "Inactive";
        string subscriptionStatus = ReadString(root, "subscription", "status");
        if (!string.IsNullOrWhiteSpace(subscriptionStatus))
            _state.SubscriptionStatus = subscriptionStatus;
        _state.ActiveDevices = ReadInt(root, "subscription", "usage", "active_devices");
        _state.MaxDevices = ReadInt(root, "subscription", "plan", "max_devices");
        _state.ActiveSessions = ReadInt(root, "subscription", "usage", "active_sessions");
        _state.MaxSessions = ReadInt(root, "subscription", "plan", "max_active_sessions");
        string activeSessionStatus = ReadString(root, "session", "status");
        if (!string.IsNullOrWhiteSpace(activeSessionStatus))
            _state.ActiveSessionStatus = activeSessionStatus;
        string deviceStatus = ReadString(root, "device", "status");
        if (!string.IsNullOrWhiteSpace(deviceStatus))
            _state.DeviceStatus = deviceStatus;
        string heartbeat = ReadString(root, "session", "last_heartbeat_at");
        if (string.IsNullOrWhiteSpace(heartbeat))
            heartbeat = ReadString(root, "last_heartbeat_at");
        if (!string.IsNullOrWhiteSpace(heartbeat))
            _state.LastHeartbeatAt = heartbeat;
        _state.LastLicenseCheckAt = DateTime.Now.ToString("g");

        var features = ReadStringArray(root, "features");
        if (features.Count == 0)
            features = ReadStringArray(root, "subscription", "plan", "features");
        _state.Features = features;
        _state.Save();
    }

    private async Task<ApiResponse> PostJsonAsync(string path, object payload)
    {
        try
        {
            string url = BuildApiUrl(path);
            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content);
            string body = await response.Content.ReadAsStringAsync();
            return new ApiResponse((int)response.StatusCode, body, response.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            return new ApiResponse(0, ex.Message, false);
        }
    }

    private async Task<ApiResponse> GetLicenseAsync()
    {
        try
        {
            string url = BuildApiUrl("/api/desktop/license?device_fingerprint=" + Uri.EscapeDataString(_state.DeviceFingerprint));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _state.SessionToken);
            using var response = await _http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            return new ApiResponse((int)response.StatusCode, body, response.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            return new ApiResponse(0, ex.Message, false);
        }
    }

    private string BuildApiUrl(string path)
    {
        string root = string.IsNullOrWhiteSpace(_state.ApiBaseUrl) ? "http://127.0.0.1:8000" : _state.ApiBaseUrl.Trim();
        return root.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private void OpenLocalRoleAxisFolder()
    {
        Directory.CreateDirectory(DesktopSessionState.LocalAppFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DesktopSessionState.LocalAppFolder.Replace("\"", "") + "\"") { UseShellExecute = true });
    }

    private void OpenCloudDevicesPage()
    {
        Process.Start(new ProcessStartInfo(BuildApiUrl("/account/devices")) { UseShellExecute = true });
    }

    private void OpenWebAccountPage()
    {
        Process.Start(new ProcessStartInfo(BuildApiUrl("/account")) { UseShellExecute = true });
    }

    private void OpenSubscriptionPage()
    {
        Process.Start(new ProcessStartInfo(BuildApiUrl("/account/subscription")) { UseShellExecute = true });
    }

    private void SwitchAccount()
    {
        LogActivity("Account", "Switch account", "Local session cleared for a new sign-in.");
        _state.ClearLicense();
        _state.Save();
        StopDesktopHeartbeatLoop();
        ShowLoginView("Sign in with another RoleAxis account.", forceForm: true);
    }

    private void LogActivity(string module, string action, string detail = "", string severity = "Info")
    {
        ActivityLogService.Add(module, action, detail, severity);
    }

    private static string FriendlyApiError(string body, string fallback)
    {
        if (string.IsNullOrWhiteSpace(body))
            return fallback;
        try
        {
            using var document = JsonDocument.Parse(body);
            string message = ReadString(document.RootElement, "error", "message");
            return string.IsNullOrWhiteSpace(message) ? fallback : message;
        }
        catch
        {
            return body.Length > 180 ? body[..180] : body;
        }
    }

    private string CurrentLicenseSummary()
    {
        if (!_state.HasToken)
            return "No desktop license session is active.";
        string plan = DisplayOrFallback(_state.PlanName, "Plan not returned by API yet");
        return $"{plan}. Devices {DeviceUsageText()}; sessions {SessionUsageText()}.";
    }

    private void ShowInterviewAssistant()
    {
        ShowInterviewAssistant("", "");
    }

    private void ShowInterviewAssistant(string resumeContext, string jobDescription)
    {
        LogActivity("Interview", "Opened Interview Assistant", "Realtime interview workspace launched.");
        ClearContent();
        SetView("interview", "Interview Assistant", "Realtime local interview copilot embedded inside RoleAxis Desktop.");
        _contentHost.AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sessionBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Navy,
            Padding = new Padding(16, 8, 16, 8)
        };
        var title = CreateLabel("Interview Assistant session", 10.5f, Ink, FontStyle.Bold);
        title.Location = new Point(18, 16);
        title.Width = 420;
        var endButton = CreateBlackPillButton("End and Return", 156);
        endButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        endButton.Left = sessionBar.Width - 178;
        endButton.Top = 10;
        sessionBar.Resize += (_, _) => endButton.Left = sessionBar.Width - 178;
        endButton.Click += (_, _) =>
        {
            EndEmbeddedAssistant();
            ShowLauncherView();
        };
        sessionBar.Controls.Add(title);
        sessionBar.Controls.Add(endButton);

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Navy
        };

        _embeddedAssistant = new AssistantForm(embedded: true)
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            Dock = DockStyle.Fill
        };
        host.Controls.Add(_embeddedAssistant);
        _embeddedAssistant.Show();
        if (!string.IsNullOrWhiteSpace(resumeContext) || !string.IsNullOrWhiteSpace(jobDescription))
            _embeddedAssistant.LoadInterviewContext(resumeContext, jobDescription);

        root.Controls.Add(sessionBar, 0, 0);
        root.Controls.Add(host, 0, 1);
        _contentHost.Controls.Add(root);
    }

    private void EndEmbeddedAssistant()
    {
        if (_embeddedAssistant == null)
            return;

        try { _embeddedAssistant.EndSession(); } catch { }
        try { _embeddedAssistant.Close(); } catch { }
        try { _embeddedAssistant.Dispose(); } catch { }
        _embeddedAssistant = null;
    }

    private void ShowPresentationAssistant()
    {
        LogActivity("Presentation", "Opened Presentation Assistant", "Presentation preparation workspace launched.");
        ShowPresentationCopilot();
    }

    private static string BuildPresentationBrief(string topic, string notes, string durationRaw)
    {
        int minutes = Math.Clamp(ParseInt(durationRaw, 10), 1, 180);
        var words = SplitWords(notes);
        int estimatedMinutes = Math.Max(1, (int)Math.Ceiling(words.Count / 135.0));
        string title = string.IsNullOrWhiteSpace(topic) ? "Untitled presentation" : topic.Trim();
        var keyLines = notes.SplitLines().Where(line => line.Trim().Length > 18).Take(6).ToList();

        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine(new string('=', Math.Min(title.Length, 80)));
        builder.AppendLine();
        builder.AppendLine($"Target time: {minutes} minutes");
        builder.AppendLine($"Estimated current speaking time: {estimatedMinutes} minutes at 135 wpm");
        builder.AppendLine($"Word count: {words.Count}");
        builder.AppendLine();
        builder.AppendLine("Recommended structure:");
        builder.AppendLine("1. Open with the decision, outcome, or audience problem.");
        builder.AppendLine("2. Prove the point with 3 crisp sections.");
        builder.AppendLine("3. Close with the ask, next step, or memorable takeaway.");
        builder.AppendLine();
        builder.AppendLine("Strong lines to rehearse:");
        if (keyLines.Count == 0)
            builder.AppendLine("- Add speaker notes to generate rehearsal anchors.");
        foreach (string line in keyLines)
            builder.AppendLine("- " + line.Trim());
        builder.AppendLine();
        builder.AppendLine("Delivery checks:");
        builder.AppendLine("- First 30 seconds are memorized.");
        builder.AppendLine("- Every slide has one main sentence.");
        builder.AppendLine("- Final minute contains the ask and why now.");
        return builder.ToString();
    }

    private void ShowMeetingAssistant()
    {
        LogActivity("Meeting", "Opened Meeting Assistant", "Live meeting workspace launched.");
        ClearContent();
        SetView("meeting", "Meeting Assistant", "Live meeting strategist with transcript, suggestions, decisions, actions, and follow-up email.");
        _contentHost.AutoScroll = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Navy
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sessionBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Navy,
            Padding = new Padding(16, 8, 16, 8)
        };
        var title = CreateLabel("Meeting Assistant live session", 10.5f, Ink, FontStyle.Bold);
        title.Location = new Point(18, 16);
        title.Width = 420;
        var endButton = CreateBlackPillButton("End and Return", 156);
        endButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        endButton.Left = sessionBar.Width - 178;
        endButton.Top = 10;
        sessionBar.Resize += (_, _) => endButton.Left = sessionBar.Width - 178;
        endButton.Click += (_, _) =>
        {
            EndEmbeddedAssistant();
            ShowLauncherView();
        };
        sessionBar.Controls.Add(title);
        sessionBar.Controls.Add(endButton);

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Navy
        };

        _embeddedAssistant = new AssistantForm(embedded: true, mode: LiveAssistMode.Meeting)
        {
            TopLevel = false,
            FormBorderStyle = FormBorderStyle.None,
            Dock = DockStyle.Fill
        };
        host.Controls.Add(_embeddedAssistant);
        _embeddedAssistant.Show();

        root.Controls.Add(sessionBar, 0, 0);
        root.Controls.Add(host, 0, 1);
        _contentHost.Controls.Add(root);
    }

    private static string BuildMeetingSummary(string agenda, string notes)
    {
        var lines = notes.SplitLines().Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        var decisions = lines.Where(line => ContainsAny(line, "decided", "decision", "approved", "agreed")).Take(10).ToList();
        var actions = lines.Where(line => ContainsAny(line, "todo", "action", "follow up", "owner", "due", "next step")).Take(10).ToList();

        var builder = new StringBuilder();
        builder.AppendLine("Meeting Summary");
        builder.AppendLine("================");
        builder.AppendLine();
        builder.AppendLine("Agenda:");
        foreach (string line in agenda.SplitLines().Where(line => line.Trim().Length > 0).Take(12))
            builder.AppendLine("- " + line.Trim());
        if (string.IsNullOrWhiteSpace(agenda))
            builder.AppendLine("- No agenda provided.");
        builder.AppendLine();
        builder.AppendLine("Decisions:");
        if (decisions.Count == 0)
            builder.AppendLine("- No explicit decisions detected. Add decision wording to the notes if needed.");
        foreach (string line in decisions)
            builder.AppendLine("- " + line);
        builder.AppendLine();
        builder.AppendLine("Action Items:");
        if (actions.Count == 0)
            builder.AppendLine("- No explicit action items detected.");
        foreach (string line in actions)
            builder.AppendLine("- " + line);
        builder.AppendLine();
        builder.AppendLine("Raw note count: " + lines.Count + " lines");
        return builder.ToString();
    }

    private void ShowVaultAgent()
    {
        LogActivity("Vault", "Opened Local Vault", "Local vault dashboard launched.");
        ShowVaultDashboard();
    }

    private static string BuildVaultIndex(string folder)
    {
        var files = EnumerateFilesSafe(folder)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(5000)
            .ToList();

        var grouped = files
            .GroupBy(file => file.Extension.ToLowerInvariant())
            .OrderByDescending(group => group.Count())
            .Take(12)
            .ToList();

        string indexPath = Path.Combine(DesktopSessionState.LocalAppFolder, "vault-index.json");
        Directory.CreateDirectory(DesktopSessionState.LocalAppFolder);
        File.WriteAllText(indexPath, JsonSerializer.Serialize(files.Take(500).Select(file => new
        {
            file.Name,
            file.Extension,
            file.Length,
            LastModifiedUtc = file.LastWriteTimeUtc,
            Path = file.FullName
        }), new JsonSerializerOptions { WriteIndented = true }));

        var builder = new StringBuilder();
        builder.AppendLine("Local Vault Metadata Index");
        builder.AppendLine("==========================");
        builder.AppendLine($"Folder: {folder}");
        builder.AppendLine($"Files indexed: {files.Count}");
        builder.AppendLine($"Index saved: {indexPath}");
        builder.AppendLine();
        builder.AppendLine("File types:");
        foreach (var group in grouped)
            builder.AppendLine($"- {(string.IsNullOrWhiteSpace(group.Key) ? "[no extension]" : group.Key)}: {group.Count()}");
        builder.AppendLine();
        builder.AppendLine("Recently modified:");
        foreach (var file in files.Take(12))
            builder.AppendLine($"- {file.LastWriteTime:yyyy-MM-dd}  {file.Name}");
        return builder.ToString();
    }

    private void ShowEvidenceScanner()
    {
        LogActivity("Evidence", "Opened Evidence Scanner", "Evidence scanner dashboard launched.");
        ShowEvidenceDashboard();
    }

    private void ShowSettingsView()
    {
        ClearContent();
        SetView("settings", "Settings", "Desktop API, local identity, and license controls.");

        var view = CreateToolLayout("Desktop settings");
        var inputs = FindToolControl<TableLayoutPanel>(view, "inputs");
        var output = FindToolControl<RichTextBox>(view, "output");

        var apiBox = AddLabeledInput(inputs, "Desktop API URL", _state.ApiBaseUrl);
        var deviceBox = AddLabeledInput(inputs, "Device name", _state.DeviceName);
        var fingerprintBox = AddLabeledInput(inputs, "Device fingerprint", _state.DeviceFingerprint);
        fingerprintBox.ReadOnly = true;
        var actions = CreateActionRow();
        var saveButton = CreateActionButton("Save Settings", Gold, Ink);
        var checkButton = CreateActionButton("Check License", Mint, Ink);
        var clearButton = CreateActionButton("Clear License", Color.FromArgb(255, 156, 146), Ink);
        actions.Controls.Add(saveButton);
        actions.Controls.Add(checkButton);
        actions.Controls.Add(clearButton);
        inputs.Controls.Add(actions);

        output.Text = CurrentLicenseSummary();

        saveButton.Click += (_, _) =>
        {
            _state.ApiBaseUrl = apiBox.Text.Trim();
            _state.DeviceName = deviceBox.Text.Trim();
            _state.Save();
            output.Text = "Settings saved.\r\n\r\n" + CurrentLicenseSummary();
            SetView("settings", "Settings", "Desktop API, local identity, and license controls.");
        };
        checkButton.Click += async (_, _) =>
        {
            _state.ApiBaseUrl = apiBox.Text.Trim();
            _state.Save();
            var status = CreateLabel("", 9.5f, SoftText);
            await CheckLicenseAsync(status);
            output.Text = status.Text;
        };
        clearButton.Click += (_, _) =>
        {
            _state.ClearLicense();
            _state.Save();
            output.Text = "Local desktop license session cleared.";
            SetView("settings", "Settings", "Desktop API, local identity, and license controls.");
        };

        _contentHost.Controls.Add(view);
    }

    private Control CreateToolLayout(string title)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 620,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Navy
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));

        var inputsCard = CreateDarkCard();
        inputsCard.Dock = DockStyle.Fill;
        inputsCard.Margin = new Padding(0, 0, 18, 0);
        inputsCard.Padding = new Padding(22);

        var inputs = new TableLayoutPanel
        {
            Name = "inputs",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            BackColor = Surface
        };
        inputsCard.Controls.Add(inputs);

        var outputCard = CreateDarkCard();
        outputCard.Dock = DockStyle.Fill;
        outputCard.Padding = new Padding(18);

        var output = new RichTextBox
        {
            Name = "output",
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(246, 248, 252),
            ForeColor = Ink,
            Font = new Font("Consolas", 10.5f),
            ReadOnly = true,
            Text = title + "\r\n" + new string('=', title.Length) + "\r\n"
        };
        outputCard.Controls.Add(output);

        layout.Controls.Add(inputsCard, 0, 0);
        layout.Controls.Add(outputCard, 1, 0);
        return layout;
    }

    private static T FindToolControl<T>(Control root, string name) where T : Control
    {
        return root.Controls.Find(name, true).OfType<T>().First();
    }

    private TextBox AddLabeledInput(TableLayoutPanel parent, string label, string value)
    {
        parent.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        parent.Controls.Add(CreateFieldLabel(label));
        var input = CreateInput(value);
        input.Margin = new Padding(0, 0, 0, 14);
        parent.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        parent.Controls.Add(input);
        return input;
    }

    private TextBox AddLabeledTextArea(TableLayoutPanel parent, string label, string value)
    {
        parent.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        parent.Controls.Add(CreateFieldLabel(label));
        var input = CreateInput(value);
        input.Multiline = true;
        input.Height = 128;
        input.ScrollBars = ScrollBars.Vertical;
        input.Margin = new Padding(0, 0, 0, 14);
        parent.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        parent.Controls.Add(input);
        return input;
    }

    private static Label CreateFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(70, 91, 109),
            Font = new Font("Segoe UI Semibold", 9.4f, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        };
    }

    private static TextBox CreateInput(string value)
    {
        return new TextBox
        {
            Text = value,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(247, 252, 255),
            ForeColor = Ink,
            Font = new Font("Segoe UI", 10f),
            Height = 34
        };
    }

    private static FlowLayoutPanel CreateActionRow()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 8)
        };
    }

    private static Button CreateActionButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Width = 156,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Segoe UI Semibold", 9.2f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 10, 0)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.08f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.08f);
        return button;
    }

    private static Button CreateBlackPillButton(string text, int width)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = BlackButton,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 8.4f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0)
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 36, 45);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(4, 12, 18);
        return button;
    }

    private static Panel CreateDarkCard()
    {
        return new RoundedPanel
        {
            BackColor = Surface,
            Radius = 12
        };
    }

    private static Label CreateLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular)
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

    private static void WireClick(Control control, EventHandler handler)
    {
        control.Click += handler;
        foreach (Control child in control.Controls)
            WireClick(child, handler);
    }

    private void PaintNotificationButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(1, 1, button.Width - 3, button.Height - 3), 17);
        using var bg = new SolidBrush(Surface);
        using var pen = new Pen(Stroke);
        e.Graphics.FillPath(bg, path);
        e.Graphics.DrawPath(pen, path);
        using var iconFont = new Font("Segoe MDL2 Assets", 10f);
        using var iconBrush = new SolidBrush(Color.FromArgb(79, 96, 112));
        e.Graphics.DrawString("\uE7F4", iconFont, iconBrush, 9, 9);
        using var dotBrush = new SolidBrush(Color.Red);
        e.Graphics.FillEllipse(dotBrush, button.Width - 12, 6, 7, 7);
    }

    private void PaintMenuButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(1, 1, button.Width - 3, button.Height - 3), 17);
        using var bg = new SolidBrush(Surface);
        using var pen = new Pen(Stroke);
        e.Graphics.FillPath(bg, path);
        e.Graphics.DrawPath(pen, path);
        using var iconFont = new Font("Segoe MDL2 Assets", 10f);
        using var iconBrush = new SolidBrush(Color.FromArgb(79, 96, 112));
        e.Graphics.DrawString("\uE712", iconFont, iconBrush, 9, 9);
    }

    private void PaintManageButton(object? sender, PaintEventArgs e)
    {
        if (sender is not Button button)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(new Rectangle(1, 1, button.Width - 3, button.Height - 3), 17);
        using var bg = new SolidBrush(Surface);
        using var pen = new Pen(Stroke);
        e.Graphics.FillPath(bg, path);
        e.Graphics.DrawPath(pen, path);
        using var iconFont = new Font("Segoe MDL2 Assets", 9f);
        using var textFont = new Font("Segoe UI", 8.2f);
        using var brush = new SolidBrush(Ink);
        e.Graphics.DrawString("\uE13D", iconFont, brush, 12, 10);
        e.Graphics.DrawString("Manage account", textFont, brush, 34, 9);
    }

    private static Bitmap CreateAvatarImage(string displayName, string email, int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        string seed = string.IsNullOrWhiteSpace(email) ? displayName : email;
        int hash = seed.Aggregate(17, (current, ch) => current * 31 + ch);
        var colorA = Color.FromArgb(40 + Math.Abs(hash % 70), 135 + Math.Abs(hash % 70), 170 + Math.Abs(hash % 50));
        var colorB = Color.FromArgb(18, 89, 121);
        using var brush = new LinearGradientBrush(new Rectangle(0, 0, size, size), colorA, colorB, LinearGradientMode.ForwardDiagonal);
        graphics.FillEllipse(brush, 0, 0, size - 1, size - 1);

        string initials = string.Join("", displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[0])).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(initials))
            initials = "RA";
        using var font = new Font("Segoe UI Semibold", Math.Max(9, size / 3.2f), FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        var measured = graphics.MeasureString(initials, font);
        graphics.DrawString(initials, font, textBrush, (size - measured.Width) / 2f, (size - measured.Height) / 2f);
        using var pen = new Pen(Color.White, Math.Max(1, size / 18f));
        graphics.DrawEllipse(pen, 1, 1, size - 3, size - 3);
        return bitmap;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private Control CreateAccountInfoCard(string label, string value)
    {
        var card = new GradientPanel
        {
            StartColor = Color.FromArgb(247, 253, 255),
            EndColor = Color.FromArgb(228, 243, 252),
            Radius = 14
        };
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(6);
        card.Padding = new Padding(16);

        var labelView = CreateLabel(label, 8.2f, Cyan, FontStyle.Bold);
        labelView.Location = new Point(16, 14);
        labelView.Width = 220;
        labelView.Height = 20;

        var valueView = CreateLabel(DisplayOrFallback(value, "Not returned by API yet"), 9.8f, Ink, FontStyle.Bold);
        valueView.Location = new Point(16, 40);
        valueView.Width = 250;
        valueView.Height = 28;
        valueView.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        card.Resize += (_, _) => valueView.Width = Math.Max(80, card.Width - 32);

        card.Controls.Add(labelView);
        card.Controls.Add(valueView);
        return card;
    }

    private Control CreateModuleAccessCard(AccountModuleSpec module, bool enabled)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(6),
            Padding = new Padding(14),
            Radius = 14,
            BackColor = enabled ? Color.FromArgb(242, 251, 255) : Color.FromArgb(232, 239, 245)
        };

        var icon = CreateLabel(module.Icon, 18f, enabled ? Cyan : SoftText, FontStyle.Regular);
        icon.Font = new Font("Segoe MDL2 Assets", 18f);
        icon.Location = new Point(16, 16);
        icon.Width = 36;
        icon.Height = 34;

        var status = CreateLabel(enabled ? "ENABLED" : "LOCKED", 7.2f, enabled ? Mint : Orange, FontStyle.Bold);
        status.TextAlign = ContentAlignment.MiddleRight;
        status.Location = new Point(card.Width - 88, 15);
        status.Width = 70;
        status.Height = 20;
        status.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        var name = CreateLabel(module.Name, 9.7f, Ink, FontStyle.Bold);
        name.Location = new Point(16, 52);
        name.Width = 228;
        name.Height = 22;
        name.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var description = CreateLabel(module.Description, 8.2f, SoftText);
        description.Location = new Point(16, 76);
        description.Width = 242;
        description.Height = 36;
        description.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        string lastUsed = ActivityLogService.LastForModule(module.ModuleKey)?.Timestamp.ToString("g") ?? "Not tracked yet";
        var last = CreateLabel("Last used: " + lastUsed, 7.6f, SoftText);
        last.Location = new Point(16, 116);
        last.Width = 190;
        last.Height = 18;
        last.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        var open = CreateAccountCommandButton(enabled ? "Open" : "Locked", 78);
        open.Enabled = enabled;
        open.Location = new Point(card.Width - 98, 108);
        open.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        open.Click += (_, _) => module.Open();

        card.Controls.Add(icon);
        card.Controls.Add(status);
        card.Controls.Add(name);
        card.Controls.Add(description);
        card.Controls.Add(last);
        card.Controls.Add(open);
        return card;
    }

    private static string DisplayOrFallback(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string ShortFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return "Not returned by API yet";
        string clean = fingerprint.Trim();
        return clean.Length <= 12 ? clean : clean[..8] + "..." + clean[^4..];
    }

    private string DeviceUsageText()
    {
        if (_state.MaxDevices <= 0)
            return "Not returned by API yet";
        return $"{_state.ActiveDevices}/{_state.MaxDevices}";
    }

    private string SessionUsageText()
    {
        if (_state.MaxSessions <= 0)
            return "Not returned by API yet";
        return $"{_state.ActiveSessions}/{_state.MaxSessions}";
    }

    private static string ReadString(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return "";
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : current.ToString();
    }

    private static int ReadInt(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return 0;
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out int value))
            return value;
        return int.TryParse(current.ToString(), out value) ? value : 0;
    }

    private static bool ReadBool(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return false;
        }

        if (current.ValueKind == JsonValueKind.True)
            return true;
        if (current.ValueKind == JsonValueKind.False)
            return false;
        return bool.TryParse(current.ToString(), out bool value) && value;
    }

    private static List<string> ReadStringArray(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return new List<string>();
        }

        if (current.ValueKind != JsonValueKind.Array)
            return new List<string>();

        return current
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }

    private static List<string> SplitWords(string value)
    {
        return value.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder); }
            catch { files = Array.Empty<string>(); }
            foreach (string file in files)
                yield return file;

            IEnumerable<string> folders;
            try { folders = Directory.EnumerateDirectories(folder); }
            catch { folders = Array.Empty<string>(); }
            foreach (string child in folders)
                pending.Push(child);
        }
    }

    private static string Csv(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

internal sealed class DesktopSessionState
{
    public static string LocalAppFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoleAxis");
    private static string SettingsPath => Path.Combine(LocalAppFolder, "desktop-settings.json");

    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:8000";
    public string DeviceName { get; set; } = Environment.MachineName;
    public string DeviceFingerprint { get; set; } = ComputeFingerprint();
    public string SessionToken { get; set; } = "";
    public string SessionTokenBackup { get; set; } = "";
    public string ProtectedSessionToken { get; set; } = "";
    public string UserDisplay { get; set; } = "";
    public string UserFullName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string PlanName { get; set; } = "";
    public string LicenseStatus { get; set; } = "";
    public string SubscriptionStatus { get; set; } = "";
    public string ActiveSessionStatus { get; set; } = "";
    public string DeviceStatus { get; set; } = "";
    public string LastLicenseCheckAt { get; set; } = "";
    public string LastHeartbeatAt { get; set; } = "";
    public List<string> Features { get; set; } = new();
    public int ActiveDevices { get; set; }
    public int MaxDevices { get; set; }
    public int ActiveSessions { get; set; }
    public int MaxSessions { get; set; }

    public bool HasToken => !string.IsNullOrWhiteSpace(SessionToken);
    public bool HasActiveLicense =>
        HasToken &&
        !string.Equals(LicenseStatus, "Inactive", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(SubscriptionStatus, "Inactive", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(SubscriptionStatus, "Canceled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(SubscriptionStatus, "PastDue", StringComparison.OrdinalIgnoreCase);

    public static DesktopSessionState Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var state = JsonSerializer.Deserialize<DesktopSessionState>(json);
                if (state != null)
                {
                    state.DeviceFingerprint = ComputeFingerprint();
                    state.SessionToken = UnprotectToken(state.ProtectedSessionToken);
                    state.SessionTokenBackup = state.SessionToken;
                    return state;
                }
            }
        }
        catch
        {
            // Defaults are safer than failing startup.
        }

        return new DesktopSessionState();
    }

    public void Save()
    {
        Directory.CreateDirectory(LocalAppFolder);
        var persisted = new DesktopSessionState
        {
            ApiBaseUrl = ApiBaseUrl,
            DeviceName = DeviceName,
            DeviceFingerprint = DeviceFingerprint,
            ProtectedSessionToken = ProtectToken(SessionToken),
            UserDisplay = UserDisplay,
            UserFullName = UserFullName,
            UserEmail = UserEmail,
            PlanName = PlanName,
            LicenseStatus = LicenseStatus,
            SubscriptionStatus = SubscriptionStatus,
            ActiveSessionStatus = ActiveSessionStatus,
            DeviceStatus = DeviceStatus,
            LastLicenseCheckAt = LastLicenseCheckAt,
            LastHeartbeatAt = LastHeartbeatAt,
            Features = Features,
            ActiveDevices = ActiveDevices,
            MaxDevices = MaxDevices,
            ActiveSessions = ActiveSessions,
            MaxSessions = MaxSessions
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void ClearLicense()
    {
        SessionToken = "";
        SessionTokenBackup = "";
        ProtectedSessionToken = "";
        UserDisplay = "";
        UserFullName = "";
        UserEmail = "";
        PlanName = "";
        LicenseStatus = "";
        SubscriptionStatus = "";
        ActiveSessionStatus = "";
        DeviceStatus = "";
        LastLicenseCheckAt = "";
        LastHeartbeatAt = "";
        Features = new List<string>();
        ActiveDevices = 0;
        MaxDevices = 0;
        ActiveSessions = 0;
        MaxSessions = 0;
    }

    private static string ComputeFingerprint()
    {
        string raw = $"{Environment.MachineName}|{Environment.UserName}|{Environment.OSVersion.VersionString}|{Environment.ProcessorCount}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }

    private static string ProtectToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return "";

        try
        {
            byte[] protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(token),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        catch
        {
            return "";
        }
    }

    private static string UnprotectToken(string protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken))
            return "";

        try
        {
            byte[] bytes = Convert.FromBase64String(protectedToken);
            byte[] unprotected = ProtectedData.Unprotect(
                bytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(unprotected);
        }
        catch
        {
            return "";
        }
    }
}

internal readonly record struct ApiResponse(int StatusCode, string Body, bool IsSuccess);

internal static class TextExtensions
{
    public static IEnumerable<string> SplitLines(this string value)
    {
        return (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }
}

internal class RoundedPanel : Panel
{
    public int Radius { get; set; } = 14;

    public RoundedPanel()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = CreatePath(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(Color.FromArgb(199, 218, 232));
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreatePath(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RaisedLogoPanel : Panel
{
    public RaisedLogoPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shadow = new SolidBrush(Color.FromArgb(38, 94, 120, 144));
        using var outer = new SolidBrush(Color.FromArgb(231, 244, 252));
        using var inner = new SolidBrush(Color.FromArgb(238, 248, 255));
        using var pen = new Pen(Color.FromArgb(188, 208, 224));
        e.Graphics.FillRectangle(shadow, 17, 18, 58, 58);
        e.Graphics.FillRectangle(outer, 10, 8, 60, 60);
        e.Graphics.DrawRectangle(pen, 10, 8, 60, 60);
        e.Graphics.FillRectangle(inner, 22, 20, 36, 36);
        e.Graphics.DrawRectangle(pen, 22, 20, 36, 36);
        using var font = new Font("Georgia", 17f, FontStyle.Regular);
        using var brush = new SolidBrush(Color.FromArgb(54, 70, 82));
        e.Graphics.DrawString("RA", font, brush, 24, 22);
    }
}

internal sealed class AccountHeroPanel : Panel
{
    public AccountHeroPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(228, 242, 252));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(198, 218, 232), 1);
        for (int i = 0; i < 8; i++)
        {
            int y = 78 + i * 12;
            var points = new[]
            {
                new Point(0, y + 38),
                new Point(130, y - 8),
                new Point(235, y + 22),
                new Point(372, y + 36),
                new Point(510, y - 10),
                new Point(690, y + 24),
                new Point(Width, y - 4)
            };
            e.Graphics.DrawCurve(pen, points);
        }
    }
}

internal sealed class GradientPanel : Panel
{
    public Color StartColor { get; set; } = Color.FromArgb(26, 54, 111);
    public Color EndColor { get; set; } = Color.FromArgb(15, 35, 77);
    public int Radius { get; set; } = 18;

    public GradientPanel()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var brush = new LinearGradientBrush(bounds, StartColor, EndColor, LinearGradientMode.ForwardDiagonal);
        using var path = CreatePath(bounds, Radius);
        e.Graphics.FillPath(brush, path);
    }

    private static GraphicsPath CreatePath(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
