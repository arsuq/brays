/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Threading.Tasks;

namespace brays
{
	public static class ExchangeExt
	{
		public static Task<Exchange> Reply<T>(this Exchange x, T arg, bool doNotReply = false, bool disposex = true) =>
			x.XPU.Reply<T>(x, arg, doNotReply, disposex);

		public static Task<Exchange> ReplyRaw(this Exchange x, Span<byte> arg, bool doNotReply = false, bool disposex = true) =>
			x.XPU.ReplyRaw(x, arg, doNotReply, disposex);
	}
}
