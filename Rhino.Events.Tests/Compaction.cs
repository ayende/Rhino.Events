using System.IO;
using Rhino.Events.Tests.Events;
using Xunit;
using System.Linq;

namespace Rhino.Events.Tests
{
	public class Compaction : EventsTest
	{
		 [Fact]
		 public void CanCompactData()
		 {
			 using(var s = NewScribe())
			 {
				 for (int i = 0; i < 2; i++)
				 {
					 s.EnqueueEventAsync("users/1", new NewUserCreated()).Wait();
				 }

				 s.EnqueueDeleteAsync("users/1").Wait();

				 var oldfile = Directory.GetFiles("TestScribe", "data.events.*").Last();

				 s.EventsStorage.Compact();

				 var newfile = Directory.GetFiles("TestScribe", "data.events.*").Last();

				 Assert.NotEqual(oldfile, newfile);
				 Assert.Equal(0, new FileInfo(newfile).Length);
			 }
		 }

		 [Fact]
		 public void CanWriteAfterCompaction()
		 {
			 using (var s = NewScribe())
			 {
				 for (int i = 0; i < 2; i++)
				 {
					 s.EnqueueEventAsync("users/1", new NewUserCreated()).Wait();
				 }

				 s.EnqueueDeleteAsync("users/1").Wait();

				 s.EventsStorage.Compact();

				 s.EnqueueEventAsync("users/1", new NewUserCreated()).Wait();

				 Assert.Equal(1, s.ReadRaw("users/1").Count());

				 Assert.Equal(1, Directory.GetFiles("TestScribe","*.events.*").Length);
			 }
		 }
	}
}