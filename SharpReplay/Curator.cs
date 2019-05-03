using Anotar.Log4Net;
using SharpReplay.Models;
using SharpReplay.Recorders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading.Tasks;

namespace SharpReplay
{
    public class Curator
    {
        private readonly RecorderOptions Options;
        private readonly IStreamVideoProvider VideoProvider;

        public Curator(RecorderOptions options, IStreamVideoProvider videoProvider)
        {
            this.Options = options;
            this.VideoProvider = videoProvider;
        }

        public async Task WriteReplayAsync(string outPath)
        {
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
            await VideoProvider.WriteDataAsync(pipe);

            curator.StandardInput.Write("qqqqqqqqqqqqqqq");

            if (!await curator.WaitForExitAsync(3000))
                curator.Kill();

            pipe.Dispose();
            curator.Dispose();

            LogTo.Info("Done writing");
        }
    }
}
