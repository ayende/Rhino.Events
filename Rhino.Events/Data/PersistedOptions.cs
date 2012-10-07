using System;
using System.Threading;
using Rhino.Events.Storage;

namespace Rhino.Events.Data
{
	public class PersistedOptions
	{
		public string DirPath { get; set; }
		public IStreamSource StreamSource { get; set; }
		public bool AllowRecovery { get; set; }

		public int WeakMaxSize { get; set; }
		public int HardMaxSize { get; set; }
		public int CheckOncePer { get; set; }

		public TimeSpan MaxTimeToWaitForFlushingToDisk { get; set; }

		public PersistedOptions()
		{
			CheckOncePer = 100;
			HardMaxSize = 10000;
			WeakMaxSize = 2500;
			MaxTimeToWaitForFlushingToDisk = TimeSpan.FromMinutes(3);
		}
	}
}