/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace brays
{
	class Block : IDisposable
	{
		public Block(int id, ushort tileSize, MemoryFragment frag)
		{
			ID = id;
			fragment = frag;
			TileSize = tileSize;
			TotalSize = frag.Length;
			TilesCount = frag.Length / tileSize;

			if (TotalSize > tileSize && frag.Length % tileSize != 0) TilesCount++;
			if (TilesCount == 0) TilesCount = 1;

			tileMap = new BitMask(TilesCount);
		}

		public Block(int id, int totalSize, ushort tileSize, IMemoryHighway hw)
		{
			ID = id;
			TileSize = tileSize;
			TotalSize = totalSize;
			TilesCount = totalSize / tileSize;

			if (TotalSize > tileSize && TotalSize % tileSize != 0) TilesCount++;
			if (TilesCount == 0) TilesCount = 1;

			IsIncoming = true;
			fragment = hw.AllocFragment(totalSize);
			tileMap = new BitMask(TilesCount);
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

		internal bool Receive(FRAME f)
		{
			lock (sync)
			{
				if (f.TotalSize != TotalSize) throw new ArgumentException();

				if (!tileMap[f.TileIndex])
				{
					var size = f.TotalSize < f.Length ? f.TotalSize : f.Length;
					var src = f.Data.Slice(0, size);
					var dst = fragment.Span().Slice(f.TileIndex * TileSize);

					src.CopyTo(dst);

					tileMap[f.TileIndex] = true;
					markedTiles++;
					Interlocked.Exchange(ref lastReceivedTileTick, DateTime.Now.Ticks);

					return true;
				}
				else return false;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void Mark(Span<byte> mask)
		{
			if (mask.Length == 0) return;

			lock (sync)
			{
				var B = new BitMask(mask, TilesCount);

				for (int i = 0; i < TilesCount; i++)
					if (B[i] && !tileMap[i])
					{
						tileMap[i] = true;
						markedTiles++;
					}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal bool timeToReqTiles(int gapMS) =>
			new TimeSpan(DateTime.Now.Ticks - Volatile.Read(ref lastReceivedTileTick)).TotalMilliseconds > gapMS;

		public MemoryFragment Fragment => fragment;
		public int MarkedTiles => Volatile.Read(ref markedTiles);
		public int RemainingTiles => TilesCount - Volatile.Read(ref markedTiles);
		public bool IsComplete => tileMap.IsComplete();
		public bool HasAllTiles => MarkedTiles == TilesCount;

		public readonly int ID;
		public readonly int TotalSize;
		public readonly int TilesCount;
		public readonly ushort TileSize;
		public readonly bool IsIncoming;

		internal readonly BitMask tileMap;
		internal DateTime sentTime = DateTime.MaxValue;
		internal long lastReceivedTileTick;
		internal bool isFaulted;
		internal bool isRejected;
		internal int isOnCompleteTriggered;
		internal int isRebeamRequestPending;

		internal Task requestTiles;
		internal Task<bool> beamTiles;

		internal byte[] frameHeader = new byte[FRAME.HEADER];

		int markedTiles;
		object sync = new object();
		MemoryFragment fragment;
	}
}
