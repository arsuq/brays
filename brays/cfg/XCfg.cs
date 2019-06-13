using System;

namespace brays
{
	public class XCfg
	{
		public XCfg(BeamerCfg bcfg, XLogCfg log, IMemoryHighway hw)
		{
			this.bcfg = bcfg;
			this.log = log;
			outHighway = hw != null ? hw : new HeapHighway();
		}

		/// <summary>
		/// The exchange response callbacks will be deleted after this amount 
		/// of time after their creation.
		/// </summary>
		public TimeSpan RepliesTTL = new TimeSpan(0, 1, 0);

		/// <summary>
		/// The collection delay interval.
		/// </summary>
		public int CleanupFreqMS = 10000;

		internal readonly IMemoryHighway outHighway;
		internal BeamerCfg bcfg;
		internal XLogCfg log;
	}
}
