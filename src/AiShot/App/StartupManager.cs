using Microsoft.Win32;
using Windows.ApplicationModel;

namespace AiShot.App;

/// <summary>
/// Liga/desliga o início automático no mecanismo pertencente a cada canal:
/// StartupTask no MSIX e chave Run na instalação Inno/portable.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AiShot";
    private const string StartupTaskId = "AiShotStartup";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static async Task<bool> IsEnabledAsync()
    {
        if (!DistributionChannel.IsStorePackage) return IsRegistryEnabled();

        var task = await StartupTask.GetAsync(StartupTaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    public static async Task SetEnabledAsync(bool enabled)
    {
        if (!DistributionChannel.IsStorePackage)
        {
            SetRegistryEnabled(enabled);
            return;
        }

        var task = await StartupTask.GetAsync(StartupTaskId);
        if (!enabled)
        {
            task.Disable();
            return;
        }

        var state = await task.RequestEnableAsync();
        if (state is not StartupTaskState.Enabled and not StartupTaskState.EnabledByPolicy)
            throw new InvalidOperationException(
                $"O Windows não habilitou a inicialização automática ({state}). " +
                "Ela pode ter sido bloqueada no Gerenciador de Tarefas.");
    }

    private static bool IsRegistryEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var val = key?.GetValue(ValueName) as string;
        return !string.IsNullOrEmpty(val);
    }

    private static void SetRegistryEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key!.SetValue(ValueName, $"\"{ExePath}\"");
        else key!.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
