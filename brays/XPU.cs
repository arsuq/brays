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
			map = new ConcurrentDictionary<string, Action<MemoryFragment>>();
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


		public bool RegisterAPI(string key, Action<MemoryFragment> f) => map.TryAdd(key, f);

		public void UnregisterAPI(string key) => map.TryRemove(key, out _);

		public IEnumerable<string> GetRemoteAPIList()
		{
			return null;
		}

		void onReceive(MemoryFragment f)
		{
			var x = new Exchange(f);

			// Not an exchange 
			if (!x.IsValid) f.Dispose();
		}

		RayBeamer beamer;
		XPUState state;
		XCfg cfg;
		ConcurrentDictionary<string, Action<MemoryFragment>> map;

		int isDisposed;
		int exchangeID;
		int stop;
	}
}
