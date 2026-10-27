using Akagi.Graphs;
using System.Runtime.InteropServices;
using System.Windows;

namespace Akagi.CharacterEditor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (GraphCli.IsCliInvocation(e.Args))
            {
                // Headless: never create a window, report through stdout/stderr and the exit code.
                GraphCli cli = new(Console.Out, Console.Error);
                int exitCode = Task.Run(() => cli.RunAsync(e.Args)).GetAwaiter().GetResult();
                Console.Out.Flush();
                Console.Error.Flush();
                Shutdown(exitCode);
                return;
            }

            HideOwnConsole();

            MainWindow window = new();
            MainWindow = window;
            window.Show();
        }

        /// <summary>
        /// The editor is a console application so headless runs block the shell and return exit codes.
        /// When started from Explorer it gets a console of its own, which is closed again for the GUI.
        /// </summary>
        private static void HideOwnConsole()
        {
            try
            {
                uint[] processes = new uint[2];
                if (GetConsoleProcessList(processes, (uint)processes.Length) == 1)
                {
                    FreeConsole();
                }
            }
            catch
            {
                // No console available
            }
        }
    }

}
