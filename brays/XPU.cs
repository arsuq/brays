using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

namespace brays
{
	public class XPU : IDisposable
	{
		public XPU(BeamerCfg bcfg, XCfg cfg)
		{
			this.cfg = cfg;
			this.beamer = new RayBeamer(onReceive, bcfg);
			map = new ConcurrentDictionary<string, Action<Exchange>>();
		}

		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref isDisposed, 1, 0) == 0)
				try
				{
					beamer.Dispose();
				}
				catch { }
		}

		public async Task<bool> Start(IPEndPoint listen, IPEndPoint target) => await beamer.LockOn(listen, target);

		public void Stop()
		{
			beamer.Stop();
		}

		public void QueueExchange<T>(string resID, T data, SerializationType st, Action<MemoryFragment> onReply)
		{

		}

		public void QueueExchange(Exchange x, Action<Exchange> onReply)
		{

		}

		public async Task ExchangeAsync(Exchange x)
		{

		}


		public bool RegisterAPI(string key, Action<Exchange> f) => map.TryAdd(key, f);

		public void UnregisterAPI(string key) => map.TryRemove(key, out _);

		public IEnumerable<string> GetRemoteAPIList()
		{
			return null;
		}

		void onReceive(MemoryFragment f)
		{
			using (var x = new Exchange(f))
				if (x.IsValid && map.TryGetValue(x.resID, out Action<Exchange> action))
					action(x);
		}

		RayBeamer beamer;
		XPUState state;
		XCfg cfg;
		ConcurrentDictionary<string, Action<Exchange>> map;

		int isDisposed;
		int exchangeID;
		int stop;
	}
}
