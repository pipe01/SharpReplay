using Anotar.Log4Net;
using Nito.AsyncEx;
using SharpReplay.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpReplay.Recorders
{
    public class FFmpegRecorder : IRecorder
    {
        private const int SegmentInterval = 3;

        public bool IsCurationNeeded => false;
        public RecorderOptions Options { get; set; }
        public bool IsRecording { get; private set; }

        private Process FFmpeg;
        private IDictionary<int, (NamedPipeServerStream Pipe, MemoryStream Stream)> Pipes;
        private readonly AsyncAutoResetEvent SegmentEvent = new AsyncAutoResetEvent();

        private int PipeNumber => (int)Math.Ceiling((float)Options.MaxReplayLengthSeconds / SegmentInterval) + 1;

        public FFmpegRecorder(RecorderOptions options)
        {
            this.Options = options;
        }

        public Task StartAsync()
        {
            Pipes = new Dictionary<int, (NamedPipeServerStream, MemoryStream)>();

            for (int i = 0; i < PipeNumber; i++)
            {
                AddPipe(i);
            }

            FFmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $"-f gdigrab -i desktop -r {Options.Framerate} -c:v {Options.VideoCodec} -b:v 5M " +
                            $"-g {SegmentInterval * Options.Framerate} -flags -global_header -map 0 -crf 0 " +
                            $"-preset ultrafast -f segment -segment_time {SegmentInterval} -segment_format ismv " +
                            $@"-y \\.\pipe\ffpipe%d",
                    RedirectStandardInput = true,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            FFmpeg.Exited += delegate { IsRecording = false; };
            FFmpeg.Start();

            IsRecording = true;
            return Task.CompletedTask;
        }

        private void AddPipe(int index = -1)
        {
            if (index == -1)
                index = Pipes.Keys.Last() + 1;

            var pipe = new NamedPipeServerStream("ffpipe" + index, PipeDirection.In, PipeNumber, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 448 * 1024, 0);
            var mem = new MemoryStream();

            new Thread(o =>
            {
                int pipeIndex = (int)o;

                try
                {
                    pipe.WaitForConnection();
                }
                catch
                {
                    return;
                }

                LogTo.Debug("Writing to pipe index {0}", pipeIndex);
                LogTo.Debug("Pipes: " + string.Join(", ", Pipes.Keys));

                if (pipeIndex == Pipes.Keys.Min() + PipeNumber - 1)
                {
                    LogTo.Debug("Pruning pipes and adding a new one");

                    var oldIndex = Pipes.Keys.Min();
                    Pipes[oldIndex].Stream.Dispose();
                    Pipes.Remove(oldIndex);

                    AddPipe(pipeIndex + 1);

                    LogTo.Debug("Pipes: " + string.Join(", ", Pipes.Keys));
                }

                int read = 0;
                byte[] buffer = new byte[pipe.InBufferSize];

                try
                {
                    while (pipe.IsConnected && (read = pipe.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        mem.Write(buffer, 0, read);
                    }
                }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    LogTo.ErrorException("Exception on pipe " + pipeIndex, ex);
                }
                finally
                {
                    pipe.Close();
                    pipe.Dispose();
                }

                SegmentEvent.Set();
                LogTo.Debug("Thread for pipe index {0} exited", pipeIndex);
            })
            {
                IsBackground = true
            }.Start(index);

            Pipes.Add(index, (pipe, mem));
        }

        public async Task StopAsync(bool discard = false)
        {
            LogTo.Info("Stopping");

            FFmpeg.StandardInput.Write("qqqqqqqqqqqq");

            if (!await FFmpeg.WaitForExitAsync(3000))
                FFmpeg.Kill();

            await Task.Delay(300);

            FFmpeg.Dispose();

            foreach (var pipe in Pipes)
            {
                pipe.Value.Pipe.Dispose();

                if (discard)
                    pipe.Value.Stream.Dispose();
            }

            if (discard)
                Pipes.Clear();

            GC.Collect();
        }

        public async Task WriteDataAsync(Stream output)
        {
            await SegmentEvent.WaitAsync();

            string tempFolder = "./temp";

            Directory.CreateDirectory(tempFolder);

            foreach (var item in Directory.EnumerateFiles(tempFolder))
            {
                File.Delete(item);
            }

            var files = new List<string>();

            LogTo.Debug("Stream lengths: " + string.Join(", ", Pipes.Select(o => o.Value.Stream.Length)));

            foreach (var item in Pipes.Where(o => o.Key < Pipes.Keys.Max())) //Get all segments except the last one
            {
                var path = Path.Combine(tempFolder, $"segment{files.Count}.mp4");
                files.Add(path);

                using (var file = File.OpenWrite(path))
                {
                    item.Value.Stream.Position = 0;

                    await item.Value.Stream.CopyToAsync(file);
                    file.Flush();
                }
            }

            var ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $@"-i ""concat:{string.Join("|", files)}"" -c copy output.mp4",
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ffmpeg.Start();

            await ffmpeg.WaitForExitAsync();
        }
    }
}
