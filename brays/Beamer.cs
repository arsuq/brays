/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("brays.tests")]

namespace brays
{
	/// <summary>
	/// A socket.
	/// </summary>
	public class Beamer : IDisposable
	{
		/// <summary>
		/// Wires the config, but does not create the socket. 
		/// </summary>
		/// <param name="onReceive">The consumer's handler.</param>
		/// <param name="cfg">The beamer configuration</param>
		public Beamer(Action<MemoryFragment> onReceive, BeamerCfg cfg) : this(cfg)
		{
			this.onReceive = onReceive ?? throw new ArgumentNullException("onReceive");
		}

		/// <summary>
		/// Wires the config, but does not create the socket. 
		/// </summary>
		/// <param name="onReceive">The consumer's handler.</param>
		/// <param name="cfg">The beamer configuration</param>
		public Beamer(Func<MemoryFragment, Task> onReceiveAsync, BeamerCfg cfg) : this(cfg)
		{
			this.onReceiveAsync = onReceiveAsync ?? throw new ArgumentNullException("onReceiveAsync");
		}

		Beamer(BeamerCfg cfg)
		{
			if (cfg == null) throw new ArgumentNullException("cfg");

			ID = Guid.NewGuid();
			this.cfg = cfg;
			outHighway = cfg.OutHighway;
			blockHighway = cfg.ReceiveHighway;
			tileXHighway = cfg.TileExchangeHighway;

			if (cfg.Log != null && cfg.Log.IsEnabled)
				log = new Log(cfg.Log.LogFilePath, cfg.Log.Ext, cfg.Log.RotationLogFileKB, cfg.Log.RotateLogAtStart);
			else
				cfg.Log = new BeamerLogCfg(null, false, 0);
		}

		/// <summary>
		/// Disposes all highways and the socket. Can be called concurrently.
		/// </summary>
		public void Dispose()
		{
			if (Interlocked.CompareExchange(ref isDisposed, 1, 0) == 0)
				try
				{
					if (cancel != null) cancel.Cancel();
					lockOnGate.Exit();
					outHighway?.Dispose();
					blockHighway?.Dispose();
					tileXHighway?.Dispose();
					socket?.Close();
					socket?.Dispose();
					outHighway = null;
					blockHighway = null;
					tileXHighway = null;

					if (log != null) log.Dispose();
					if (blocks != null)
						foreach (var b in blocks.Values)
							if (b != null) b.Dispose();
				}
				catch { }
		}

		/// <summary>
		/// Sends a probe request and awaits for a probe reply.
		/// </summary>
		/// <param name="awaitMS">The default is 2 sec.</param>
		/// <returns>False if no response was received within the awaitMS time interval.</returns>
		public bool Probe(int awaitMS = 2000)
		{
			if (lockOnGate.IsAcquired) lockOnRst.Wait();
			socket.Send(probeReqLead);
			return probeReqAwait.WaitOne(awaitMS);
		}

		/// <summary>
		/// Starts a probing loop and awaits for a response. 
		/// </summary>
		/// <param name="awaitMS">By default waits forever (-1).</param>
		/// <returns>False on timeout.</returns>
		public Task<bool> TargetIsActive(int awaitMS = -1)
		{
			if (socket.ProtocolType == ProtocolType.Tcp) return Task.FromResult(true);

			var tcs = new TaskCompletionSource<bool>();
			ThreadPool.RegisterWaitForSingleObject(probeReqAwait,
				(_, to) => tcs.TrySetResult(!to), null, awaitMS, true);

			Task.Run(async () =>
			{
				try
				{
					while (!tcs.Task.IsCompleted)
					{
						if (lockOnGate.IsAcquired) lockOnRst.Wait();
						socket.Send(probeReqLead);
						await Task.Delay(cfg.ProbeFreqMS).ConfigureAwait(false);
					}
				}
				catch { }
			});

			return tcs.Task;
		}

		/// <summary>
		/// Creates a new socket, starts the listening, cleanup and pulsing tasks. 
		/// If invoked concurrently only the first call will enter, the rest will 
		/// return false immediately.
		/// </summary>
		/// <param name="listen">The local endpoint.</param>
		/// <param name="target">The remote endpoint.</param>
		/// <returns>True on success.</returns>
		public bool LockOn(IPEndPoint listen, IPEndPoint target)
		{
			var r = false;

			if (lockOnGate.Enter())
				try
				{
					Volatile.Write(ref isLocked, false);
					lockOnRst.Reset();

					cancel = new CancellationTokenSource();

					this.source = listen;
					this.target = target;

					if (socket != null) socket.Dispose();
					socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
					socket.Bind(this.source);
					socket.Connect(target);

					socket.ReceiveBufferSize = cfg.ReceiveBufferSize;
					socket.SendBufferSize = cfg.SendBufferSize;

					lockOnRst.Set();

					if (receivers == null)
					{
						receivers = new Task[cfg.MaxConcurrentReceives];

						for (int i = 0; i < receivers.Length; i++)
							receivers[i] = receive();
					}

					if (probingTask == null) probingTask = probe();
					if (cleanupTask == null) cleanupTask = cleanup();
					if (autoPulseTask == null) autoPulseTask = autoPulse();

					if (ccBeams != null) ccBeams.Dispose();
					ccBeams = new SemaphoreSlim(cfg.MaxBeamedTilesAtOnce, cfg.MaxBeamedTilesAtOnce);

					Volatile.Write(ref isLocked, true);

					trace(LogFlags.LockOn, 0, $"{source.ToString()} target: {target.ToString()}");

					r = true;
				}
				catch (Exception ex)
				{
					if (!lockOnRst.IsSet) lockOnRst.Set();
					trace(LogFlags.Exception, 0, ex.Message, ex.ToString());
					throw;
				}
				finally
				{
					lockOnGate.Exit();
				}

			return r;
		}

		/// <summary>
		/// Combines LockOn and TargetIsActive().
		/// </summary>
		/// <param name="listen">The local endpoint.</param>
		/// <param name="target">The remote endpoint.</param>
		/// <param name="awaitMS">Infinite timeout is -1.</param>
		/// <returns>False if either LockOn or TargetIsActive return false.</returns>
		public async Task<bool> LockOn(IPEndPoint listen, IPEndPoint target, int awaitMS)
		{
			if (LockOn(listen, target))
				return await TargetIsActive(awaitMS);

			return false;
		}

