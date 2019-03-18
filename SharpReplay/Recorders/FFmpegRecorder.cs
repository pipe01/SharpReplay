using Anotar.Log4Net;
using Nito.AsyncEx;
using SharpReplay.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Timers.Timer;

namespace SharpReplay.Recorders
{
    public class FFmpegRecorder : MP4Recorder
    {
        private Process FFmpeg;
        private NamedPipeServerStream FFPipe;

        public FFmpegRecorder(RecorderOptions options) : base(options)
        {
            this.Options = options;
        }

        protected override async Task<Stream> StartStreamAsync()
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
                            $"-b:a 128k -g {Options.Framerate} -c:v {Options.VideoCodec} {(Options.LosslessInMemory ? "-crf 0 -preset ultrafast" : "")} -b:v 5M " +
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

            return FFPipe;
        }

        public override async Task StopAsync()
        {
            LogTo.Info("Stopping");

            FFmpeg.StandardInput.Write("qqqqqqqqqqqq");

            if (!await FFmpeg.WaitForExitAsync(3000))
                FFmpeg.Kill();

            await Task.Delay(300);

            FFmpeg.Dispose();
            FFPipe.Dispose();

            GC.Collect();

            await base.StopAsync();
        }
    }
}
