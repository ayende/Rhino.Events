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
			 using(var s = NewScribe())
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

				 Assert.Equal("Oren Eini", ((UserNameChanged)items[0]).NewName);
				 Assert.Equal("Oren", ((NewUserCreated)items[1]).Name);
			 }
		 }
	}
}