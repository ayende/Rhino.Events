using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
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
		private static readonly ILog log = LogManager.GetCurrentClassLogger();

		private readonly PersistedOptions options;
		private const long DoesNotExists = -1;
		private const long Deleted = -2;

		private readonly IStreamSource streamSource;
		private readonly string dataPath = "data.events";
		private readonly string offsetsPath = "data.offsets";
		private readonly Task writerTask;
		private readonly ManualResetEventSlim hasItems = new ManualResetEventSlim(false);
		private readonly ConcurrentQueue<WriteState> itemsToWrite = new ConcurrentQueue<WriteState>();
		private readonly CancellationTokenSource cts = new CancellationTokenSource();
		private readonly JsonDataCache<PersistedEvent> cache;
		private readonly ReaderWriterLockSlim compactionLock = new ReaderWriterLockSlim();
		private readonly ConcurrentDictionary<string, StreamInformation> idToPos = new ConcurrentDictionary<string, StreamInformation>(StringComparer.InvariantCultureIgnoreCase);
		private readonly ConcurrentQueue<Stream> cachedReadStreams = new ConcurrentQueue<Stream>();

		private BinaryWriter binaryWriter;
		private Stream file;


		private long lastWriteSnapshotCount;
		private DateTime lastWriteSnapshotTime;

		private long eventsCount;
		private long deleteCount;

		private DateTime lastWrite;
		bool hadWrites;

		private volatile bool disposed;
		private volatile Exception corruptingException;

		private class WriteState : PersistedEvent
		{
			public readonly TaskCompletionSource<object> TaskCompletionSource = new TaskCompletionSource<object>();
		}

		public PersistedEventsStorage(PersistedOptions options)
		{
			cache = new JsonDataCache<PersistedEvent>(options);
			this.options = options;
			streamSource = options.StreamSource;
			file = streamSource.OpenReadWrite(dataPath);

			ReadAllFromDisk();

			binaryWriter = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);

			writerTask = Task.Factory.StartNew(() =>
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
			file.Position = ReadOffsets(Path.GetFileName(streamSource.GetLatestName(dataPath)));
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
						lastWriteSnapshotCount = eventsCount;
						lastWriteSnapshotTime = DateTime.UtcNow;
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
							deleteCount++;
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
			Stream stream;

			compactionLock.EnterReadLock();
			try
			{
				if (idToPos.TryGetValue(id, out previous) == false)
					return null;

				if(cachedReadStreams.TryDequeue(out stream) == false)
				{
					stream = streamSource.OpenRead(dataPath);
				}
			}
			finally
			{
				compactionLock.ExitReadLock();
			}

			return ReadInternal(stream, previous.LastPosition).Select(x => new EventData
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

		private IEnumerable<PersistedEvent> ReadInternal(Stream stream, long previous)
		{
			try
			{
				foreach (var p in ReadFromCache(previous))
				{
					previous = p.Previous;
					yield return p;
				}

				using (var reader = new BinaryReader(stream, leaveOpen: true, encoding: Encoding.UTF8))
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
			finally
			{
				cachedReadStreams.Enqueue(stream);
			}
		}

		private static void WriteItem(PersistedEvent item, BinaryWriter binaryWriter)
		{
			binaryWriter.Write(item.Id);
			binaryWriter.Write(item.Previous);
			binaryWriter.Write(item.StreamLength);
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
					FlushToDisk(tasksToNotify, closing: true);
					return;
				}

				WriteState item;
				if (hadWrites)
				{
					if ((DateTime.UtcNow - lastWrite) > options.MaxTimeToWaitForFlush)
					{
						// we have to flush to disk now, because we have writes and nothing else is forthcoming
						// or we have so many writes, that we need to flush to clear the buffers

						FlushToDisk(tasksToNotify);
						continue;
					}
				}
				if (itemsToWrite.TryDequeue(out item) == false)
				{
					if (hadWrites)
					{
						FlushToDisk(tasksToNotify);
					}
					else
					{
						if (deleteCount > (eventsCount / 4)) // we have > 25% wasted space
						{
							// we waited enough time to be pretty sure we are idle
							// and can run compaction without too much problems.
							if (hasItems.Wait(options.IdleTime, cts.Token) == false)
							{
								Compact();
								continue;
							}
						}
						else
						{
							hasItems.Wait(cts.Token);
						}
						hasItems.Reset();
					}
					continue;
				}

				StreamInformation info;
				idToPos.TryGetValue(item.Id, out info);

				var prevPos = info == null ? DoesNotExists : info.LastPosition;
				var currentPos = file.Position;

				var persistedEvent = new PersistedEvent
					{
						Id = item.Id,
						Data = item.Data,
						State = item.State,
						Previous = prevPos,
						Metadata = item.Metadata
					};

				WriteItem(persistedEvent, binaryWriter);

				var action = CreateCompletionAction(tasksToNotify, item, currentPos);
				tasksToNotify.Add(action);

				hadWrites = true;
			}
		}

		public void Compact()
		{
			var newPositions = new Dictionary<string, StreamInformation>(StringComparer.InvariantCultureIgnoreCase);

			var newFilePath = dataPath + ".compacting";
			streamSource.DeleteIfExists(newFilePath);

			using (var newFile = streamSource.OpenReadWrite(newFilePath))
			using (var newWriter = new BinaryWriter(newFile))
			using (var current = streamSource.OpenRead(dataPath))
			using (var reader = new BinaryReader(current))
			{
				while (true)
				{
					PersistedEvent persistedEvent;
					try
					{
						persistedEvent = ReadPersistedEvent(reader);
					}
					catch (EndOfStreamException)
					{
						break;
					}
					StreamInformation value;
					if (idToPos.TryGetValue(persistedEvent.Id, out value) == false)
						continue;


					if (value.LastPosition == Deleted || value.LastPosition == DoesNotExists)
						continue;

					StreamInformation information;
					if (newPositions.TryGetValue(persistedEvent.Id, out information) == false)
					{
						information =new StreamInformation
							{
								LastPosition = DoesNotExists,
								StreamLength = 0
							};
					}

					persistedEvent.Previous = information.LastPosition;

					newPositions[persistedEvent.Id] = new StreamInformation
						{
							LastPosition = newFile.Position,
							StreamLength = persistedEvent.StreamLength
						};
					WriteItem(persistedEvent, newWriter);
				}

				streamSource.Flush(newFile);

				compactionLock.EnterWriteLock();
				try
				{
					newFile.Close();
					streamSource.DeleteOnClose(dataPath);

					Stream result;
					while (cachedReadStreams.TryDequeue(out result))
					{
						result.Dispose();
					}

					binaryWriter.Dispose();
					file.Dispose();

					streamSource.RenameToLatest(newFilePath, dataPath);
					file = streamSource.OpenReadWrite(dataPath);
					binaryWriter = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true);
					
					streamSource.DeleteIfExists("data.offsets");
					FlushOffsets();
					
					idToPos.Clear();
					foreach (var val in newPositions)
					{
						idToPos[val.Key] = val.Value;
					}
				}
				finally
				{
					compactionLock.ExitWriteLock();
				}

			}
		}

		public long DeleteCount
		{
			get { return deleteCount; }
		}

		public long EventsCount
		{
			get { return eventsCount; }
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

		private void FlushToDisk(ICollection<Action<Exception>> tasksToNotify, bool closing = false)
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

			if (closing ||
				(eventsCount - lastWriteSnapshotCount) > options.WritesBetweenOffsetSnapshots ||
				(eventsCount > lastWriteSnapshotCount) && (DateTime.UtcNow - lastWriteSnapshotTime) > options.MinTimeForOffsetSnapshots)
			{
				FlushOffsets();
			}
		}

		private long ReadOffsets(string currentFileName)
		{
			long position;
			if (streamSource.Exists(offsetsPath) == false)
				return 0;
			using (var offsets = streamSource.OpenRead(offsetsPath))
			using(var reader = new BinaryReader(offsets))
			{
				var fileName = reader.ReadString();
				if(fileName != currentFileName)
				{
					// wrong file, skipping
					streamSource.DeleteOnClose(offsetsPath);
					return 0;
				}
				position = reader.ReadInt64();

				while (true)
				{
					string key;
					try
					{
						key = reader.ReadString();
					}
					catch (EndOfStreamException)
					{
						break;
					}
					var pos = reader.ReadInt64();
					var count = reader.ReadInt32();

					idToPos[key] = new StreamInformation
						{
							LastPosition = pos,
							StreamLength = count
						};
				}
			}

			return position;
		}

		private void FlushOffsets()
		{
			lastWriteSnapshotCount = eventsCount;
			lastWriteSnapshotTime = DateTime.UtcNow;

			using(var offsets = streamSource.OpenReadWrite( offsetsPath + ".new"))
			using(var writer = new BinaryWriter(offsets))
			{
				writer.Write(Path.GetFileName(streamSource.GetLatestName(dataPath)));
				writer.Write(file.Position);
				foreach (var streamInformation in idToPos)
				{
					writer.Write(streamInformation.Key);
					writer.Write(streamInformation.Value.LastPosition);
					writer.Write(streamInformation.Value.StreamLength);
				}
				writer.Flush();
			}
			streamSource.DeleteOnClose(offsetsPath);
			streamSource.RenameToLatest(offsetsPath + ".new", offsetsPath);
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

			itemsToWrite.Enqueue(item);
			hasItems.Set();
			return item.TaskCompletionSource.Task;
		}

		public void Dispose()
		{
			var e = new ExceptionAggregator();
			e.Execute(cts.Cancel);
			e.Execute(writerTask.Wait);
			e.Execute(binaryWriter.Dispose);
			e.Execute(file.Dispose);
			e.Execute(cache.Dispose);
			e.Execute(hasItems.Dispose);

			foreach (var cachedReadStream in cachedReadStreams)
			{
				e.Execute(cachedReadStream.Dispose);
			}

			e.ThrowIfNeeded();

			disposed = true;
		}
	}
}