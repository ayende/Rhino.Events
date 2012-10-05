using System.IO;
using Rhino.Events.Tests.Events;
using Xunit;
using System.Linq;

namespace Rhino.Events.Tests
{
	public class BasicPersistence : EventsTest
	{
		[Fact]
		public void CanEnqueueSeveralEventsSameStream()
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
			}
		}

		[Fact]
		public void CanLoadEventsForSameStream()
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


				var items = s.ReadRaw("users/1").ToArray();
				Assert.Equal(2, items.Length);
				Assert.Equal("Oren Eini", ((UserNameChanged)items[0]).NewName);
				Assert.Equal("Oren", ((NewUserCreated)items[1]).Name);
			}
		}

		[Fact]
		public void CanHandleCorrupteData()
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
			}

			// simulate file corruption
			using (var file = File.Open(@"TestScribe\data.events", FileMode.Open, FileAccess.ReadWrite))
			{
				file.SetLength(file.Length - 5);
			}

			using(var s = NewScribe())
			{
				var items = s.ReadRaw("users/1").ToArray();
				Assert.Equal(1, items.Length);
				Assert.Equal("Oren", ((NewUserCreated)items[0]).Name);
			}
		}

		[Fact]
		public void CanCloseAndOpenAndRead()
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
			}

			using (var s = NewScribe())
			{
				var items = s.ReadRaw("users/1").ToArray();

				Assert.Equal("Oren Eini", ((UserNameChanged)items[0]).NewName);
				Assert.Equal("Oren", ((NewUserCreated)items[1]).Name);
			}
		}
	}
}
