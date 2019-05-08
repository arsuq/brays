
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
		CfgX
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
		CfgX = 1,
		ReqTiles = 2,
		Beam = 4,
		Status = 8,
		Signal = 16,
		ProcBlock = 32,
		ProcError = 64,
		ProcStatus = 128,
		ProcSignal = 256,
		ProcCfgX = 512
#if DEBUG
		, DropFrame = 1024
#endif
	}
}
