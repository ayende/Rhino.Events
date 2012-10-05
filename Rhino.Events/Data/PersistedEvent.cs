using Newtonsoft.Json.Linq;

namespace Rhino.Events
{
	public class PersistedEvent
	{
		public JObject Data;
		public JObject Metadata;
		public long Previous;
	}

	public class Event
	{
		public JObject Data;
		public JObject Metadata;
	}
}