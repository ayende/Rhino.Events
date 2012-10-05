using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Linq;
using Rhino.Events.Data;

namespace Rhino.Events.Impl
{
	public class JsonDataCache<T> : IDisposable
		where T : class
	{
		private readonly PersistedOptions options;
		private ConcurrentDictionary<long, CacheData> cache = new ConcurrentDictionary<long, CacheData>();

		public JsonDataCache(PersistedOptions options)
		{
			this.options = options;
		}

		private int sets;

		private class CacheData
		{
			public T Data;
			public WeakReference Weak;
			public int Usage;
		}

		public T Get(long pos)
		{
			CacheData data;
			if(cache.TryGetValue(pos, out data) == false)
				return null;
			Interlocked.Increment(ref data.Usage);
			var result = data.Data ?? (T) data.Weak.Target;
			if(result == null)
				cache.TryRemove(pos, out data);
			return result;
		}

		public void Set(long pos, T val)
		{
			var cacheData = new CacheData
				{
					Data = val,
					Usage = 1,
					Weak = new WeakReference(val)
				};
			cache.AddOrUpdate(pos, cacheData, (l, data) => cacheData);


			var currentSet = Interlocked.Increment(ref sets);
			if (cache.Count <= options.WeakMaxSize || currentSet% options.CheckOncePer!= 0)
				return;

			// release the strong references to them, but keep the weak ones
			foreach (var source in cache.Where(x => x.Value.Data != null).OrderBy(x => x.Value.Usage).Take(cache.Count / 2))
			{
				source.Value.Data = null;
			}

			if(cache.Count <= options.HardMaxSize)
				return;

			foreach (var source in cache.OrderBy(x => x.Value.Usage).Take(cache.Count / 4))
			{
				CacheData data;
				cache.TryRemove(source.Key, out data);
			}
		}

		public void Dispose()
		{
		}
	}
}