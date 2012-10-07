using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rhino.Events;
using Rhino.Events.Data;
using Rhino.Events.Storage;

namespace Tryouts
{
	class Program
	{

		static volatile bool run = true;
		private static int count = 0;
		static void Main()
		{

			var scribe = new Scribe(new PersistedOptions
				{
					DirPath = "ScribedEvents",
					StreamSource = new FileStreamSource("ScribedEvents"),
					MaxTimeToWaitForFlush = TimeSpan.FromMilliseconds(200)
				});

			var sp = Stopwatch.StartNew();
			Task.Factory.StartNew(() =>
			{
				while (run)
				{
					Console.WriteLine("{0:#,#} in {1:#,#} ms", count, sp.ElapsedMilliseconds);
					Thread.Sleep(500);
				}
			});
			var data = new UserCreated {UserId = Guid.NewGuid(), Name = "Ayende"};
			Parallel.For(0, 1000 * 10, i =>
			{
				var tasks = new Task[1000];
				for (int j = 0; j < 1000; j++)
				{
					tasks[j] = scribe.EnqueueEventAsync("users/" + j, data);
					Interlocked.Increment(ref count);
				}
				Task.WaitAll(tasks);
			});

			run = false;

			Console.WriteLine(sp.ElapsedMilliseconds);

			for (int i = 124; i < 1000 * 10; i += 1293)
			{
				sp.Restart();

				scribe.ReadRaw("users/" + i).Count();

				Console.WriteLine(i + " " + sp.ElapsedMilliseconds);
			}

		}
	}

	public class UserCreated
	{
		public Guid UserId { get; set; }
		public string Name { get; set; }
	}
}
