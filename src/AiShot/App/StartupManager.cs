using Microsoft.Win32;

namespace AiShot.App;

/// <summary>
/// Liga/desliga o início automático com o Windows via chave Run do registro
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AiShot";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var val = key?.GetValue(ValueName) as string;
        return !string.IsNullOrEmpty(val);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key!.SetValue(ValueName, $"\"{ExePath}\"");
        else key!.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
