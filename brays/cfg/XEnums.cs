﻿using System;

namespace brays
{
	[Flags]
	public enum XFlags : int
	{
		NotSet = 0,
		InArg = 1,
		OutArg = 2,
		IsReply = 4
	}

	public enum XState : byte
	{
		Created = 0,
		Beamed = 1,
		Received = 2,
		Processing = 3,
		Faulted = 4,
		Disposed = 5
	}

	public enum XPUErrorCode : int
	{
		NotSet = 0,
		NotBeamed,
		Timeout,
		Deserialization
	}
}
