using Anotar.Log4Net;
using Nito.AsyncEx;
using SharpReplay.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Timer = System.Timers.Timer;

namespace SharpReplay
{
    public class RecorderOptions
    {
        public enum HardwareAccel
        {
            None,
            AMD,
            NVIDIA
        }

        private readonly static IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .Build();

        private static readonly ISerializer Serializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeInspector(inner => new CommentGatheringTypeInspector(inner))
            .WithEmissionPhaseObjectGraphVisitor(args => new CommentsObjectGraphVisitor(args.InnerVisitor))
            .EmitDefaults()
            .Build();

        private const string H264Presets = "ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow";


        public int MaxReplayLengthSeconds { get; set; } = 15;
        public int Framerate { get; set; } = 60;
        public string[] AudioDevices { get; set; } = new string[0];
        public HardwareAccel HardwareAcceleration { get; set; }
        [Description("If disabled this compresses captured video on memory, trading reduced memory usage for more CPU usage")]
        public bool LosslessInMemory { get; set; } = true;

        [Description("100 means lossless image, 0 means there's barely any video")]
        public double OutputQuality { get; set; } = 50;
        [Description("H.264 preset. From worst to best quality: " + H264Presets)]
        public string OutputPreset { get; set; } = "slow";
        public int OutputBitrateMegabytes { get; set; } = 5;

        public bool LogFFmpegOutput { get; set; }
        public Hotkey SaveReplayHotkey { get; set; } = new Hotkey(Key.P, ModifierKeys.Control | ModifierKeys.Alt);

        [YamlIgnore]
        public string VideoCodec => "h264_" + HardwareAcceleration.GetH264Suffix();

        public void Save(string path) => File.WriteAllText(path, Serializer.Serialize(this));

        public static RecorderOptions Load(string path, out bool exists)
        {
            RecorderOptions opt;

            if (!File.Exists(path))
            {
                opt = new RecorderOptions();
                exists = false;
            }
            else
            {
                opt = Deserializer.Deserialize<RecorderOptions>(File.ReadAllText(path));
                exists = true;
            }

            if (opt.OutputQuality < 0 || opt.OutputQuality > 100)
                opt.OutputQuality = 50;

            if (!H264Presets.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Contains(opt.OutputPreset))
                opt.OutputPreset = "slow";

            opt.Save(path);

            return opt;
        }
    }

    public class Recorder
    {
        private class Fragment
        {
            public DateTimeOffset Time { get; }
            public Mp4Box[] Boxes { get; }

            public Fragment(DateTimeOffset time, Mp4Box[] boxes)
            {
                this.Time = time;
                this.Boxes = boxes;
            }
        }

        private class RecState
        {
            public int AudioTrackID, VideoTrackID;
        }

        public bool IsRecording { get; private set; }

        public RecorderOptions Options { get; set; } = new RecorderOptions();

        private readonly AsyncManualResetEvent FragmentEvent = new AsyncManualResetEvent();

        private Process FFmpeg;
        private NamedPipeServerStream FFPipe;

        private ContinuousList<Fragment> Fragments;
        private byte[] Mp4Header;
        private RecState State;

        private int FragmentCounter;
        private readonly Timer FragmentTimer;

        public Recorder()
        {
            this.FragmentTimer = new Timer(5000);
            this.FragmentTimer.Elapsed += this.FragmentTimer_Elapsed;
            this.FragmentTimer.Start();
        }

        public Recorder(RecorderOptions options) : this()
        {
            this.Options = options;
        }

        public async Task StartAsync()
        {
            if (IsRecording)
            {
                LogTo.Error("Tried to start recording while already recording");
                throw new InvalidOperationException("Already recording");
            }

            LogTo.Info("Start recording");

            Fragments = new ContinuousList<Fragment>(10);
            State = new RecState();
            Mp4Header = null;

            await StartPipeAndProcess();

            var thread = new Thread(FragmentsThread)
            {
                IsBackground = true,
                Name = "RecorderThread"
            };

            thread.Start();

            LogTo.Debug("Thread started with ID {0}", thread.ManagedThreadId);

            IsRecording = true;
        }