		/// <summary>
		/// Awaits for the remote config.
		/// </summary>
		public Task<bool> ConfigRequest() => configExchange(0, true);

		/// <summary>
		/// Updates the target with the current config.
		/// If the listen endpoint setting has changed, the target beamer will LockOn the new one.
		/// </summary>
		public Task<bool> ConfigPush() => configExchange(0, false);

		/// <summary>
		/// Sends a tile in a fire-and-forget manner, i.e. there will be no retries.
		/// </summary>
		/// <param name="data">The bits.</param>
		/// <param name="onTileXAwait">If the tile reaches the target it may reply.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TileXFF(Span<byte> data, Action<MemoryFragment> onTileXAwait) =>
			tilexOnce((byte)Lead.Tile, data, 0, 0, onTileXAwait);

		/// <summary>
		/// Compacts multiple small packets into datagrams of TileSizeBytes size. 
		/// The bits are sent after either the PulseRetentionMS interval expires or enough
		/// bytes are zipped. 
		/// </summary>
		/// <param name="data">The bytes.</param>
		/// <returns>A reset event to wait for a status (SignalKind.ACK).
		/// Note that the resetEvent is shared and will be reset for the next pulse.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ResetEvent Pulse(Span<byte> data) => pack(data);

		/// <summary>
		/// Sends the fragment to the target beamer in BeamRetriesCount retries, awaiting BeamAwaitMS on failure.
		/// </summary>
		/// <param name="f">The data to transfer.</param>
		/// <param name="timeoutMS">By default is infinite (-1).</param>
		/// <returns>False on failure, including a timeout.</returns>
		public async Task<bool> Beam(MemoryFragment f, int timeoutMS = -1)
		{
			Block b = null;

			try
			{
				if (f.Length <= cfg.TileSizeBytes)
					return await tilex((byte)Lead.Tile, f, null, 0, 0, 0, 0, null, timeoutMS)
						.ConfigureAwait(false) == (int)SignalKind.ACK;
				else
				{
					var bid = Interlocked.Increment(ref blockCounter);
					b = new Block(bid, cfg.TileSizeBytes, f);

					if (blocks.TryAdd(bid, b))
					{
						if (timeoutMS > 0) Task.Delay(timeoutMS).ContinueWith((x) => b.beamLoop.SetResult(false));
						beamLoop(b);

						return await b.beamLoop.Task.ConfigureAwait(false);
					}
					else return false;
				}
			}
			catch (AggregateException aex)
			{
				trace(LogFlags.Exception, 0, "Beam", aex.Flatten().ToString());
			}
			catch (Exception ex)
			{
				trace(LogFlags.Exception, 0, "Beam", ex.ToString());
			}

			return false;
		}

		public readonly Guid ID;
		public bool IsStopped => cancel.IsCancellationRequested;
		public bool IsDisposed => Volatile.Read(ref isDisposed) > 0;
		public bool IsTargetLocked => isLocked;
		public int FrameCounter => frameCounter;
		public int ReceivedDgrams => receivedDgrams;
		public IPEndPoint Target => target;
		public IPEndPoint Source => source;
		public DateTime LastProbe => new DateTime(Volatile.Read(ref lastReceivedProbeTick));
		public CfgX GetTargetConfig() => targetCfg.Clone();
		public BeamerCfg Config => cfg;

		async Task<int> beam(Block b)
		{
			// [!!] This method should be invoked once at a time per block because
			// the MemoryFragment bits are mutated. Consequently the block should
			// never be shared outside brays as it's not safe to read while sending.

			int sent = 0;

			for (int i = 0; i < b.tileMap.Count; i++)
				if (!b.tileMap[i])
				{
					int leftpos = 0;

					try
					{
						if (Volatile.Read(ref b.isRejected) || IsStopped)
						{
							trace("Beam", "Stopped");
							return 0;
						}

						// Wait if the socket is reconfiguring.
						if (lockOnGate.IsAcquired) lockOnRst.Wait();

						await ccBeams.WaitAsync();

						var fid = Interlocked.Increment(ref frameCounter);
						var sentBytes = 0;

						// [!] In order to avoid copying the tile data into a FRAME span 
						// just to prepend the FRAME header, the block MemoryFragment 
						// is modified in place. This doesn't work for the first tile. 

						var dgramLen = FRAME.HEADER + b.Fragment.Length - (i * b.TileSize);

						if (dgramLen > b.TileSize + FRAME.HEADER)
							dgramLen = b.TileSize + FRAME.HEADER;

						if (i > 0)
						{
							leftpos = (i * b.TileSize) - FRAME.HEADER;

							b.Fragment.Span()
								.Slice(leftpos, FRAME.HEADER)
								.CopyTo(b.frameHeader);

							// After this call the block fragment will be corrupted
							// until the finally clause.
							FRAME.Make(fid, b.ID, b.TotalSize, i,
								(ushort)(dgramLen - FRAME.HEADER), 0, default,
								b.Fragment.Span().Slice(leftpos, FRAME.HEADER));

							sentBytes = socket.Send(b.Fragment.Span().Slice(leftpos, dgramLen));
						}
						else using (var mf = outHighway.AllocFragment(dgramLen))
							{
								FRAME.Make(fid, b.ID, b.TotalSize, i,
									(ushort)(dgramLen - FRAME.HEADER), 0,
									b.Fragment.Span().Slice(0, dgramLen - FRAME.HEADER), mf);

								sentBytes = socket.Send(mf);
							}

						var logop = ((LogFlags.Beam & cfg.Log.Flags) == LogFlags.Beam && cfg.Log.IsEnabled);

						if (sentBytes == dgramLen)
						{
							if (logop) trace(LogFlags.Beam, fid, $"{sentBytes} B: {b.ID} T: {i}");
							sent++;
						}
						else
						{
							if (logop) trace(LogFlags.Beam, fid, $"Faulted for sending {sentBytes} of {dgramLen} byte dgram.");

							b.isFaulted = true;
							return 0;
						}
					}
					catch (Exception ex)
					{
						trace(LogFlags.Exception, 0, ex.Message, ex.StackTrace);
					}
					finally
					{
						// [!] Restore the original bytes no matter what happens.
						if (i > 0) b.frameHeader.AsSpan().CopyTo(
							b.Fragment.Span().Slice(leftpos, FRAME.HEADER));

						ccBeams.Release();
					}
				}

			b.sentTime = DateTime.Now;

			return await status(b.ID, sent, true, null).ConfigureAwait(false);
		}

