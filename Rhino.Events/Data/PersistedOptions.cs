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

		public int CacheWeakMaxSize { get; set; }
		public int CacheHardMaxSize { get; set; }
		public int ClearCacheAfterSetCalledTimes { get; set; }

		public TimeSpan IdleTime { get; set; }
		
		public TimeSpan MaxTimeToWaitForFlush { get; set; }

		public int WritesBetweenOffsetSnapshots { get; set; }
		public TimeSpan MinTimeForOffsetSnapshots { get; set; }

		public PersistedOptions()
		{
			ClearCacheAfterSetCalledTimes = 1000;
			CacheHardMaxSize = 100000;
			CacheWeakMaxSize = 25000;
			MaxTimeToWaitForFlush = TimeSpan.FromMilliseconds(200);
			IdleTime = TimeSpan.FromMinutes(3);
			WritesBetweenOffsetSnapshots = 15000;
			MinTimeForOffsetSnapshots = TimeSpan.FromMinutes(3);
		}
	}
}