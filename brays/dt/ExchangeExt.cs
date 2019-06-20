using System;
using System.Threading.Tasks;

namespace brays
{
	public static class ExchangeExt
	{
		public static Task<Exchange> Trigger(this Exchange ox, TimeSpan timeout = default) =>
			ox.XPU.Trigger(ox, timeout);

		public static Task<Exchange<I>> Request<I>(this Exchange ox, TimeSpan timeout = default) =>
			ox.XPU.Request<I>(ox, timeout);

		public static Task<bool> Reply<T>(this Exchange x, T arg, bool disposex = true) =>
			x.XPU.Reply<T>(x, arg, disposex);
	}
}
