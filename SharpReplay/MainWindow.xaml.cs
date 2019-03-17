using Anotar.Log4Net;
using MahApps.Metro.Controls;
using NHotkey.Wpf;
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
    public partial class MainWindow : MetroWindow, INotifyPropertyChanged
    {
        public RecorderOptions Options { get; set; }
        private Recorder Recorder;

#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

        public AudioDevice[] AudioDevices { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            this.DataContext = this;
        }

        private async void Window_Initialized(object sender, EventArgs e)
        {
            Options = RecorderOptions.Load("./config.json");
            AudioDevices = (await Utils.GetAudioDevices());

            foreach (var device in AudioDevices)
            {
                device.Enabled = Options.AudioDevices.Contains(device.AltName);
            }

            SaveHotkey.Hotkey = Options.SaveReplayHotkey;

            SaveOptions();

            Recorder = new Recorder(Options);
            await Recorder.StartAsync();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            await Recorder.WriteReplayAsync();
        }

        private async void Restart_Click(object sender, RoutedEventArgs e)
        {
            await Recorder.StopAsync();
            await Task.Delay(200);
            await Recorder.StartAsync();
        }

        private async void Window_Closing(object sender, CancelEventArgs e)
        {
            if (Recorder.IsRecording)
                await Recorder.StopAsync();
        }

        private void MetroWindow_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
        }

        private void TaskbarIcon_TrayLeftMouseDown(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SaveOptions()
        {
            Options.Save("./config.json");

            HotkeyManager.Current.AddOrReplace("SaveReplay", Options.SaveReplayHotkey, async (_, e) =>
            {
                e.Handled = true;
                await Recorder.WriteReplayAsync();
            });
        }

        private void HotkeyEditorControl_HotkeyChanged(object sender, RoutedEventArgs e)
        {
            SaveOptions();
        }

        private void Audio_CheckedChanged(object sender, RoutedEventArgs e)
        {
            Options.AudioDevices = AudioDevices.Where(o => o.Enabled).Select(o => o.AltName).ToArray();

            SaveOptions();
        }
    }
}
