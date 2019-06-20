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
			this.beamer = new Beamer(onReceive, cfg.bcfg);
			resAPIs = new ConcurrentDictionary<string, Action<Exchange>>();
			refAwaits = new ConcurrentDictionary<int, ExchangeAwait>();

			if (cfg.log != null && cfg.log.IsEnabled)
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
					beamer?.Dispose();
					cfg?.outHighway?.Dispose();
				}
				catch { }
		}

		public async Task<(bool, Exception)> Start(IPEndPoint listen, IPEndPoint target) =>
			await beamer.LockOn(listen, target).ConfigureAwait(false);

		public void Stop()
		{
			beamer.Stop();
			trace("Stopped");
		}

		public Task<Exchange> Request(Exchange ox, TimeSpan rplTimeout = default)
		{
			var tc = new TaskCompletionSource<Exchange>();

			if (rplTimeout != default && rplTimeout != Timeout.InfiniteTimeSpan) Task.Delay(rplTimeout).ContinueWith((_) =>
			{
				var tx = new Exchange((int)XPUErrorCode.Timeout);
				if (tc.TrySetResult(tx)) trace(tx);
			});

			refAwaits.TryAdd(ox.ID, new ExchangeAwait((ix) =>
			{
				if (tc.TrySetResult(ix)) trace(ix);
			}));

			Task.Run(async () =>
			{
				if (await beamer.Beam(ox.Fragment))
				{
					ox.Mark(XState.Beamed);
					trace(ox);
				}
				else
				{
					var nb = new Exchange((int)XPUErrorCode.NotBeamed);
					if (tc.TrySetResult(nb)) trace(nb);
				}
			});

			return tc.Task;
		}

		public Task<Exchange> Request(string res, TimeSpan rplTimeout = default) =>
			 Request(new Exchange(this, 0, 0, 0, res, default, cfg.outHighway), rplTimeout);

		public Task<Exchange> Request<O>(string res, O arg, TimeSpan rplTimeout = default) =>
			 Request(new Exchange<O>(this, 0, (int)XFlags.InArg, 0, res, arg, cfg.outHighway), rplTimeout);


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
					ox.Mark(XState.Beamed);
					r = true;
				}
				else ox.Mark(XState.Faulted);

				trace(ox);
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
			var ix = await Request(ox.ResID).ConfigureAwait(false);

			if (ix.IsOK) remoteAPI = ix.Make<List<string>>();

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
					x.Mark(XState.Processing);
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
				x.Mark(XState.Processing);
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
