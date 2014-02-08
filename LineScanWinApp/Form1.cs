using Hanbo.Camera;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LineScanWinApp
{
	public partial class Form1 : Form
	{
		private LineScan _linescan;
		public Form1()
		{
			InitializeComponent();
			_linescan = new LineScan();
			long w = 4096;
			long h = 2000;
			_linescan.SetPEGMode(w, h);
			_linescan.On_RunningMessage += _linescan_On_RunningMessage;
		}

		void _linescan_On_RunningMessage(object sender, GrabImageEventArgs e)
		{
			
		}

		private void button1_Click(object sender, EventArgs e)
		{
			_linescan.StartGrab();
		}
	}
}
