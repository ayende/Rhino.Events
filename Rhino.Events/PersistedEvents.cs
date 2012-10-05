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

namespace Rhino.Events
{
	public class PersistedEvents : IDisposable
	{
		private const long DoesNotExists = -1;

		private readonly IStreamSource streamSource;
		private readonly string path;
		private readonly Stream file;
		private readonly Task writerThread;
		private readonly ManualResetEventSlim hasItems = new ManualResetEventSlim(false);
		private readonly ConcurrentQueue<WriteState> writer = new ConcurrentQueue<WriteState>();
		private readonly CancellationTokenSource cts = new CancellationTokenSource();
		private readonly JsonDataCache<PersistedEvent> cache = new JsonDataCache<PersistedEvent>();
		private readonly ConcurrentDictionary<string, long> idToPos = new ConcurrentDictionary<string, long>(StringComparer.InvariantCultureIgnoreCase);
		private readonly BinaryWriter binaryWriter;
		
		private DateTime lastWrite;
		bool hadWrites;

		private volatile bool disposed;
		private volatile Exception corruptingException;

		private class WriteState
		{
			public readonly TaskCompletionSource<object> TaskCompletionSource = new TaskCompletionSource<object>();
			public string Id;
			public JObject Data;
			public JObject Metadata;
		}

		public PersistedEvents(IStreamSource streamSource, string dirPath)
		{
			this.streamSource = streamSource;
			path = Path.Combine(dirPath, "data.events");
			file = streamSource.OpenWrite(path);
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

		public IEnumerable<Event> Read(string id)
		{
			AssertValidState();

			long previous;
			if (idToPos.TryGetValue(id, out previous) == false)
				return null;

			return ReadInternal(previous).Select(x=>new Event
				{
					Data = x.Data,
					Metadata = x.Metadata
				});
		}

		private void AssertValidState()
		{
			var exception = corruptingException;
			if (exception != null)
				throw new InvalidOperationException("Instance state corrupted, can't read", exception);

			if(disposed)
				throw new ObjectDisposedException("PersistedEvents");
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

					if (previous == DoesNotExists)
						yield break;

					var itemPos = previous;
					stream.Position = previous;
					reader.ReadString(); // skip the id
					previous = reader.ReadInt64();
					var metadata = (JObject)JToken.ReadFrom(new BsonReader(reader));
					var data = (JObject)JToken.ReadFrom(new BsonReader(reader));
					var persistedEvent = new PersistedEvent
						{
							Data = data, Metadata = metadata, Previous = previous
						};
					cache.Set(itemPos, persistedEvent);
					yield return persistedEvent;
				}
			}
		}

		private IEnumerable<PersistedEvent> ReadFromCache(long previous)
		{
			if (previous == DoesNotExists)
				yield break;

			var persistedEvent = cache.Get(previous);
			while (persistedEvent != null)
			{
				if (previous == DoesNotExists)
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

				long prevPos;
				if (idToPos.TryGetValue(item.Id, out prevPos) == false)
					prevPos = DoesNotExists;

				var currentPos = file.Position;
				binaryWriter.Write(item.Id);
				binaryWriter.Write(prevPos);

				item.Metadata.WriteTo(new BsonWriter(binaryWriter));
				item.Data.WriteTo(new BsonWriter(binaryWriter));

				cache.Set(currentPos, new PersistedEvent
					{
						Data = item.Data,
						Metadata = item.Metadata,
						Previous = prevPos
					});

				tasksToNotify.Add(exception =>
					{
						if (exception == null)
						{
							idToPos.AddOrUpdate(item.Id, currentPos, (s, l) => currentPos);
							item.TaskCompletionSource.SetResult(null);
						}
						else
						{
							item.TaskCompletionSource.SetException(exception);
						}
					});

				hadWrites = true;
			}
		}

		public TimeSpan MaxDurationForFlush { get; set; }

		private void FlushToDisk(ICollection<Action<Exception>> tasksToNotify)
		{
			try
			{
				streamSource.Flush(file);
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

		public Task EnqueueAsync(string id, JObject metadata, JObject data)
		{

			AssertValidState();

			var item = new WriteState
				{
					Data = data,
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