        private async Task StartPipeAndProcess()
        {
            LogTo.Debug("Creating pipe");

            FFPipe = new NamedPipeServerStream("ffpipe", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 448 * 1024, 0);

            string audioArgs = Options.AudioDevices.Length == 0 ? "" :
                string.Join(" ", Options.AudioDevices?.Select(o => $@"-f dshow -i audio=""{o}""")) +
                $@" -filter_complex ""{string.Concat(Options.AudioDevices.Select((_, i) => $"[{i + 1}:0]volume = 1[a{i}];"))}{string.Concat(Options.AudioDevices.Select((_, i) => $"[a{i}]"))}amix=inputs={Options.AudioDevices.Length}[a]"" -map ""[a]"" ";

            FFmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $"-framerate {Options.Framerate} -f gdigrab -i desktop " + audioArgs +
                            $"-b:a 128k -g {DetermineGOP()} -c:v {Options.VideoCodec} {(Options.LosslessInMemory ? "-crf 0 -preset ultrafast" : "")} -b:v 5M " +
                           $@"-r {Options.Framerate} -map 0:v -probesize 10M -f ismv -movflags frag_keyframe -y \\.\pipe\ffpipe",
                    RedirectStandardInput = true,
                    RedirectStandardError = Options.LogFFmpegOutput,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            FFmpeg.Exited += (_, __) => LogTo.Debug("FFmpeg exited");

            LogTo.Debug("Launching FFmpeg with arguments:");
            LogTo.Debug(FFmpeg.StartInfo.Arguments);

            FFmpeg.Start();
            FFmpeg.PriorityClass = ProcessPriorityClass.High;

            LogTo.Debug("Waiting for FFmpeg to connect to pipe");

            if (Options.LogFFmpegOutput)
            {
                new Thread(() =>
                {
                    while (!FFmpeg.HasExited)
                    {
                        var line = FFmpeg.StandardError.ReadLine();

                        if (line != null)
                            LogTo.Debug($"[FFMPEG] {line}");
                    }
                })
                {
                    IsBackground = true
                }.Start();
            }

            await FFPipe.WaitForConnectionAsync();
        }

        private int DetermineGOP()
        {
            return Options.Framerate;
        }

        public async Task<string> WriteReplayAsync()
        {
            string outPath = $"./out/{DateTime.Now:yyyyMMdd_hhmmss}.mp4";

            LogTo.Info("Writing replay to \"{0}\"", outPath);

            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            var pipe = new NamedPipeServerStream("outpipe", PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 448 * 1024);

            var curator = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $@"-r {Options.Framerate} -i \\.\pipe\outpipe -c:v {Options.VideoCodec} -crf {(int)(51 - (Options.OutputQuality / 100 * 51))} -preset {Options.OutputPreset} -b:a 128k -b:v {Options.OutputBitrateMegabytes}M {outPath}",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                }
            };
            curator.Exited += (_, __) => LogTo.Debug("Curator FFmpeg exited");

            LogTo.Debug("Launching FFmpeg with arguments:");
            LogTo.Debug(curator.StartInfo.Arguments);

            curator.Start();

            await pipe.WaitForConnectionAsync();
            await WriteReplayAsync(pipe, false);

            curator.StandardInput.Write("qqqqqqqqqqqqqqq");

            if (!await curator.WaitForExitAsync(3000))
                curator.Kill();

            pipe.Dispose();

            LogTo.Info("Done writing");
            await StartAsync();