		async Task beamLoop(Block b)
		{
			// Force scheduling
			await Task.Yield();

			if (Interlocked.Increment(ref b.isInBeamLoop) > 1) throw new Exception("Concurrent beams");

			try
			{
				var awaitMS = cfg.BeamRetryDelayStartMS;

				for (int i = 0; i < cfg.BeamRetriesCount; i++)
					if (await beam(b).ConfigureAwait(false) == (int)SignalKind.ACK)
					{
						if (Interlocked.CompareExchange(ref b.isRebeamRequestPending, 0, 1) != 1)
							return;
					}
					else
					{
						awaitMS = (int)(awaitMS * cfg.RetryDelayStepMultiplier);
						await Task.Delay(awaitMS).ConfigureAwait(false);
					}

				b.Dispose();
			}
			catch (AggregateException aex)
			{
				trace(LogFlags.Exception, 0, "BeamLoop", aex.Flatten().ToString());
			}
			catch (Exception ex)
			{
				trace(LogFlags.Exception, 0, "BeamLoop", ex.ToString());
			}
			finally
			{
				Interlocked.Decrement(ref b.isInBeamLoop);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void rebeam(Block b)
		{
			Interlocked.CompareExchange(ref b.isRebeamRequestPending, 1, 0);

			if (Volatile.Read(ref b.isInBeamLoop) < 1)
				beamLoop(b);
		}

		async Task<int> status(int blockID, int tilesCount, bool awaitRsp, MemoryFragment tileMap)
		{
			try
			{
				if (IsStopped) return (int)SignalKind.NOTSET;

				var fid = Interlocked.Increment(ref frameCounter);
				var len = (tileMap != null ? tileMap.Length : 0) + STATUS.HEADER;
				var logop = ((LogFlags.Status & cfg.Log.Flags) == LogFlags.Status && cfg.Log.IsEnabled);
				ResetEvent rst = null;
				var rsp = 0;

				if (awaitRsp) rst = new ResetEvent();

				using (var frag = outHighway.AllocFragment(len))
				{
					STATUS.Make(fid, blockID, tilesCount,
						tileMap != null ? tileMap.Span() : default,
						frag.Span());

					var ackq = !awaitRsp || signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
					{
						Volatile.Write(ref rsp, mark);
						if (logop) trace(LogFlags.Status, fid, $"{mark} B: {blockID}");
						rst.Set(true);
					}));

					if (ackq)
					{
						var map = string.Empty;
						var awaitMS = cfg.RetryDelayStartMS;

						if (blocks.TryGetValue(blockID, out Block b) && tileMap != null)
							map = $"M: {b.tileMap.ToString()} ";

						for (int i = 0; i < cfg.SendRetries && !IsStopped; i++)
						{
							if (lockOnGate.IsAcquired) lockOnRst.Wait();

							var sent = socket.Send(frag, SocketFlags.None);

							if (logop)
								if (sent == 0) trace(LogFlags.Status, fid, $"Socket sent 0 bytes. B: {blockID} #{i + 1}");
								else trace(LogFlags.Status, fid, $"B: {blockID} {map} #{i + 1}");

							if (!awaitRsp || await rst.Wait(awaitMS) > 0) break;
							awaitMS = (int)(awaitMS * cfg.RetryDelayStepMultiplier);
						}
					}

					return Volatile.Read(ref rsp);
				}
			}
			catch (Exception ex)
			{
				trace(LogFlags.Exception, 0, "Status", ex.ToString());
			}

			return 0;
		}

		int signal(int refID, int mark, bool isError = false, bool keep = true)
		{
			var fid = Interlocked.Increment(ref frameCounter);
			var signal = new SIGNAL(fid, refID, mark, isError);
			Span<byte> s = stackalloc byte[signal.LENGTH];

			signal.Write(s);
			var sent = socket.Send(s);
			var logop = ((LogFlags.Signal & cfg.Log.Flags) == LogFlags.Signal && cfg.Log.IsEnabled);

			if (sent > 0)
			{
				if (keep) sentSignalsFor.TryAdd(refID, new SignalResponse(signal.Mark, isError));
				if (logop) trace(LogFlags.Signal, fid, $"M: {mark} R: {refID}");
			}
			else if (logop) trace(LogFlags.Signal, fid, $"Failed to send M: {mark} R: {refID}");

			return sent;
		}

		async Task<int> tilex(
			byte kind,
			MemoryFragment data,
			ManualResetEventSlim crst = null,
			int dfrom = 0,
			int dLen = 0,
			int refid = 0,
			int fid = 0,
			Action<MemoryFragment> onTileXAwait = null,
			int timeoutMS = -1)
		{
			// [i] The data could be null if CfgX request.

			var dataLen = data != null ? data.Length : 0;

			if (dataLen > 0 && dLen > 0) dataLen = dLen;
			else dLen = dataLen;

			if (dataLen > cfg.TileSizeBytes) throw new ArgumentException("data size");
			if (IsStopped) return (int)SignalKind.NOTSET;

			var rst = new ResetEvent();

			if (fid == 0) fid = Interlocked.Increment(ref frameCounter);
			var len = dataLen + TILEX.HEADER;
			var logop = ((LogFlags.Tile & cfg.Log.Flags) == LogFlags.Tile && cfg.Log.IsEnabled);
			var rsp = 0;
			var timeoutTicks = timeoutMS > 0 ? DateTime.Now.AddMilliseconds(timeoutMS).Ticks : 0;

			if (onTileXAwait != null) tileXAwaits.TryAdd(fid, new TileXAwait(onTileXAwait));

			// [i] If TCP no need of SignalAwait  

			if (signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
				{
					Volatile.Write(ref rsp, mark);
					rst.Set(true);
				})))
			{
				using (var frag = outHighway.AllocFragment(len))
				{
					if (dataLen > 0)
						TILEX.Make(kind, fid, refid, (ushort)dataLen, data.Span().Slice(dfrom, dLen), frag);
					else
						TILEX.Make(kind, fid, refid, 0, default, frag);

					if (crst != null) crst.Set();

					var awaitMS = cfg.RetryDelayStartMS;

					for (int i = 0; i < cfg.SendRetries && !IsStopped; i++)
					{
						if (timeoutTicks > 0 && DateTime.Now.Ticks >= timeoutTicks) break;
						if (lockOnGate.IsAcquired) lockOnRst.Wait();

						var sent = socket.Send(frag, SocketFlags.None);

						if (logop)
							if (sent == frag.Length) trace(LogFlags.Tile, fid, $"K: {(Lead)kind} b:{sent} #{i + 1}");
							else trace(LogFlags.Tile, fid, $"Failed K: {(Lead)kind}");

						if (await rst.Wait(awaitMS) > 0) break;
						awaitMS = (int)(awaitMS * cfg.RetryDelayStepMultiplier);
					}
				}
			}

