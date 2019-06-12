using System;

namespace brays
{
	public enum SerializationType
	{
		None,
		Binary,
		Json,
		Xml
	}

	[Flags]
	public enum ExchangeFlags : byte
	{
		NotSet = 0,
		ReplyAwaits = 1
	}

	[Flags]
	public enum XTraceOps : int
	{
		None = 0,
		xIn = 1,
		xInRef = 1 << 2,
		xInError = 1 << 3,
		xOut = 1 << 4
	}

	public enum XPUState
	{
		Idle,
		Connected,
		Faulted
	}

	public enum XPUErrorCode : int
	{
		NotSet = 0,
		NotBeamed,
		Timeout,
		Deserialization
	}
}
