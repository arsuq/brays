/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Threading.Tasks;

namespace brays
{
	public static class ExchangeExt
	{
		public static Task<Exchange> Reply<T>(this Exchange x, T arg, int errorCode = 0, bool doNotReply = false,
			bool disposex = true, TimeSpan timeout = default) =>
			x.XPU.Reply<T>(x, arg, errorCode, doNotReply, disposex, timeout);

		public static Task<Exchange> RawReply(this Exchange x, Span<byte> arg,
			bool doNotReply = false, bool disposex = true, TimeSpan timeout = default) =>
			x.XPU.RawReply(x, arg, doNotReply, disposex, timeout);

		public static Task<Exchange> RawReply(this Exchange x, int errorCode, bool disposex = true,
			TimeSpan timeout = default) => x.XPU.RawReply(x, errorCode, disposex, timeout);
	}
}
