/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

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
			resAPIs = new ConcurrentDictionary<string, ResFunction>();
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

		/// <summary>
		/// Locks on the target endpoint.
		/// </summary>
		/// <param name="listen">The local endpoint</param>
		/// <param name="target">The remote endpoint</param>
		/// <returns>True if succeeds</returns>
		public bool Start(IPEndPoint listen, IPEndPoint target) =>
			 beamer.LockOn(listen, target);

		/// <summary>
		/// Locks on the target endpoint and awaits the target to become active.
		/// </summary>
		/// <param name="listen">The local endpoint</param>
		/// <param name="target">The remote endpoint</param>
		/// <param name="awaitMS">The beamer's TargetIsActive() timeout.</param>
		/// <returns>True if succeeds</returns>
		public Task<bool> Start(IPEndPoint listen, IPEndPoint target, int awaitMS) =>
			 beamer.LockOn(listen, target, awaitMS);

		/// <summary>
		/// Requests and awaits for a probe to arrive.
		/// </summary>
		/// <param name="awaitMS">The timeout.</param>
		/// <returns></returns>
		public Task<bool> TargetIsActive(int awaitMS = -1) => beamer.TargetIsActive(awaitMS);

		public Task<Exchange> Request(Exchange ox, TimeSpan timeout = default)
		{
			var tc = new TaskCompletionSource<Exchange>();

			if (timeout != default && timeout != Timeout.InfiniteTimeSpan) Task.Delay(timeout).ContinueWith((_) =>
			{
				var tx = new Exchange((int)XErrorCode.Timeout);
				if (tc.TrySetResult(tx)) trace(tx);
			});

			if (!ox.DoNotReply)
			{
				refAwaits.TryAdd(ox.ID, new ExchangeAwait((ix) =>
				{
					if (tc.TrySetResult(ix)) trace(ix);
				}));
			}

			Task.Run(async () =>
			{
				if (await beamer.Beam(ox.Fragment))
				{
					ox.Mark(XState.Beamed);
					trace(ox);
					if (ox.DoNotReply) tc.TrySetResult(ox);
				}
				else
				{
					var nb = new Exchange((int)XErrorCode.NotBeamed);
					if (tc.TrySetResult(nb)) trace(nb);
				}
			});

			return tc.Task;
		}

		public Task<Exchange> Request(string res, bool doNotReply = false, TimeSpan timeout = default)
		{
			var flags = doNotReply ? (int)XFlags.DoNotReply : 0;
			return Request(new Exchange(this, 0, flags, 0, res, default, cfg.outHighway), timeout);
		}

		public Task<Exchange> Request<O>(string res, O arg, bool doNotReply = false, TimeSpan timeout = default)
		{
			var flags = doNotReply ? (int)XFlags.DoNotReply : 0;
			return Request(new Exchange<O>(this, 0, flags, 0, res, arg, cfg.outHighway), timeout);
		}

		public Task<Exchange> RawRequest(string res, Span<byte> arg, bool doNotReply = false, TimeSpan timeout = default)
		{
			XFlags flags = XFlags.NoSerialization;

			if (doNotReply) flags = flags | XFlags.DoNotReply;

			return Request(new Exchange(this, 0, (int)flags, 0, res, arg, cfg.outHighway), timeout);
		}

		public Task<Exchange> Reply<O>(Exchange x, O arg, int errorCode = 0, bool doNotReply = false,
			bool disposex = true, TimeSpan timeout = default)
		{
			if (disposex) x.Dispose();

			XFlags flags = XFlags.IsReply;
			if (doNotReply) flags = flags | XFlags.DoNotReply;

			return Request(new Exchange<O>(this, x.ID, (int)flags, errorCode, string.Empty, arg, cfg.outHighway), timeout);
		}

		public Task<Exchange> RawReply(Exchange x, Span<byte> arg, bool doNotReply = false,
			bool disposex = true, TimeSpan timeout = default)
		{
			if (disposex) x.Dispose();

			XFlags flags = XFlags.IsReply | XFlags.NoSerialization;
			if (doNotReply) flags = flags | XFlags.DoNotReply;

			return Request(new Exchange(this, x.ID, (int)flags, 0, string.Empty, arg, cfg.outHighway), timeout);
		}

		public Task<Exchange> RawReply(Exchange x, int errorCode, bool disposex = true, TimeSpan timeout = default)
		{
			if (disposex) x.Dispose();

			var flags = XFlags.IsReply | XFlags.NoSerialization | XFlags.DoNotReply;

			return Request(new Exchange(this, x.ID, (int)flags, errorCode, string.Empty, default, cfg.outHighway), timeout);
		}

		public bool RegisterAPI(string key, Action<Exchange> f) => resAPIs.TryAdd(key, new ResFunction(f));

		public bool RegisterAPI(string key, Func<Exchange, Task> f) => resAPIs.TryAdd(key, new ResFunction(f));

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
			// [1] The disposing is left to the handlers,
			// so that async work wouldn't require a fragment copy.

			// [2] The thread this method will execute on is determined by
			// the beamer's config ScheduleCallbacksOn value.

			// [3] Unhandled exceptions, aggregated or not, will be caught 
			// by the beamer's scheduler and the fragment will be disposed. 

			var x = new Exchange(this, f);

			if (!x.IsValid) return;

			try
			{
				trace(x);

				if (x.IsReply)
				{
					// [i] The refAwaits are handled once.

					if (refAwaits.TryRemove(x.RefID, out ExchangeAwait xa))
					{
						x.Mark(XState.Processing);
						xa.OnReferred(x);
					}
					else x.Dispose();
				}
				else if (resAPIs.TryGetValue(x.ResID, out ResFunction rf))
				{
					x.Mark(XState.Processing);

					if (rf.Func != null) rf.Func(x);
					else rf.Action(x);
				}
				else if (!x.DoNotReply) x.RawReply((int)XErrorCode.ResourceNotFound);
			}
			catch (Exception ex)
			{
				if (!x.DoNotReply)
				{
					if (cfg.PropagateExceptions) x.Reply(ex, (int)XErrorCode.SerializedException);
					else x.RawReply((int)XErrorCode.General);
				}

				throw;
			}
		}

		void listAPIs(Exchange x)
		{
			trace(x);

			var API = new List<string>(resAPIs.Keys);

			if (!Reply(x, API).Result.IsOK)
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
		ConcurrentDictionary<string, ResFunction> resAPIs;
		ConcurrentDictionary<int, ExchangeAwait> refAwaits;

		int exchangeID;
		int isDisposed;
	}
}
