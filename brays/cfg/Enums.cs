
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

	enum FrameOptions : byte
	{
		None,
		ReqAckBlockTransfer
	}

	public enum ErrorCode : int
	{
		NoTranferAck,
	}

	[Flags]
	public enum TraceOps : int
	{
		None = 0,
		ReqAck = 1,
		ReqTiles = 2,
		Beam = 4,
		Status = 8,
		Signal = 16,
		ProcBlock = 32,
		ProcError = 64,
		ProcStatus = 128,
		ProcSignal = 256
#if DEBUG
		,DropFrame = 512
#endif
	}
}
