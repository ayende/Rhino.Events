using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rhino.Events.Tryouts
{
	class Program
	{
		static void Main(string[] args)
		{
			using (var w = File.Open("Test", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete))
			{
				using (var r = File.Open("Test", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					using(var c = new FileStream("Test", FileMode.Open,FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.DeleteOnClose))
					{
						Console.WriteLine(File.Exists("Test"));
					}
					Console.WriteLine(File.Exists("Test"));
				}
				Console.WriteLine(File.Exists("Test"));
			}
			Console.WriteLine(File.Exists("Test"));
		}
	}
}
