
using System;

namespace brays
{
	enum Lead : byte
	{
		Probe,
		Signal,
		Error,
		Block,
		Status,
		CfgX,
		TileX
	}

	enum SignalKind : int
	{
		NOTSET = 0,
		NACK = 1,
		ACK = 2,
		UNK = 3,
		ERR = 4
	}

	public enum ErrorCode : int
	{
		Unknown,
		Rejected
	}

	[Flags]
	public enum TraceOps : int
	{
		None = 0,
		TileX = 1,
		ReqTiles = 2,
		Beam = 4,
		Status = 8,
		Signal = 16,
		ProcBlock = 32,
		ProcError = 64,
		ProcStatus = 128,
		ProcSignal = 256,
		ProcTileX = 512
#if DEBUG
		, DropFrame = 1024
#endif
	}
}
