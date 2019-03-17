using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SharpReplay.UI
{
    /// <summary>
    /// Interaction logic for SavingReplayWindow.xaml
    /// </summary>
    public partial class SavingReplayWindow : Window, INotifyPropertyChanged
    {
        public double StartLeft { get; set; }
        public double EndLeft { get; set; }

        public bool IsSaved { get; set; }
        public bool IsNotSaved => !IsSaved;
        public string ReplayPath { get; set; }

#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

        public SavingReplayWindow()
        {
            InitializeComponent();
            this.Left = 1000000000;

            this.DataContext = this;

            var screen = WpfScreen.GetScreenFrom(this);

            StartLeft = screen.DeviceBounds.Width;
            EndLeft = screen.WorkingArea.Width - this.Width;

            this.Top = screen.WorkingArea.Height - this.Height - 10;
        }

        public void End(bool force = false)
        {
            Task.Run(async () =>
            {
                bool wasOver = IsMouseOver;

                while (IsMouseOver && !force)
                {
                    await Task.Delay(100);
                }

                if (wasOver && !force)
                {
                    await Task.Delay(2000);
                    End();
                }
                else
                {
                    Dispatcher.Invoke(() => ((Storyboard)Resources["CloseAnim"]).Begin(this));
                }
            });
        }

        private void Storyboard_Completed(object sender, EventArgs e)
        {
            this.Close();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(ReplayPath);
            this.End(true);
        }
    }
}
