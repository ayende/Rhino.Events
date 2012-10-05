using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using System.Linq;
using Raven.Abstractions.Logging;
using Rhino.Events.Data;
using Rhino.Events.Impl;

namespace Rhino.Events.Storage
{
	public class PersistedEventsStorage : IDisposable
	{
		private static ILog log = LogManager.GetCurrentClassLogger();

		private readonly PersistedOptions options;
		private const long DoesNotExists = -1;
		private const long Deleted = -2;

		private readonly IStreamSource streamSource;
		private readonly string path;
		private readonly Stream file;
		private readonly Task writerThread;
		private readonly ManualResetEventSlim hasItems = new ManualResetEventSlim(false);
		private readonly ConcurrentQueue<WriteState> writer = new ConcurrentQueue<WriteState>();
		private readonly CancellationTokenSource cts = new CancellationTokenSource();
		private readonly JsonDataCache<PersistedEvent> cache;
		private readonly ConcurrentDictionary<string, StreamInformation> idToPos = new ConcurrentDictionary<string, StreamInformation>(StringComparer.InvariantCultureIgnoreCase);
		private readonly BinaryWriter binaryWriter;

		private long eventsCount;
		private long deleteCount;

		private DateTime lastWrite;
		bool hadWrites;

		private volatile bool disposed;
		private volatile Exception corruptingException;

		private class WriteState
		{
			public readonly TaskCompletionSource<object> TaskCompletionSource = new TaskCompletionSource<object>();
			public string Id;
			public JObject Data;
			public EventState State;

			public JObject Metadata;
		}

		public PersistedEventsStorage(PersistedOptions options)
		{
			cache = new JsonDataCache<PersistedEvent>(options);
			this.options = options;
			streamSource = options.StreamSource;
			path = Path.Combine(options.DirPath, "data.events");
			file = streamSource.OpenReadWrite(path);

			ReadAllFromDisk();

			MaxDurationForFlush = TimeSpan.FromMilliseconds(200);

			binaryWriter = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);

			writerThread = Task.Factory.StartNew(() =>
				{
					try
					{
						WriteToDisk();
					}
					catch (Exception e)
					{
						corruptingException = e;
					}
				});
		}

