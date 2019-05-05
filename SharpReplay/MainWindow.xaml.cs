using Anotar.Log4Net;
using MahApps.Metro.Controls;
using NHotkey.Wpf;
using SharpReplay.Models;
using SharpReplay.Recorders;
using SharpReplay.UI;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace SharpReplay
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow, INotifyPropertyChanged
    {
        private const string ConfigFile = "./config.yml";

        public RecorderOptions Options { get; set; }
        public string Status { get; set; } = "starting up";
        public Brush StatusColor { get; set; } = new SolidColorBrush(Colors.Orange);

        private IRecorder Recorder;

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
            Options = RecorderOptions.Load(ConfigFile, out var exists);

            if (!exists)
            {
                bool amd = await Utils.IsAcceleratorAvailable(RecorderOptions.HardwareAccel.AMD);

                if (amd)
                {
                    Options.HardwareAcceleration = RecorderOptions.HardwareAccel.AMD;
                }
                else
                {
                    bool nvidia = await Utils.IsAcceleratorAvailable(RecorderOptions.HardwareAccel.NVIDIA);

                    if (nvidia)
                    {
                        Options.HardwareAcceleration = RecorderOptions.HardwareAccel.NVIDIA;
                    }
                }

                Options.Save(ConfigFile);
            }

            AudioDevices = (await Utils.GetAudioDevices());

            foreach (var device in AudioDevices)
            {
                device.Enabled = Options.AudioDevices.Contains(device.AltName);
            }

            SaveHotkey.Hotkey = Options.SaveReplayHotkey;

            SaveOptions();

            Recorder = new FFmpegRecorder(Options);
            Recorder.Stopped += this.Recorder_Stopped;
            Recorder.Started += this.Recorder_Started;

            await Recorder.StartAsync();
        }

        private void SetStatus(string text, Color color)
        {
            Dispatcher.Invoke(() =>
            {
                this.Status = text;
                this.StatusColor = new SolidColorBrush(color);
            });
        }

        private void Recorder_Started()
        {
            SetStatus("recording", Colors.Green);
        }

        private async void Recorder_Stopped(bool requested)
        {
            SetStatus("stopped", Colors.Red);

            if (!requested)
            {
                LogTo.Error("Recorder unexpectedly stopped, restarting in 1 second");

                await Task.Delay(1000);
                await Recorder.StartAsync();
            }
        }

        private async void Restart_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("restarting", Colors.Orange);

            await Recorder.StopAsync();
            await Task.Delay(200);
            await Recorder.StartAsync();
        }

        private async Task SaveReplay()
        {
            var window = new SavingReplayWindow();
            window.Show();

            string outPath = $"./out/{DateTime.Now:yyyyMMdd_hhmmss}.mp4";

            if (Recorder is IStreamVideoProvider streamVideo)
            {
                var curator = new Curator(Options, streamVideo);

                if (streamVideo.IsCurationNeeded)
                {
                    await curator.WriteReplayAsync(outPath);
                }
                else
                {
                    using (var file = File.OpenWrite(outPath))
                    {
                        await streamVideo.WriteDataAsync(file);
                    }
                }
            }
            else if (Recorder is IFileVideoProvider fileVideo)
            {
                await fileVideo.WriteDataAsync(outPath);
            }

            window.ReplayPath = Path.GetFullPath(outPath);
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
