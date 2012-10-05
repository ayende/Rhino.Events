using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino.Events.Data;
using Rhino.Events.Storage;

namespace Rhino.Events
{
	public class Scribe : IDisposable
	{
		readonly PersistedEventsStorage eventsStorage;

		public JsonSerializer Serializer { get; set; }

		public Scribe()
			: this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScribedEvents"))
		{
			
		}

		public Scribe(string dir)
			: this(new FileStreamSource(), dir)
		{

		}

		public Scribe(IStreamSource source, string dir)
		{
			Serializer = new JsonSerializer();
			eventsStorage = new PersistedEventsStorage(source, dir);
		}

		public Task EnqueueEventAsync(string streamId, object @event)
		{
			return eventsStorage.EnqueueAsync(streamId, EventState.Event, GetSerialized(@event));
		}

		public Task EnqueueSnapshotAsync(string streamId, object @event)
		{
			return eventsStorage.EnqueueAsync(streamId, EventState.Snapshot, GetSerialized(@event));
		}

		public Task EnqueueDeleteAsync(string streamId)
		{
			return eventsStorage.EnqueueAsync(streamId, EventState.Delete, null);
		}

		private JObject GetSerialized(object obj)
		{
			if(obj == null)
				return new JObject();
			
			var jTokenWriter = new JTokenWriter();
			Serializer.Serialize(jTokenWriter, obj);
			var jObject = (JObject) jTokenWriter.Token;
			return jObject;
		}


		public void Dispose()
		{
			throw new NotImplementedException();
		}
	}
}