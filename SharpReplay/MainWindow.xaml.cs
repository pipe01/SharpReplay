using ScreenRecorderLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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

            var cont = new ContinuousStream(3, 3);
            var data = Enumerable.Range(0, 50).Select(o => (byte)(o + 1)).ToArray();

            cont.Write(data, 0, data.Length);

            var mem = new MemoryStream();
            cont.CopyTo(mem);

            var a = mem.ToArray();
        }

        private Recorder Rec;
        private ContinuousStream RecStream;

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists("out.mp4"))
                File.Delete("out.mp4");

            RecStream?.Dispose();
            RecStream = new ContinuousStream(100, 1 * 1024 * 1024);

            Rec = Recorder.CreateRecorder(new RecorderOptions
            {
                VideoOptions = new VideoOptions
                {
                    BitrateMode = BitrateControlMode.Quality,
                    Bitrate = 8000 * 1000,
                    Framerate = 60,
                    Quality = 70,
                    IsMousePointerEnabled = false,
                    IsFixedFramerate = true,
                    EncoderProfile = H264Profile.Main
                },
                AudioOptions = new AudioOptions
                {
                    Bitrate = AudioBitrate.bitrate_128kbps,
                    Channels = AudioChannels.Stereo,
                    IsAudioEnabled = true
                },
                IsLowLatencyEnabled = true,
                IsFragmentedMp4Enabled = true,
                //IsMp4FastStartEnabled = true,
                RecorderMode = RecorderMode.Video
            });
            Rec.Record(RecStream);
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            Rec.Stop();

            RecStream.CopyTo(File.OpenWrite("out.mp4"));
        }
    }
}
