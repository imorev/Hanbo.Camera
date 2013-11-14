using Hanbo.Camera;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

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
			lineScan.On_RunningMessage += lineScan_On_RunningMessage;
			lineScan.On_ErrorOccured += lineScan_On_ErrorOccured;
			lineScan.StartGrab();
			while (Console.ReadLine() != "")
			{
				Thread.Sleep(200);
			}
			//lineScan.StartGrab();
		}

		static void lineScan_On_ErrorOccured(object sender, GrabImageEventArgs e)
		{
			Console.WriteLine(e.Message);
		}

		static void lineScan_On_RunningMessage(object sender, GrabImageEventArgs e)
		{
			Console.WriteLine(e.Message);
		}
	}
}
