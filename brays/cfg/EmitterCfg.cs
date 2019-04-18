using System;

namespace brays
{
	public class EmitterCfg
	{
		public ushort TileSize = 40_000;
		public int ProbeFreqMS = 4000;
		public int ErrorAwaitMS = 3000;
		public int MaxRetries = 8;
		public int CleanupFreqMS = 8000;
		public TimeSpan SentBlockRetention = new TimeSpan(0, 0, 30);
		public TimeSpan SignalAwait = new TimeSpan(0, 0, 30);

	}
}
