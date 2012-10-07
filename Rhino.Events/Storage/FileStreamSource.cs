using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;

namespace Rhino.Events.Storage
{
	public class FileStreamSource : IStreamSource
	{
		private readonly string basePath;

		private class FlushingToDiskFileStream: FileStream
		{
			public FlushingToDiskFileStream(string path, FileMode mode, FileAccess access, FileShare share) 
				: base(path, mode, access, share)
			{
			}

			public override void Flush()
			{
				Flush(flushToDisk: true);
			}
		}

		public FileStreamSource(string basePath)
		{
			this.basePath = basePath;
			if (Directory.Exists(basePath) == false)
				Directory.CreateDirectory(basePath);
		
		}

		public Stream OpenReadWrite(string file)
		{
			var path = Path.Combine(basePath, file);
			var fileStream = new FlushingToDiskFileStream(LastFileVersion(path), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
			return new BufferedStream( fileStream, 32*1024 );
		}

		public Stream OpenRead(string file)
		{
			var path = Path.Combine(basePath, file);
			return new BufferedStream(File.Open(LastFileVersion(path), FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.ReadWrite));
		}

		public void DeleteOnClose(string file)
		{
			var path = Path.Combine(basePath, file);
		
			using (new FileStream(LastFileVersion(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.DeleteOnClose))
			{
			}
		}

		public void Flush(Stream stream)
		{
			stream.Flush();
		}

		public void DeleteIfExists(string file)
		{
			var path = Path.Combine(basePath, file);
		
			if (File.Exists(path))
				File.Delete(path);
		}

		public void RenameToLatest(string newFile, string file)
		{
			var path = Path.Combine(basePath, file);
			var newFilePath = Path.Combine(basePath, newFile);
		
			var fileVersion = LastFileVersion(path);
			var extension = Path.GetExtension(fileVersion);
			Debug.Assert(extension != null);
			var numeric = extension.Substring(1);
			int lastFileId = int.Parse(numeric);
			var newName = path + "." + (lastFileId + 1).ToString("00000000");

			File.Move(LastFileVersion(newFilePath), newName);
			
			string _;
			pathCache.TryRemove(path, out _);
		}

		public string GetLatestName(string file)
		{
			var path = Path.Combine(basePath, file);
		
			return LastFileVersion(path);
		}

		public bool Exists(string file)
		{
			var path = Path.Combine(basePath, file);
			return File.Exists(path);

		}

		readonly ConcurrentDictionary<string,string> pathCache = new ConcurrentDictionary<string, string>();

		private string LastFileVersion(string path)
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