            return Path.GetFullPath(outPath);
        }

        public async Task WriteReplayAsync(Stream toStream, bool closeStream = true)
        {
            if (!IsRecording)
                throw new InvalidOperationException("Not recording");

            LogTo.Info("Writing replay");

            LogTo.Debug("Waiting for one more fragment");
            FragmentEvent.Reset();
            await FragmentEvent.WaitAsync();

            LogTo.Debug("Current time: {0}", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            LogTo.Debug("Last fragment time: {0}", Fragments.Last.Time.ToUnixTimeMilliseconds());

            LogTo.Debug("Waiting for FFmpeg to exit");

            await StopAsync();

            LogTo.Debug("Writing fragments");

            await toStream.WriteAsync(Mp4Header, 0, Mp4Header.Length);

            var frags = Fragments.Where(o => (Fragments.Last.Time - o.Time).TotalSeconds < Options.MaxReplayLengthSeconds);
            int count = 0;
            foreach (var item in frags)
            {
                foreach (var box in item.Boxes)
                {
                    await toStream.WriteAsync(box.Data, 0, box.Data.Length);
                }

                count++;
            }

            if (closeStream)
                toStream.Dispose();

            LogTo.Info("Written {0} fragments", count);
        }

        public async Task StopAsync()
        {
            LogTo.Info("Stopping");

            FFmpeg.StandardInput.Write("qqqqqqqqqqqq");

            if (!await FFmpeg.WaitForExitAsync(3000))
                FFmpeg.Kill();

            await Task.Delay(300);

            IsRecording = false;

            FFmpeg.Dispose();
            FFPipe.Dispose();

            GC.Collect();
        }

        private void FragmentTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!IsRecording)
                return;

            double fragmentsPerSecond = FragmentCounter / (FragmentTimer.Interval / 1000);
            FragmentCounter = 0;

            LogTo.Debug("Fragments per second: {0}", fragmentsPerSecond);

            int totalFragmentsNeeded = (int)Math.Ceiling(fragmentsPerSecond * (Options.MaxReplayLengthSeconds + 2));

            if (totalFragmentsNeeded > Fragments.Capacity)
            {
                LogTo.Debug("Resizing fragments buffer to {0} in order to to fit {1} frags/s", totalFragmentsNeeded, fragmentsPerSecond);
                Fragments.SetCapacity(totalFragmentsNeeded);
            }
        }

        private void FragmentsThread()
        {
            byte[] buffer = new byte[FFPipe.InBufferSize];

            var boxes = new BoxParser(FFPipe);

            var headerBoxes = boxes.Take(2).ToArray();
            Mp4Header = headerBoxes.SelectMany(o => o.Data).ToArray();

            foreach (var trak in headerBoxes[1].Children.Where(o => o.Name == "trak"))
            {
                var hdlr = trak["mdia"]["hdlr"];
                int trackId = trak["tkhd"].Data.ToInt32BigEndian(58);

                if (hdlr.Data.Contains(Encoding.UTF8.GetBytes("SoundHandler")))
                    State.AudioTrackID = trackId;
                else if (hdlr.Data.Contains(Encoding.UTF8.GetBytes("VideoHandler")))
                    State.VideoTrackID = trackId;
                else
                    throw new InvalidDataException("Invalid TRAK box found: " + BitConverter.ToString(trak.Data));
            }

            Mp4Box[] fragBoxes = new Mp4Box[Options.AudioDevices.Length > 0 ? 4 : 2];
            int boxCounter = 0;

            foreach (var box in boxes)
            {
                //if (Options.LogFFmpegOutput)
                //    LogTo.Debug("Box: " + box.Name);

                if (box.Name == "moof" || box.Name == "mdat")
                {
                    fragBoxes[boxCounter++] = box;

                    if (boxCounter == fragBoxes.Length)
                    {
                        boxCounter = 0;

                        Fragments.Add(new Fragment(DateTimeOffset.Now, fragBoxes));
                        FragmentCounter++;
                        FragmentEvent.Set();

                        fragBoxes = new Mp4Box[fragBoxes.Length];

                        LogTo.Debug("New fragment");
                    }
                }
            }
        }
    }
}
