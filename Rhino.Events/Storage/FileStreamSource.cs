using System.Collections.Concurrent;
using System.IO;
using System.Linq;

namespace Rhino.Events.Storage
{
	public class FileStreamSource : IStreamSource
	{
		public Stream OpenReadWrite(string path)
		{
			var dir = Path.GetDirectoryName(path);
			if (Directory.Exists(dir) == false)
				Directory.CreateDirectory(dir);
			return new FileStream(LastFileVersion(path), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
		}

		public Stream OpenRead(string path)
		{
			return File.Open(LastFileVersion(path), FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.ReadWrite);
		}

		public void DeleteOnClose(string path)
		{
			using (new FileStream(LastFileVersion(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.DeleteOnClose))
			{
			}
		}

		public void Flush(Stream stream)
		{
			((FileStream)stream).Flush(true);
		}

		public void DeleteIfExists(string path)
		{
			if (File.Exists(path))
				File.Delete(path);
		}

		public void RenameToLatest(string newFilePath, string path)
		{
			var fileVersion = LastFileVersion(path);
			var numeric = Path.GetExtension(fileVersion).Substring(1);
			int lastFileId = int.Parse(numeric);
			var newName = path + "." + (lastFileId + 1).ToString("00000000");

			File.Move(LastFileVersion(newFilePath), newName);
			
			string _;
			pathCache.TryRemove(path, out _);
		}

		readonly ConcurrentDictionary<string,string> pathCache = new ConcurrentDictionary<string, string>(); 

		public string LastFileVersion(string path)
		{
			return pathCache.GetOrAdd(path, s =>
				{
					var lastFileVersion =
						Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".*")
							.OrderByDescending(file =>
								{
									var extension = Path.GetExtension(file);
									if (string.IsNullOrWhiteSpace(extension))
										return -2;

									int result;
									if (int.TryParse(extension.Substring(1), out result) == false)
										return -1;
									return result;
								})
							.FirstOrDefault();

					if (lastFileVersion == null)
						lastFileVersion = path + ".00000001";
					return lastFileVersion;
				});
		}
	}
}