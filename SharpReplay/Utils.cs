using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SharpReplay
{
    public class AudioDevice
    {
        public string PrettyName { get; }
        public string AltName { get; }
        public bool Enabled { get; set; }

        public AudioDevice(string prettyName, string altName)
        {
            this.PrettyName = prettyName;
            this.AltName = altName;
        }
    }

    public static class Utils
    {
        private readonly static Regex IsAudioLineRegex = new Regex(@"^\[.*?\]  ");
        private readonly static Regex AudioNameRegex = new Regex(@"(?<="").*?(?="")");

        public static async Task<AudioDevice[]> GetAudioDevices()
        {
            var ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = "-list_devices true -f dshow -i dummy",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                }
            };

            ffmpeg.Start();
            await ffmpeg.WaitForExitAsync();

            var ret = new List<AudioDevice>();
            string error;

            using (var reader = new StreamReader(ffmpeg.StandardError.BaseStream, Encoding.UTF8))
                error = await reader.ReadToEndAsync();

            string[] lines = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (IsAudioLineRegex.IsMatch(lines[i]))
                {
                    string prettyName = AudioNameRegex.Match(line).Value;
                    string altName = AudioNameRegex.Match(lines[i + 1]).Value;

                    ret.Add(new AudioDevice(prettyName, altName));
                    i++;
                }
            }

            return ret.ToArray();
        }
    }
}
