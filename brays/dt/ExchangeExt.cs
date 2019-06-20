/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Threading.Tasks;

namespace brays
{
	public static class ExchangeExt
	{
		public static Task<Exchange> Trigger(this Exchange ox, TimeSpan timeout = default) =>
			ox.XPU.Request(ox, timeout);

		public static Task<bool> Reply<T>(this Exchange x, T arg, bool disposex = true) =>
			x.XPU.Reply<T>(x, arg, disposex);
	}
}
