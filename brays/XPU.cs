﻿using System;
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
			trace("Stopped");
		}

		public Task<Exchange> Trigger(string res, int timeoutMS = -1)
		{
			var id = Interlocked.Increment(ref exchangeID);
			var ox = new Exchange(this, id, 0, 0, 0, 0, res, default, cfg.outHighway);

			return trigger(timeoutMS, ox);
		}

		public Task<Exchange> Trigger<O>(string res, O arg,
			SerializationType st = SerializationType.Binary, int timeoutMS = -1)
		{
			var id = Interlocked.Increment(ref exchangeID);
			var xf = XFlags.InArg;
			var ox = new Exchange<O>(this, id, 0, (int)xf, st, 0, res, arg, cfg.outHighway).Instance;

			return trigger(timeoutMS, ox);
		}

		public Task<Exchange<I>> Request<I>(string res,
			SerializationType st = SerializationType.Binary, int timeoutMS = -1)
		{
			var id = Interlocked.Increment(ref exchangeID);
			var xf = XFlags.OutArg;
			var ox = new Exchange(this, id, 0, (int)xf, st, 0, res, default, cfg.outHighway);

			return request<I>(timeoutMS, ox);
		}


		public Task<Exchange<I>> Request<I, O>(string res, O arg,
			SerializationType st = SerializationType.Binary, int timeoutMS = -1)
		{
			var id = Interlocked.Increment(ref exchangeID);
			var xf = XFlags.InArg | XFlags.OutArg;
			var ox = new Exchange<O>(this, id, 0, (int)xf, st, 0, res, arg, cfg.outHighway).Instance;

			return request<I>(timeoutMS, ox);
		}

		public async Task<bool> Reply<T>(Exchange x, T arg, bool disposex = true)
		{
			var id = Interlocked.Increment(ref exchangeID);
			var xf = XFlags.IsReply | XFlags.InArg;

			using (var ox = new Exchange<T>(this,
				id, x.ID, (int)xf, x.SerializationType, 0,
				string.Empty, arg, cfg.outHighway).Instance)
			{
				if (disposex) x.Dispose();
				trace(ox);
				return await beamer.Beam(ox.Fragment);
			}
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
			var ox = new Exchange(this, id, 0, 0, SerializationType.Binary, 0, API_LIST, default, cfg.outHighway);

			var ix = await Request<List<string>>(ox.ResID);
			if (ix.IsOK) remoteAPI = ix.Arg;

			return remoteAPI;
		}

		Task<Exchange<I>> request<I>(int timeoutMS, Exchange ox)
		{
			var tc = new TaskCompletionSource<Exchange<I>>();

			if (timeoutMS > -1) Task.Delay(timeoutMS).ContinueWith((_) =>
			{
				var tx = new Exchange<I>((int)XPUErrorCode.Timeout);
				tc.TrySetResult(tx);
				trace(tx.Instance);
			});

			refAwaits.TryAdd(ox.ID, new ExchangeAwait((ix) =>
			{
				var k = new Exchange<I>(this, ix.Fragment);
				tc.TrySetResult(k);
				trace(k.Instance);
			}));

			Task.Run(() =>
			{
				if (!beamer.Beam(ox.Fragment).Result)
				{
					var nb = new Exchange<I>((int)XPUErrorCode.NotBeamed);
					tc.TrySetResult(nb);
					trace(nb.Instance);
				}
				else
				{
					ox.MarkAsBeamed();
					trace(ox);
				}
			});

			return tc.Task;
		}

		Task<Exchange> trigger(int timeoutMS, Exchange ox)
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

		void onReceive(MemoryFragment f)
		{
			// [!] The disposing is left to the handlers,
			// so that async work wouldn't require a frag copy.

			var x = new Exchange(this, f);

			if (!x.IsValid) return;

			trace(x);

			if (x.RefID > 0)
			{
				if (refAwaits.TryGetValue(x.RefID, out ExchangeAwait xa))
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
			trace(x, API_LIST, string.Empty);

			var API = new List<string>(resAPIs.Keys);

			if (!Reply(x, API).Result)
				trace("Ex", "listAPIs", "Failed to reply");
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void trace(Exchange x, string title = null, string msg = null)
		{
			if (log != null && cfg.log.IsOn(x.ExchangeFlags))
			{
				var isIn = (x.State & XState.Received) == XState.Received ? "i" : "o";

				log.Write(string.Format("{0, 12} {1}: XF: {2} R: {3} EX: {4} RES: {5} {6}",
					x.ID, isIn, (int)x.ExchangeFlags, x.RefID, x.ErrorCode, x.ResID, title), msg);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void trace(string op, string title = null, string msg = null)
		{
			if (log != null && Volatile.Read(ref cfg.log.IsEnabled))
				log.Write(string.Format("{0,-14} {1} {2}", " ", op, title), msg);
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

		List<string> remoteAPI;
		ConcurrentDictionary<string, Action<Exchange>> resAPIs;
		ConcurrentDictionary<int, ExchangeAwait> refAwaits;

		int isDisposed;
		int exchangeID;
		int stop;
	}
}
