using System;

namespace brays
{
	public class EmitterCfg
	{
		public EmitterCfg(IMemoryHighway receiveHighway = null)
		{
			if (receiveHighway == null)
				ReceiveHighway = new HeapHighway();
		}

		public string LogFilePath;
		public bool Log = false;
		public bool EnableProbes = false;
		public ushort TileSize = 40_000;
		public int ProbeFreqMS = 4000;
		public int ErrorAwaitMS = 3000;
		public int MaxReceiveRetries = 8;
		public int SendRetries = 1;
		public int RetryDelayMS = 8000;
		public int CleanupFreqMS = 8000;

		/// <summary>
		/// A set of received frame IDs is kept for protecting against double processing.
		/// </summary>
		public TimeSpan ProcessedFramesIDRetention = new TimeSpan(0, 10, 0);

		/// <summary>
		/// All sent blocks are deleted this amount of time after being sent.
		/// Re-beams offset the sent time.
		/// </summary>
		public TimeSpan SentBlockRetention = new TimeSpan(0, 5, 0);

		/// <summary>
		/// The amount of time before deleting the awaiting frame reply callbacks.
		/// </summary>
		public TimeSpan SignalAwait = new TimeSpan(0, 5, 0);

		/// <summary>
		/// If all tiles are sent but not received yet, the receiver will wait this amount
		/// of time before sending the current tile bit map for re-beaming. 
		/// </summary>
		/// <remarks>
		/// In practice the last few tiles of a block will arrive after the all-sent status signal,
		/// so this value should be greater than zero in order to prevent unnecessary re-transmissions. 
		/// </remarks>
		public int WaitForTilesAfterAllSentMS = 1000;

		/// <summary>
		/// Where the blocks are assembled.
		/// </summary>
		public IMemoryHighway ReceiveHighway;

	}
}
