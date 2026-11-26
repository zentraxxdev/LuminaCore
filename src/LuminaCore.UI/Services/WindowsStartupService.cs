using Microsoft.Win32;

namespace LuminaCore.UI.Services;

/// <summary>
/// Manages the HKCU Windows startup Registry key for LuminaCore (user-scoped, no UAC required).
/// </summary>
public sealed class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "LuminaCore";

    public bool IsEnabledInRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AppValueName) is string val && !string.IsNullOrWhiteSpace(val);
        }
        catch
        {
            return false;
        }
    }

    public void SetStartup(bool enable, bool startMinimized)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (!enable)
            {
                key.DeleteValue(AppValueName, throwOnMissingValue: false);
                return;
            }

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            string cmd = startMinimized ? $"\"{exePath}\" --minimized" : $"\"{exePath}\"";
            key.SetValue(AppValueName, cmd, RegistryValueKind.String);
        }
        catch
        {
            // Ignore restricted registry environments gracefully
        }
    }
}