			return Volatile.Read(ref rsp);
		}

		void tilexOnce(byte kind, Span<byte> data, int refid, int fid = 0,
			Action<MemoryFragment> onTileXAwait = null)
		{
			if (data.Length > cfg.TileSizeBytes) throw new ArgumentException("data size");
			if (IsStopped) return;

			if (fid == 0) fid = Interlocked.Increment(ref frameCounter);
			var len = data.Length + TILEX.HEADER;
			var logop = ((LogFlags.Tile & cfg.Log.Flags) == LogFlags.Tile && cfg.Log.IsEnabled);

			if (onTileXAwait != null) tileXAwaits.TryAdd(fid, new TileXAwait(onTileXAwait));

			using (var frag = outHighway.AllocFragment(len))
			{
				var tilex = new TILEX(kind, fid, refid, 0, data);

				tilex.Write(frag);

				if (lockOnGate.IsAcquired) lockOnRst.Wait();
				var sent = socket.Send(frag, SocketFlags.None);

				if (logop)
					if (sent == frag.Length) trace(LogFlags.Tile, fid, $"TileXOnce K: {kind}");
					else trace(LogFlags.Tile, fid, $"TileXOnce Failed:  Sent {sent} of {frag.Length} bytes");
			}
		}

		ResetEvent pack(Span<byte> s)
		{
			if (s.Length + USH_LEN > cfg.TileSizeBytes) throw new ArgumentException("Data length");

			// [i] The ResetEvent is shared for one tile pulse.

			lock (packLock)
			{
				if (packTile == null) packTile = outHighway.AllocFragment(cfg.TileSizeBytes);
				if (packTileOffset + USH_LEN + s.Length >= cfg.TileSizeBytes) pulseAndReload();

				if (BitConverter.TryWriteBytes(packTile.Span().Slice(packTileOffset), (ushort)s.Length))
				{
					s.CopyTo(packTile.Span().Slice(packTileOffset + USH_LEN));
					packTileOffset += s.Length + USH_LEN;
					autoPulseRst.Set();

					return packResultRst;
				}
				else throw new Exception("Pack length write.");
			}
		}

		void pulseAndReload()
		{
			lock (packLock)
			{
				if (packTileOffset == 0) return;

				reloadCopyRst.Reset();

				ThreadPool.QueueUserWorkItem(async (x) =>
				{
					var mark = 0;

					try
					{
#if ASSERT
				if (!ASSERT_packTile()) throw new Exception("Corrupt pulse tile.");
#endif
						mark = await tilex((byte)Lead.Pulse, packTile, reloadCopyRst, 0, x.len)
					.ConfigureAwait(false);
					}
					catch (AggregateException aex)
					{
						trace(LogFlags.Exception, 0, "Pulse-TileX", aex.Flatten().ToString());
					}
					finally
					{
						x.rr.Set(mark == (int)SignalKind.ACK);
					}
				}, (rr: packResultRst, len: (ushort)packTileOffset), true);

				// Reload
				reloadCopyRst.Wait();
				packTile.Dispose();
				packTile = outHighway.AllocFragment(cfg.TileSizeBytes);
				packTile.Span().Clear();
				packTileOffset = 0;
			}
		}

#if ASSERT

		bool ASSERT_packTile()
		{
			var pos = 0;
			var len = -1;
			var lim = 0;
			var s = packTile.Span();

			while (pos + 2 < s.Length)
			{
				len = BitConverter.ToUInt16(s.Slice(pos));

				if (len == 0) break;

				pos += 2;
				lim = pos + len - 1;
				var b = s[pos];

				for (; pos <= lim; pos++)
					if (b != s[pos]) return false;
			}

			return true;
		}

