namespace Rhino.Events.Tests.Events
{
	public class NewUserCreated
	{
		public string UserId { get; set; }
		public string Name { get; set; }
	}

	public class UserNameChanged
	{
		public string UserId { get; set; }
		public string NewName { get; set; }
	}
}