using System.IO;
using System.Threading.Tasks;

namespace SharpReplay.Recorders
{
    public interface IStreamVideoProvider
    {
        bool IsCurationNeeded { get; }

        Task WriteDataAsync(Stream stream);
    }

    public interface IFileVideoProvider
    {
        Task WriteDataAsync(string fileName);
    }
}
