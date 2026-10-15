using System;
using System.Threading;
using System.Windows;

namespace AutoClicker
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "AutoClicker.SingleInstance.6b1f", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("AutoClicker ya está abierto.", "AutoClicker",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    MessageBox.Show(e.Exception.ToString(), "AutoClicker — error inesperado",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };

                MainController controller = new MainController();
                app.Run(controller.Window);
            }
        }
    }
}
