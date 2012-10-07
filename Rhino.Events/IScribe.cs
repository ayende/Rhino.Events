using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rhino.Events
{
	public interface IScribe : IDisposable
	{
		IEnumerable<object> ReadRaw(string streamId, bool untilLastSnapshot = true);
		Task EnqueueEventAsync(string streamId, object @event);
		Task EnqueueSnapshotAsync(string streamId, object @event);
		Task EnqueueDeleteAsync(string streamId);
	}
}