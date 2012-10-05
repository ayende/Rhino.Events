using System;

namespace Rhino.Events.Data
{
	[Flags]
	public enum EventState
	{
		Event = 1,
		Snapshot = 2,
		Delete = 4 
	}
}