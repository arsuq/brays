using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace brays
{
	public class BitMask
	{
		public BitMask(int count)
		{
			if (count < 0) throw new ArgumentOutOfRangeException();

			if (count > 32)
			{
				var c = count >> 5;
				if ((count & 31) != 0) c++;
				tiles = new uint[c];
				doneMask = new uint[c];
				Bytes = c * 4;
			}
			else
			{
				tiles = new uint[1];
				doneMask = new uint[1];
				Bytes = 4;
			}

			for (int i = 0; i < count; i++)
				doneMask[i >> 5] |= (uint)(1 << i);

			Count = count;
		}

		public BitMask(Span<byte> s, int count)
		{
			if (count > s.Length * 8) throw new ArgumentException();

			var len = count >> 5;
			if ((len & 31) != 0) len++;
			if (len == 0) len = 1;

			tiles = new uint[len];
			s.Slice(0, len * 4).CopyTo(MemoryMarshal.AsBytes<uint>(tiles));
		}

		public BitMask(Span<uint> s, int count)
		{
			tiles = s.ToArray();
			Count = count;
		}

		public bool this[int index]
		{
			get
			{
				if (index < 0 || index >= tiles.Length * 32) throw new ArgumentOutOfRangeException();

				var pos = index >> 5;
				var v = Volatile.Read(ref tiles[pos]);
				var m = 1 << index;

				return (v & m) != 0;
			}
			set
			{
				if (index < 0 || index >= tiles.Length * 32) throw new ArgumentOutOfRangeException();

				lock (wsync)
				{
					uint m = (uint)(1 << index);
					var pos = index >> 5;
					ref uint tile = ref tiles[pos];

					if (value) tile |= m;
					else tile &= ~m;
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteTo(Span<byte> s)
		{
			if (s.Length < tiles.Length * 4) throw new ArgumentException();

			var snap = new Span<uint>(tiles);
			MemoryMarshal.AsBytes(snap).CopyTo(s);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void WriteTo(Span<uint> s)
		{
			if (s.Length < Count) throw new ArgumentException();

			tiles.CopyTo(s);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Or(BitMask mask)
		{
			if (mask.Count > Count) throw new ArgumentException($"The OR mask has more than {Count} bits.");

			lock (wsync)
				for (int i = 0; i < mask.tiles.Length; i++)
					tiles[i] |= mask.tiles[i];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsComplete()
		{
			for (int i = 0; i < tiles.Length; i++)
				if ((Volatile.Read(ref tiles[i]) ^ doneMask[i]) != 0)
					return false;

			return true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ToBinaryString()
		{
			var sb = new StringBuilder();

			foreach (var tile in tiles)
			{
				var M = Convert.ToString(tile, 2);
				sb.Append(MASK.Substring(0, 32 - M.Length) + M);
			}

			return sb.ToString();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ToString(int binaryIfCountLessThan = 64) =>
			Count < binaryIfCountLessThan ? ToBinaryString() : ToUintString();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ToUintString()
		{
			var sb = new StringBuilder();

			for (int i = tiles.Length - 1; i >= 0; i--)
				sb.AppendFormat("-{0}", tiles[i]);

			sb.Remove(0, 1);

			return sb.ToString();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint[] GetTiles()
		{
			var c = new uint[tiles.Length];
			Array.Copy(tiles, c, tiles.Length);
			return c;
		}

		public readonly int Count;
		public readonly int Bytes;
		object wsync = new object();
		uint[] tiles;
		uint[] doneMask;
		const string MASK = "00000000000000000000000000000000";
	}
}
