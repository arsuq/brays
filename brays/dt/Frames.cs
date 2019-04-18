using System;

namespace brays
{
	readonly ref struct FRAME
	{
		public FRAME(int fid, int bid, int tc, int ti, ushort l, byte o, Span<byte> s)
		{
			Kind = (byte)Lead.Block;
			FrameID = fid;
			BlockID = bid;
			TileCount = tc;
			TileIndex = ti;
			Length = l;
			Options = o;
			Data = s;
		}

		public FRAME(Span<byte> s)
		{
			Kind = s[0];
			FrameID = BitConverter.ToInt32(s.Slice(1));
			BlockID = BitConverter.ToInt32(s.Slice(5));
			TileCount = BitConverter.ToInt32(s.Slice(9));
			TileIndex = BitConverter.ToInt32(s.Slice(13));
			Length = BitConverter.ToUInt16(s.Slice(17));
			Options = s[21];
			Data = s.Slice(22);
		}

		public void Write(Span<byte> s)
		{
			s[0] = Kind;
			BitConverter.TryWriteBytes(s.Slice(1), FrameID);
			BitConverter.TryWriteBytes(s.Slice(5), BlockID);
			BitConverter.TryWriteBytes(s.Slice(9), TileCount);
			BitConverter.TryWriteBytes(s.Slice(13), TileIndex);
			BitConverter.TryWriteBytes(s.Slice(17), Length);
			s[21] = Options;
			Data.CopyTo(s.Slice(22));
		}

		public readonly byte Kind;
		public readonly int FrameID;
		public readonly int BlockID;
		public readonly int TileCount;
		public readonly int TileIndex;
		public readonly ushort Length;
		public readonly byte Options;
		public readonly Span<byte> Data;

		public int LENGTH => 20 + Data.Length;
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
		public STATUS(bool ask, int fid, int bid, int tc, Span<byte> s)
		{
			Kind = ask ? (byte)Lead.AskStatus : (byte)Lead.Status;
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
			TileMap = s.Slice(13);
		}

		public void Write(Span<byte> s)
		{
			s[0] = Kind;
			BitConverter.TryWriteBytes(s.Slice(1), FrameID);
			BitConverter.TryWriteBytes(s.Slice(5), BlockID);
			BitConverter.TryWriteBytes(s.Slice(9), TileCount);
			TileMap.CopyTo(s.Slice(13));
		}

		public static void Make(bool ask, int fid, int bid, int tc, Span<byte> map, Span<byte> s)
		{
			s[0] = ask ? (byte)Lead.AskStatus : (byte)Lead.Status;
			BitConverter.TryWriteBytes(s.Slice(1), fid);
			BitConverter.TryWriteBytes(s.Slice(5), bid);
			BitConverter.TryWriteBytes(s.Slice(9), tc);
			if (map != null) map.CopyTo(s.Slice(13));
		}

		public readonly byte Kind;
		public readonly int FrameID;
		public readonly int BlockID;
		public readonly int TileCount;
		public readonly Span<byte> TileMap;

		public int LENGTH => 13 + (TileMap != null ? TileMap.Length : 0);
	}
}
