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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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

        public bool LogFFmpegOutput { get; set; }
        public Hotkey SaveReplayHotkey { get; set; } = new Hotkey(Key.P, ModifierKeys.Control | ModifierKeys.Alt);

        [YamlIgnore]
        public string VideoCodec => "h264" + (
            HardwareAcceleration == HardwareAccel.AMD ? "_amf" :
            HardwareAcceleration == HardwareAccel.NVIDIA ? "_nvenc" : "");

        public void Save(string path) => File.WriteAllText(path, Serializer.Serialize(this));

        public static RecorderOptions Load(string path)
        {
            RecorderOptions opt;

            if (!File.Exists(path))
                opt = new RecorderOptions();
            else
                opt = Deserializer.Deserialize<RecorderOptions>(File.ReadAllText(path));

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
        private readonly struct Fragment
        {
            public DateTimeOffset Time { get; }
            public Mp4Box[] Boxes { get; }

            public Fragment(DateTimeOffset time, Mp4Box[] boxes)
            {
                this.Time = time;
                this.Boxes = boxes;
            }
        }

        private readonly static Regex FrameTimeRegex = new Regex(@"(?<=dts=)(\d|\.)*?(?= )");

        public bool IsRecording { get; private set; }

        public RecorderOptions Options { get; set; } = new RecorderOptions();

        private readonly AsyncAutoResetEvent KeyframeEvent = new AsyncAutoResetEvent();

        private Process FFmpeg;
        private NamedPipeServerStream OutputPipe;

        private ContinuousList<Fragment> Fragments;
        private byte[] Mp4Header;
        private List<Mp4Box> Footer;
        private DateTimeOffset LastReportedTime;
        private DateTimeOffset StartTime;

        public Recorder()
        {
        }

        public Recorder(RecorderOptions options)
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

            Fragments = new ContinuousList<Fragment>(12 * Options.Framerate);
            Footer = new List<Mp4Box>();
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

            OutputPipe = new NamedPipeServerStream("ffpipe", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 448 * 1024, 0);

            string audioArgs = Options.AudioDevices.Length == 0 ? "" :
                string.Join(" ", Options.AudioDevices?.Select(o => $@"-f dshow -i audio=""{o}""")) + " "+
                $@"-filter_complex ""{string.Concat(Options.AudioDevices.Select((_, i) => $"[{i + 1}:0]volume = 1[a{i}];"))}{string.Concat(Options.AudioDevices.Select((_, i) => $"[a{i}]"))}amix=inputs={Options.AudioDevices.Length}[a]"" -map 0:v -map ""[a]"" ";

            FFmpeg = new Process();
            FFmpeg.StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg.exe",
                Arguments = $"-f gdigrab -framerate {Options.Framerate} -r {Options.Framerate} -i desktop " + 
                            audioArgs +
                            $"-b:a 128k -g 10 -strict experimental {(Options.LosslessInMemory ? "-crf 0 -preset ultrafast" : "")} -b:v 5M -c:v {Options.VideoCodec} " +
                           $@"-r {Options.Framerate} -f ismv -movflags frag_keyframe -y \\.\pipe\ffpipe",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            LogTo.Debug("Launching FFmpeg with arguments:");
            LogTo.Debug(FFmpeg.StartInfo.Arguments);

            FFmpeg.Start();
            FFmpeg.PriorityClass = ProcessPriorityClass.High;

            LogTo.Debug("Waiting for FFmpeg to connect to pipe");

            new Thread(() =>
            {
                while (!FFmpeg.HasExited)
                {
                    var line = FFmpeg.StandardError.ReadLine();

                    if (line == null)
                        continue;

                    if (Options.LogFFmpegOutput)
                        LogTo.Debug($"[FFMPEG] {line}");

                    if (line.StartsWith("  dts="))
                    {
                        string timeStr = FrameTimeRegex.Match(line).Value;
                        long time = (long)(double.Parse(timeStr, CultureInfo.InvariantCulture) * 1000);

                        LastReportedTime = DateTimeOffset.FromUnixTimeMilliseconds(time);
                        KeyframeEvent.Set();
                    }
                }
            })
            {
                IsBackground = true
            }.Start();

            await OutputPipe.WaitForConnectionAsync();

            FFmpeg.StandardInput.Write("-h");
        }

        public async Task<string> WriteReplayAsync()
        {
            LogTo.Debug("Current time: {0}", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            LogTo.Debug("Last fragment time: {0}", Fragments.Last.Time);

            string outPath = $"./out/{DateTime.Now:yyyyMMdd_hhmmss}.mp4";

            LogTo.Info("Writing replay to \"{0}\"", outPath);

            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            var pipe = new NamedPipeServerStream("outpipe", PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 448 * 1024);

            var curator = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $@"-i \\.\pipe\outpipe -c:v {Options.VideoCodec} -crf {(int)(Options.OutputQuality * 51)} -preset {Options.OutputPreset} -b:a 128k {outPath}",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                }
            };
            curator.Start();

            await pipe.WaitForConnectionAsync();
            await WriteReplayAsync(pipe, false);

            curator.StandardInput.Write("qqqqqqqqqqqqqqq");
            await curator.WaitForExitAsync();

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
            await KeyframeEvent.WaitAsync();

            toStream.Write(Mp4Header, 0, Mp4Header.Length);

            await StopAsync();
            
            var frags = Fragments.Where(o => (Fragments.Last.Time - o.Time).TotalSeconds < Options.MaxReplayLengthSeconds);
            int count = 0;
            foreach (var item in frags)
            {
                foreach (var box in item.Boxes)
                {
                    toStream.Write(box.Data, 0, box.Data.Length);
                }

                count++;
            }

            byte[] footer = Footer.SelectMany(o => o.Data).ToArray();
            toStream.Write(footer, 0, footer.Length);

            if (closeStream)
                toStream.Dispose();

            LogTo.Info("Written {0} fragments", count);
        }

        public async Task StopAsync()
        {
            LogTo.Info("Stopping");
            double fragsPerSecond = Fragments.Count() / (DateTimeOffset.Now - StartTime).TotalSeconds;

            FFmpeg.StandardInput.Write("qqqqqqqqqqqqqqqq");
            await FFmpeg.WaitForExitAsync();

            IsRecording = false;

            FFmpeg.Close();
            FFmpeg.Dispose();
            OutputPipe.Dispose();
            
            GC.Collect();
        }

        private void FragmentsThread()
        {
            byte[] buffer = new byte[OutputPipe.InBufferSize];

            var boxes = new BoxParser(OutputPipe);

            var headerBoxes = boxes.Take(2).ToArray();
            Mp4Header = headerBoxes.SelectMany(o => o.Data).ToArray();

            Mp4Box lastMoof = default;

            StartTime = DateTimeOffset.Now;

            foreach (var box in boxes)
            {
                if (Options.LogFFmpegOutput)
                    LogTo.Debug("Box: " + box.Name);

                if (box.Name == "moof")
                {
                    lastMoof = box;
                }
                else if (box.Name == "mdat")
                {
                    Fragments.Add(new Fragment(LastReportedTime, new[] { lastMoof, box }));
                }
                else
                {
                    Footer.Add(box);
                }
            }
        }
    }
}
