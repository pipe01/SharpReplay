﻿using Anotar.Log4Net;
using MahApps.Metro.Controls;
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
            AudioDevices = await Utils.GetAudioDevices();
            Options = RecorderOptions.Load("./config.json");

            Recorder = new Recorder(Options);
            await Recorder.StartAsync();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            await Recorder.WriteReplayAsync();
        }

        private async void Restart_Click(object sender, RoutedEventArgs e)
        {
            Recorder.Options.AudioDevices = AudioDevices.Where(o => o.Enabled).Select(o => o.AltName).ToArray();

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
    }
}
