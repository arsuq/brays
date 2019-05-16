using System;
using System.Runtime.CompilerServices;

namespace brays
{
	// [!] Magic numbers ahead

	readonly ref struct FRAME
	{
		public FRAME(int fid, int bid, int ts, int ti, ushort l, byte o, Span<byte> s)
		{
			Kind = (byte)Lead.Block;
			FrameID = fid;
			BlockID = bid;
			TotalSize = ts;
			TileIndex = ti;
			Length = s.Length > 0 ? (ushort)s.Length : l;
			Options = o;
			Data = s;
		}

		public FRAME(Span<byte> s)
		{
			Kind = s[0];
			FrameID = BitConverter.ToInt32(s.Slice(1));
			BlockID = BitConverter.ToInt32(s.Slice(5));
			TotalSize = BitConverter.ToInt32(s.Slice(9));
			TileIndex = BitConverter.ToInt32(s.Slice(13));
			Length = BitConverter.ToUInt16(s.Slice(17));
			Options = s[19];
			Data = s.Slice(HEADER);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Write(Span<byte> s, bool headerOnly = false)
		{
			s[0] = Kind;
			BitConverter.TryWriteBytes(s.Slice(1), FrameID);
			BitConverter.TryWriteBytes(s.Slice(5), BlockID);
			BitConverter.TryWriteBytes(s.Slice(9), TotalSize);
			BitConverter.TryWriteBytes(s.Slice(13), TileIndex);
			BitConverter.TryWriteBytes(s.Slice(17), Length);
			s[19] = Options;
			if (!headerOnly) Data.CopyTo(s.Slice(HEADER));
		}

		public readonly byte Kind;
		public readonly int FrameID;
		public readonly int BlockID;
		public readonly int TotalSize;
		public readonly int TileIndex;
		public readonly ushort Length;
		public readonly byte Options;
		public readonly Span<byte> Data;

		public const int HEADER = 20;
		public int LENGTH => HEADER + Data.Length;
	}

	readonly ref struct SIGNAL
	{
		public SIGNAL(int frameID, int refID, int mark, bool isError = false)
		{
			Kind = isError ? (byte)Lead.Error : (byte)Lead.Signal;
			FrameID = frameID;
			RefID = refID;
			Mark = mark;
		}

		public SIGNAL(Span<byte> s)
		{
			Kind = s[0];
			FrameID = BitConverter.ToInt32(s.Slice(1));
			RefID = BitConverter.ToInt32(s.Slice(5));
			Mark = BitConverter.ToInt32(s.Slice(9));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Write(Span<byte> s)
		{
			s[0] = Kind;
			BitConverter.TryWriteBytes(s.Slice(1), FrameID);
			BitConverter.TryWriteBytes(s.Slice(5), RefID);
			BitConverter.TryWriteBytes(s.Slice(9), Mark);
		}

		public readonly byte Kind;
		public readonly int FrameID;
		public readonly int RefID;
		public readonly int Mark;

		public int LENGTH => 13;
	}

	readonly ref struct STATUS
	{
		public STATUS(int fid, int bid, int tc, Span<byte> s)
		{
			Kind = (byte)Lead.Status;
			FrameID = fid;
			BlockID = bid;
			TileCount = tc;
			TileMap = s;
		}

		public STATUS(Span<byte> s)
		{
			Kind = s[0];
			FrameID = BitConverter.ToInt32(s.Slice(1));
			BlockID = BitConverter.ToInt32(s.Slice(5));
			TileCount = BitConverter.ToInt32(s.Slice(9));
			TileMap = s.Slice(HEADER);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Write(Span<byte> s)
		{
			s[0] = Kind;
			BitConverter.TryWriteBytes(s.Slice(1), FrameID);
			BitConverter.TryWriteBytes(s.Slice(5), BlockID);
			BitConverter.TryWriteBytes(s.Slice(9), TileCount);
			TileMap.CopyTo(s.Slice(HEADER));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Make(int fid, int bid, int tc, Span<byte> map, Span<byte> s)
		{
			s[0] = (byte)Lead.Status;
			BitConverter.TryWriteBytes(s.Slice(1), fid);
			BitConverter.TryWriteBytes(s.Slice(5), bid);
			BitConverter.TryWriteBytes(s.Slice(9), tc);
			if (map != null) map.CopyTo(s.Slice(HEADER));
		}

		public readonly byte Kind;
		public readonly int FrameID;
		public readonly int BlockID;
		public readonly int TileCount;
		public readonly Span<byte> TileMap;

		public const int HEADER = 13;
		public int LENGTH => HEADER + (TileMap != null ? TileMap.Length : 0);
	}

	readonly ref struct TILEX
	{
		public TILEX(byte kind, int fid, int refid, ushort l, Span<byte> s)
		{
			Kind = kind;
			FrameID = fid;
			RefID = refid;
			Length = s.Length > 0 ? (ushort)s.Length : l;
			Data = s;
		}

		public TILEX(Span<byte> s)
		{
			Kind = s[0];
			FrameID = BitConverter.ToInt32(s.Slice(1));
			RefID = BitConverter.ToInt32(s.Slice(5));
			Length = BitConverter.ToUInt16(s.Slice(9));
			Data = s.Slice(HEADER, Length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Write(Span<byte> s)
		{
			s[0] = Kind;
			BitConverter.TryWriteBytes(s.Slice(1), FrameID);
			BitConverter.TryWriteBytes(s.Slice(5), RefID);
			BitConverter.TryWriteBytes(s.Slice(9), Length);
			Data.CopyTo(s.Slice(HEADER));
		}

		public readonly byte Kind;
		public readonly int FrameID;
		public readonly int RefID;
		public readonly ushort Length;
		public readonly Span<byte> Data;

		public const int HEADER = 11;
		public int LENGTH => HEADER + Data.Length;
	}
}
