using Anotar.Log4Net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SharpReplay
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Recorder Recorder = new Recorder();

        public event PropertyChangedEventHandler PropertyChanged;

        public AudioDevice[] AudioDevices { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            this.DataContext = this;
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            Recorder.Options.AudioDevices = AudioDevices.Where(o => o.Enabled).Select(o => o.AltName).ToArray();
            await Recorder.StartAsync();
        }
        
        private async void Stop_Click(object sender, RoutedEventArgs e)
        {
            await Recorder.StopAsync();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            await Recorder.WriteReplayAsync();
        }

        private async void Window_Initialized(object sender, EventArgs e)
        {
            AudioDevices = await Utils.GetAudioDevices();
        }
    }
}
