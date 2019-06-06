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
	public enum XTraceOps : int
	{
		None = 0,
		Exchange = 1,
		Request = 1 << 2,
		Reply = 1 << 3,
		Error = 1 << 4
	}

	public enum XPUState
	{
		Idle,
		Connected,
		Faulted
	}
}
