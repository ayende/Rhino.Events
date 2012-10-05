using System.IO;

namespace Rhino.Events.Storage
{
	public class FileStreamSource : IStreamSource
	{
		public Stream OpenWrite(string path)
		{
			var dir = Path.GetDirectoryName(path);
			if (Directory.Exists(dir) == false)
				Directory.CreateDirectory(dir);
			var fileStream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
			fileStream.Seek(0, SeekOrigin.End);
			return fileStream;
		}

		public Stream OpenRead(string path)
		{
			return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.ReadWrite);
		}

		public void Flush(Stream stream)
		{
			((FileStream) stream).Flush(true);
		}
	}
}