using System;
using System.Windows.Forms;

namespace LanGameHub;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) =>
            MessageBox.Show("Error: " + e.Exception.Message + "\n\n" + e.Exception.StackTrace, "WLAN Game Hub");
        Application.Run(new MainForm());
    }
}
