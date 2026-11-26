using System.Diagnostics;
using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;

namespace LuminaCore.Capture;

/// <summary>
/// Lightweight background process watcher that auto-switches lighting profiles when configured
/// game executables (e.g., cod.exe, ForzaHorizon5.exe, RDR2.exe) are launched or closed.
/// </summary>
public sealed class ProcessWatcherService : IProcessWatcherService
{
    private readonly object _lock = new();
    private Dictionary<string, string> _processToProfileMap = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Task? _watchTask;
    private volatile bool _isRunning;
    private string? _activeMatchedProcess;
    private string? _activeMatchedProfileId;

    public bool IsRunning => _isRunning;
    public string? ActiveMatchedProcess => _activeMatchedProcess;
    public string? ActiveMatchedProfileId => _activeMatchedProfileId;

    public event EventHandler<ProcessMatchEventArgs>? ProcessMatchChanged;

    public void UpdateProfiles(IReadOnlyList<LightingProfile> profiles)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (profile.AssociatedProcesses is null) continue;
            foreach (var raw in profile.AssociatedProcesses)
            {
                string normalized = NormalizeProcessName(raw);
                if (!string.IsNullOrEmpty(normalized) && !map.ContainsKey(normalized))
                {
                    map[normalized] = profile.Id;
                }
            }
        }

        lock (_lock)
        {
            _processToProfileMap = map;
        }
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _cts = new CancellationTokenSource();
        _watchTask = Task.Run(() => WatchLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            CheckRunningProcesses();
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                CheckRunningProcesses();
            }
        }
        catch (OperationCanceledException)
        {
            // Clean exit
        }
    }

    private void CheckRunningProcesses()
    {
        Dictionary<string, string> snapshot;
        lock (_lock)
        {
            if (_processToProfileMap.Count == 0)
            {
                NotifyIfChanged(null, null);
                return;
            }
            snapshot = _processToProfileMap;
        }

        string? matchedProc = null;
        string? matchedProfile = null;

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return;
        }

        try
        {
            foreach (var proc in processes)
            {
                try
                {
                    string name = proc.ProcessName;
                    if (snapshot.TryGetValue(name, out var profileId))
                    {
                        matchedProc = name + ".exe";
                        matchedProfile = profileId;
                        break;
                    }
                }
                catch
                {
                    // Access denied or process exited
                }
            }
        }
        finally
        {
            foreach (var proc in processes)
            {
                try { proc.Dispose(); } catch { }
            }
        }

        NotifyIfChanged(matchedProc, matchedProfile);
    }

    private void NotifyIfChanged(string? processName, string? profileId)
    {
        if (string.Equals(_activeMatchedProfileId, profileId, StringComparison.Ordinal) &&
            string.Equals(_activeMatchedProcess, processName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeMatchedProcess = processName;
        _activeMatchedProfileId = profileId;

        ProcessMatchChanged?.Invoke(this, new ProcessMatchEventArgs
        {
            ProcessName = processName,
            ProfileId = profileId
        });
    }

    public static string NormalizeProcessName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string trimmed = Path.GetFileName(input.Trim());
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];
        return trimmed.Trim();
    }

    public void Dispose()
    {
        Stop();
    }
}