		private void ReadAllFromDisk()
		{
			while (true)
			{
				using (var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true))
				{
					var itemPos = file.Position;
					PersistedEvent persistedEvent;
					try
					{
						persistedEvent = ReadPersistedEvent(reader);
						eventsCount++;
					}
					catch (EndOfStreamException)
					{
						return;
					}
					catch (Exception e)
					{
						log.WarnException("Error during initial events read, file corrupted", e);
						if (options.AllowRecovery == false)
							throw;
						file.SetLength(itemPos);
						log.Warn("Recovered from corrupted file by truncating file to last known good position: " + itemPos);
						return;
					}
					switch (persistedEvent.State)
					{
						case EventState.Event:
						case EventState.Snapshot:
							idToPos.AddOrUpdate(persistedEvent.Id, s => new StreamInformation
								{
									LastPosition = itemPos,
									StreamLength = 1
								}, (s, information) => new StreamInformation
									{
										LastPosition = itemPos,
										StreamLength = information.StreamLength + 1
									});
							break;
						case EventState.Delete:
							var streamInformation = new StreamInformation
								{
									LastPosition = Deleted,
									StreamLength = 0
								};
							idToPos.AddOrUpdate(persistedEvent.Id, s => streamInformation, (s, information) =>
								{
									deleteCount += information.StreamLength;
									return streamInformation;
								});
							break;
						default:
							throw new ArgumentOutOfRangeException(persistedEvent.State.ToString());
					}

				}
			}

		}

		public IEnumerable<EventData> Read(string id)
		{
			AssertValidState();

			StreamInformation previous;
			if (idToPos.TryGetValue(id, out previous) == false)
				return null;

			return ReadInternal(previous.LastPosition).Select(x => new EventData
				{
					Data = x.Data,
					State = x.State,
					Metadata = x.Metadata
				});
		}

		private void AssertValidState()
		{
			var exception = corruptingException;
			if (exception != null)
				throw new InvalidOperationException("Instance state corrupted, can't read", exception);

			if (disposed)
				throw new ObjectDisposedException("PersistedEventsStorage");
		}

		private IEnumerable<PersistedEvent> ReadInternal(long previous)
		{
			foreach (var p in ReadFromCache(previous))
			{
				previous = p.Previous;
				yield return p;
			}

			using (var stream = streamSource.OpenRead(path))
			using (var reader = new BinaryReader(stream))
			{
				while (true)
				{
					foreach (var p in ReadFromCache(previous))
					{
						previous = p.Previous;
						yield return p;
					}

					if (previous == DoesNotExists || previous == Deleted)
						yield break;

					var itemPos = previous;
					stream.Position = previous;
					var persistedEvent = ReadPersistedEvent(reader);
					previous = persistedEvent.Previous;
					cache.Set(itemPos, persistedEvent);
					yield return persistedEvent;
				}
			}
		}

		private void WriteItem(WriteState item, long prevPos, StreamInformation info)
		{
			binaryWriter.Write(item.Id);
			binaryWriter.Write(prevPos);
			binaryWriter.Write(info == null ? 1 : info.StreamLength);
			binaryWriter.Write((int)item.State);

			item.Metadata.WriteTo(new BsonWriter(binaryWriter));
			item.Data.WriteTo(new BsonWriter(binaryWriter));
		}

		private static PersistedEvent ReadPersistedEvent(BinaryReader reader)
		{
			var id = reader.ReadString();
			long previous = reader.ReadInt64();
			var len = reader.ReadInt32();
			var state = (EventState)reader.ReadInt32();
			var metadata = (JObject)JToken.ReadFrom(new BsonReader(reader));
			var data = (JObject)JToken.ReadFrom(new BsonReader(reader));
			return new PersistedEvent
				{
					Id = id,
					Data = data,
					Metadata = metadata,
					Previous = previous,
					State = state,
					StreamLength = len
				};
		}

		private IEnumerable<PersistedEvent> ReadFromCache(long previous)
		{
			if (previous == DoesNotExists || previous == Deleted)
				yield break;

			var persistedEvent = cache.Get(previous);
			while (persistedEvent != null)
			{
				if (previous == DoesNotExists || previous == Deleted)
					yield break;

				previous = persistedEvent.Previous;

				yield return persistedEvent;

				persistedEvent = cache.Get(previous);
			}
		}

		private void WriteToDisk()
		{
			var tasksToNotify = new List<Action<Exception>>();
			while (true)
			{
				if (cts.IsCancellationRequested)
				{
					FlushToDisk(tasksToNotify);
					return;
				}

				WriteState item;
				if (hadWrites)
				{
					if ((DateTime.UtcNow - lastWrite) > MaxDurationForFlush)
					{
						// we have to flush to disk now, because we have writes and nothing else is forthcoming
						// or we have so many writes, that we need to flush to clear the buffers

						FlushToDisk(tasksToNotify);
						continue;
					}
				}
				if (writer.TryDequeue(out item) == false)
				{
					if (hadWrites)
					{
						FlushToDisk(tasksToNotify);
					}
					else
					{
						hasItems.Wait(cts.Token);
						hasItems.Reset();
					}
					continue;
				}

				StreamInformation info;
				idToPos.TryGetValue(item.Id, out info);

				var prevPos = info == null ? DoesNotExists : info.LastPosition;
				var currentPos = file.Position;
				WriteItem(item, prevPos, info);

				cache.Set(currentPos, new PersistedEvent
					{
						Id = item.Id,
						Data = item.Data,
						State = item.State,
						Previous = prevPos,
						Metadata = item.Metadata
					});

				var action = CreateCompletionAction(tasksToNotify, item, currentPos);
				tasksToNotify.Add(action);

				hadWrites = true;
			}
		}


		private Action<Exception> CreateCompletionAction(List<Action<Exception>> tasksToNotify, WriteState item, long currentPos)
		{
			return exception =>
				{
					if (exception == null)
					{
						HandleFlushSuccessful(item, currentPos);
					}
					else
					{
						item.TaskCompletionSource.SetException(exception);
					}
				};
		}

		private void HandleFlushSuccessful(WriteState item, long currentPos)
		{
			switch (item.State)
			{
				case EventState.Event:
				case EventState.Snapshot:
					idToPos.AddOrUpdate(item.Id, s => new StreamInformation
						{
							LastPosition = currentPos,
							StreamLength = 1
						}, (s, l) => new StreamInformation
							{
								LastPosition = currentPos,
								StreamLength = l.StreamLength + 1
							});
					break;
				case EventState.Delete:
					var streamInformation = new StreamInformation
						{
							LastPosition = Deleted,
							StreamLength = 0
						};
					idToPos.AddOrUpdate(item.Id, s => streamInformation, (s, l) =>
						{
							deleteCount += l.StreamLength;
							return streamInformation;
						});
					break;
				default:
					throw new ArgumentOutOfRangeException(item.State.ToString());
			}
			item.TaskCompletionSource.SetResult(null);
		}

		public TimeSpan MaxDurationForFlush { get; set; }

		private void FlushToDisk(ICollection<Action<Exception>> tasksToNotify)
		{
			try
			{
				streamSource.Flush(file);
				eventsCount += tasksToNotify.Count;
				foreach (var taskCompletionSource in tasksToNotify)
				{
					taskCompletionSource(null);
				}
			}
			catch (Exception e)
			{
				foreach (var taskCompletionSource in tasksToNotify)
				{
					taskCompletionSource(e);
				}
				throw;
			}
			finally
			{

				tasksToNotify.Clear();
				lastWrite = DateTime.UtcNow;
				hadWrites = false;
			}
		}

		public Task EnqueueAsync(string id, EventState state, JObject data, JObject metadata)
		{
			AssertValidState();

			var item = new WriteState
				{
					Data = data,
					State = state,
					Metadata = metadata,
					Id = id
				};

			writer.Enqueue(item);
			hasItems.Set();
			return item.TaskCompletionSource.Task;
		}

		public void Dispose()
		{
			var e = new ExceptionAggregator();
			e.Execute(cts.Cancel);
			e.Execute(writerThread.Wait);
			e.Execute(binaryWriter.Dispose);
			e.Execute(file.Dispose);
			e.Execute(cache.Dispose);
			e.Execute(hasItems.Dispose);

			e.ThrowIfNeeded();

			disposed = true;
		}
	}
}