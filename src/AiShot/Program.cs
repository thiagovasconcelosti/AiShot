using System.Windows.Forms;
using AiShot.App;

namespace AiShot;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Modo de verificação de UI: abre só a tela de Configurações.
        if (Environment.GetCommandLineArgs().Contains("--settings"))
        {
            Application.ThreadException += (_, e) => ShowFatal(e.Exception);
            Application.Run(new Settings.SettingsForm(Config.AppConfig.Load()));
            return;
        }

        // Instância única: duas cópias registrariam dois hooks globais na mesma
        // tecla e dois ícones na bandeja. A que já roda é trazida ao foco.
        using var instance = SingleInstance.TryAcquire();
        if (instance is null) return;

        // Rede de segurança para exceções não tratadas (async void, threads).
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

        Application.Run(new TrayAppContext());
    }

    private static void ShowFatal(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            MessageBox.Show(ex.Message, "AiShot — erro inesperado",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { /* nada a fazer */ }
    }
}
