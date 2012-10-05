using Rhino.Events.Storage;

namespace Rhino.Events.Data
{
	public class PersistedOptions
	{
		public string DirPath { get; set; }
		public IStreamSource StreamSource { get; set; }
		public bool AllowRecovery { get; set; }
	}
}