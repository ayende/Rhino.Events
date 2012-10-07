using System.IO;

namespace Rhino.Events.Storage
{
	public interface IStreamSource
	{
		Stream OpenReadWrite(string path);
		
		Stream OpenRead(string path);
		
		void DeleteOnClose(string path);
		
		void Flush(Stream stream);

		void DeleteIfExists(string path);

		void RenameToLatest(string newFilePath, string path);
	}
}