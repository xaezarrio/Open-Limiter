using System.Drawing;
using System.Windows.Threading;
using OpenLimiter.Core.Models;
using OpenLimiter.Core.Versioning;
using OpenLimiter.Windows.Service;
using Forms = System.Windows.Forms;

namespace OpenLimiter.App;

internal sealed class TrayIconController : IDisposable
{
    private static readonly string ApplicationVersion =
        typeof(TrayIconController).Assembly.GetName().Version?.ToString() ?? "unknown";
    private readonly Action showWindow;
    private readonly Action requestExit;
    private readonly IPolicyServiceClient policyService = new PolicyServiceClient();
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.ToolStripMenuItem pauseAllItem = new("Pause all policies");
    private readonly Forms.ToolStripMenuItem resumeAllItem = new("Resume all policies");
    private readonly Forms.ToolStripMenuItem ruleItems = new("Saved policies");
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool isBusy;
    private bool hiddenMessageShown;

    public TrayIconController(Action showWindow, Action requestExit)
    {
        this.showWindow = showWindow;
        this.requestExit = requestExit;

        var openItem = new Forms.ToolStripMenuItem("Open OpenLimiter");
        openItem.Click += (_, _) => showWindow();
        pauseAllItem.Click += async (_, _) => await SetAllRulesEnabledAsync(enabled: false);
        resumeAllItem.Click += async (_, _) => await SetAllRulesEnabledAsync(enabled: true);
        var exitItem = new Forms.ToolStripMenuItem("Exit OpenLimiter");
        exitItem.Click += (_, _) => requestExit();

        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(pauseAllItem);
        menu.Items.Add(resumeAllItem);
        menu.Items.Add(ruleItems);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        notifyIcon = new()
        {
            ContextMenuStrip = menu,
            Icon = ExtractApplicationIcon(),
            Text = "OpenLimiter network policies",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => showWindow();

        refreshTimer.Tick += async (_, _) => await RefreshRulesAsync();
        refreshTimer.Start();
        _ = RefreshRulesAsync();
    }

    public void ShowWindowHiddenMessage()
    {
        if (hiddenMessageShown)
        {
            return;
        }

        hiddenMessageShown = true;
        ShowMessage("OpenLimiter is still running", "Open the tray menu to manage policies or exit.");
    }

    public void Dispose()
    {
        refreshTimer.Stop();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
    }

    private async Task RefreshRulesAsync()
    {
        if (isBusy)
        {
            return;
        }

        try
        {
            var rules = await policyService.ListRulesAsync();
            RebuildRuleItems(rules);
            pauseAllItem.Enabled = rules.Any(rule => rule.Enabled);
            resumeAllItem.Enabled = rules.Any(rule => !rule.Enabled);
        }
        catch (Exception exception)
        {
            ruleItems.DropDownItems.Clear();
            ruleItems.DropDownItems.Add(new Forms.ToolStripMenuItem("Policy Service unavailable")
            {
                Enabled = false,
                ToolTipText = exception.Message,
            });
            pauseAllItem.Enabled = false;
            resumeAllItem.Enabled = false;
        }
    }

    private void RebuildRuleItems(IReadOnlyList<ApplicationRule> rules)
    {
        ruleItems.DropDownItems.Clear();
        if (rules.Count == 0)
        {
            ruleItems.DropDownItems.Add(new Forms.ToolStripMenuItem("No saved policies") { Enabled = false });
            return;
        }

        foreach (var rule in rules.OrderBy(rule => rule.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var action = rule.Enabled ? "Pause" : "Resume";
            var item = new Forms.ToolStripMenuItem($"{action} {rule.DisplayName}")
            {
                ToolTipText = rule.ExecutablePath,
            };
            item.Click += async (_, _) => await SetRuleEnabledAsync(rule, !rule.Enabled);
            ruleItems.DropDownItems.Add(item);
        }
    }

    private async Task SetRuleEnabledAsync(ApplicationRule rule, bool enabled)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        SetMutationItemsEnabled(false);
        try
        {
            await EnsureCurrentServiceAsync();
            var result = await policyService.ApplyAsync(rule with { Enabled = enabled });
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", result.Errors));
            }

            ShowMessage(
                enabled ? "Policy resumed" : "Policy paused",
                $"{rule.DisplayName}: Windows enforcement is now {(enabled ? "active" : "paused")}.");
        }
        catch (Exception exception)
        {
            ShowMessage("Policy change failed", exception.Message, Forms.ToolTipIcon.Error);
        }
        finally
        {
            isBusy = false;
            await RefreshRulesAsync();
        }
    }

    private async Task SetAllRulesEnabledAsync(bool enabled)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        SetMutationItemsEnabled(false);
        try
        {
            await EnsureCurrentServiceAsync();
            var rules = await policyService.ListRulesAsync();
            var updatedRules = rules.Select(rule => rule with { Enabled = enabled }).ToArray();
            var result = await policyService.ReplaceRulesAsync(updatedRules);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", result.Errors));
            }

            ShowMessage(
                enabled ? "All policies resumed" : "All policies paused",
                enabled
                    ? "Saved Firewall and QoS enforcement is active."
                    : "Saved configurations remain available in OpenLimiter.");
        }
        catch (Exception exception)
        {
            ShowMessage("Policy change failed", exception.Message, Forms.ToolTipIcon.Error);
        }
        finally
        {
            isBusy = false;
            await RefreshRulesAsync();
        }
    }

    private async Task EnsureCurrentServiceAsync()
    {
        var response = await policyService.PingAsync();
        if (!response.Succeeded || !ComponentVersion.MatchesRelease(ApplicationVersion, response.ServiceVersion))
        {
            throw new InvalidOperationException(
                $"App {ApplicationVersion} and policy service {response.ServiceVersion ?? "unknown"} do not match. Install this release's service first.");
        }
    }

    private void SetMutationItemsEnabled(bool enabled)
    {
        pauseAllItem.Enabled = enabled;
        resumeAllItem.Enabled = enabled;
        ruleItems.Enabled = enabled;
    }

    private void ShowMessage(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        notifyIcon.BalloonTipIcon = icon;
        notifyIcon.BalloonTipTitle = title;
        notifyIcon.BalloonTipText = message;
        notifyIcon.ShowBalloonTip(4000);
    }

    private static Icon ExtractApplicationIcon()
    {
        if (Environment.ProcessPath is { } path && Icon.ExtractAssociatedIcon(path) is { } icon)
        {
            return icon;
        }

        return SystemIcons.Application;
    }
}
