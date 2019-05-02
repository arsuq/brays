using System;
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
				if ((count & 5) != 0) c++;
				tiles = new int[c];
				doneMask = new int[c];
				Bytes = c;
			}
			else
			{
				tiles = new int[1];
				doneMask = new int[1];
				Bytes = 4;
			}

			for (int i = 0; i < count; i++)
				doneMask[i >> 5] |= 1 << i;

			Count = count;
		}

		public BitMask(Span<byte> s, int count)
		{
			if (count > s.Length * 8) throw new ArgumentException();

			var len = count / 32;
			if (len % 32 != 0) len++;
			if (len == 0) len = 1;

			tiles = new int[len];
			s.Slice(0, len * 4).CopyTo(MemoryMarshal.AsBytes<int>(tiles));
		}

		public BitMask(Span<int> s, int count)
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
					var m = 1 << index;
					var pos = index >> 5;
					ref int tile = ref tiles[pos];

					if (value) tile |= m;
					else tile &= ~m;
				}
			}
		}

		public void WriteTo(Span<byte> s)
		{
			if (s.Length < tiles.Length * 4) throw new ArgumentException();

			var snap = new Span<int>(tiles);
			MemoryMarshal.AsBytes(snap).CopyTo(s);
		}

		public void WriteTo(Span<int> s)
		{
			if (s.Length < Count) throw new ArgumentException();

			tiles.CopyTo(s);
		}

		public void Or(BitMask mask)
		{
			if (mask.Count > Count) throw new ArgumentException($"The OR mask has more than {Count} bits.");

			lock (wsync)
				for (int i = 0; i < mask.tiles.Length; i++)
					tiles[i] |= mask.tiles[i];
		}

		public bool IsComplete()
		{
			for (int i = 0; i < tiles.Length; i++)
				if ((Volatile.Read(ref tiles[i]) ^ doneMask[i]) != 0)
					return false;

			return true;
		}

		public string ToBinaryString()
		{
			var sb = new StringBuilder();

			foreach (var tile in tiles)
				sb.Append(Convert.ToString(tile, 2));

			return sb.ToString();
		}

		public readonly int Count;
		public readonly int Bytes;
		object wsync = new object();
		int[] tiles;
		int[] doneMask;
	}
}
