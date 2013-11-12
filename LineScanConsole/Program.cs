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
			long w = 4096;
			long h = 1024;
			lineScan.SetPEGMode(w, h);
			//lineScan.Action();
			lineScan.StartGrab();
		}
	}
}
