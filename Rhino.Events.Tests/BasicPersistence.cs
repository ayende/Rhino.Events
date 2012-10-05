using Rhino.Events.Tests.Events;
using Xunit;

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

				 s.EnqueueEventAsync("users/1", new UseNameChanged
				 {
					 UserId = "users/1",
					 NewName = "Oren Eini"
				 }).Wait();
			 }
		 }
	}
}