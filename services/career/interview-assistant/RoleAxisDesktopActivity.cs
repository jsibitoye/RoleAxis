using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RoleAxis.Career.InterviewAssistant;

internal sealed class ActivityLogItem
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Module { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Severity { get; set; } = "Info";
}

internal static class ActivityLogService
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string ActivityPath => Path.Combine(DesktopSessionState.LocalAppFolder, "activity-log.json");

    public static void Add(string module, string action, string detail = "", string severity = "Info")
    {
        try
        {
            lock (Gate)
            {
                var items = LoadAllUnlocked();
                items.Insert(0, new ActivityLogItem
                {
                    Timestamp = DateTime.Now,
                    Module = module,
                    Action = action,
                    Detail = detail,
                    Severity = severity
                });

                Directory.CreateDirectory(DesktopSessionState.LocalAppFolder);
                File.WriteAllText(ActivityPath, JsonSerializer.Serialize(items.Take(250).ToList(), JsonOptions));
            }
        }
        catch
        {
            // Activity history is useful, but it should never block the assistant.
        }
    }

    public static List<ActivityLogItem> LoadRecent(int max = 12)
    {
        try
        {
            lock (Gate)
            {
                return LoadAllUnlocked()
                    .OrderByDescending(item => item.Timestamp)
                    .Take(max)
                    .ToList();
            }
        }
        catch
        {
            return new List<ActivityLogItem>();
        }
    }

    public static int Count(string module, string actionContains = "")
    {
        return LoadRecent(250).Count(item =>
            item.Module.Equals(module, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(actionContains) ||
             item.Action.Contains(actionContains, StringComparison.OrdinalIgnoreCase)));
    }

    public static ActivityLogItem? LastForModule(string module)
    {
        return LoadRecent(250).FirstOrDefault(item =>
            item.Module.Equals(module, StringComparison.OrdinalIgnoreCase));
    }

    private static List<ActivityLogItem> LoadAllUnlocked()
    {
        if (!File.Exists(ActivityPath))
            return new List<ActivityLogItem>();

        string json = File.ReadAllText(ActivityPath);
        return JsonSerializer.Deserialize<List<ActivityLogItem>>(json) ?? new List<ActivityLogItem>();
    }
}
