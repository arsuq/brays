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
			this.beamer = new Beamer(onReceive, cfg.bcfg);
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
			await beamer.LockOn(listen, target).ConfigureAwait(false);

		public void Stop()
		{
			beamer.Stop();
			trace("Stopped");
		}

		public Task<Exchange> Trigger(Exchange ox, int timeoutMS = -1)
		{
			var tc = new TaskCompletionSource<Exchange>();

			if (timeoutMS > -1) Task.Delay(timeoutMS).ContinueWith((_) =>
			{
				var tx = new Exchange((int)XPUErrorCode.Timeout);
				tc.TrySetResult(tx);
				trace(tx);
			});

			refAwaits.TryAdd(ox.ID, new ExchangeAwait((ix) =>
			{
				tc.TrySetResult(ix);
				trace(ix);
			}));

			Task.Run(() =>
			{
				if (!beamer.Beam(ox.Fragment).Result)
				{
					var nb = new Exchange((int)XPUErrorCode.NotBeamed);
					tc.TrySetResult(nb);
					trace(nb);
				}
				else
				{
					ox.MarkAsBeamed();
					trace(ox);
				}
			});

			return tc.Task;
		}

		public Task<Exchange<I>> Request<I>(Exchange ox, int timeoutMS = -1)
		{
			var tc = new TaskCompletionSource<Exchange<I>>();

			if (timeoutMS > -1) Task.Delay(timeoutMS).ContinueWith((_) =>
			{
				var tx = new Exchange<I>((int)XPUErrorCode.Timeout);
				tc.TrySetResult(tx);
				trace(tx);
			});

			refAwaits.TryAdd(ox.ID, new ExchangeAwait((ix) =>
			{
				var k = new Exchange<I>(this, ix.Fragment);
				tc.TrySetResult(k);
				trace(k);
			}));

			Task.Run(() =>
			{
				if (!beamer.Beam(ox.Fragment).Result)
				{
					var nb = new Exchange<I>((int)XPUErrorCode.NotBeamed);
					tc.TrySetResult(nb);
					trace(nb);
				}
				else
				{
					ox.MarkAsBeamed();
					trace(ox);
				}
			});

			return tc.Task;
		}

		public Task<Exchange> Trigger(string res, int timeoutMS = -1) =>
			 Trigger(new Exchange(this, 0, 0, 0, res, default, cfg.outHighway), timeoutMS);

		public Task<Exchange> Trigger<O>(string res, O arg, int timeoutMS = -1) =>
			 Trigger(new Exchange<O>(this, 0, (int)XFlags.InArg, 0, res, arg, cfg.outHighway), timeoutMS);

		public Task<Exchange<I>> Request<I>(string res, int timeoutMS = -1) =>
			 Request<I>(new Exchange(this, 0, (int)XFlags.OutArg, 0, res, default, cfg.outHighway), timeoutMS);

		public Task<Exchange<I>> Request<I, O>(string res, O arg, int timeoutMS = -1) =>
			 Request<I>(new Exchange<O>(this,
					0, (int)(XFlags.InArg | XFlags.OutArg),
					0, res, arg, cfg.outHighway), timeoutMS);

		public async Task<bool> Reply<T>(Exchange x, T arg, bool disposex = true)
		{
			var xf = XFlags.IsReply | XFlags.InArg;
			var r = false;

			using (Exchange ox = new Exchange<T>(this,
				x.ID, (int)xf, 0, string.Empty,
				arg, cfg.outHighway))
			{
				if (disposex) x.Dispose();

				if (await beamer.Beam(ox.Fragment).ConfigureAwait(false))
				{
					ox.MarkAsBeamed();
					trace(ox);
					r = true;
				}
				else trace("Ex", "Failed to beam a reply");
			}

			return r;
		}

		public bool RegisterAPI<T>(string key, Action<Exchange<T>> f) =>
			resAPIs.TryAdd(key, (x) =>
			{
				if (x != null) f(new Exchange<T>(this, x.Fragment));
				else f(null);
			});

		public bool RegisterAPI(string key, Action<Exchange> f) => resAPIs.TryAdd(key, f);

		public void UnregisterAPI(string key) => resAPIs.TryRemove(key, out _);

		public async Task<IEnumerable<string>> GetRemoteAPIList()
		{
			var ox = new Exchange(this, 0, 0, 0, API_LIST, default, cfg.outHighway);
			var ix = await Request<List<string>>(ox.ResID).ConfigureAwait(false);

			if (ix.IsOK) remoteAPI = ix.Arg;

			return remoteAPI;
		}

		internal int nextExchangeID() => Interlocked.Increment(ref exchangeID);

		void onReceive(MemoryFragment f)
		{
			// [!] The disposing is left to the handlers,
			// so that async work wouldn't require a fragment copy.

			var x = new Exchange(this, f);

			if (!x.IsValid) return;

			trace(x);

			if (x.RefID > 0)
			{
				// [i] The refAwaits should be handled only once.

				if (refAwaits.TryRemove(x.RefID, out ExchangeAwait xa))
				{
					x.MarkAsProcessing();
					xa.OnReferred(x);
				}
				else
				{
					trace(x, "Disposing, no refAwait was found.");
					x.Dispose();
				}
			}
			else if (resAPIs.TryGetValue(x.ResID, out Action<Exchange> action))
			{
				x.MarkAsProcessing();
				action(x);
			}
		}

		void listAPIs(Exchange x)
		{
			trace(x);

			var API = new List<string>(resAPIs.Keys);

			if (!Reply(x, API).Result)
				trace("Ex", "listAPIs", "Failed to reply");
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void trace(Exchange x, string title = null, string msg = null)
		{
			// [!!] DO NOT CHANGE THE FORMAT OFFSETS
			// The logmerge tool relies on hard-coded positions to parse the log lines.

			var state = x.State;
			var flags = x.ExchangeFlags;

			if (log != null && cfg.log.IsOn(flags, state))
			{
				var dir = " ";

				if (state == XState.Received) dir = "i";
				else if (state == XState.Created || state == XState.Beamed) dir = "o";

				log.Write(string.Format("{0, 11}:{1} @{2, 11} F: {3} E: {4} S: {5, -10} RES: {6} {7}",
					x.ID, dir, x.RefID, (int)flags, x.ErrorCode, state, x.ResID, title), msg);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void trace(string op, string title = null, string msg = null)
		{
			if (log != null && Volatile.Read(ref cfg.log.IsEnabled))
				log.Write(string.Format("{0,13} {1} {2}", " ", op, title), msg);
		}

		async Task cleanup()
		{
			await Task.Yield();

			while (true)
			{
				var DTN = DateTime.Now;

				try
				{
#if DEBUG
					trace("Cleanup", string.Empty);
#endif
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

		Beamer beamer;
		XCfg cfg;
		Log log;
		Task cleanupTask;

		List<string> remoteAPI;
		ConcurrentDictionary<string, Action<Exchange>> resAPIs;
		ConcurrentDictionary<int, ExchangeAwait> refAwaits;

		int exchangeID;
		int isDisposed;
	}
}
