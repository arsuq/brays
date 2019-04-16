using System;

namespace brays
{
	enum Lead : byte
	{
		Probe = 1,
		Signal = 2,
		Block = 3,
		AskTile = 4,
		AskBlock = 5,
		Status = 6,
		AskStatus = 7
	}

	enum SignalKind : int
	{
		NACK = 0,
		ACK = 1,
		WTF = 2
	}

	readonly ref struct FRAME
	{
		public FRAME(int fid, int bid, int tc, int ti, ushort l, byte o, Span<byte> s)
		{
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
			FrameID = BitConverter.ToInt32(s);
			BlockID = BitConverter.ToInt32(s.Slice(4));
			TileCount = BitConverter.ToInt32(s.Slice(8));
			TileIndex = BitConverter.ToInt32(s.Slice(12));
			Length = BitConverter.ToUInt16(s.Slice(16));
			Options = s[20];
			Data = s.Slice(21);
		}

		public void Write(Span<byte> s)
		{
			BitConverter.TryWriteBytes(s, FrameID);
			BitConverter.TryWriteBytes(s.Slice(4), BlockID);
			BitConverter.TryWriteBytes(s.Slice(8), TileCount);
			BitConverter.TryWriteBytes(s.Slice(12), TileIndex);
			BitConverter.TryWriteBytes(s.Slice(16), Length);
			s[20] = Options;
			Data.CopyTo(s.Slice(21));
		}

		public readonly int FrameID;
		public readonly int BlockID;
		public readonly int TileCount;
		public readonly int TileIndex;
		public readonly ushort Length;
		public readonly byte Options;
		public readonly Span<byte> Data;
	}

	readonly ref struct SIGNAL
	{
		public SIGNAL(byte lead, int frameID, int refID, int mark)
		{
			Lead = lead;
			FrameID = frameID;
			RefID = refID;
			Mark = mark;
		}

		public SIGNAL(Span<byte> s)
		{
			Lead = s[0];
			FrameID = BitConverter.ToInt32(s.Slice(1));
			RefID = BitConverter.ToInt32(s.Slice(5));
			Mark = BitConverter.ToInt32(s.Slice(9));
		}

		public void Write(Span<byte> s)
		{
			s[0] = Lead;
			BitConverter.TryWriteBytes(s.Slice(1), FrameID);
			BitConverter.TryWriteBytes(s.Slice(5), RefID);
			BitConverter.TryWriteBytes(s.Slice(9), Mark);
		}

		public readonly byte Lead;
		public readonly int FrameID;
		public readonly int RefID;
		public readonly int Mark;
	}

	readonly ref struct STATUS
	{
		public STATUS(int fid, int bid, int tc, int ti)
		{
			FrameID = fid;
			BlockID = bid;
			TileCount = tc;
			TileIndex = ti;
		}

		public STATUS(Span<byte> s)
		{
			FrameID = BitConverter.ToInt32(s);
			BlockID = BitConverter.ToInt32(s.Slice(4));
			TileCount = BitConverter.ToInt32(s.Slice(8));
			TileIndex = BitConverter.ToInt32(s.Slice(12));
		}

		public void Write(Span<byte> s)
		{
			BitConverter.TryWriteBytes(s, FrameID);
			BitConverter.TryWriteBytes(s.Slice(4), BlockID);
			BitConverter.TryWriteBytes(s.Slice(8), TileCount);
			BitConverter.TryWriteBytes(s.Slice(12), TileIndex);
		}

		public readonly int FrameID;
		public readonly int BlockID;
		public readonly int TileCount;
		public readonly int TileIndex;
	}
}
