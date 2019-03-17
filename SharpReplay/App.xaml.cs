using Anotar.Log4Net;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SharpReplay
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;

            LogTo.FatalException("Unhandled exception", exception);

            MessageBox.Show($"{exception.GetType().FullName}: {exception.Message}\n", "Unhandled exception", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
    }
}
