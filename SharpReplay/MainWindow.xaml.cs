﻿using Anotar.Log4Net;
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
            Console.WriteLine("Begin");

            await Recorder.StartAsync();
        }
        
        private async void Stop_Click(object sender, RoutedEventArgs e)
        {
            await Recorder.StopAsync();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists("out.mp4"))
                File.Delete("out.mp4");

            using (var file = File.OpenWrite("out.mp4"))
            {
                await Recorder.WriteReplayAsync(file);
            }
        }
    }
}
