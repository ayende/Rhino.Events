using Newtonsoft.Json.Linq;

namespace Rhino.Events.Data
{
	public class EventData
	{
		public JObject Data;
		public EventState State;
		public JObject Metadata;
	}
}