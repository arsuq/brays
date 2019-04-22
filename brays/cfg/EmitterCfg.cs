using System;

namespace brays
{
	public class EmitterCfg
	{
		public EmitterCfg(IMemoryHighway receiveHighway = null)
		{
			if (receiveHighway == null)
				ReceiveHighway = new MappedHighway();
		}

		public ushort TileSize = 40_000;
		public int ProbeFreqMS = 4000;
		public int ErrorAwaitMS = 3000;
		public int MaxReceiveRetries = 8;
		public int SendRetries = 2;
		public int RetryDelayMS = 800;
		public int CleanupFreqMS = 8000;
		public TimeSpan ProcessedFramesIDRetention = new TimeSpan(0, 10, 0);
		public TimeSpan SentBlockRetention = new TimeSpan(0, 0, 30);
		public TimeSpan SignalAwait = new TimeSpan(0, 0, 30);
		public IMemoryHighway ReceiveHighway;
	}
}
