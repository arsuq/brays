/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;

namespace brays
{
	public class BeamerCfg
	{
		/// <summary>
		/// Creates a RayBeamer configuration instance.
		/// </summary>
		/// <param name="receiveHighway">For the blocks, i.e. do not assume lengths.</param>
		/// <param name="tileXHighway">For tile exchanges. Pass null to init with a heap hw with 65K lanes.</param>
		/// <param name="logcfg">The logging settings.</param>
		public BeamerCfg(
			IMemoryHighway receiveHighway = null,
			IMemoryHighway tileXHighway = null,
			IMemoryHighway outHighway = null,
			BeamerLogCfg logcfg = null)
		{
			ReceiveHighway = receiveHighway != null ? receiveHighway : new HeapHighway();
			TileExchangeHighway = tileXHighway != null ?
				tileXHighway :
				new HeapHighway(
					new HighwaySettings(ushort.MaxValue, 10000),
					ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);

			OutHighway = outHighway != null ?
				outHighway :
				new HeapHighway(
					new HighwaySettings(ushort.MaxValue, 10000),
					ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
			Log = logcfg;
		}

		/// <summary>
		/// Pulsing reserves one thread for arranging packets into dgrams.
		/// This is useful when one needs to send large number of tiny messages
		/// as they are zipped together into few tiles.
		/// </summary>
		public bool EnablePulsing;

		/// <summary>
		/// The max pulse delay.
		/// </summary>
		public int PulseRetentionMS = 20;

		/// <summary>
		/// The await for the remote config when exchanging.
		/// </summary>
		public TimeSpan ConfigExchangeTimeout = new TimeSpan(0, 0, 40);

		/// <summary>
		/// The default value is ushort.MaxValue * 400
		/// </summary>
		public int ReceiveBufferSize = ushort.MaxValue * 400;

		/// <summary>
		/// The default value is ushort.MaxValue * 200
		/// </summary>
		public int SendBufferSize = ushort.MaxValue * 200;

		/// <summary>
		/// Prevents dgram losses and re-beams.
		/// This threshold affects only the block beams, where each tile is TileSizeBytes.
		/// If the remote ReceiveBufferSize / remote.TileSizeBytes is less than this value
		/// the smaller setting will be applied.
		/// </summary>
		public int MaxBeamedTilesAtOnce = 100;

		/// <summary>
		/// The concurrent socket listeners count.
		/// </summary>
		public int MaxConcurrentReceives = 4;

		/// <summary>
		/// The log configuration.
		/// </summary>
		public BeamerLogCfg Log;

		/// <summary>
		/// If true - sends probe dgrams every ProbeFreqMS. 
		/// The default is false.
		/// </summary>
		public bool EnableProbes = false;

		/// <summary>
		/// The default value is 4sec.
		/// </summary>
		public int ProbeFreqMS = 4000;

		/// <summary>
		/// The desired dgram size. It's used for all block exchanges.
		/// The default value is 40K.
		/// </summary>
		public ushort TileSizeBytes = 40_000;

		/// <summary>
		/// This is the receive loop error sleep between retries.
		/// </summary>
		public int ErrorAwaitMS = 3000;

		/// <summary>
		/// After receiving this number of 0-bytes, the Beamer shuts down.
		/// The failed receives are separated by ErrorAwaitMS.
		/// </summary>
		public int MaxReceiveRetries = 8;

		/// <summary>
		/// The number of unconfirmed status dgram sends before bailing the ongoing operation. 
		/// </summary>
		public int SendRetries = 40;

		/// <summary>
		/// The SendRetries loop starting await value in milliseconds. 
		/// Each subsequent failure multiplies the await by RetryDelayStepMultiplier.
		/// </summary>
		public int RetryDelayStartMS = 100;

		/// <summary>
		/// After each retry the RetryDelayStartMS is multiplied by this value.
		/// </summary>
		public double RetryDelayStepMultiplier = 1.8;

		/// <summary>
		/// The cleanup triggering frequency.
		/// The default is 8sec.
		/// </summary>
		public int CleanupFreqMS = 8000;

		/// <summary>
		/// The starting retry delay in the Beam() loop in milliseconds.
		/// Each subsequent failure multiplies the await by RetryDelayStepMultiplier.
		/// </summary>
		public int BeamRetryDelayStartMS = 2000;

		/// <summary>
		/// Beam() is invoked in a loop up to this number of times or until a status is received.
		/// </summary>
		public int BeamRetriesCount = 3;

		/// <summary>
		/// A set of received frame IDs is kept for protecting against double processing.
		/// </summary>
		public TimeSpan ProcessedFramesIDRetention = new TimeSpan(0, 10, 0);

		/// <summary>
		/// The out signals are kept for re-sending when dgrams are lost.
		/// </summary>
		public TimeSpan SentSignalsRetention = new TimeSpan(0, 5, 0);

		/// <summary>
		/// All sent blocks are deleted this amount of time after being sent.
		/// Re-beams offsets the sent time.
		/// Note that the blocks may be disposed long before the cleanup.
		/// </summary>
		public TimeSpan SentBlockRetention = new TimeSpan(0, 5, 0);

		/// <summary>
		/// The amount of time before deleting the awaiting frame reply callbacks.
		/// </summary>
		public TimeSpan AwaitsCleanupAfter = new TimeSpan(0, 2, 0);

		/// <summary>
		/// If all tiles are sent but not received yet, the receiver will wait this amount
		/// of time before sending the current tile bit map for re-beaming. 
		/// </summary>
		/// <remarks>
		/// In practice the last few tiles of a block will arrive after the all-sent status signal,
		/// so this value should be greater than zero in order to prevent unnecessary re-transmissions. 
		/// </remarks>
		public int WaitAfterAllSentMS = 1000;

		/// <summary>
		/// Where the dgrams are composed.
		/// </summary>
		public IMemoryHighway OutHighway;

		/// <summary>
		/// Where the blocks are assembled.
		/// </summary>
		public IMemoryHighway ReceiveHighway;

		/// <summary>
		/// Where the tileX response fragments are allocated.
		/// </summary>
		public IMemoryHighway TileExchangeHighway;

		/// <summary>
		/// Where the callback will be scheduled on.
		/// </summary>
		public CallbackThread ScheduleCallbacksOn = CallbackThread.Task;

#if DEBUG || ASSERT
		internal bool dropFrame()
		{
			if (!dropFrames) return false;
			lock (rdm) return rdm.Next(100) < deopFramePercent;
		}

		internal Random rdm = new Random();
		internal bool dropFrames;
		internal int deopFramePercent;
#endif

	}
}
