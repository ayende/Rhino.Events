using System;
using System.Collections.Concurrent;

namespace Rhino.Events.Impl
{
	public class ExceptionAggregator
	{
		readonly ConcurrentQueue<Exception> list = new ConcurrentQueue<Exception>();

		public void Execute(Action action)
		{
			try
			{
				action();
			}
			catch (Exception e)
			{
				list.Enqueue(e);
			}
		}

		public void ThrowIfNeeded()
		{
			if (list.Count == 0)
				return;


			var aggregateException = new AggregateException(list);
			throw aggregateException;
		}
	}
}