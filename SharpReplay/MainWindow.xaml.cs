﻿using Anotar.Log4Net;
using MahApps.Metro.Controls;
using NHotkey.Wpf;
using SharpReplay.UI;
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
        private const string ConfigFile = "./config.yml";

        public RecorderOptions Options { get; set; }
        private Recorder Recorder;

#pragma warning disable CS0067
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore CS0067

        public AudioDevice[] AudioDevices { get; set; }

        public MainWindow()
        {
            if (Environment.GetCommandLineArgs().Contains("--startup"))
                this.Hide();

            InitializeComponent();

            this.DataContext = this;
        }

        private async void Window_Initialized(object sender, EventArgs e)
        {
            Options = RecorderOptions.Load(ConfigFile);
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

        private async void Restart_Click(object sender, RoutedEventArgs e)
        {
            await Recorder.StopAsync();
            await Task.Delay(200);
            await Recorder.StartAsync();
        }

        private async Task SaveReplay()
        {
            var window = new SavingReplayWindow();

            window.Show();
            await Task.Delay(500);

            window.ReplayPath = await Recorder.WriteReplayAsync();
            window.IsSaved = true;

            await Task.Delay(2000);
            window.End();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            await SaveReplay();
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
            Options.Save(ConfigFile);

            HotkeyManager.Current.AddOrReplace("SaveReplay", Options.SaveReplayHotkey, async (_, e) =>
            {
                if (!Recorder.IsRecording)
                    return;

                e.Handled = true;
                await SaveReplay();
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

        private void MetroWindow_Closed(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
