using System.IO;

namespace Rhino.Events.Storage
{
	public interface IStreamSource
	{
		Stream OpenWrite(string path);
		Stream OpenRead(string path);

		void Flush(Stream stream);
	}
}