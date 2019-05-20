using System;

namespace brays
{
	struct TileDgram
	{
		public TileDgram(MemoryFragment frag)
		{
			Fragment = frag;
			Sent = DateTime.MaxValue;
			ResponseMark = 0;
		}

		public MemoryFragment Fragment;
		public DateTime Sent;
		public int ResponseMark;
	}
}
