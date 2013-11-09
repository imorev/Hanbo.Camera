using Hanbo.Camera;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LineScanConsole
{
	class Program
	{
		static void Main(string[] args)
		{
			var lineScan = new LineScan();

			lineScan.SetPEGMode((long)4096, (long)4096);
			lineScan.Action();
		}
	}
}
