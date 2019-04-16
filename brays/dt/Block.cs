using System;
using System.Collections;
using System.Threading;

namespace brays
{
	public class Block : IDisposable
	{
		public Block(int id, ushort tileSize, MemoryFragment frag)
		{
			ID = id;
			fragment = frag;
			TileSize = tileSize;
			TileCount = frag.Length / tileSize;

			if (frag.Length % tileSize != 0)
				TileCount++;

			blockTiles = new BitArray(TileCount);
		}

		public Block(int id, int tileCount, ushort tileSize, IMemoryHighway hw)
		{
			ID = id;
			TileSize = tileSize;
			TileCount = tileCount;
			fragment = hw.AllocFragment(tileSize * tileCount);
			blockTiles = new BitArray(TileCount);
		}

		internal void Receive(FRAME f)
		{
			lock (sync)
			{
				if (f.TileCount != TileCount) throw new Exception();

				if (!blockTiles[f.TileIndex])
				{
					blockTiles[f.TileIndex] = true;
					markedTiles++;
					f.Data.CopyTo(fragment.Span().Slice(f.TileIndex * TileCount));
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
						if (!blockTiles[i])
						{
							blockTiles[i] = true;
							markedTiles++;
						}
				}
				else if (!blockTiles[idx])
				{
					blockTiles[idx] = true;
					markedTiles++;
				}
			}
		}

		public bool this[int index]
		{
			get
			{
				lock (sync) return blockTiles[index];
			}
		}

		public int MarkedLine()
		{
			lock (sync)
			{
				for (int i = 0; i < blockTiles.Length; i++)
					if (!blockTiles[i]) return i - 1;

				return 0;
			}
		}

		public void Dispose()
		{
			lock (sync)
				if (fragment != null && !fragment.IsDisposed)
				{
					fragment.Dispose();
					fragment = null;
				}
		}

		public MemoryFragment Memory => fragment;
		public int MarkedTiles => Volatile.Read(ref markedTiles);
		public bool IsComplete => MarkedTiles == TileCount;

		public readonly int ID;
		public readonly int TileCount;
		public readonly ushort TileSize;

		BitArray blockTiles;
		int markedTiles;
		object sync = new object();
		MemoryFragment fragment;
	}
}
