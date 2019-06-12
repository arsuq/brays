using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Runtime.CompilerServices;

namespace brays
{
	/// <summary>
	/// Exchange processing unit
	/// </summary>
	public class XPU : IDisposable
	{
		public XPU(XCfg cfg)
		{
			this.cfg = cfg;
			this.beamer = new RayBeamer(onReceive, cfg.bcfg);
			resAPIs = new ConcurrentDictionary<string, Action<Exchange>>();
			refAwaits = new ConcurrentDictionary<int, ExchangeAwait>();

			if (cfg.log != null)
				log = new Log(cfg.log.LogFilePath, cfg.log.Ext,
					cfg.log.RotationLogFileKB, cfg.log.RotateLogAtStart);
			else
				cfg.log = new XLogCfg(null, false, 0);

			cleanupTask = cleanup();

			RegisterAPI(API_LIST, listAPIs);
		}

		/// <summary>
		/// Disposes the underlying Beamer. 
		/// Safe for concurrent calls.
		/// </summary>
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref isDisposed, 1, 0) == 0)
				try
				{
					beamer.Dispose();
				}
				catch { }
		}

		public async Task<bool> Start(IPEndPoint listen, IPEndPoint target) =>
			await beamer.LockOn(listen, target);

		public void Stop()
		{
			beamer.Stop();
		}

		public Task<Exchange> Trigger(string res, int timeoutMS = -1)
		{
			var id = Interlocked.Increment(ref exchangeID);
			var ox = new Exchange(this, id, 0, 0, 0, 0, 0, res, default, cfg.outHighway);
			var tc = new TaskCompletionSource<Exchange>();

			if (timeoutMS > -1) Task.Delay(timeoutMS).ContinueWith((_) =>
				tc.TrySetResult(new Exchange(XPUErrorCode.Timeout)));

			refAwaits.TryAdd(ox.ID, new ExchangeAwait((ix) => tc.TrySetResult(ix)));

			Task.Run(() =>
			{
				if (!beamer.Beam(ox.Fragment).Result)
					tc.TrySetResult(new Exchange(XPUErrorCode.NotBeamed));
			});

			return tc.Task;
		}

		public Task<Exchange> Request<O>(string res, O arg,
			SerializationType st = SerializationType.Binary, int timeoutMS = -1)
		{
			var id = Interlocked.Increment(ref exchangeID);
			var ox = new Exchange<O>(this, id, 0, 0, st, 0, 0, res, arg, cfg.outHighway).Instance;
			var tc = new TaskCompletionSource<Exchange>();

			if (timeoutMS > -1) Task.Delay(timeoutMS).ContinueWith((_) =>
				tc.TrySetResult(new Exchange(XPUErrorCode.Timeout)));

			refAwaits.TryAdd(ox.ID, new ExchangeAwait((ix) =>
				tc.TrySetResult(new Exchange(this, ix.Fragment))));

			Task.Run(() =>
			{
				trace(XTraceOps.xOut, ox.ID, $"F: {ox.ExchangeFlags}");

				if (!beamer.Beam(ox.Fragment).Result)
					tc.TrySetResult(new Exchange(XPUErrorCode.NotBeamed));
			});

			return tc.Task;
		}

		public Task<Exchange<I>> Request<I>(string res,
			SerializationType st = SerializationType.Binary, int timeoutMS = -1)
		{
			var id = Interlocked.Increment(ref exchangeID);
			var ox = new Exchange(this, id, 0, 0, st, 0, 0, res, default, cfg.outHighway);
			var tc = new TaskCompletionSource<Exchange<I>>();

			if (timeoutMS > -1) Task.Delay(timeoutMS).ContinueWith((_) =>
				tc.TrySetResult(new Exchange<I>(XPUErrorCode.Timeout)));

			refAwaits.TryAdd(ox.ID, new ExchangeAwait((ix) =>
				tc.TrySetResult(new Exchange<I>(this, ix.Fragment))));

			Task.Run(() =>
			{
				trace(XTraceOps.xOut, ox.ID, $"F: {ox.ExchangeFlags}");

				if (!beamer.Beam(ox.Fragment).Result)
					tc.TrySetResult(new Exchange<I>(XPUErrorCode.NotBeamed));
			});

			return tc.Task;
		}

		public Task<Exchange<I>> Request<I, O>(string res, O arg,
			SerializationType st = SerializationType.Binary, int timeoutMS = -1)
		{
			var id = Interlocked.Increment(ref exchangeID);
			var ox = new Exchange<O>(this, id, 0, 0, st, 0, 0, res, arg, cfg.outHighway).Instance;
			var tc = new TaskCompletionSource<Exchange<I>>();

			if (timeoutMS > -1) Task.Delay(timeoutMS).ContinueWith((_) =>
				tc.TrySetResult(new Exchange<I>(XPUErrorCode.Timeout)));

			refAwaits.TryAdd(ox.ID, new ExchangeAwait((ix) =>
				tc.TrySetResult(new Exchange<I>(this, ix.Fragment))));

			Task.Run(() =>
			{
				trace(XTraceOps.xOut, ox.ID, $"F: {ox.ExchangeFlags}");

				if (!beamer.Beam(ox.Fragment).Result)
					tc.TrySetResult(new Exchange<I>(XPUErrorCode.NotBeamed));
			});

			return tc.Task;
		}

		public Task<bool> Reply<T>(Exchange x, T arg)
		{
			if ((x.ExchangeFlags | ExchangeFlags.ReplyAwaits) != ExchangeFlags.ReplyAwaits)
				throw new ArgumentException("Can't reply because there is no awaiting handler.");

			var id = Interlocked.Increment(ref exchangeID);
			var ox = new Exchange<T>(this,
				id, x.ID, 0, x.SerializationType, 0, 0,
				string.Empty, arg, cfg.outHighway).Instance;

			return beamer.Beam(ox.Fragment);
		}

		public bool RegisterAPI<T>(string key, Action<Exchange<T>> f) =>
			resAPIs.TryAdd(key, (x) =>
			{
				if (x != null) f(new Exchange<T>(this, x.Fragment));
				else f(null);
			});

		public bool RegisterAPI(string key, Action<Exchange> f) => resAPIs.TryAdd(key, (x) => { f(x); });

		public void UnregisterAPI(string key) => resAPIs.TryRemove(key, out _);

		public async Task<IEnumerable<string>> GetRemoteAPIList()
		{
			var id = Interlocked.Increment(ref exchangeID);
			var ox = new Exchange(this, id, 0, 0, SerializationType.Binary, 0, 0, API_LIST, default, cfg.outHighway);

			var ix = await Request<List<string>>(ox.ResID);
			if (ix.IsOK) remoteAPI = ix.Arg;

			return remoteAPI;
		}

		void onReceive(MemoryFragment f)
		{
			using (var x = new Exchange(this, f))
			{
				if (!x.IsValid) return;

				if (x.RefID > 0)
				{
					trace(XTraceOps.xInRef, x.ID, $"R: {x.RefID}");

					if (refAwaits.TryGetValue(x.RefID, out ExchangeAwait xa))
						xa.OnReferred(x);
				}
				else if (resAPIs.TryGetValue(x.ResID, out Action<Exchange> action))
				{
					trace(XTraceOps.xIn, x.ID, $"R: {x.RefID}");
					action(x);
				}
			}
		}

		void listAPIs(Exchange x)
		{
			trace(XTraceOps.xIn, x.ID, API_LIST, string.Empty);

			var API = new List<string>(resAPIs.Keys);

			if (!Reply(x, API).Result)
				trace("Ex", "listAPIs", "Failed to reply");
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void trace(XTraceOps op, int xID, string title, string msg = null) =>
			log.Write(string.Format("{0,12} {1, -10} {2}", xID, op, title), msg);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void trace(string op, string title, string msg = null)
		{
			if (log != null && Volatile.Read(ref cfg.log.IsEnabled))
				log.Write(string.Format("{0,-12} {1, -10} {2}", " ", op, title), msg);
		}

		async Task cleanup()
		{
			await Task.Yield();

			while (true)
			{
				var DTN = DateTime.Now;

				try
				{
					trace("Cleanup", string.Empty);

					foreach (var ra in refAwaits)
						if (ra.Value.OnReferred != null && DateTime.Now.Subtract(ra.Value.Created) > cfg.RepliesTTL)
							refAwaits.TryRemove(ra.Key, out ExchangeAwait _);
				}
				catch (Exception ex)
				{
					trace("Ex", "Cleanup", ex.ToString());
				}

				await Task.Delay(cfg.CleanupFreqMS).ConfigureAwait(false);
			}
		}

		public const string API_LIST = "list-API";

		RayBeamer beamer;
		XPUState state;
		XCfg cfg;
		Log log;
		Task cleanupTask;

		// 2d0: make it a dict and check before a req
		List<string> remoteAPI;
		ConcurrentDictionary<string, Action<Exchange>> resAPIs;
		ConcurrentDictionary<int, ExchangeAwait> refAwaits;

		int isDisposed;
		int exchangeID;
		int stop;
	}
}
