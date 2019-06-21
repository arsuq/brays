/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;

namespace brays
{
	[Flags]
	public enum XFlags : int
	{
		NotSet = 0,
		IsReply = 1,
		DoNotReply = 2,
		NoSerialization = 4
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
