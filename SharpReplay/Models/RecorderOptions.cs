using SharpReplay.UI;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SharpReplay.Models
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
        public string VideoCodec => "h264" + HardwareAcceleration.GetH264Suffix();

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
}
