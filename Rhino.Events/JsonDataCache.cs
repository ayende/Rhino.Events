using System;
using Newtonsoft.Json.Linq;

namespace Rhino.Events
{
	public class JsonDataCache : IDisposable
	{
		//private readonly MemoryCache cache = new MemoryCache("events");

		public Tuple<JObject,long> Get(long pos)
		{
			//var o = cache.Get(pos.ToString(CultureInfo.InvariantCulture)) as Tuple<JObject, long>;
			//if(o == null)
				return null;
			//return Tuple.Create((JObject) o.Item1.DeepClone(), o.Item2);
		}

		public void Set(long pos, JObject val, long prev)
		{
			//cache.Set(new CacheItem(pos.ToString(CultureInfo.InvariantCulture), Tuple.Create(val, prev)), new CacheItemPolicy
			//	{
			//		SlidingExpiration = TimeSpan.FromMilliseconds(1),
			//	});
		}
		public void Dispose()
		{
			//cache.Dispose();
		}
	}
}