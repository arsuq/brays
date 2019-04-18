using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace brays
{
	public class BitMask
	{
		public BitMask(int length)
		{
			if (length < 0) throw new ArgumentOutOfRangeException();

			if (length > 32)
			{
				var count = length >> 5;
				if ((length & 5) != 0) count++;
				tiles = new int[count];
				Bytes = count;
			}
			else
			{
				tiles = new int[1];
				Bytes = 4;
			}

			Length = length;
		}

		public BitMask(Span<byte> s)
		{
			var len = s.Length / 4;
			if (len % 4 != 0) len++;

			tiles = new int[len];
			s.CopyTo(MemoryMarshal.AsBytes<int>(tiles));
		}

		public BitMask(Span<int> s)
		{
			tiles = s.ToArray();
		}

		public bool this[int index]
		{
			get
			{
				if (index < 0 || index >= tiles.Length) throw new ArgumentOutOfRangeException();

				var pos = index >> 5;
				var v = Volatile.Read(ref tiles[pos]);
				var m = 1 << index;

				return (v & m) != 0;
			}
			set
			{
				if (index < 0 || index >= tiles.Length) throw new ArgumentOutOfRangeException();

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
			if (s.Length < Length) throw new ArgumentException();

			tiles.CopyTo(s);
		}

		public readonly int Length;
		public readonly int Bytes;
		object wsync = new object();
		int[] tiles;
	}
}
