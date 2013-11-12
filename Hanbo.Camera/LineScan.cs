using HalconDotNet;
using PylonC.NET;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Hanbo.Camera
{
	/// <summary>
	/// Line Scan Camera
	/// </summary>
	public class LineScan
	{
		#region properties
		#endregion
		private BackgroundWorker _bgWorker;
		private uint _timeout = 1000 * 10;// timeout 時間, 預設為 10 秒


		string fpath = "";
		string fdir = @"D:\tmp";


		const uint NUM_BUFFERS = 2;         /* Number of buffers used for grabbing. */
		private uint _numDevices;                       /* Number of available devices. */
		private PYLON_STREAMGRABBER_HANDLE _hGrabber;   /* Handle for the pylon stream grabber. */
		private PYLON_CHUNKPARSER_HANDLE _hChunkParser; /* Handle for the parser extracting the chunk data. */
		private PYLON_WAITOBJECT_HANDLE _hWait;         /* Handle used for waiting for a grab to be finished. */
		private uint _payloadSize;                      /* Size of an image frame in bytes. */
		private Dictionary<PYLON_STREAMBUFFER_HANDLE, PylonBuffer<Byte>> _buffers; /* Holds handles and buffers used for grabbing. */
		private PylonGrabResult_t _grabResult;          /* Stores the result of a grab operation. */
		private PYLON_DEVICE_HANDLE _hDev;

		int nGrabs;                            /* Counts the number of buffers grabbed. */
		uint nStreams;                         /* The number of streams the device provides. */
		bool isAvail;                           /* Used for checking feature availability */
		bool isReady;                           /* Used as an output parameter */
		int i;                                 /* Counter. */
		string triggerSelectorValue = "FrameStart"; /* Preselect the trigger for image acquisition */
		bool isAvailFrameStart;                /* Used for checking feature availability */
		bool isAvailAcquisitionStart;          /* Used for checking feature availability */


		/// <summary>
		/// 有兩個 Mode, FreeMode, and PEGMode
		/// 預設為 FreeMode 模式
		/// </summary>
		public LineScan()
		{
			_hDev = new PYLON_DEVICE_HANDLE(); /* Handle for the pylon device. */
#if DEBUG
			/* This is a special debug setting needed only for GigE cameras.
                See 'Building Applications with pylon' in the programmers guide */
			Environment.SetEnvironmentVariable("PYLON_GIGE_HEARTBEAT", "300000" /*ms*/);
#endif
			initialize();
			initializeBackgroundWorker();
		}

		private void initializeBackgroundWorker()
		{
			_bgWorker = new BackgroundWorker();
			_bgWorker.WorkerSupportsCancellation = true;
			_bgWorker.WorkerReportsProgress = true;
			_bgWorker.DoWork += _bgWorker_DoWork;
			_bgWorker.ProgressChanged += _bgWorker_ProgressChanged;
			_bgWorker.RunWorkerCompleted += _bgWorker_RunWorkerCompleted;
		}

		void _bgWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			//throw new NotImplementedException();
		}

		void _bgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			//傳回 halcon Image
		}

		void _bgWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			var worker = sender as BackgroundWorker;
			while (true)
			{
				if (worker.CancellationPending)
				{
					e.Cancel = true;
					break;
				}
				else
				{
					//dosomething
					start(worker);
				}
			}
		}



		/// <summary>
		/// DeConstructor
		/// </summary>
		~LineScan()
		{
			releaseResource();
		}

		private void releaseResource()
		{
			/* ... When all buffers are retrieved from the stream grabber, they can be deregistered.
					  After deregistering the buffers, it is safe to free the memory */
			try
			{
				foreach (KeyValuePair<PYLON_STREAMBUFFER_HANDLE, PylonBuffer<Byte>> pair in _buffers)
				{
					Pylon.StreamGrabberDeregisterBuffer(_hGrabber, pair.Key);
					pair.Value.Dispose();
				}
				_buffers = null;

				/* ... Release grabbing related resources. */
				Pylon.StreamGrabberFinishGrab(_hGrabber);


				/* After calling PylonStreamGrabberFinishGrab(), parameters that impact the payload size (e.g., 
				the AOI width and height parameters) are unlocked and can be modified again. */

				/* ... Close the stream grabber. */
				Pylon.StreamGrabberClose(_hGrabber);


				/* ... Release the chunk parser. */
				Pylon.DeviceDestroyChunkParser(_hDev, _hChunkParser);


				/*  Disable the software trigger and chunk mode. */
				if (_hDev.IsValid)
				{
					Pylon.DeviceSetBooleanFeature(_hDev, "ChunkModeActive", false);
					Pylon.DeviceFeatureFromString(_hDev, "TriggerMode", "Off");
				}

				/* ... Close and release the pylon device. The stream grabber becomes invalid
				   after closing the pylon device. Don't call stream grabber related methods after 
				   closing or releasing the device. */
				Pylon.DeviceClose(_hDev);
				Pylon.DestroyDevice(_hDev);
			}
			catch (Exception ex)
			{
				//throw ex;
			}
			finally
			{
				/* ... Shut down the pylon runtime system. Don't call any pylon method after 
				   calling PylonTerminate(). */
				Pylon.Terminate();
			}
		}

		private void initialize()
		{
			/* Before using any pylon methods, the pylon runtime must be initialized. */
			Pylon.Initialize();

			/* Enumerate all camera devices. You must call 
                PylonEnumerateDevices() before creating a device. */
			_numDevices = Pylon.EnumerateDevices();

			if (0 == _numDevices)
			{
				throw new Exception("No devices found!");
			}

			/* Get a handle for the first device found.  */
			_hDev = Pylon.CreateDeviceByIndex(0);

			/* Before using the device, it must be opened. Open it for configuring
			parameters and for grabbing images. */
			Pylon.DeviceOpen(_hDev, Pylon.cPylonAccessModeControl | Pylon.cPylonAccessModeStream);

			/* Set the pixel format to Mono8, where gray values will be output as 8 bit values for each pixel. */
			/* ... Check first to see if the device supports the Mono8 format. */
			isAvail = Pylon.DeviceFeatureIsAvailable(_hDev, "EnumEntry_PixelFormat_Mono8");

			if (!isAvail)
			{
				/* Feature is not available. */
				throw new Exception("Device doesn't support the Mono8 pixel format.");
			}
			/* ... Set the pixel format to Mono8. */
			Pylon.DeviceFeatureFromString(_hDev, "PixelFormat", "Mono8");


			/* For GigE cameras, we recommend increasing the packet size for better 
                   performance. If the network adapter supports jumbo frames, set the packet 
                   size to a value > 1500, e.g., to 8192. In this sample, we only set the packet size
                   to 1500. */
			/* ... Check first to see if the GigE camera packet size parameter is supported and if it is writable. */
			isAvail = Pylon.DeviceFeatureIsWritable(_hDev, "GevSCPSPacketSize");

			if (isAvail)
			{
				/* ... The device supports the packet size feature. Set a value. */
				Pylon.DeviceSetIntegerFeature(_hDev, "GevSCPSPacketSize", 1500);
			}

			/* Before enabling individual chunks, the chunk mode in general must be activated. */
			isAvail = Pylon.DeviceFeatureIsWritable(_hDev, "ChunkModeActive");

			if (!isAvail)
			{
				throw new Exception("The device doesn't support the chunk mode.");
			}

			/* Activate the chunk mode. */
			Pylon.DeviceSetBooleanFeature(_hDev, "ChunkModeActive", true);

			/* Enable some individual chunks... */

			/* ... The frame counter chunk feature. */
			/* Is the chunk feature available? */
			isAvail = Pylon.DeviceFeatureIsAvailable(_hDev, "EnumEntry_ChunkSelector_Framecounter");

			if (isAvail)
			{
				/* Select the frame counter chunk feature. */
				Pylon.DeviceFeatureFromString(_hDev, "ChunkSelector", "Framecounter");

				/* Can the chunk feature be activated? */
				isAvail = Pylon.DeviceFeatureIsWritable(_hDev, "ChunkEnable");

				if (isAvail)
				{
					/* Activate the chunk feature. */
					Pylon.DeviceSetBooleanFeature(_hDev, "ChunkEnable", true);
				}
			}
			/* ... The CRC checksum chunk feature. */
			/*  Note: Enabling the CRC checksum chunk feature is not a prerequisite for using
			   chunks. Chunks can also be handled when the CRC checksum chunk feature is disabled. */
			isAvail = Pylon.DeviceFeatureIsAvailable(_hDev, "EnumEntry_ChunkSelector_PayloadCRC16");

			if (isAvail)
			{
				/* Select the CRC checksum chunk feature. */
				Pylon.DeviceFeatureFromString(_hDev, "ChunkSelector", "PayloadCRC16");

				/* Can the chunk feature be activated? */
				isAvail = Pylon.DeviceFeatureIsWritable(_hDev, "ChunkEnable");

				if (isAvail)
				{
					/* Activate the chunk feature. */
					Pylon.DeviceSetBooleanFeature(_hDev, "ChunkEnable", true);
				}
			}

			/* The data block containing the image chunk and the other chunks has a self-descriptive layout. 
			   A chunk parser is used to extract the appended chunk data from the grabbed image frame.
			   Create a chunk parser. */
			_hChunkParser = Pylon.DeviceCreateChunkParser(_hDev);

			if (!_hChunkParser.IsValid)
			{
				/* The transport layer doesn't provide a chunk parser. */
				throw new Exception("No chunk parser available.");
			}

			setStreamGrabber();

			//Free Run Settings
			setFreeRunParams();

			prepareDevice();
		}

		private void setStreamGrabber()
		{
			/* Image grabbing is done using a stream grabber.  
					   A device may be able to provide different streams. A separate stream grabber must 
					   be used for each stream. In this sample, we create a stream grabber for the default 
					   stream, i.e., the first stream ( index == 0 ).
					   */

			/* Get the number of streams supported by the device and the transport layer. */
			nStreams = Pylon.DeviceGetNumStreamGrabberChannels(_hDev);

			if (nStreams < 1)
			{
				throw new Exception("The transport layer doesn't support image streams.");
			}

			/* Create and open a stream grabber for the first channel. */
			_hGrabber = Pylon.DeviceGetStreamGrabber(_hDev, 0);
			Pylon.StreamGrabberOpen(_hGrabber);

			/* Get a handle for the stream grabber's wait object. The wait object
			   allows waiting for buffers to be filled with grabbed data. */
			_hWait = Pylon.StreamGrabberGetWaitObject(_hGrabber);

			/* Determine the required size of the grab buffer. Since activating chunks will increase the
			   payload size and thus the required buffer size, do this after enabling the chunks. */
			_payloadSize = checked((uint)Pylon.DeviceGetIntegerFeature(_hDev, "PayloadSize"));
			/* We must tell the stream grabber the number and size of the buffers 
				we are using. */
			/* .. We will not use more than NUM_BUFFERS for grabbing. */
			Pylon.StreamGrabberSetMaxNumBuffer(_hGrabber, NUM_BUFFERS);

			/* .. We will not use buffers bigger than payloadSize bytes. */
			Pylon.StreamGrabberSetMaxBufferSize(_hGrabber, _payloadSize);
		}

		private void prepareDevice()
		{
			//StreamGrabberPrepareGrab 執行後，就不能再設定 pixel Width and pixel Height , 要注意
			/*  Allocate the resources required for grabbing. After this, critical parameters 
						 that impact the payload size must not be changed until FinishGrab() is called. */
			Pylon.StreamGrabberPrepareGrab(_hGrabber);

			/* Before using the buffers for grabbing, they must be registered at
			   the stream grabber. For each registered buffer, a buffer handle
			   is returned. After registering, these handles are used instead of the
			   buffer objects pointers. The buffer objects are held in a dictionary,
			   that provides access to the buffer using a handle as key.
			 */
			_buffers = new Dictionary<PYLON_STREAMBUFFER_HANDLE, PylonBuffer<Byte>>();
			for (i = 0; i < NUM_BUFFERS; ++i)
			{
				PylonBuffer<Byte> buffer = new PylonBuffer<byte>(_payloadSize, true);
				PYLON_STREAMBUFFER_HANDLE handle = Pylon.StreamGrabberRegisterBuffer(_hGrabber, ref buffer);
				_buffers.Add(handle, buffer);
			}

			/* Feed the buffers into the stream grabber's input queue. For each buffer, the API 
			   allows passing in an integer as additional context information. This integer
			   will be returned unchanged when the grab is finished. In our example, we use the index of the 
			   buffer as context information. */
			i = 0;
			foreach (KeyValuePair<PYLON_STREAMBUFFER_HANDLE, PylonBuffer<Byte>> pair in _buffers)
			{
				Pylon.StreamGrabberQueueBuffer(_hGrabber, pair.Key, i++);
			}
		}

		private bool releaseStreamAndBuffers()
		{
			bool isRelease = true;
			try
			{
				/* ... We must issue a cancel call to ensure that all pending buffers are put into the
			   stream grabber's output queue. */
				Pylon.StreamGrabberCancelGrab(_hGrabber);

				/* ... The buffers can now be retrieved from the stream grabber. */
				do
				{
					isReady = Pylon.StreamGrabberRetrieveResult(_hGrabber, out _grabResult);

				} while (isReady);

				foreach (KeyValuePair<PYLON_STREAMBUFFER_HANDLE, PylonBuffer<Byte>> pair in _buffers)
				{
					Pylon.StreamGrabberDeregisterBuffer(_hGrabber, pair.Key);
					pair.Value.Dispose();
				}
				_buffers = null;

				/* ... Release grabbing related resources. */
				Pylon.StreamGrabberFinishGrab(_hGrabber);
			}
			catch (Exception ex)
			{
				isRelease = false;
			}

			return isRelease;
		}

		private void setFreeRunParams()
		{
			var iswritable = Pylon.DeviceFeatureIsWritable(_hDev, "Height");
			if (iswritable)
			{
				Pylon.DeviceSetIntegerFeature(_hDev, "Height", 4096);
			}
			iswritable = Pylon.DeviceFeatureIsWritable(_hDev, "Width");
			if (iswritable)
			{
				Pylon.DeviceSetIntegerFeature(_hDev, "Width", 4096);
			}


			/* Check the available camera trigger mode(s) to select the appropriate one: acquisition start trigger mode (used by previous cameras;
		   do not confuse with acquisition start command) or frame start trigger mode (equivalent to previous acquisition start trigger mode). */
			isAvailAcquisitionStart = Pylon.DeviceFeatureIsAvailable(_hDev, "EnumEntry_TriggerSelector_AcquisitionStart");
			isAvailFrameStart = Pylon.DeviceFeatureIsAvailable(_hDev, "EnumEntry_TriggerSelector_FrameStart");

			/* Check to see if the camera implements the acquisition start trigger mode only. */
			if (isAvailAcquisitionStart && !isAvailFrameStart)
			{
				/* Camera uses the acquisition start trigger as the only trigger mode. */
				Pylon.DeviceFeatureFromString(_hDev, "TriggerSelector", "AcquisitionStart");
				Pylon.DeviceFeatureFromString(_hDev, "TriggerMode", "On");
				triggerSelectorValue = "AcquisitionStart";
			}
			else
			{
				/* Camera may have the acquisition start trigger mode and the frame start trigger mode implemented.
				In this case, the acquisition trigger mode must be switched off. */
				if (isAvailAcquisitionStart)
				{
					Pylon.DeviceFeatureFromString(_hDev, "TriggerSelector", "AcquisitionStart");
					Pylon.DeviceFeatureFromString(_hDev, "TriggerMode", "Off");
				}
				/* To trigger each single frame by software or external hardware trigger: Enable the frame start trigger mode. */
				Pylon.DeviceFeatureFromString(_hDev, "TriggerSelector", "FrameStart");
				Pylon.DeviceFeatureFromString(_hDev, "TriggerMode", "On");
			}
			/* Note: the trigger selector must be set to the appropriate trigger mode 
				before setting the trigger source or issuing software triggers.
				Frame start trigger mode for newer cameras, acquisition start trigger mode for previous cameras. */
			Pylon.DeviceFeatureFromString(_hDev, "TriggerSelector", triggerSelectorValue);

			/* Enable software triggering. */
			/* ... Select the software trigger as the trigger source. */
			Pylon.DeviceFeatureFromString(_hDev, "TriggerSource", "Software");
			/* When using software triggering, the Continuous frame mode should be used. Once 
				  acquisition is started, the camera sends one image each time a software trigger is 
				  issued. */
			Pylon.DeviceFeatureFromString(_hDev, "AcquisitionMode", "Continuous");
		}

		/* Simple "image processing" function returning the minimum and maximum gray 
        value of an 8 bit gray value image. */
		static void getMinMax(Byte[] imageBuffer, long width, long height, out Byte min, out Byte max)
		{
			min = 255; max = 0;
			long imageDataSize = width * height;

			for (long i = 0; i < imageDataSize; ++i)
			{
				Byte val = imageBuffer[i];
				if (val > max)
					max = val;
				if (val < min)
					min = val;
			}
		}

		/// <summary>
		/// 啟動 line scan
		/// </summary>
		/// <param name="worker"></param>
		private void start(BackgroundWorker worker)
		{
			Console.WriteLine("Start and Wait....");

		}
		public void Action()
		{
			/* Issue an acquisition start command. Because the trigger mode is enabled, issuing the start command
                   itself will not trigger any image acquisitions. Issuing the start command simply prepares the camera. 
                   Once the camera is prepared it will acquire one image for every trigger it receives. */
			Pylon.DeviceExecuteCommandFeature(_hDev, "AcquisitionStart");
			Console.WriteLine("Start and Wait....");

			/* Trigger the first image. */
			//Pylon.DeviceExecuteCommandFeature(hDev, "TriggerSoftware");

			/* Grab NUM_GRABS images. */
			nGrabs = 0;                           /* Counts the number of images grabbed. */
			while (true)
			{
				int bufferIndex;              /* Index of the buffer. */
				Byte min = 255, max = 0;
				long chunkWidth = 0; /* data retrieved from the chunk parser */
				long chunkHeight = 0; /* data retrieved from the chunk parser */

				/* Wait for the next buffer to be filled. Wait up to 1000 ms. */
				isReady = Pylon.WaitObjectWait(_hWait, _timeout);

				if (!isReady)
				{
					break;
					/* Timeout occurred. */
					throw new Exception("Grab timeout occurred.\n");

				}

				/* Since the wait operation was successful, the result of at least one grab 
				   operation is available. Retrieve it. */
				isReady = Pylon.StreamGrabberRetrieveResult(_hGrabber, out _grabResult);

				if (!isReady)
				{
					/* Oops. No grab result available? We should never have reached this point. 
					   Since the wait operation above returned without a timeout, a grab result 
					   should be available. */
					throw new Exception("Failed to retrieve a grab result.\n");
				}

				nGrabs++;
				fpath = Path.Combine(fdir, nGrabs.ToString(@"D2"));
				/* Trigger the next image. Since we passed more than one buffer to the stream grabber, 
				   the triggered image will be grabbed while the image processing is performed.  */
				//Pylon.DeviceExecuteCommandFeature(hDev, "TriggerSoftware");

				/* Get the buffer index from the context information. */
				bufferIndex = (int)_grabResult.Context;

				/* Check to see if the image was grabbed successfully. */
				if (_grabResult.Status == EPylonGrabStatus.Grabbed)
				{
					/*  The grab is successful.  */

					PylonBuffer<Byte> buffer;        /* Reference to the buffer attached to the grab result. */

					/* Get the buffer from the dictionary. Since we also got the buffer index, 
					   we could alternatively use an array, e.g. buffers[bufferIndex]. */
					if (!_buffers.TryGetValue(_grabResult.hBuffer, out buffer))
					{
						/* Oops. No buffer available? We should never have reached this point. Since all buffers are
						   in the dictionary. */
						throw new Exception("Failed to find the buffer associated with the handle returned in grab result.");
					}

					Console.WriteLine("Grabbed frame {0} into buffer {1}.", nGrabs, bufferIndex);

					/* Check to see if we really got image data plus chunk data. */
					if (_grabResult.PayloadType != EPylonPayloadType.PayloadType_ChunkData)
					{
						Console.WriteLine("Received a buffer not containing chunk data?");
					}
					else
					{
						/* Process the chunk data. This is done by passing the grabbed image buffer
						   to the chunk parser. When the chunk parser has processed the buffer, the chunk 
						   data can be accessed in the same manner as "normal" camera parameters. 
						   The only exception is the CRC checksum feature. There are dedicated functions for
						   checking the CRC checksum. */

						bool hasCRC;

						/* Let the parser extract the data. */
						try
						{
							Pylon.ChunkParserAttachBuffer(_hChunkParser, buffer);
						}
						catch (Exception ex)
						{
							String msg = GenApi.GetLastErrorDetail();
							//GenApiGetLastErrorDetail							
						}


						/* Check the CRC checksum. */
						hasCRC = Pylon.ChunkParserHasCRC(_hChunkParser);

						if (hasCRC)
						{
							bool isOk = Pylon.ChunkParserCheckCRC(_hChunkParser);

							Console.WriteLine("Frame {0} contains a CRC checksum. The checksum {1} ok.", nGrabs, isOk ? "is" : "is not");
						}

						/* Retrieve the frame counter value. */
						/* ... Check the availability. */
						isAvail = Pylon.DeviceFeatureIsAvailable(_hDev, "ChunkFramecounter");

						Console.WriteLine("Frame {0} {1} a frame counter chunk.", nGrabs, isAvail ? "contains" : "doesn't contain");
						if (isAvail)
						{
							/* ... Get the value. */
							long counter;
							counter = Pylon.DeviceGetIntegerFeature(_hDev, "ChunkFramecounter");

							Console.WriteLine("Frame counter of frame {0}: {1}.", nGrabs, counter);
						}

						/* Retrieve the chunk width value. */
						/* ... Check the availability. */
						isAvail = Pylon.DeviceFeatureIsAvailable(_hDev, "ChunkWidth");

						Console.WriteLine("Frame {0} {1} a frame width chunk.", nGrabs, isAvail ? "contains" : "doesn't contain");
						if (isAvail)
						{
							/* ... Get the value. */
							chunkWidth = Pylon.DeviceGetIntegerFeature(_hDev, "ChunkWidth");

							Console.WriteLine("Width of frame {0}: {1}.", nGrabs, chunkWidth);
						}

						/* Retrieve the chunk height value. */
						/* ... Check the availability. */
						isAvail = Pylon.DeviceFeatureIsAvailable(_hDev, "ChunkHeight");

						Console.WriteLine("Frame {0} {1} a frame height chunk.", nGrabs, isAvail ? "contains" : "doesn't contain");
						if (isAvail)
						{
							/* ... Get the value. */
							chunkHeight = Pylon.DeviceGetIntegerFeature(_hDev, "ChunkHeight");

							Console.WriteLine("Height of frame {0}: {1}.", nGrabs, chunkHeight);
						}
					}

					/* Perform the image processing. */
					getMinMax(buffer.Array, chunkWidth, chunkHeight, out min, out max);
					Console.WriteLine("Min. gray value  = {0}, Max. gray value = {1}", min, max);

					//Save Image
					Console.WriteLine("SAVE Image");
					IntPtr pointer = buffer.Pointer;
					try
					{
						/**/
						var myBuf = buffer.Array;
						Console.WriteLine(String.Format("W: {0} H: {1}  Length: {2}", chunkWidth, chunkHeight, myBuf.Length));
						var hImag = new HImage("byte", (int)chunkWidth, (int)chunkHeight, pointer);

						HOperatorSet.WriteImage(hImag, "png", 0, fpath);

					}
					catch (Exception ex)
					{

						Console.WriteLine("SAVE Image Error : " + ex.Message);
					}


					/* Before requeueing the buffer, you should detach it from the chunk parser. */
					Pylon.ChunkParserDetachBuffer(_hChunkParser);  /* Now the chunk data in the buffer is no longer accessible. */
				}
				else if (_grabResult.Status == EPylonGrabStatus.Failed)
				{
					Console.Error.WriteLine("Frame {0} wasn't grabbed successfully.  Error code = {1}", nGrabs, _grabResult.ErrorCode);
				}

				/* Once finished with the processing, requeue the buffer to be filled again. */
				Pylon.StreamGrabberQueueBuffer(_hGrabber, _grabResult.hBuffer, bufferIndex);

			}//endwhile

			/*  ... Stop the camera. */
			Pylon.DeviceExecuteCommandFeature(_hDev, "AcquisitionStop");

			/* ... We must issue a cancel call to ensure that all pending buffers are put into the
			   stream grabber's output queue. */
			Pylon.StreamGrabberCancelGrab(_hGrabber);

			/* ... The buffers can now be retrieved from the stream grabber. */
			do
			{
				isReady = Pylon.StreamGrabberRetrieveResult(_hGrabber, out _grabResult);

			} while (isReady);
		}

		/// <summary>
		/// Free Run Mode ( trigger off)
		/// </summary>
		public void SetFreeRunMode()
		{
			releaseStreamAndBuffers();
			setFreeRunParams();
			prepareDevice();
		}

		/// <summary>
		/// PEG Mode，設定模式
		/// pegW, means the width of each frame (pixel)
		/// pegH, means the height of each frame (pixel)
		/// </summary>
		/// <param name="pegW">Width (pixel)</param>
		/// <param name="pegH">Height (pixel)</param>
		public void SetPEGMode(long pegW, long pegH)
		{
			releaseStreamAndBuffers();
			setPEGParams(pegW, pegH);
			prepareDevice();
		}

		private void setPEGParams(long pegW, long pegH)
		{
			var iswritable = Pylon.DeviceFeatureIsWritable(_hDev, "Height");
			if (iswritable)
			{
				Pylon.DeviceSetIntegerFeature(_hDev, "Height", pegH);
			}
			iswritable = Pylon.DeviceFeatureIsWritable(_hDev, "Width");
			if (iswritable)
			{
				Pylon.DeviceSetIntegerFeature(_hDev, "Width", pegW);
			}
			/* Disable acquisition start trigger if available. */
			if (Pylon.DeviceFeatureIsAvailable(_hDev, "EnumEntry_TriggerSelector_AcquisitionStart"))
			{
				Pylon.DeviceFeatureFromString(_hDev, "TriggerSelector", "AcquisitionStart");
				Pylon.DeviceFeatureFromString(_hDev, "TriggerMode", "Off");
			}

			/* Disable frame start trigger if available. */
			if (Pylon.DeviceFeatureIsAvailable(_hDev, "EnumEntry_TriggerSelector_FrameStart"))
			{
				Pylon.DeviceFeatureFromString(_hDev, "TriggerSelector", "FrameStart");
				Pylon.DeviceFeatureFromString(_hDev, "TriggerMode", "Off");
			}
			/*Enable Line Start trigger if available */

			try
			{
				if (Pylon.DeviceFeatureIsAvailable(_hDev, "EnumEntry_TriggerSelector_LineStart"))
				{
					Pylon.DeviceFeatureFromString(_hDev, "TriggerSelector", "LineStart");
					Pylon.DeviceFeatureFromString(_hDev, "TriggerMode", "On");
				}

				if (Pylon.DeviceFeatureIsAvailable(_hDev, "EnumEntry_ExposureMode_TriggerWidth"))
				{
					Pylon.DeviceFeatureFromString(_hDev, "ExposureMode", "TriggerWidth");
					//Pylon.DeviceFeatureFromString(m_hDevice, "TriggerMode", "On");
				}
			}
			catch (Exception ex)
			{
				var msg = ex.Message;
			}
		}
		#region APIs

		/// <summary>
		/// 開始擷取圖形
		/// </summary>
		public void StartGrab()
		{
			if (!_bgWorker.IsBusy)
			{
				_bgWorker.RunWorkerAsync();
			}
		}

		public void StopGrab()
		{
			_bgWorker.CancelAsync();
		}

		public void OneShot()
		{
		}

		#endregion


	}
}
