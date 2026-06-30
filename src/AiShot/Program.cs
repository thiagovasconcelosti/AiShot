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
        Application.Run(new TrayAppContext());
    }
}
