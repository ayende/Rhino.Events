﻿using Newtonsoft.Json.Linq;

namespace Rhino.Events.Data
{
	public class PersistedEvent
	{
		public JObject Data;
		public JObject Metadata;
		public long Previous;
	}
}