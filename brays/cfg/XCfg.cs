/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;

namespace brays
{
	public class XCfg
	{
		/// <param name="outHighway">The memory highway, used for sending tiles. 
		/// If null, a HeapHighway with default capacity of 65KB is used.</param>
		public XCfg(BeamerCfg bcfg, XLogCfg log, IMemoryHighway outHighway)
		{
			this.bcfg = bcfg;
			this.log = log;
			this.outHighway = outHighway != null ? outHighway :
				new HeapHighway(new HighwaySettings(Beamer.UDP_MAX), Beamer.UDP_MAX);
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
