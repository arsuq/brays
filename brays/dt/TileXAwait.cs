using System;

namespace brays
{
	struct TileXAwait
	{
		public TileXAwait(Action<MemoryFragment> onTileX)
		{
			OnTileExchange = onTileX;
			Created = DateTime.Now;
		}

		public Action<MemoryFragment> OnTileExchange;
		public DateTime Created;
	}
}
