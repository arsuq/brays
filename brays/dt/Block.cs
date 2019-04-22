using System;
using System.Threading;

namespace brays
{
	class Block : IDisposable
	{
		public Block(int id, ushort tileSize, MemoryFragment frag)
		{
			ID = id;
			fragment = frag;
			TileSize = tileSize;
			TileCount = frag.Length / tileSize;

			if (frag.Length % tileSize != 0)
				TileCount++;

			tileMap = new BitMask(TileCount);
		}

		public Block(int id, int tileCount, ushort tileSize, IMemoryHighway hw)
		{
			ID = id;
			TileSize = tileSize;
			TileCount = tileCount;
			IsIncoming = true;
			fragment = hw.AllocFragment(tileSize * tileCount);
			tileMap = new BitMask(TileCount);
		}

		public bool this[int index]
		{
			get => tileMap[index];
		}

		public void Dispose()
		{
			if (fragment != null && !fragment.IsDisposed)
			{
				fragment.Dispose();
				fragment = null;
			}
		}

		internal void Receive(FRAME f)
		{
			lock (sync)
			{
				if (f.TileCount != TileCount) throw new Exception();

				if (!tileMap[f.TileIndex])
				{
					var src = f.Data.Slice(0, f.Length);
					var dst = fragment.Span().Slice(f.TileIndex * TileSize);

					src.CopyTo(dst);
					tileMap[f.TileIndex] = true;
					markedTiles++;
				}
			}
		}

		internal void Mark(int idx, bool upTo)
		{
			lock (sync)
			{
				if (upTo)
				{
					for (int i = 0; i <= idx; i++)
						if (!tileMap[i])
						{
							tileMap[i] = true;
							markedTiles++;
						}
				}
				else if (!tileMap[idx])
				{
					tileMap[idx] = true;
					markedTiles++;
				}
			}
		}

		internal void Mark(Span<byte> mask)
		{
			if (mask != null) return;

			lock (sync)
			{
				var B = new BitMask(mask);

				for (int i = 0; i < mask.Length; i++)
					if (B[i])
					{
						tileMap[i] = true;
						markedTiles++;
					}
			}
		}

		public MemoryFragment Fragment => fragment;
		public int MarkedTiles => Volatile.Read(ref markedTiles);
		public bool IsComplete => MarkedTiles == TileCount;

		public readonly int ID;
		public readonly int TileCount;
		public readonly ushort TileSize;
		public readonly bool IsIncoming;

		internal readonly BitMask tileMap;
		internal byte[] reqAckDgram;
		internal DateTime sentTime;
		internal bool isFaulted;

		int markedTiles;
		object sync = new object();
		MemoryFragment fragment;
	}
}
