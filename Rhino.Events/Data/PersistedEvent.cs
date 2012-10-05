using Newtonsoft.Json.Linq;

namespace Rhino.Events.Data
{
	public class PersistedEvent
	{
		public JObject Data;
		public EventState State;
		public long Previous;
	}
}