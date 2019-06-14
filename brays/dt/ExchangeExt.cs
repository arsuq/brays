using System.Threading.Tasks;

namespace brays
{
	public static class ExchangeExt
	{
		public static Task<Exchange> Trigger(this Exchange ox, int timeoutMS = -1) =>
			ox.XPU.Trigger(ox, timeoutMS);

		public static Task<Exchange<I>> Request<I>(this Exchange ox, int timeoutMS = -1) =>
			ox.XPU.Request<I>(ox, timeoutMS);

		public static Task<bool> Reply<T>(this Exchange x, T arg, bool disposex = true) =>
			x.XPU.Reply<T>(x, arg, disposex);
	}
}
