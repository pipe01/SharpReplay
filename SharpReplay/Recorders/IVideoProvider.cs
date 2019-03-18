using System.IO;
using System.Threading.Tasks;

namespace SharpReplay.Recorders
{
    public interface IVideoProvider
    {
        Task WriteDataAsync(Stream output);
    }
}
