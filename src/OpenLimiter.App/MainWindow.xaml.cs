using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenLimiter.Core.Models;
using OpenLimiter.Core.Parsing;
using OpenLimiter.Core.Versioning;
using OpenLimiter.Protocol;
using OpenLimiter.Windows.Processes;
using OpenLimiter.Windows.Service;
using OpenLimiter.Windows.Wfp;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;

namespace OpenLimiter.App;

public partial class MainWindow : Window
{
    private static readonly string ApplicationVersion =
        typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
    private readonly ObservableCollection<ProcessListItem> processes = [];
    private readonly ObservableCollection<RuleListItem> rules = [];
    private readonly ObservableCollection<ProfileListItem> profiles = [];
    private readonly ObservableCollection<ScheduleListItem> schedules = [];
    private readonly ObservableCollection<DriverFlowListItem> driverFlows = [];
    private readonly ConcurrentDictionary<string, ulong> applicationHashCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ProcessCatalog processCatalog = new();
    private readonly IPolicyServiceClient policyService = new PolicyServiceClient();
    private readonly DispatcherTimer driverFlowTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer trafficTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ProcessListItem? selectedProcess;
    private bool isBusy;
    private bool isServiceConnected;
    private bool isServiceVersionCurrent;
    private bool isLoadingDriverFlows;
    private bool isLoadingTraffic;
    private bool sortProcessesByTraffic;
    private string? connectedServiceVersion;

    public MainWindow()
    {
        InitializeComponent();

        ProcessList.ItemsSource = processes;
        RuleList.ItemsSource = rules;
        ProfileList.ItemsSource = profiles;
        ScheduleList.ItemsSource = schedules;
        DriverFlowList.ItemsSource = driverFlows;
        driverFlowTimer.Tick += DriverFlowTimer_Tick;
        trafficTimer.Tick += TrafficTimer_Tick;
        ConfigureServiceState(isConnected: false, "Checking policy service");
        ConfigureDriverState(isAvailable: null, "Checking WFP driver");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
        driverFlowTimer.Start();
        trafficTimer.Start();
        await CaptureWindowIfRequestedAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        await LoadProcessesAsync();
        await RefreshServiceDataAsync();
    }

    private async Task RefreshServiceDataAsync()
    {
        await LoadServiceHealthAsync();
        await Task.WhenAll(LoadRulesAsync(), LoadAutomationAsync(), LoadDriverFlowsAsync(), LoadNetworkTrafficAsync());
    }

    private async Task LoadAutomationAsync()
    {
        try
        {
            var snapshot = await policyService.GetAutomationAsync();
            ApplyAutomationSnapshot(snapshot);
        }
        catch (PolicyServiceUnavailableException)
        {
            profiles.Clear();
            schedules.Clear();
            AutomationStateText.Text = "Automation unavailable";
        }
        catch (Exception exception)
        {
            profiles.Clear();
            schedules.Clear();
            AutomationStateText.Text = "Automation error";
            SetStatus(exception.Message, isError: true);
        }
    }

    private void ApplyAutomationSnapshot(AutomationSnapshot snapshot)
    {
        var selectedProfileId = (ProfileList.SelectedItem as ProfileListItem)?.Profile.Id;
        profiles.Clear();
        foreach (var profile in snapshot.Profiles)
        {
            profiles.Add(new(profile));
        }

        schedules.Clear();
        foreach (var schedule in snapshot.Schedules.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var profileName = snapshot.Profiles.FirstOrDefault(profile => profile.Id == schedule.ProfileId)?.Name ?? "Missing profile";
            schedules.Add(new(schedule, profileName));
        }

        ProfileList.SelectedItem = profiles.FirstOrDefault(item => item.Profile.Id == selectedProfileId) ?? profiles.FirstOrDefault();
        AutomationStateText.Text = $"{profiles.Count} profile{(profiles.Count == 1 ? string.Empty : "s")} · {schedules.Count} schedule{(schedules.Count == 1 ? string.Empty : "s")}";
    }

