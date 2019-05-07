
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
	}

	enum SignalKind : int
	{
		NACK,
		ACK,
		UNK,
		ERR
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
		ReqTiles = 1,
		Beam = 2,
		Status = 4,
		Signal = 8,
		ProcBlock = 16,
		ProcError = 32,
		ProcStatus = 64,
		ProcSignal = 128
#if DEBUG
		, DropFrame = 256
#endif
	}
}
