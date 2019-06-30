/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */


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
		CfgReq,
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

	/// <summary>
	/// Where the onReceived and onReceivedAsync callbacks will execute.
	/// </summary>
	public enum CallbackThread
	{
		ThreadPool,
		Task,
		SocketThread
	}

	[Flags]
	public enum LogFlags : int
	{
		None = 0,
		Exception = 1,
		Cleanup = 1 << 2,
		LockOn = 1 << 3,
		Tile = 1 << 4,
		AutoPulse = 1 << 5,
		ReqTiles = 1 << 6,
		Beam = 1 << 7,
		Status = 1 << 8,
		Signal = 1 << 9,
		ProcBlock = 1 << 10,
		ProcError = 1 << 11,
		ProcStatus = 1 << 12,
		ProcSignal = 1 << 13,
		ProcTile = 1 << 14,
		ProcPulse = 1 << 15
#if DEBUG
		, DropFrame = 1 << 16,
#endif
	}
}
