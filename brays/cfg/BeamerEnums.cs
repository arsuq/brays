﻿
using System;

namespace brays
{
	enum Lead : byte
	{
		Probe,
		ProbeReq,
		Signal,
		Error,
		Block,
		Status,
		Cfg,
		Tile,
		Pulse
	}

	public enum SignalKind : int
	{
		NOTSET = 0,
		ACK = 1,
		NACK = 2,
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
		Tile = 1,
		AutoPulse = 1 << 2,
		ReqTiles = 1 << 3,
		Beam = 1 << 4,
		Status = 1 << 5,
		Signal = 1 << 6,
		ProcBlock = 1 << 7,
		ProcError = 1 << 8,
		ProcStatus = 1 << 9,
		ProcSignal = 1 << 10,
		ProcTile = 1 << 11,
		ProcPulse = 1 << 12
#if DEBUG
		, DropFrame = 1 << 13,
#endif
	}
}
