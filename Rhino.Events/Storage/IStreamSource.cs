using System.IO;

namespace Rhino.Events.Storage
{
	public interface IStreamSource
	{
		Stream OpenReadWrite(string file);
		
		Stream OpenRead(string path);
		
		void DeleteOnClose(string path);
		
		void Flush(Stream stream);

		void DeleteIfExists(string path);

		void RenameToLatest(string newFilePath, string path);

		string GetLatestName(string path);
		bool Exists(string offsetsPath);
	}
}