#endif

		async Task<bool> configExchange(int reqID, bool isRequest)
		{
			MemoryFragment sf = null;
			ResetEvent rst = null;

			try
			{
				if (!isRequest)
				{
					var cfgx = new CfgX(cfg, source);

					sf = outHighway.AllocFragment(CfgX.LENGTH);
					cfgx.Write(sf);
				}
				else rst = new ResetEvent();

				var fid = Interlocked.Increment(ref frameCounter);

				if (!isRequest) return await tilex((byte)Lead.Cfg, sf, null, 0, 0, reqID, fid)
						.ConfigureAwait(false) == (int)SignalKind.ACK;
				else
				{
					if (await tilex((byte)Lead.CfgReq, sf, null, 0, 0, 0, fid, (f) =>
					{
						rst.Set();
						if (f != null) f.Dispose();
					}).ConfigureAwait(false) != (int)SignalKind.ACK) return false;

					return await rst.Wait(cfg.ConfigExchangeTimeout) > 0;
				}
			}
			catch (AggregateException aex)
			{
				trace(LogFlags.Exception, 0, "ConfigExchange", aex.Flatten().ToString());

				return false;
			}
			finally
			{
				if (sf != null) sf.Dispose();
			}
		}

		bool isClone(LogFlags op, int frameID, bool signalWithLastReply = true)
		{
			if (!procFrames.TryAdd(frameID, DateTime.Now))
			{
				int crpl = -1;

				if (signalWithLastReply && sentSignalsFor.TryGetValue(frameID, out SignalResponse sr))
				{
					signal(frameID, sr.Mark, sr.IsError);
					crpl = sr.Mark;
				}

				var logop = ((op & cfg.Log.Flags) == op && cfg.Log.IsEnabled);
				if (logop) trace(op, frameID, $"Clone Cached reply: {crpl}");

				return true;
			}
			else return false;
		}

		void procFrame(MemoryFragment f)
		{
			try
			{
				Volatile.Write(ref lastReceivedDgramTick, DateTime.Now.Ticks);

				var lead = (Lead)f.Span()[0];

				switch (lead)
				{
					case Lead.Probe: procProbe(); break;
					case Lead.ProbeReq: procProbeReq(); break;
					case Lead.Signal: procSignal(f); break;
					case Lead.Error: procError(f); break;
					case Lead.Block: procBlock(f); break;
					case Lead.Status: procStatus(f); break;
					case Lead.Tile: procTileX(f); break;
					case Lead.Cfg: procCfgX(f); break;
					case Lead.CfgReq: procCfgReq(f); break;
					case Lead.Pulse: procPulse(f); break;
					default:
					{
						trace("ProcFrame", $"Unknown lead: {(byte)lead}");
						break;
					}
				}
			}
			catch (AggregateException aex)
			{
				if (cfg.Log.LogUnhandledCallbackExceptions)
					trace(LogFlags.Exception, 0, "ProcFrame", aex.Flatten().ToString());
			}
			catch (Exception ex)
			{
				if (cfg.Log.LogUnhandledCallbackExceptions)
					trace(LogFlags.Exception, 0, ex.Message, ex.ToString());
			}
			finally
			{
				// [i] The Block and tileX procs use copies of the fragment.
				f.Dispose();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void procProbe()
		{
			Volatile.Write(ref lastReceivedProbeTick, DateTime.Now.Ticks);
			probeReqAwait.Set();
			probeReqAwait.Reset();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void procProbeReq()
		{
			try
			{
				if (lockOnGate.IsAcquired) lockOnRst.Wait();
				socket.Send(probeLead);
			}
			catch { }
		}

		void procCfgReq(MemoryFragment frag)
		{
			var f = new TILEX(frag.Span());
#if DEBUG || ASSERT
			if (cfg.dropFrame())
			{
				if (((LogFlags.DropFrame & cfg.Log.Flags) == LogFlags.DropFrame && cfg.Log.IsEnabled))
					trace(LogFlags.DropFrame, f.FrameID, $"Cfg R: {f.RefID}");
				return;
			}
#endif
			if (isClone(LogFlags.ProcTile, f.FrameID)) return;
			signal(f.FrameID, (int)SignalKind.ACK);

			if (((LogFlags.ProcTile & cfg.Log.Flags) == LogFlags.ProcTile && cfg.Log.IsEnabled))
				trace(LogFlags.ProcTile, f.FrameID, "K: CfgReq");

			configExchange(f.FrameID, false).Wait();
		}

		void procCfgX(MemoryFragment frag)
		{
			var f = new TILEX(frag.Span());
#if DEBUG || ASSERT
			if (cfg.dropFrame())
			{
				if (((LogFlags.DropFrame & cfg.Log.Flags) == LogFlags.DropFrame && cfg.Log.IsEnabled))
					trace(LogFlags.DropFrame, f.FrameID, $"Cfg R: {f.RefID}");
				return;
			}
#endif
			if (isClone(LogFlags.ProcTile, f.FrameID)) return;
			signal(f.FrameID, (int)SignalKind.ACK);

			var tcfg = new CfgX(f.Data);
			targetCfgUpdate(tcfg);

			if (tileXAwaits.TryGetValue(f.RefID, out TileXAwait ta) && ta.OnTileExchange != null)
				ta.OnTileExchange(null);

			if (((LogFlags.ProcTile & cfg.Log.Flags) == LogFlags.ProcTile && cfg.Log.IsEnabled))
				trace(LogFlags.ProcTile, f.FrameID, $"K: Cfg " +
				$"MaxBeams: {tcfg.MaxBeamedTilesAtOnce} MaxRcvs: {tcfg.MaxConcurrentReceives} " +
				$"SBuff: {tcfg.SendBufferSize} RBuff: {tcfg.ReceiveBufferSize}");


		}

		void procTileX(MemoryFragment frag)
		{
			var f = new TILEX(frag.Span());

#if ASSERT
			var at = f.Length + TILEX.HEADER;
			var f2 = new TILEX(frag.Span().Slice(at));

			if (f2.Kind == (byte)Lead.Tile && Math.Abs(f2.FrameID - f.FrameID) < 100)
				throw new Exception("Multiple frames received.");
#endif

#if DEBUG || ASSERT
			if (cfg.dropFrame())
			{
				if (((LogFlags.DropFrame & cfg.Log.Flags) == LogFlags.DropFrame && cfg.Log.IsEnabled))
					trace(LogFlags.DropFrame, f.FrameID, $"Tile R: {f.RefID}");
				return;
			}
#endif
			if (isClone(LogFlags.ProcTile, f.FrameID)) return;

			signal(f.FrameID, (int)SignalKind.ACK);

			if (((LogFlags.ProcTile & cfg.Log.Flags) == LogFlags.ProcTile && cfg.Log.IsEnabled))
				trace(LogFlags.ProcTile, f.FrameID, $"K: {(Lead)f.Kind}");

			MemoryFragment xf = null;

			// The procTile fragment will be disposed immediately after 
			// this method exit (i.e. no async for the callback). 
			// The newly created OnTileExchange frag MUST be disposed 
			// in the callback.

			if (f.Data.Length > 0)
			{
				xf = tileXHighway.AllocFragment(f.Data.Length);
				f.Data.CopyTo(xf);
			}

			if (tileXAwaits.TryGetValue(f.RefID, out TileXAwait ta) &&
				ta.OnTileExchange != null) ta.OnTileExchange(xf);
			else schedule(CallbackThread.SocketThread, xf);
		}

		void procPulse(MemoryFragment frag)
		{
			var f = new TILEX(frag.Span());

#if DEBUG || ASSERT
			if (cfg.dropFrame())
			{
				if (((LogFlags.DropFrame & cfg.Log.Flags) == LogFlags.DropFrame && cfg.Log.IsEnabled))
					trace(LogFlags.DropFrame, f.FrameID, "ProcPulse");
				return;
			}
#endif
			if (isClone(LogFlags.ProcPulse, f.FrameID)) return;

			signal(f.FrameID, (int)SignalKind.ACK);

			if (((LogFlags.ProcPulse & cfg.Log.Flags) == LogFlags.ProcPulse && cfg.Log.IsEnabled))
				trace(LogFlags.ProcPulse, f.FrameID, $"Len: {f.Data.Length}");

			try
			{
				int pos = 0;

				while (pos + 2 < f.Length)
				{
					var len = BitConverter.ToUInt16(f.Data.Slice(pos));

					if (len == 0) break;

					var pfr = tileXHighway.AllocFragment(len);
					var data = f.Data.Slice(pos + USH_LEN, len);

					pfr.Span().Clear();
					data.CopyTo(pfr);
					pos += len + USH_LEN;

					schedule(cfg.ScheduleCallbacksOn, pfr);
				}
			}
			catch (Exception ex)
			{
				trace(LogFlags.Exception, 0, "ProcPack", ex.ToString());
			}
		}

		void procBlock(MemoryFragment frag)
		{
			var f = new FRAME(frag.Span());

#if ASSERT
			var at = f.Length + FRAME.HEADER;
			var f2 = new FRAME(frag.Span().Slice(at));

			if (f2.Kind == (byte)Lead.Block && Math.Abs(f2.FrameID - f.FrameID) < 100)
				throw new Exception("Multiple frames received.");
#endif

#if DEBUG || ASSERT
			if (cfg.dropFrame())
			{
				if (((LogFlags.DropFrame & cfg.Log.Flags) == LogFlags.DropFrame && cfg.Log.IsEnabled))
					trace(LogFlags.DropFrame, f.FrameID, $"ProcBlock B: {f.BlockID} T: {f.TileIndex}");
				return;
			}
#endif
			if (isClone(LogFlags.ProcBlock, f.FrameID)) return;
			if (!blocks.ContainsKey(f.BlockID))
			{
				// Reject logic + Error or:
				if (blocks.TryAdd(f.BlockID, new Block(f.BlockID, f.TotalSize, cfg.TileSizeBytes, blockHighway)))
					signal(f.FrameID, (int)SignalKind.ACK);
			}

			var b = blocks[f.BlockID];
			var logop = ((LogFlags.ProcBlock & cfg.Log.Flags) == LogFlags.ProcBlock && cfg.Log.IsEnabled);

			if (!b.Receive(f))
			{
				if (logop) trace(LogFlags.ProcBlock, f.FrameID, $"Tile ignore B: {b.ID} T: {f.TileIndex}");
				return;
			}

			if (logop) trace(LogFlags.ProcBlock, f.FrameID, $"{f.Length} B: {f.BlockID} T: {f.TileIndex}");

			int fid = f.FrameID;

			if (b.IsComplete && Interlocked.CompareExchange(ref b.isOnCompleteTriggered, 1, 0) == 0)
			{
				Task.Run(async () =>
				{
					// Notify the sender that it's done
					using (var mapFrag = outHighway.AllocFragment(b.tileMap.Bytes))
					{
						b.tileMap.WriteTo(mapFrag);
						await status(b.ID, b.MarkedTiles, true, mapFrag);
					}
				});

				if (logop) trace(LogFlags.ProcBlock, fid, $"B: {b.ID} completed.");
				schedule(cfg.ScheduleCallbacksOn, b.Fragment);
			}
		}

		void procError(MemoryFragment frag)
		{
			var sg = new SIGNAL(frag.Span());

#if DEBUG || ASSERT
			if (cfg.dropFrame())
			{
				if (((LogFlags.DropFrame & cfg.Log.Flags) == LogFlags.DropFrame && cfg.Log.IsEnabled))
					trace(LogFlags.DropFrame, sg.FrameID, $"ProcError");
				return;
			}
#endif
			var logop = ((LogFlags.ProcError & cfg.Log.Flags) == LogFlags.ProcError && cfg.Log.IsEnabled);

			if (!isClone(LogFlags.ProcError, sg.FrameID))
			{
				var err = (ErrorCode)sg.Mark;

				switch (err)
				{
					case ErrorCode.Rejected:
					{
						if (blocks.TryGetValue(sg.RefID, out Block b))
						{
							Volatile.Write(ref b.isRejected, true);
							if (logop) trace(LogFlags.ProcError, sg.FrameID, $"Rejected B: [{sg.RefID}.");
						}

						break;
					}
					case ErrorCode.Unknown:
					default:
					if (logop) trace(LogFlags.ProcError, sg.FrameID, $"Unknown code M: {sg.Mark} R: [{sg.RefID}.");
					break;
				}
			}
		}

		void procStatus(MemoryFragment frag)
		{
			var st = new STATUS(frag.Span());
#if DEBUG || ASSERT
			if (cfg.dropFrame())
			{
				if (((LogFlags.DropFrame & cfg.Log.Flags) == LogFlags.DropFrame && cfg.Log.IsEnabled))
					trace(LogFlags.DropFrame, st.FrameID, $"ProcStatus B: {st.BlockID}");
				return;
			}
#endif

			if (!isClone(LogFlags.ProcStatus, st.FrameID))
			{
				var logop = ((LogFlags.ProcStatus & cfg.Log.Flags) == LogFlags.ProcStatus && cfg.Log.IsEnabled);

				if (blocks.TryGetValue(st.BlockID, out Block b))
				{
					// ACK the frame regardless of the context.
					signal(st.FrameID, (int)SignalKind.ACK);

					if (b.IsIncoming)
					{
						if (logop) trace(LogFlags.ProcStatus, st.FrameID, $"All-sent for B: {st.BlockID}");

						// This signal is received after the other side has sent all tiles.
						// Sends a re-beam request if no tile is received for x ms.
						if (Volatile.Read(ref b.requestTiles) == null)
							Volatile.Write(ref b.requestTiles, reqMissingTiles(b));
					}
					else
					{
						b.Mark(st.TileMap);

						if (b.IsComplete)
						{
							if (logop) trace(LogFlags.ProcStatus, st.FrameID, $"Out B: {st.BlockID} completed.");
							blocks.TryRemove(st.BlockID, out Block rem);
							b.beamLoop.TrySetResult(true);
							b.Dispose();
						}
						else
						{
							if (logop) trace(LogFlags.ProcStatus, st.FrameID,
								$"Re-beam B: {st.BlockID} M: {b.tileMap.ToString()}");

							rebeam(b);
						}
					}
				}
				else
				{
					signal(st.FrameID, (int)SignalKind.UNK);
					if (logop) trace(LogFlags.ProcStatus, st.FrameID, $"Unknown B: {st.BlockID}");
				}
			}
		}

		void procSignal(MemoryFragment frag)
		{
			var sg = new SIGNAL(frag.Span());

#if DEBUG || ASSERT
			if (cfg.dropFrame())
			{
				if (((LogFlags.DropFrame & cfg.Log.Flags) == LogFlags.DropFrame && cfg.Log.IsEnabled))
					trace(LogFlags.DropFrame, sg.FrameID, $"ProcSignal M: {sg.Mark} R: {sg.RefID}");
				return;
			}
#endif
			if (!isClone(LogFlags.ProcSignal, sg.FrameID))
			{
				var logop = ((LogFlags.ProcSignal & cfg.Log.Flags) == LogFlags.ProcSignal && cfg.Log.IsEnabled);

				if (signalAwaits.TryGetValue(sg.RefID, out SignalAwait sa) && sa.OnSignal != null)
					try
					{
						if (logop) trace(LogFlags.ProcSignal, sg.FrameID, $"M: {sg.Mark} R: {sg.RefID}");
						sa.OnSignal(sg.Mark);
					}
					catch { }
				else if (logop) trace(LogFlags.ProcSignal, sg.FrameID, $"NoAwait M: {sg.Mark} R: {sg.RefID}");
			}
		}

		void targetCfgUpdate(CfgX tcfg)
		{
			lock (cfgUpdateLock)
			{
				targetCfg = tcfg;
				var tep = tcfg.EndPoint;

				var maxccBeams = targetCfg.ReceiveBufferSize / targetCfg.TileSizeBytes;
				if (maxccBeams < cfg.MaxBeamedTilesAtOnce) cfg.MaxBeamedTilesAtOnce = maxccBeams;

				if (!Source.Equals(target)) LockOn(source, tep);
			}
		}

		async Task probe()
		{
			await Task.Yield();

			if (cfg.EnableProbes)
				while (!IsStopped && Volatile.Read(ref cfg.EnableProbes))
					try
					{
						if (lockOnGate.IsAcquired) lockOnRst.Wait();
						socket.Send(probeLead);
						await Task.Delay(cfg.ProbeFreqMS).ConfigureAwait(false);
					}
					catch { }
		}

		async Task receive()
		{
			await Task.Yield();

			using (var inHighway = new HeapHighway(new HighwaySettings(UDP_MAX), UDP_MAX, UDP_MAX))
				while (!IsStopped)
					try
					{
						if (lockOnGate.IsAcquired) lockOnRst.Wait();

						var frag = inHighway.Alloc(UDP_MAX);
						var read = await socket.ReceiveAsync(frag, SocketFlags.None, cancel.Token).ConfigureAwait(false);

						// [!] At the time of writing the cancellations throw ObjectDisposed ex,
						// thus the following graceful exit will be bypassed.
						if (cancel.IsCancellationRequested) break;

						if (read > 0)
						{
							Interlocked.Increment(ref receivedDgrams);

							if (read == 1) procFrame(frag);
							else ThreadPool.QueueUserWorkItem((f) => procFrame(f), frag, true);

							if (Volatile.Read(ref retries) > 0) Volatile.Write(ref retries, 0);
						}
						else
						{
							frag.Dispose();

							if (Interlocked.Increment(ref retries) > cfg.MaxReceiveRetries) break;
							else await Task.Delay(cfg.ErrorAwaitMS).ConfigureAwait(false);
						}
					}
					catch (ObjectDisposedException) { } // This is a very wrong way of canceling, MS! 
					catch (SocketException) { }         // Don't care, don't want to log.
					catch (Exception ex)
					{
						trace(LogFlags.Exception, 0, "Receive", ex.ToString());
					}

			trace("Receive", "Not listening.");
		}

		async Task cleanup()
		{
			await Task.Yield();

			while (true)
			{
				var DTN = DateTime.Now;

				try
				{
					trace(LogFlags.Cleanup, 0, $"Blocks: {blocks.Count} SignalAwaits: {signalAwaits.Count} SentSignals: {sentSignalsFor.Count}");

					foreach (var b in blocks.Values)
						if (DateTime.Now.Subtract(b.sentTime) > cfg.SentBlockRetention)
						{
							blocks.TryRemove(b.ID, out Block x);
							b.Dispose();
						}

					foreach (var sa in signalAwaits)
						if (sa.Value.OnSignal != null && DateTime.Now.Subtract(sa.Value.Created) > cfg.AwaitsCleanupAfter)
							signalAwaits.TryRemove(sa.Key, out SignalAwait x);

					foreach (var sa in tileXAwaits)
						if (sa.Value.OnTileExchange != null && DateTime.Now.Subtract(sa.Value.Created) > cfg.AwaitsCleanupAfter)
							tileXAwaits.TryRemove(sa.Key, out TileXAwait x);

					foreach (var fi in procFrames)
						if (DTN.Subtract(fi.Value) > cfg.ProcessedFramesIDRetention)
							procFrames.TryRemove(fi.Key, out DateTime x);

					foreach (var ss in sentSignalsFor)
						if (DTN.Subtract(ss.Value.Created) > cfg.SentSignalsRetention)
							sentSignalsFor.TryRemove(ss.Key, out SignalResponse x);
				}
				catch (Exception ex)
				{
					trace(LogFlags.Exception, 0, ex.Message, ex.ToString());
				}

				await Task.Delay(cfg.CleanupFreqMS).ConfigureAwait(false);
			}
		}

		async Task reqMissingTiles(Block b)
		{
			await Task.Yield();

			try
			{
				int c = 0;
				var logop = (LogFlags.ReqTiles & cfg.Log.Flags) == LogFlags.ReqTiles && cfg.Log.IsEnabled;

				while (!IsStopped && !b.isFaulted && !b.isRejected)
				{
					await Task.Delay(cfg.WaitAfterAllSentMS).ConfigureAwait(false);

					if (b.timeToReqTiles(cfg.WaitAfterAllSentMS) && b.RemainingTiles > 0)
					{
						if (logop) trace(LogFlags.ReqTiles, 0, $"B: {b.ID} MT: {b.MarkedTiles}/{b.TilesCount} #{c++}");

						using (var frag = outHighway.AllocFragment(b.tileMap.Bytes))
						{
							b.tileMap.WriteTo(frag);
							await status(b.ID, b.MarkedTiles, false, frag);
						}
						if (b.IsComplete) break;
					}
				}

				if (logop) trace(LogFlags.ReqTiles, 0, $"Out-of-ReqTiles B: {b.ID} IsComplete: {b.IsComplete}");
			}
			catch (Exception ex)
			{
				trace(LogFlags.Exception, 0, "ReqMissingTiles", ex.ToString());
			}
		}

		async Task autoPulse()
		{
			await Task.Yield();

			try
			{
				var logop = (LogFlags.AutoPulse & cfg.Log.Flags) == LogFlags.AutoPulse && cfg.Log.IsEnabled;

				while (!IsStopped && cfg.EnablePulsing)
				{
					autoPulseRst.Reset();
					autoPulseRst.Wait();
					await Task.Delay(cfg.PulseRetentionMS);

					if (Monitor.TryEnter(packLock, 0))
						try
						{
							if (logop) trace(LogFlags.AutoPulse, 0, $"Len: {packTileOffset}");
							pulseAndReload();
						}
						finally
						{
							Monitor.Exit(packLock);
						}
				}
			}
			catch (Exception ex)
			{
				trace(LogFlags.Exception, 0, "AutoPulse", ex.ToString());
			}

			trace(LogFlags.AutoPulse, 0, "Not pulsing");
		}

		void schedule(CallbackThread cbt, MemoryFragment f, bool disposeOnEx = true)
		{
			switch (cfg.ScheduleCallbacksOn)
			{
				case CallbackThread.ThreadPool:
				{
					ThreadPool.QueueUserWorkItem((frag) => run(frag, disposeOnEx), f, true);
					break;
				}
				case CallbackThread.Task:
				{
					Task.Run(() => run(f, disposeOnEx));
					break;
				}
				case CallbackThread.SocketThread:
				{
					run(f, disposeOnEx);
					break;
				}
				default: throw new ArgumentException("ScheduleCallbacksOn");
			}
		}

		void run(MemoryFragment f, bool disposeOnEx)
		{
			try
			{
				if (onReceive != null) onReceive(f);
				else onReceiveAsync(f);
			}
			catch (AggregateException aex)
			{
				if (disposeOnEx) f?.Dispose();

				if (cfg.Log.LogUnhandledCallbackExceptions)
					trace(LogFlags.Exception, 0, "Callback", aex.Flatten().ToString());
			}
			catch (Exception ex)
			{
				if (disposeOnEx) f?.Dispose();

				if (cfg.Log.LogUnhandledCallbackExceptions)
					trace(LogFlags.Exception, 0, "Callback", ex.ToString());
			}
		}

		void trace(LogFlags op, int frame, string title, string msg = null)
		{
			if (log == null) return;

			// [!!] DO NOT CHANGE THE FORMAT OFFSETS
			// The logmerge tool relies on hard-coded positions to parse the log lines.

			string ttl = string.Empty;
			switch (op)
			{
				case LogFlags.Beam:
				case LogFlags.Status:
				case LogFlags.Signal:
				case LogFlags.Tile:
				case LogFlags.AutoPulse:
				ttl = string.Format("{0,11}:o {1, -12} {2}", frame, op, title);
				break;
				case LogFlags.ReqTiles:
				case LogFlags.LockOn:
				case LogFlags.Exception:
				case LogFlags.Cleanup:
				ttl = string.Format("{0,-13} {1, -12} {2}", " ", op, title);
				break;
				case LogFlags.ProcBlock:
				case LogFlags.ProcError:
				case LogFlags.ProcStatus:
				case LogFlags.ProcSignal:
				case LogFlags.ProcTile:
				case LogFlags.ProcPulse:
				ttl = string.Format("{0,11}:i {1, -12} {2}", frame, op, title);
				break;
#if DEBUG || ASSERT
				case LogFlags.DropFrame:
				ttl = string.Format("{0,11}:i {1, -12} {2}", frame, op, title);
				break;
#endif
				case LogFlags.None:
				default:
				return;
			}

			log.Write(ttl, msg);
			if (cfg.Log.OnTrace != null)
				try { cfg.Log.OnTrace(op, ttl, msg); }
				catch { }
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal void trace(string op, string title, string msg = null)
		{
			if (log != null && Volatile.Read(ref cfg.Log.IsEnabled))
			{
				var s = string.Format("{0,-13} {1, -12} {2}", " ", op, title);
				log.Write(s, msg);
				if (cfg.Log.OnTrace != null)
					try { cfg.Log.OnTrace(LogFlags.None, s, msg); }
					catch { }
			}
		}

		CancellationTokenSource cancel;
		Action<MemoryFragment> onReceive;
		Func<MemoryFragment, Task> onReceiveAsync;
		IMemoryHighway outHighway;
		IMemoryHighway blockHighway;
		IMemoryHighway tileXHighway;
		BeamerCfg cfg;
		IPEndPoint target;
		IPEndPoint source;
		Socket socket;
		Log log;

		int frameCounter;
		int blockCounter;
		int receivedDgrams;
		int retries;
		bool isLocked;
		int isDisposed;
		long lastReceivedProbeTick;
		long lastReceivedDgramTick;
		byte[] probeLead = new byte[] { (byte)Lead.Probe };
		byte[] probeReqLead = new byte[] { (byte)Lead.ProbeReq };

		SemaphoreSlim ccBeams;

		// [!] Don't make the probeReqAwait Slim!
		ManualResetEvent probeReqAwait = new ManualResetEvent(false);
		ManualResetEventSlim lockOnRst = new ManualResetEventSlim(true);
		Gate lockOnGate = new Gate();
		object cfgUpdateLock = new object();
		Task probingTask;
		Task cleanupTask;
		Task[] receivers;
		Task autoPulseTask;
		CfgX targetCfg;

		// All keys are frame or block IDs

		ConcurrentDictionary<int, SignalAwait> signalAwaits = new ConcurrentDictionary<int, SignalAwait>();
		ConcurrentDictionary<int, Block> blocks = new ConcurrentDictionary<int, Block>();
		ConcurrentDictionary<int, DateTime> procFrames = new ConcurrentDictionary<int, DateTime>();
		ConcurrentDictionary<int, SignalResponse> sentSignalsFor = new ConcurrentDictionary<int, SignalResponse>();
		ConcurrentDictionary<int, TileXAwait> tileXAwaits = new ConcurrentDictionary<int, TileXAwait>();

		ManualResetEventSlim autoPulseRst = new ManualResetEventSlim();
		ManualResetEventSlim reloadCopyRst = new ManualResetEventSlim();
		ResetEvent packResultRst = new ResetEvent();
		MemoryFragment packTile;
		object packLock = new object();
		int packTileOffset;

		public const ushort UDP_MAX = ushort.MaxValue;
		public const ushort USH_LEN = 2;
	}
}
