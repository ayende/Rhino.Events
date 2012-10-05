using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;

namespace Rhino.Events
{
	public class OnDiskData : IDisposable
    {
	    private readonly IStreamSource streamSource;
	    private readonly string path;
	    private readonly Stream file;
	    private readonly Thread writerThread;
		private readonly ManualResetEventSlim hasItems = new ManualResetEventSlim(false);
		private readonly ConcurrentQueue<WriteState> writer = new ConcurrentQueue<WriteState>();
		private readonly CancellationTokenSource cts = new CancellationTokenSource();
		private readonly JsonDataCache cache = new JsonDataCache();
		private readonly ConcurrentDictionary<string, long> idToPos = new ConcurrentDictionary<string, long>(StringComparer.InvariantCultureIgnoreCase);
	    private readonly BinaryWriter binaryWriter;
		private DateTime lastWrite;
	    private class WriteState
		{
			public readonly TaskCompletionSource<object> TaskCompletionSource = new TaskCompletionSource<object>();
			public string Id;
			public JObject Data;
		}
	
	    public OnDiskData(IStreamSource streamSource,string dirPath)
	    {
		    this.streamSource = streamSource;
		    path = Path.Combine(dirPath, "data.events");
		    file = streamSource.OpenWrite(path);

		    binaryWriter = new BinaryWriter(file, Encoding.UTF8, leaveOpen:true);

		    writerThread = new Thread(WriteToDisk)
			    {
				    IsBackground = true
			    };
		    writerThread.Start();
	    }

		public IEnumerable<JObject> Read(string id)
		{
			long previous;
			if(idToPos.TryGetValue(id, out previous)==false)
				return null;

			return ReadInternal(previous);
		}

		private IEnumerable<JObject> ReadInternal(long previous)
		{
			foreach (var p in ReadFromCache(previous))
			{
				previous = p.Item2;
				yield return p.Item1;
			}

			using (var stream = streamSource.OpenRead(path))
			using (var reader = new BinaryReader(stream))
			{
				while (true)
				{
					foreach (var p in ReadFromCache(previous))
					{
						previous = p.Item2;
						yield return p.Item1;
					}

					if (previous == -1)
						yield break;

					var itemPos = previous;
					stream.Position = previous;
					reader.ReadString(); // skip the id
					previous = reader.ReadInt64();
					var item = (JObject) JToken.ReadFrom(new BsonReader(reader));
					cache.Set(itemPos, item, previous);
					yield return item;
				}
			}
		}

		private IEnumerable<Tuple<JObject,long>> ReadFromCache(long previous)
		{
			if(previous == -1)
				yield break;

			var tuple = cache.Get(previous);
			while (tuple != null)
			{
				if (previous == -1)
					yield break;

				previous = tuple.Item2;

				yield return tuple;

				tuple = cache.Get(previous);
			}
		}
		bool hadWrites = false;

		private void WriteToDisk()
	    {
		    var tasksToNotify = new List<Action>(); 
		    while (true)
		    {
				if (cts.IsCancellationRequested)
				{
					FlushToDisk(tasksToNotify);
					return;
				}

				WriteState item;
				if(hadWrites)
				{
					if ((DateTime.UtcNow - lastWrite).TotalSeconds > 1)
					{
						// we have to flush to disk now, because we have writes and nothing else is forthcoming
						// or we have so many writes, that we need to flush to clear the buffers

						FlushToDisk(tasksToNotify);
						continue;
					}
				}
				if (writer.TryDequeue(out item) == false)
				{
					if(hadWrites)
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
			    if(idToPos.TryGetValue(item.Id, out prevPos) == false)
				    prevPos = -1;

			    var currentPos = file.Position;
				binaryWriter.Write(item.Id);
			    binaryWriter.Write(prevPos);
				
				item.Data.WriteTo(new BsonWriter(binaryWriter));

				cache.Set(currentPos, item.Data, prevPos);

				tasksToNotify.Add(() =>
					{
						idToPos.AddOrUpdate(item.Id, currentPos, (s, l) => currentPos);
						item.TaskCompletionSource.SetResult(null);
					});
				
				hadWrites = true;
		    }
	    }

		private void FlushToDisk(ICollection<Action> tasksToNotify)
		{
			streamSource.Flush(file);

			foreach (var taskCompletionSource in tasksToNotify)
			{
				taskCompletionSource();
			}

			tasksToNotify.Clear();
			lastWrite = DateTime.UtcNow;
			hadWrites = false;
						
		}

		public Task Enqueue(string id, JObject data)
	    {
		    var item = new WriteState
			    {
				    Data = data,
				    Id = id
			    };

			writer.Enqueue(item);
			hasItems.Set();
		    return item.TaskCompletionSource.Task;
	    }

	    public void Dispose()
	    {
		    try
		    {
				cts.Cancel();
		    }
		    finally
		    {
			    writerThread.Join();
			    binaryWriter.Dispose();
			    file.Dispose();
				cache.Dispose();
				hasItems.Dispose();
		    }
	    }
    }
}