using System;
using System.Collections.Generic;
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

		public Func<string, Type> FindClrType { get; set; }

		public Scribe()
			: this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScribedEvents"))
		{

		}

		public Scribe(string dir)
			: this(new PersistedOptions
				{
					AllowRecovery = true,
					StreamSource = new FileStreamSource(),
					DirPath = dir
				})
		{

		}

		public Scribe(PersistedOptions options)
		{
			Serializer = new JsonSerializer();
			FindClrType = s => Type.GetType(s, throwOnError: true);
			eventsStorage = new PersistedEventsStorage(options);
		}

		public IEnumerable<object> ReadRaw(string streamId, bool untilLastSnapshot = true)
		{
			var eventDatas = eventsStorage.Read(streamId);
			if (eventDatas == null)
				yield break;

			foreach (var data in eventDatas)
			{
				var typeString = data.Metadata.Value<string>("Clr-Type");
				var clrType = FindClrType(typeString);
				yield return Serializer.Deserialize(new JTokenReader(data.Data), clrType);
				if (data.State.HasFlag(EventState.Snapshot) && untilLastSnapshot)
				{
					yield break;
				}
			}
		}

		public Task EnqueueEventAsync(string streamId, object @event)
		{
			return eventsStorage.EnqueueAsync(streamId, EventState.Event, GetSerialized(@event), GetMetadata(@event));
		}

		private JObject GetMetadata(object @event)
		{
			var assemblyQualifiedName = @event.GetType().AssemblyQualifiedName;
			return new JObject
				{
					{"Clr-Type", assemblyQualifiedName}
				};
		}

		public Task EnqueueSnapshotAsync(string streamId, object @event)
		{
			return eventsStorage.EnqueueAsync(streamId, EventState.Snapshot, GetSerialized(@event), GetMetadata(@event));
		}

		public Task EnqueueDeleteAsync(string streamId)
		{
			return eventsStorage.EnqueueAsync(streamId, EventState.Delete, new JObject(), new JObject());
		}

		private JObject GetSerialized(object obj)
		{
			if (obj == null)
				return new JObject();

			var jTokenWriter = new JTokenWriter();
			Serializer.Serialize(jTokenWriter, obj);
			var jObject = (JObject)jTokenWriter.Token;
			return jObject;
		}


		public void Dispose()
		{
			eventsStorage.Dispose();
		}
	}
}