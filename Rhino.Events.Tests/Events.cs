using System.Linq;
using Rhino.Events.Tests.Events;
using Rhino.Events.Tests.Snapshots;
using Xunit;

namespace Rhino.Events.Tests
{
	public class ReadingEvents : EventsTest
	{
		[Fact]
		public void CanReadEventsUpToSnapshot()
		{
			using (var s = NewScribe())
			{
				s.EnqueueEventAsync("users/1", new NewUserCreated
					{
						UserId = "users/1",
						Name = "Oren"
					}).Wait();

				s.EnqueueEventAsync("users/1", new UserNameChanged
					{
						UserId = "users/1",
						NewName = "Oren Eini"
					}).Wait();

				s.EnqueueSnapshotAsync("users/1", new UserSnapshot
					{
						UserId = "users/1",
						Name = "Oren Eini"
					}).Wait();

			}

			using(var s = NewScribe())
			{
				var item = s.ReadRaw("users/1").Single();
				Assert.Equal("Oren Eini", ((UserSnapshot) item).Name);
			}
		}
	}
}