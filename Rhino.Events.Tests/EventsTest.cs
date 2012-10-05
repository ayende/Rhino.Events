using System;
using System.IO;

namespace Rhino.Events.Tests
{
	public class EventsTest : IDisposable
	{
		public Scribe NewScribe()
		{
			return new Scribe("TestScribe");
		}

		public void Dispose()
		{
			Directory.Delete("TestScribe", true);
		}
	}
}