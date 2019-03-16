using Anotar.Log4Net;
using System;
using System.Collections.Generic;
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
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private Recorder Recorder = new Recorder();

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            Recorder.RecordAudio = true;
            Recorder.AudioDevice = (string)Audio.SelectedValue;

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
            Audio.ItemsSource = await Utils.GetAudioDevices();
            Audio.SelectedIndex = 0;
        }
    }
}