    private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || string.IsNullOrWhiteSpace(ProfileNameBox.Text))
        {
            SetStatus("Enter a profile name before saving the current policy set.", isError: true);
            return;
        }
        if (!CanChangePolicies())
        {
            ReportUnavailablePolicyMutation();
            return;
        }

        SetBusy(true, "Saving profile...");
        try
        {
            var snapshot = await policyService.SaveProfileAsync(new()
            {
                Id = Guid.NewGuid(),
                Name = ProfileNameBox.Text.Trim(),
                Rules = rules.Select(item => item.Rule).ToArray(),
            });
            ApplyAutomationSnapshot(snapshot);
            ProfileNameBox.Clear();
            SetStatus("Saved the current policy set as a reusable profile.");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ActivateProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || sender is not Button { Tag: ProfileListItem item })
        {
            return;
        }
        if (!CanChangePolicies())
        {
            ReportUnavailablePolicyMutation();
            return;
        }

        SetBusy(true, $"Activating {item.Profile.Name}...");
        try
        {
            var result = await policyService.ActivateProfileAsync(item.Profile.Id);
            if (!result.Succeeded)
            {
                SetStatus(string.Join(" ", result.Errors), isError: true);
                return;
            }
            await LoadRulesAsync();
            SetStatus($"Activated profile {item.Profile.Name}.");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || sender is not Button { Tag: ProfileListItem item } ||
            MessageBox.Show(this, $"Delete profile '{item.Profile.Name}'?", "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "Deleting profile...");
        try
        {
            ApplyAutomationSnapshot(await policyService.DeleteProfileAsync(item.Profile.Id));
            SetStatus($"Deleted profile {item.Profile.Name}.");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AddScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || ProfileList.SelectedItem is not ProfileListItem profile ||
            string.IsNullOrWhiteSpace(ScheduleNameBox.Text) ||
            !ScheduleDaysParser.TryParse(ScheduleDaysBox.Text, out var days) ||
            !TimeOnly.TryParseExact(ScheduleTimeBox.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            SetStatus("Schedule needs a name, profile, days (daily/Weekdays/Mon,Wed), and 24-hour time such as 09:00.", isError: true);
            return;
        }

        SetBusy(true, "Saving schedule...");
        try
        {
            var snapshot = await policyService.SaveScheduleAsync(new()
            {
                Id = Guid.NewGuid(),
                Name = ScheduleNameBox.Text.Trim(),
                ProfileId = profile.Profile.Id,
                Days = days,
                Hour = time.Hour,
                Minute = time.Minute,
            });
            ApplyAutomationSnapshot(snapshot);
            ScheduleNameBox.Clear();
            SetStatus("Saved the profile schedule. Times use this PC's local clock.");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || sender is not Button { Tag: ScheduleListItem item } ||
            MessageBox.Show(this, $"Delete schedule '{item.Schedule.Name}'?", "Delete schedule", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "Deleting schedule...");
        try
        {
            ApplyAutomationSnapshot(await policyService.DeleteScheduleAsync(item.Schedule.Id));
            SetStatus($"Deleted schedule {item.Schedule.Name}.");
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadServiceHealthAsync()
    {
        try
        {
            var response = await policyService.PingAsync();
            if (!response.Succeeded)
            {
                throw new InvalidOperationException(response.ErrorMessage ?? "Policy service rejected the status request.");
            }

            connectedServiceVersion = response.ServiceVersion;
            isServiceVersionCurrent = ComponentVersion.MatchesRelease(ApplicationVersion, connectedServiceVersion);
            ConfigureServiceState(isConnected: true, "Policy service connected");

            if (!isServiceVersionCurrent)
            {
                SetStatus(ServiceVersionMismatchMessage(), isError: true);
            }
        }
        catch (PolicyServiceUnavailableException exception)
        {
            ConfigureServiceState(isConnected: false, "Policy service unavailable");
            SetStatus(exception.Message, isError: true);
        }
        catch (Exception exception)
        {
            ConfigureServiceState(isConnected: false, "Policy service unavailable");
            SetStatus(exception.Message, isError: true);
        }
    }

    private async Task LoadProcessesAsync()
    {
        ProcessLoadingState.Visibility = Visibility.Visible;
        ProcessEmptyState.Visibility = Visibility.Collapsed;
        ProcessErrorState.Visibility = Visibility.Collapsed;
        ProcessList.Visibility = Visibility.Collapsed;

        try
        {
            var snapshots = await Task.Run(processCatalog.GetRunningProcesses);
            var groups = ProcessCatalog.GroupByExecutable(snapshots);
            processes.Clear();
            foreach (var group in groups)
            {
                processes.Add(new(group));
            }

            CollectionViewSource.GetDefaultView(ProcessList.ItemsSource).Filter = FilterProcess;
            ProcessLoadingState.Visibility = Visibility.Collapsed;
            ProcessList.Visibility = Visibility.Visible;
            UpdateProcessState();
            SetStatus($"Grouped {snapshots.Count} running processes into {groups.Count} executable entries.");
        }
        catch (Exception exception)
        {
            ProcessLoadingState.Visibility = Visibility.Collapsed;
            ProcessErrorState.Visibility = Visibility.Visible;
            ProcessErrorText.Text = $"{exception.Message} Refresh to try again.";
            SetStatus("Could not read the process list.", isError: true);
        }
    }

    private async Task LoadNetworkTrafficAsync()
    {
        if (isLoadingTraffic)
        {
            return;
        }

        isLoadingTraffic = true;
        try
        {
            var snapshot = await policyService.GetNetworkTrafficAsync();
            if (!snapshot.IsAvailable)
            {
                TrafficStateText.Text = "Traffic unavailable";
                TrafficStateText.ToolTip = snapshot.Message;
                foreach (var process in processes)
                {
                    process.SetTrafficUnavailable(
                        "Traffic unavailable",
                        snapshot.Message ?? "The Policy Service cannot read kernel network telemetry on this system.");
                }
                return;
            }

            var samples = snapshot.Samples.ToDictionary(sample => sample.ProcessId);
            foreach (var process in processes)
            {
                process.UpdateTraffic(samples);
            }

            TrafficStateText.Text = $"Traffic sampled {snapshot.SampledAtUtc.ToLocalTime():HH:mm:ss}";
            TrafficStateText.ToolTip = snapshot.Message;
            if (sortProcessesByTraffic)
            {
                SortProcesses();
            }
        }
        catch (PolicyServiceUnavailableException exception)
        {
            TrafficStateText.Text = "Traffic unavailable";
            TrafficStateText.ToolTip = exception.Message;
            foreach (var process in processes)
            {
                process.SetTrafficUnavailable("Traffic: Policy Service unavailable", exception.Message);
            }
        }
        catch (Exception exception)
        {
            TrafficStateText.Text = "Traffic error";
            TrafficStateText.ToolTip = exception.Message;
            foreach (var process in processes)
            {
                process.SetTrafficUnavailable("Traffic error", exception.Message);
            }
        }
        finally
        {
            isLoadingTraffic = false;
        }
    }

    private async Task LoadRulesAsync()
    {
        RuleLoadingState.Visibility = Visibility.Visible;
        RuleEmptyState.Visibility = Visibility.Collapsed;
        RuleErrorState.Visibility = Visibility.Collapsed;
        RuleList.Visibility = Visibility.Collapsed;
        ClearRulesButton.IsEnabled = false;

        try
        {
            var savedRules = await policyService.ListRulesAsync();
            ConfigureServiceState(isConnected: true, "Policy service connected");
            rules.Clear();
            foreach (var rule in savedRules)
            {
                rules.Add(new(rule));
            }

            RuleLoadingState.Visibility = Visibility.Collapsed;
            RuleList.Visibility = rules.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            RuleEmptyState.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RuleCountText.Text = rules.Count == 1 ? "1 policy" : $"{rules.Count} policies";
            PopulateRuleEditor();
            UpdateRuleActions();
        }
        catch (PolicyServiceUnavailableException exception)
        {
            RuleLoadingState.Visibility = Visibility.Collapsed;
            RuleErrorState.Visibility = Visibility.Visible;
            RuleErrorText.Text = $"{exception.Message} Start the OpenLimiter Policy Service, then retry.";
            ConfigureServiceState(isConnected: false, "Policy service unavailable");
            SetStatus("Policy service is unavailable. No network policy can be changed.", isError: true);
        }
        catch (Exception exception)
        {
            RuleLoadingState.Visibility = Visibility.Collapsed;
            RuleErrorState.Visibility = Visibility.Visible;
            RuleErrorText.Text = $"{exception.Message} Check the rules file and refresh.";
            ClearRulesButton.IsEnabled = false;
            SetStatus("Could not load saved policies.", isError: true);
        }
    }

    private async Task LoadDriverFlowsAsync(bool showLoading = true)
    {
        if (isLoadingDriverFlows)
        {
            return;
        }

        isLoadingDriverFlows = true;
        if (showLoading)
        {
            DriverFlowLoadingState.Visibility = Visibility.Visible;
            DriverFlowEmptyState.Visibility = Visibility.Collapsed;
            DriverFlowUnavailableState.Visibility = Visibility.Collapsed;
            DriverFlowErrorState.Visibility = Visibility.Collapsed;
            DriverFlowList.Visibility = Visibility.Collapsed;
        }
        if (showLoading)
        {
            DriverFlowRefreshButton.IsEnabled = false;
        }

        try
        {
            var snapshot = await policyService.GetDriverFlowsAsync();
            ConfigureServiceState(isConnected: true, "Policy service connected");
            driverFlows.Clear();

            if (!snapshot.IsAvailable)
            {
                ConfigureDriverState(isAvailable: false, "WFP driver offline");
                DriverFlowLoadingState.Visibility = Visibility.Collapsed;
                DriverFlowUnavailableState.Visibility = Visibility.Visible;
                DriverFlowUnavailableTitle.Text = "WFP driver offline";
                DriverFlowUnavailableText.Text = string.IsNullOrWhiteSpace(snapshot.Message)
                    ? "The policy service cannot reach the OpenLimiter WFP driver. Download limiting is in the project backlog."
                    : $"{snapshot.Message} Download limiting is in the project backlog.";
                return;
            }


            if (!snapshot.PolicySessionActive)
            {
                ConfigureDriverState(isAvailable: false, "WFP filters inactive");
                DriverFlowLoadingState.Visibility = Visibility.Collapsed;
                DriverFlowUnavailableState.Visibility = Visibility.Visible;
                DriverFlowUnavailableTitle.Text = "WFP filters inactive";
                DriverFlowUnavailableText.Text = string.IsNullOrWhiteSpace(snapshot.Message)
                    ? "The driver is online, but the policy service did not install its temporary flow filters."
                    : snapshot.Message;
                return;
            }

            var applicationPaths = processes
                .Where(item => item.ExecutablePath is not null)
                .Select(item => (Path: item.ExecutablePath!, item.Name))
                .ToArray();
            var namesByApplicationHash = await Task.Run(() => BuildApplicationNameIndex(applicationPaths));
            foreach (var flowEvent in snapshot.Events.OrderByDescending(item => item.Sequence))
            {
                var processName = processes.FirstOrDefault(item =>
                    item.ProcessIds.Any(processId => (ulong)processId == flowEvent.ProcessId))?.Name;
                if (processName is null)
                {
                    namesByApplicationHash.TryGetValue(flowEvent.ApplicationIdHash, out processName);
                }

                driverFlows.Add(new(flowEvent, processName));
            }

            ConfigureDriverState(isAvailable: true, "WFP driver online");
            DriverFlowLoadingState.Visibility = Visibility.Collapsed;
            DriverFlowStateText.Text = snapshot.LatestSequence == 0
                ? "READY"
                : $"LATEST #{snapshot.LatestSequence}";
            DriverFlowList.Visibility = driverFlows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            DriverFlowEmptyState.Visibility = driverFlows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (PolicyServiceUnavailableException exception)
        {
            DriverFlowLoadingState.Visibility = Visibility.Collapsed;
            DriverFlowErrorState.Visibility = Visibility.Visible;
            DriverFlowErrorText.Text = $"{exception.Message} Start the OpenLimiter Policy Service, then retry.";
            ConfigureServiceState(isConnected: false, "Policy service unavailable");
            ConfigureDriverState(isAvailable: null, "WFP status unknown");
        }
        catch (Exception exception)
        {
            DriverFlowLoadingState.Visibility = Visibility.Collapsed;
            DriverFlowErrorState.Visibility = Visibility.Visible;
            DriverFlowErrorText.Text = exception.Message;
            ConfigureDriverState(isAvailable: null, "WFP status unknown");
        }
        finally
        {
            isLoadingDriverFlows = false;
            if (showLoading)
            {
                DriverFlowRefreshButton.IsEnabled = !isBusy;
            }
        }
    }

    private void ProcessSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ProcessList.ItemsSource is null)
        {
            return;
        }

        CollectionViewSource.GetDefaultView(ProcessList.ItemsSource).Refresh();
        UpdateProcessState();
    }

    private void ProcessSortButton_Click(object sender, RoutedEventArgs e)
    {
        sortProcessesByTraffic = !sortProcessesByTraffic;
        ProcessSortButton.Content = sortProcessesByTraffic ? "Sort by name" : "Sort by traffic";
        ProcessSortButton.ToolTip = sortProcessesByTraffic
            ? "Return executable groups to alphabetical order."
            : "Order executable groups by current measured upload and download rate.";
        SortProcesses();
    }

    private void SortProcesses()
    {
        var selectedPath = selectedProcess?.ExecutablePath;
        var ordered = sortProcessesByTraffic
            ? processes.OrderByDescending(item => item.TotalBytesPerSecond).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray()
            : processes.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        for (var targetIndex = 0; targetIndex < ordered.Length; targetIndex++)
        {
            var currentIndex = processes.IndexOf(ordered[targetIndex]);
            if (currentIndex != targetIndex)
            {
                processes.Move(currentIndex, targetIndex);
            }
        }

        if (selectedPath is not null)
        {
            ProcessList.SelectedItem = processes.FirstOrDefault(item =>
                string.Equals(item.ExecutablePath, selectedPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private bool FilterProcess(object item)
    {
        if (item is not ProcessListItem process)
        {
            return false;
        }

        var query = ProcessSearchBox.Text.Trim();
        return query.Length == 0 ||
               process.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               process.ProcessIds.Any(processId => processId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) ||
               (process.ExecutablePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void UpdateProcessState()
    {
        if (ProcessList.ItemsSource is null)
        {
            return;
        }

        var visibleGroups = CollectionViewSource.GetDefaultView(ProcessList.ItemsSource)
            .Cast<ProcessListItem>()
            .ToArray();
        var processCount = visibleGroups.Sum(group => group.InstanceCount);
        ProcessCountText.Text = $"{visibleGroups.Length} groups{Environment.NewLine}{processCount} processes";
        ProcessEmptyState.Visibility = visibleGroups.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProcessList.Visibility = visibleGroups.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedProcess = ProcessList.SelectedItem as ProcessListItem;
        if (selectedProcess is null)
        {
            NoSelectionState.Visibility = Visibility.Visible;
            RuleEditor.Visibility = Visibility.Collapsed;
            return;
        }

        NoSelectionState.Visibility = Visibility.Collapsed;
        RuleEditor.Visibility = Visibility.Visible;
        SelectedProcessName.Text = selectedProcess.Name;
        SelectedProcessPath.Text = selectedProcess.ExecutablePath ?? "Windows did not expose this executable path.";
        PopulateRuleEditor();
        UpdateEditorAvailability();
    }

    private void PopulateRuleEditor()
    {
        if (selectedProcess is null)
        {
            return;
        }

        var existingRule = FindRuleForSelectedProcess()?.Rule;
        RuleNameBox.Text = existingRule?.DisplayName ?? selectedProcess.Name;
        BlockInboundCheckBox.IsChecked = existingRule is not null &&
            (existingRule.BlockedDirections & TrafficDirection.Inbound) != TrafficDirection.None;
        BlockOutboundCheckBox.IsChecked = existingRule is not null &&
            (existingRule.BlockedDirections & TrafficDirection.Outbound) != TrafficDirection.None;
        UploadRateBox.Text = existingRule?.UploadLimitBitsPerSecond is { } rate
            ? (rate / 1_000_000m).ToString("0.###", CultureInfo.InvariantCulture)
            : string.Empty;
        DurationMinutesBox.Text = existingRule?.ExpiresAtUtc is { } expiresAtUtc
            ? Math.Max(1, Math.Ceiling((expiresAtUtc - DateTimeOffset.UtcNow).TotalMinutes))
                .ToString("0", CultureInfo.InvariantCulture)
            : string.Empty;
        ApplyRuleButton.Content = existingRule is null
            ? "Apply network rule"
            : existingRule.Enabled
                ? "Update network rule"
                : "Update paused rule";
    }

    private RuleListItem? FindRuleForSelectedProcess() => rules.FirstOrDefault(item =>
        selectedProcess?.ExecutablePath is not null &&
        string.Equals(item.Rule.ExecutablePath, selectedProcess.ExecutablePath, StringComparison.OrdinalIgnoreCase));

    private void OutboundControl_Changed(object sender, RoutedEventArgs e)
    {
        UploadRateBox.IsEnabled = BlockOutboundCheckBox.IsChecked != true;
        EditorHintText.Text = BlockOutboundCheckBox.IsChecked == true
            ? "Outbound blocking overrides an upload rate limit."
            : "Leave upload rate blank when no throttling is needed.";
    }

    private async void ApplyRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || selectedProcess?.ExecutablePath is null)
        {
            return;
        }

        if (!CanChangePolicies())
        {
            ReportUnavailablePolicyMutation();
            return;
        }

        if (!TryCreateRule(out var rule, out var validationMessage))
        {
            SetStatus(validationMessage, isError: true);
            return;
        }

        SetBusy(true, "Applying Windows network policy...");
        try
        {
            var result = await policyService.ApplyAsync(rule);
            if (!result.Succeeded)
            {
                SetStatus(string.Join(" ", result.Errors), isError: true);
                return;
            }

            await LoadRulesAsync();

            var message = string.Join(" ", result.Applied.Concat(result.Warnings));
            SetStatus(string.IsNullOrWhiteSpace(message) ? "Policy applied." : message);
        }
        catch (PolicyServiceUnavailableException exception)
        {
            ConfigureServiceState(isConnected: false, "Policy service unavailable");
            SetStatus(exception.Message, isError: true);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryCreateRule(out ApplicationRule rule, out string validationMessage)
    {
        rule = null!;
        validationMessage = string.Empty;

        var blockInbound = BlockInboundCheckBox.IsChecked == true;
        var blockOutbound = BlockOutboundCheckBox.IsChecked == true;
        long? uploadLimit = null;
        DateTimeOffset? expiresAtUtc = null;

        if (!blockOutbound && !string.IsNullOrWhiteSpace(UploadRateBox.Text))
        {
            if (!DataRateParser.TryParse($"{UploadRateBox.Text}mbps", out var parsedRate))
            {
                validationMessage = "Enter an upload rate greater than zero, for example 2.5 Mbps.";
                return false;
            }

            uploadLimit = parsedRate;
        }

        if (!string.IsNullOrWhiteSpace(DurationMinutesBox.Text))
        {
            if (!RuleDurationParser.TryParse($"{DurationMinutesBox.Text}m", out var duration))
            {
                validationMessage = "Temporary duration must be a whole number from 1 to 10080 minutes (7 days).";
                return false;
            }

            expiresAtUtc = DateTimeOffset.UtcNow.Add(duration);
        }

        if (!blockInbound && !blockOutbound && uploadLimit is null)
        {
            validationMessage = "Choose inbound block, outbound block, or an upload rate before applying.";
            return false;
        }

        var direction = TrafficDirection.None;
        if (blockInbound)
        {
            direction |= TrafficDirection.Inbound;
        }

        if (blockOutbound)
        {
            direction |= TrafficDirection.Outbound;
        }

        var existingRule = FindRuleForSelectedProcess()?.Rule;
        rule = new()
        {
            Id = existingRule?.Id ?? Guid.NewGuid(),
            DisplayName = string.IsNullOrWhiteSpace(RuleNameBox.Text) ? selectedProcess!.Name : RuleNameBox.Text.Trim(),
            ExecutablePath = selectedProcess!.ExecutablePath!,
            Enabled = existingRule?.Enabled ?? true,
            BlockedDirections = direction,
            UploadLimitBitsPerSecond = uploadLimit,
            ExpiresAtUtc = expiresAtUtc,
        };
        return true;
    }

    private async void ToggleRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || sender is not Button { Tag: RuleListItem item })
        {
            return;
        }

        if (!CanChangePolicies())
        {
            ReportUnavailablePolicyMutation();
            return;
        }

        var enabled = !item.Rule.Enabled;
        var action = enabled ? "Resuming" : "Pausing";
        SetBusy(true, $"{action} policy for {item.Rule.DisplayName}...");
        try
        {
            var result = await policyService.ApplyAsync(item.Rule with { Enabled = enabled });
            if (!result.Succeeded)
            {
                SetStatus(string.Join(" ", result.Errors), isError: true);
                return;
            }

            await LoadRulesAsync();
            SetStatus($"{(enabled ? "Resumed" : "Paused")} the policy for {item.Rule.DisplayName}.");
        }
        catch (PolicyServiceUnavailableException exception)
        {
            ConfigureServiceState(isConnected: false, "Policy service unavailable");
            SetStatus(exception.Message, isError: true);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || sender is not Button { Tag: RuleListItem item })
        {
            return;
        }

        if (!CanChangePolicies())
        {
            ReportUnavailablePolicyMutation();
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Remove the saved policy for {item.Rule.DisplayName}?\n\nThis also removes its Windows firewall and QoS enforcement.",
            "Remove network policy",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, $"Removing policy for {item.Rule.DisplayName}...");
        try
        {
            var result = await policyService.RemoveAsync(item.Rule.Id);
            if (!result.Succeeded)
            {
                SetStatus(string.Join(" ", result.Errors), isError: true);
                return;
            }

            await LoadRulesAsync();
            SetStatus($"Removed the policy for {item.Rule.DisplayName}.");
        }
        catch (PolicyServiceUnavailableException exception)
        {
            ConfigureServiceState(isConnected: false, "Policy service unavailable");
            SetStatus(exception.Message, isError: true);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ClearRulesButton_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy || rules.Count == 0)
        {
            return;
        }

        if (!CanChangePolicies())
        {
            ReportUnavailablePolicyMutation();
            return;
        }

        var policyCount = rules.Count;
        var policyLabel = policyCount == 1 ? "1 saved policy" : $"all {policyCount} saved policies";
        var confirmation = MessageBox.Show(
            this,
            $"Remove {policyLabel}?\n\nThis also removes their Windows firewall and QoS enforcement.",
            "Remove saved policies",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, $"Removing {policyLabel}...");
        try
        {
            var result = await policyService.ClearAsync();
            if (!result.Succeeded)
            {
                SetStatus(string.Join(" ", result.Errors), isError: true);
                return;
            }

            await LoadRulesAsync();
            SetStatus(policyCount == 1 ? "Removed the saved policy." : $"Removed all {policyCount} saved policies.");
        }
        catch (PolicyServiceUnavailableException exception)
        {
            ConfigureServiceState(isConnected: false, "Policy service unavailable");
            SetStatus(exception.Message, isError: true);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ServiceRetryButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshServiceDataAsync();
    }

    private async void DriverFlowRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadDriverFlowsAsync();
    }

    private async void DriverFlowTimer_Tick(object? sender, EventArgs e)
    {
        if (isServiceConnected && !isBusy && !isLoadingDriverFlows)
        {
            await LoadDriverFlowsAsync(showLoading: false);
        }
    }

    private async void TrafficTimer_Tick(object? sender, EventArgs e)
    {
        if (isServiceConnected && !isLoadingTraffic)
        {
            await LoadNetworkTrafficAsync();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        driverFlowTimer.Stop();
        trafficTimer.Stop();
    }

    private void ConfigureServiceState(bool isConnected, string label)
    {
        isServiceConnected = isConnected;
        if (!isConnected)
        {
            connectedServiceVersion = null;
            isServiceVersionCurrent = false;
        }

        var requiresUpdate = isConnected && !isServiceVersionCurrent;
        AccessStateText.Text = requiresUpdate ? "Service update required" : label;
        AccessStateMarker.Fill = (Brush)FindResource(
            requiresUpdate ? "DangerBrush" : isConnected ? "AmberBrush" : "MutedBrush");
        AccessStateBorder.ToolTip = requiresUpdate
            ? ServiceVersionMismatchMessage()
            : isConnected
                ? $"App {ApplicationVersion}; policy service {connectedServiceVersion ?? "unknown"}."
                : label;
        ServiceRetryButton.Content = requiresUpdate ? "Check service" : "Retry service";
        ServiceRetryButton.Visibility = isConnected && !requiresUpdate
            ? Visibility.Collapsed
            : Visibility.Visible;

        UpdateEditorAvailability();
        UpdateRuleActions();
    }

    private void ConfigureDriverState(bool? isAvailable, string label)
    {
        DriverStateText.Text = label;
        DriverStateMarker.Fill = (Brush)FindResource(isAvailable == true ? "CyanBrush" : "MutedBrush");
        DriverFlowStateText.Text = isAvailable switch
        {
            true => "READY",
            false => "OFFLINE",
            null => "UNKNOWN",
        };
        DriverFlowStateText.Foreground = (Brush)FindResource(isAvailable == true ? "CyanBrush" : "MutedBrush");
    }

    private Dictionary<ulong, string> BuildApplicationNameIndex(
        IEnumerable<(string Path, string Name)> applications)
    {
        var result = new Dictionary<ulong, string>();
        foreach (var application in applications)
        {
            if (applicationHashCache.TryGetValue(application.Path, out var cachedHash))
            {
                result.TryAdd(cachedHash, application.Name);
            }
            else if (WfpApplicationIdentity.TryGetHash(application.Path, out var resolvedHash))
            {
                applicationHashCache.TryAdd(application.Path, resolvedHash);
                result.TryAdd(resolvedHash, application.Name);
            }
        }

        return result;
    }

    private void UpdateEditorAvailability()
    {
        var hasPath = selectedProcess?.ExecutablePath is not null;
        ApplyRuleButton.IsEnabled = !isBusy && CanChangePolicies() && hasPath;
        ApplyRuleButton.ToolTip = !isServiceConnected
            ? "Start the OpenLimiter Policy Service to change Windows network policy."
            : !isServiceVersionCurrent
                ? "Update the OpenLimiter Policy Service before changing Windows network policy."
            : !hasPath
                ? "Windows did not expose an executable path for this process."
                : "Apply this policy to the selected executable.";
    }

    private void UpdateRuleActions()
    {
        ClearRulesButton.IsEnabled = !isBusy && CanChangePolicies() && rules.Count > 0;
        ClearRulesButton.ToolTip = rules.Count == 0
            ? "There are no saved policies to remove."
            : !isServiceConnected
                ? "Reconnect the OpenLimiter Policy Service before removing policies."
                : !isServiceVersionCurrent
                    ? "Update the OpenLimiter Policy Service before removing policies."
                : "Remove every saved policy and its Windows enforcement.";
    }

    private bool CanChangePolicies() => isServiceConnected && isServiceVersionCurrent;

    private void ReportUnavailablePolicyMutation()
    {
        SetStatus(
            isServiceConnected ? ServiceVersionMismatchMessage() : "Policy service is unavailable.",
            isError: true);
    }

    private string ServiceVersionMismatchMessage() =>
        $"App {ApplicationVersion} and policy service {connectedServiceVersion ?? "unknown"} do not match. " +
        "Run install-service.ps1 from this release as Administrator, then select Check service.";

    private void SetBusy(bool busy, string? message = null)
    {
        isBusy = busy;
        ServiceRetryButton.IsEnabled = !busy;
        ProcessList.IsEnabled = !busy;
        RuleList.IsEnabled = !busy;
        DriverFlowRefreshButton.IsEnabled = !busy;
        UpdateEditorAvailability();
        UpdateRuleActions();
        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message);
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "MutedBrush");
    }

    private async Task CaptureWindowIfRequestedAsync()
    {
        var capturePath = Environment.GetEnvironmentVariable("OPENLIMITER_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(capturePath))
        {
            return;
        }

        var requestedProcess = Environment.GetEnvironmentVariable("OPENLIMITER_CAPTURE_PROCESS");
        var process = processes.FirstOrDefault(item =>
            item.ExecutablePath is not null &&
            (string.IsNullOrWhiteSpace(requestedProcess) || item.Name.Contains(requestedProcess, StringComparison.OrdinalIgnoreCase)));
        if (process is not null)
        {
            ProcessList.SelectedItem = process;
            ProcessList.ScrollIntoView(process);
        }

        var requestedSize = Environment.GetEnvironmentVariable("OPENLIMITER_CAPTURE_SIZE")?.Split('x');
        if (requestedSize is [var widthText, var heightText] &&
            double.TryParse(widthText, out var requestedWidth) &&
            double.TryParse(heightText, out var requestedHeight))
        {
            Width = Math.Max(MinWidth, requestedWidth);
            Height = Math.Max(MinHeight, requestedHeight);
        }

        await Task.Delay(250);
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(this);

        var directory = Path.GetDirectoryName(capturePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using (var stream = File.Create(capturePath))
        {
            encoder.Save(stream);
        }

        System.Windows.Application.Current.Shutdown();
    }
}

public sealed class ProcessListItem(ProcessGroupSnapshot group) : INotifyPropertyChanged
{
    private long uploadBytesPerSecond;
    private long downloadBytesPerSecond;
    private long totalUploadBytes;
    private long totalDownloadBytes;
    private bool trafficAvailable;
    private string unavailableTrafficLabel = "Traffic: waiting for Policy Service";
    private string unavailableTrafficToolTip = "Install and connect the matching Policy Service to read measured traffic.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProcessGroupSnapshot Group { get; } = group;

    public IReadOnlyList<int> ProcessIds => Group.ProcessIds;

    public int InstanceCount => Group.InstanceCount;

    public string Name => Group.Name;

    public string? ExecutablePath => Group.ExecutablePath;

    public string InstanceLabel => InstanceCount == 1 ? "1 process" : $"{InstanceCount} processes";

    public string ProcessIdsLabel => $"PID {string.Join(", ", ProcessIds)}";

    public string PathLabel => ExecutablePath ?? "Protected or already exited";

    public long TotalBytesPerSecond => uploadBytesPerSecond + downloadBytesPerSecond;

    public string TrafficLabel => trafficAvailable
        ? $"Down {FormatBytes(downloadBytesPerSecond)}/s · Up {FormatBytes(uploadBytesPerSecond)}/s"
        : unavailableTrafficLabel;

    public string TrafficToolTip => trafficAvailable
        ? $"Since Policy Service start: downloaded {FormatBytes(totalDownloadBytes)}, uploaded {FormatBytes(totalUploadBytes)}"
        : unavailableTrafficToolTip;

    public void UpdateTraffic(IReadOnlyDictionary<int, NetworkTrafficSample> samples)
    {
        uploadBytesPerSecond = ProcessIds.Sum(processId => samples.GetValueOrDefault(processId)?.UploadBytesPerSecond ?? 0);
        downloadBytesPerSecond = ProcessIds.Sum(processId => samples.GetValueOrDefault(processId)?.DownloadBytesPerSecond ?? 0);
        totalUploadBytes = ProcessIds.Sum(processId => samples.GetValueOrDefault(processId)?.TotalUploadBytes ?? 0);
        totalDownloadBytes = ProcessIds.Sum(processId => samples.GetValueOrDefault(processId)?.TotalDownloadBytes ?? 0);
        trafficAvailable = true;
        NotifyTrafficChanged();
    }

    public void SetTrafficUnavailable(string label, string toolTip)
    {
        uploadBytesPerSecond = 0;
        downloadBytesPerSecond = 0;
        totalUploadBytes = 0;
        totalDownloadBytes = 0;
        trafficAvailable = false;
        unavailableTrafficLabel = label;
        unavailableTrafficToolTip = toolTip;
        NotifyTrafficChanged();
    }

    private void NotifyTrafficChanged()
    {
        OnPropertyChanged(nameof(TotalBytesPerSecond));
        OnPropertyChanged(nameof(TrafficLabel));
        OnPropertyChanged(nameof(TrafficToolTip));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        decimal value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}

public sealed record RuleListItem(ApplicationRule Rule)
{
    public string StatusLabel => Rule.Enabled ? "Status: Active" : "Status: Paused";

    public string ToggleActionLabel => Rule.Enabled ? "Pause rule" : "Resume rule";

    public string ToggleAutomationName => $"{ToggleActionLabel} for {Rule.DisplayName}";

    public string BlockLabel => Rule.BlockedDirections == TrafficDirection.None
        ? "Not blocked"
        : Rule.BlockedDirections.ToString();

    public string UploadLabel => Rule.UploadLimitBitsPerSecond is { } rate
        ? FormatRate(rate)
        : "No limit";

    public string ExpiryLabel => Rule.ExpiresAtUtc is { } expiresAtUtc
        ? $"Expires {expiresAtUtc.ToLocalTime():MMM d, HH:mm}"
        : "Permanent";

    private static string FormatRate(long bitsPerSecond)
    {
        if (bitsPerSecond >= 1_000_000_000 && bitsPerSecond % 1_000_000_000 == 0)
        {
            return $"{bitsPerSecond / 1_000_000_000m:0.##} Gbps";
        }

        if (bitsPerSecond >= 1_000_000)
        {
            return $"{bitsPerSecond / 1_000_000m:0.##} Mbps";
        }

        return $"{bitsPerSecond / 1_000m:0.##} Kbps";
    }
}

public sealed record ProfileListItem(PolicyProfileSummary Profile)
{
    public string RuleCountLabel => Profile.RuleCount == 1 ? "1 rule" : $"{Profile.RuleCount} rules";

    public override string ToString() => Profile.Name;
}

public sealed record ScheduleListItem(PolicySchedule Schedule, string ProfileName)
{
    public string TimingLabel => $"{Schedule.Days} · {Schedule.Hour:00}:{Schedule.Minute:00} local";

    public string StateLabel => Schedule.Enabled ? "Enabled" : "Disabled";
}

public sealed record DriverFlowListItem(WfpFlowEvent FlowEvent, string? ProcessName)
{
    public string SequenceLabel => $"#{FlowEvent.Sequence}";

    public string ProcessLabel => ProcessName ?? "Unknown process";

    public string ProcessIdLabel => $"PID {FlowEvent.ProcessId}";

    public string DirectionLabel => FlowEvent.Direction switch
    {
        WfpFlowDirection.Inbound => "Inbound",
        WfpFlowDirection.Outbound => "Outbound",
        _ => "Unknown",
    };

    public string TransportLabel => $"{FormatProtocol(FlowEvent.IpProtocol)} · {IpVersionLabel}";

    private string IpVersionLabel => FlowEvent.IpVersion switch
    {
        4 => "IPv4",
        6 => "IPv6",
        _ => $"IP {FlowEvent.IpVersion}",
    };

    private static string FormatProtocol(byte protocol) => protocol switch
    {
        1 => "ICMP",
        6 => "TCP",
        17 => "UDP",
        58 => "ICMPv6",
        _ => $"Protocol {protocol}",
    };

    public string ApplicationIdHashLabel => $"0x{FlowEvent.ApplicationIdHash:X16}";
}
