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
