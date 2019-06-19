﻿using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("brays.tests")]

namespace brays
{
	public class Beamer : IDisposable
	{
		public Beamer(Action<MemoryFragment> onReceive, BeamerCfg cfg)
		{
			if (onReceive == null || cfg == null) throw new ArgumentNullException();

			ID = Guid.NewGuid();
			this.onReceive = onReceive;
			this.cfg = cfg;
			outHighway = new HeapHighway(new HighwaySettings(UDP_MAX), UDP_MAX, UDP_MAX, UDP_MAX);
			blockHighway = cfg.ReceiveHighway;
			tileXHigheay = cfg.TileExchangeHighway;

			if (cfg.Log != null)
				log = new Log(cfg.Log.LogFilePath, cfg.Log.Ext, cfg.Log.RotationLogFileKB, cfg.Log.RotateLogAtStart);
			else
				cfg.Log = new BeamerLogCfg(null, false, 0);
		}

		public void Dispose()
		{
			if (!isDisposed)
				try
				{
					Interlocked.Exchange(ref stop, 1);
					outHighway?.Dispose();
					blockHighway?.Dispose();
					tileXHigheay?.Dispose();
					socket?.Dispose();
					outHighway = null;
					blockHighway = null;
					tileXHigheay = null;

					if (log != null) log.Dispose();
					if (blocks != null)
						foreach (var b in blocks.Values)
							if (b != null) b.Dispose();
				}
				catch (Exception ex)
				{
				}
				finally
				{
					isDisposed = true;
				}
		}

		public void Stop() => Volatile.Write(ref stop, 1);

		public bool Probe(int awaitMS = 2000)
		{
			socket.Send(probeReqLead);
			return probeReqAwait.WaitOne(awaitMS);
		}

		public Task<bool> IsTargetActive(int awaitMS = -1)
		{
			var tcs = new TaskCompletionSource<bool>();
			ThreadPool.RegisterWaitForSingleObject(probeReqAwait,
				(_, to) => tcs.TrySetResult(!to), null, awaitMS, true);

			Task.Run(async () =>
			{
				try
				{
					while (!tcs.Task.IsCompleted)
					{
						socket.Send(probeReqLead);
						await Task.Delay(cfg.ProbeFreqMS).ConfigureAwait(false);
					}
				}
				catch (Exception ex) { }
			});

			return tcs.Task;
		}

		public async Task<(bool ok, Exception ex)> LockOn(IPEndPoint listen, IPEndPoint target, bool configExchange = false)
		{
			if (lockOnGate.Enter())
				try
				{
					Volatile.Write(ref isLocked, false);
					lockOnRst.Reset();

					this.source = listen;
					this.target = target;

					if (socket != null) socket.Close();

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

					if (configExchange)
					{
						if (!await ConfigExchange(0, true))
						{
							Volatile.Write(ref stop, 1);
							return (false, null);
						}

						if (targetCfg.TileSizeBytes < cfg.TileSizeBytes)
							cfg.TileSizeBytes = targetCfg.TileSizeBytes;

						var maxccBeams = targetCfg.ReceiveBufferSize / targetCfg.TileSizeBytes;
						if (maxccBeams < cfg.MaxBeamedTilesAtOnce) cfg.MaxBeamedTilesAtOnce = maxccBeams;
					}

					if (ccBeams != null) ccBeams.Dispose();
					ccBeams = new SemaphoreSlim(cfg.MaxBeamedTilesAtOnce, cfg.MaxBeamedTilesAtOnce);

					Volatile.Write(ref isLocked, true);

					trace("Lock-on", $"{source.ToString()} target: {target.ToString()}");
					return (true, null);
				}
				catch (Exception ex)
				{
					if (!lockOnRst.IsSet) lockOnRst.Set();
					trace("Ex", "LockOn", ex.ToString());
					return (false, ex);
				}
				finally
				{
					lockOnGate.Exit();
				}

			return (false, null);
		}

		public async Task<bool> ConfigExchange(int refID = 0, bool awaitRemote = false)
		{
			var sf = refID > 0 ? outHighway.Alloc(CfgX.LENGTH) : null;

			// If the refID is 0 the cfg is treated as a request.
			if (refID > 0)
			{
				var cfgx = new CfgX(cfg, source);
				cfgx.Write(sf);
			}

			var fid = Interlocked.Increment(ref frameCounter);
			var rst = new ResetEvent(false);

			try
			{
				if (awaitRemote)
				{
					void cfgReceived(MemoryFragment frag)
					{
						rst.Set();
						if (frag != null) frag.Dispose();
					}

					var mark = await tilex((byte)Lead.Cfg, sf, null, 0, 0,
						refID, fid, cfgReceived).ConfigureAwait(false);

					if (mark != (int)SignalKind.ACK) return false;

					return await rst.Wait(cfg.ConfigExchangeTimeout).ConfigureAwait(false) > 0;
				}
				else return await tilex((byte)Lead.Cfg, sf, null, 0, 0, refID, fid)
						.ConfigureAwait(false) == (int)SignalKind.ACK;
			}
			finally
			{
				if (sf != null) sf.Dispose();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TileXFF(Span<byte> data) => tilexOnce((byte)Lead.Tile, data, 0, 0, null);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ResetEvent Pulse(Span<byte> data) => pack(data);

		public async Task<bool> Beam(MemoryFragment f)
		{
			try
			{
				if (f.Length <= cfg.TileSizeBytes)
					return await tilex((byte)Lead.Tile, f).ConfigureAwait(false) == (int)SignalKind.ACK;
				else
				{
					var bid = Interlocked.Increment(ref blockCounter);
					var b = new Block(bid, cfg.TileSizeBytes, f);

					blocks.TryAdd(bid, b);
					b.beamTiles = beamLoop(b);
					return await b.beamTiles.ConfigureAwait(false);
				}
			}
			catch (AggregateException aex)
			{
				trace("Ex", "Beam", aex.Flatten().ToString());
			}
			catch (Exception ex)
			{
				trace("Ex", "Beam", ex.ToString());
			}

			return false;
		}

		public readonly Guid ID;
		public bool IsStopped => Volatile.Read(ref stop) > 0;
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
						if (Volatile.Read(ref b.isRejected) || Volatile.Read(ref stop) > 0)
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
						else
						{
							using (var mf = outHighway.Alloc(dgramLen))
							{
								FRAME.Make(fid, b.ID, b.TotalSize, i,
									(ushort)(dgramLen - FRAME.HEADER), 0,
									b.Fragment.Span().Slice(0, dgramLen - FRAME.HEADER), mf);

								sentBytes = socket.Send(mf);
							}
						}

						var logop = ((TraceOps.Beam & cfg.Log.Flags) == TraceOps.Beam && cfg.Log.IsEnabled);

						if (sentBytes == dgramLen)
						{
							if (logop) trace(TraceOps.Beam, fid, $"{sentBytes} B: {b.ID} T: {i}");
							sent++;
						}
						else
						{
							if (logop) trace(TraceOps.Beam, fid, $"Faulted for sending {sentBytes} of {dgramLen} byte dgram.");

							b.isFaulted = true;
							return 0;
						}
					}
					catch (Exception ex)
					{
						trace("Ex", ex.Message, ex.StackTrace);
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

		async Task<bool> beamLoop(Block b)
		{
			await Task.Yield();

			try
			{
				for (int i = 0; i < cfg.TotalReBeamsCount; i++)
					if (await beam(b).ConfigureAwait(false) == (int)SignalKind.ACK)
					{
						if (Interlocked.CompareExchange(ref b.isRebeamRequestPending, 0, 1) == 1)
							Volatile.Write(ref b.beamTiles, beamLoop(b));
						else
							Volatile.Write(ref b.beamTiles, null);

						return true;
					}
					else await Task.Delay(cfg.BeamAwaitMS).ConfigureAwait(false);

				b.Dispose();
			}
			catch (AggregateException aex)
			{
				var faex = aex.Flatten();
				trace("Ex", faex.Message, faex.StackTrace);
			}
			catch (Exception ex)
			{
				trace("Ex", ex.Message, ex.StackTrace);
			}

			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void rebeam(Block b)
		{
			var req = Interlocked.CompareExchange(ref b.isRebeamRequestPending, 1, 0);
			var bst = Volatile.Read(ref b.beamTiles);

			if (req == 0 && (bst == null || bst.Status == TaskStatus.RanToCompletion))
				Volatile.Write(ref b.beamTiles, beamLoop(b));
		}

		async Task<int> status(int blockID, int tilesCount, bool awaitRsp, MemoryFragment tileMap)
		{
			try
			{
				if (Volatile.Read(ref stop) > 0) return (int)SignalKind.NOTSET;

				var fid = Interlocked.Increment(ref frameCounter);
				var len = (tileMap != null ? tileMap.Length : 0) + STATUS.HEADER;
				var logop = ((TraceOps.Status & cfg.Log.Flags) == TraceOps.Status && cfg.Log.IsEnabled);
				ResetEvent rst = null;
				var rsp = 0;

				if (awaitRsp) rst = new ResetEvent();

				using (var frag = outHighway.Alloc(len))
				{
					STATUS.Make(fid, blockID, tilesCount,
						tileMap != null ? tileMap.Span() : default,
						frag.Span());

					var ackq = !awaitRsp || signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
					{
						Volatile.Write(ref rsp, mark);
						if (logop) trace(TraceOps.Status, fid, $"{mark} B: {blockID}");
						rst.Set(true);
					}));

					if (ackq)
					{
						var map = string.Empty;
						var awaitMS = cfg.RetryDelayStartMS;

						if (blocks.TryGetValue(blockID, out Block b) && tileMap != null)
							map = $"M: {b.tileMap.ToString()} ";

						for (int i = 0; i < cfg.SendRetries && Volatile.Read(ref stop) < 1; i++)
						{
							if (lockOnGate.IsAcquired) lockOnRst.Wait();

							var sent = socket.Send(frag, SocketFlags.None);

							if (logop)
								if (sent == 0) trace(TraceOps.Status, fid, $"Socket sent 0 bytes. B: {blockID} #{i + 1}");
								else trace(TraceOps.Status, fid, $"B: {blockID} {map} #{i + 1}");

							if (!awaitRsp || await rst.Wait(awaitMS) > 0) break;
							awaitMS = (int)(awaitMS * cfg.RetryDelayStepMultiplier);
						}
					}

					return Volatile.Read(ref rsp);
				}
			}
			catch (Exception ex)
			{
				trace("Ex", "Status", ex.ToString());
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
			var logop = ((TraceOps.Signal & cfg.Log.Flags) == TraceOps.Signal && cfg.Log.IsEnabled);

			if (sent > 0)
			{
				if (keep) sentSignalsFor.TryAdd(refID, new SignalResponse(signal.Mark, isError));
				if (logop) trace(TraceOps.Signal, fid, $"M: {mark} R: {refID}");
			}
			else if (logop) trace(TraceOps.Signal, fid, $"Failed to send M: {mark} R: {refID}");

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
			Action<MemoryFragment> onTileXAwait = null)
		{
			// [i] The data could be null if CfgX request.

			var dataLen = data != null ? data.Length : 0;

			if (dataLen > 0 && dLen > 0) dataLen = dLen;
			else dLen = dataLen;

			if (dataLen > cfg.TileSizeBytes) throw new ArgumentException("data size");
			if (Volatile.Read(ref stop) > 0) return (int)SignalKind.NOTSET;

			var rst = new ResetEvent();

			if (fid == 0) fid = Interlocked.Increment(ref frameCounter);
			var len = dataLen + TILEX.HEADER;
			var logop = ((TraceOps.Tile & cfg.Log.Flags) == TraceOps.Tile && cfg.Log.IsEnabled);
			var rsp = 0;

			if (onTileXAwait != null) tileXAwaits.TryAdd(fid, new TileXAwait(onTileXAwait));

			if (signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
				{
					Volatile.Write(ref rsp, mark);
					rst.Set(true);
				})))
			{
				using (var frag = outHighway.Alloc(len))
				{
					if (dataLen > 0)
						TILEX.Make(kind, fid, refid, (ushort)dataLen, data.Span().Slice(dfrom, dLen), frag);
					else
						TILEX.Make(kind, fid, refid, 0, default, frag);

					if (crst != null) crst.Set();

					var awaitMS = cfg.RetryDelayStartMS;

					for (int i = 0; i < cfg.SendRetries && Volatile.Read(ref stop) < 1; i++)
					{
						if (lockOnGate.IsAcquired) lockOnRst.Wait();

						var sent = socket.Send(frag, SocketFlags.None);

						if (logop)
							if (sent == frag.Length) trace(TraceOps.Tile, fid, $"K: {(Lead)kind} b:{sent} #{i + 1}");
							else trace(TraceOps.Tile, fid, $"Failed K: {(Lead)kind}");

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
			if (Volatile.Read(ref stop) > 0) return;

			if (fid == 0) fid = Interlocked.Increment(ref frameCounter);
			var len = data.Length + TILEX.HEADER;
			var logop = ((TraceOps.Tile & cfg.Log.Flags) == TraceOps.Tile && cfg.Log.IsEnabled);

			if (onTileXAwait != null) tileXAwaits.TryAdd(fid, new TileXAwait(onTileXAwait));

			using (var frag = outHighway.Alloc(len))
			{
				var tilex = new TILEX(kind, fid, refid, 0, data);

				tilex.Write(frag);

				if (lockOnGate.IsAcquired) lockOnRst.Wait();
				var sent = socket.Send(frag, SocketFlags.None);

				if (logop)
					if (sent == frag.Length) trace(TraceOps.Tile, fid, $"TileXOnce K: {kind}");
					else trace(TraceOps.Tile, fid, $"TileXOnce Failed:  Sent {sent} of {frag.Length} bytes");
			}
		}

		ResetEvent pack(Span<byte> s)
		{
			if (s.Length + USH_LEN > cfg.TileSizeBytes) throw new ArgumentException("Data length");

			// [i] The ResetEvent is shared for one tile pulse.

			lock (packLock)
			{
				if (packTile == null) packTile = outHighway.Alloc(cfg.TileSizeBytes);
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
						trace("Ex", "Pulse-TileX", aex.Flatten().ToString());
					}
					finally
					{
						x.rr.Set(mark == (int)SignalKind.ACK);
					}
				}, (rr: packResultRst, len: (ushort)packTileOffset), true);

				// Reload
				reloadCopyRst.Wait();
				packTile.Dispose();
				packTile = outHighway.Alloc(cfg.TileSizeBytes);
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

		bool isClone(TraceOps op, int frameID, bool signalWithLastReply = true)
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
				socket.Send(probeLead);
			}
			catch { }
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
				trace("Ex", "ProcFrame", aex.Flatten().ToString());
			}
			catch (Exception ex)
			{
				trace("Ex", ex.Message, ex.ToString());
			}
			finally
			{
				f.Dispose();
			}
		}

		void procTileX(MemoryFragment frag)
		{
			var f = new TILEX(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				if (((TraceOps.DropFrame & cfg.Log.Flags) == TraceOps.DropFrame && cfg.Log.IsEnabled))
					trace(TraceOps.DropFrame, f.FrameID, $"Tile R: {f.RefID}");
				return;
			}
#endif
			if (isClone(TraceOps.ProcTile, f.FrameID)) return;

			signal(f.FrameID, (int)SignalKind.ACK);

			if (((TraceOps.ProcTile & cfg.Log.Flags) == TraceOps.ProcTile && cfg.Log.IsEnabled))
				trace(TraceOps.ProcTile, f.FrameID, $"K: {(Lead)f.Kind}");

			MemoryFragment xf = null;

			// The procTile fragment will be disposed immediately after 
			// this method exit (i.e. no async for the callback). 
			// The newly created OnTileExchange frag MUST be disposed 
			// in the callback.

			if (f.Data.Length > 0)
			{
				xf = tileXHigheay.AllocFragment(f.Data.Length);
				f.Data.CopyTo(xf);
			}

			if (tileXAwaits.TryGetValue(f.RefID, out TileXAwait ta) &&
				ta.OnTileExchange != null) ta.OnTileExchange(xf);
			else onReceive(xf);
		}

		void procPulse(MemoryFragment frag)
		{
			var f = new TILEX(frag.Span());

#if DEBUG
			if (cfg.dropFrame())
			{
				if (((TraceOps.DropFrame & cfg.Log.Flags) == TraceOps.DropFrame && cfg.Log.IsEnabled))
					trace(TraceOps.DropFrame, f.FrameID, "ProcPulse");
				return;
			}
#endif
			if (isClone(TraceOps.ProcPulse, f.FrameID)) return;

			signal(f.FrameID, (int)SignalKind.ACK);

			if (((TraceOps.ProcPulse & cfg.Log.Flags) == TraceOps.ProcPulse && cfg.Log.IsEnabled))
				trace(TraceOps.ProcPulse, f.FrameID, $"Len: {f.Data.Length}");

			try
			{
				int pos = 0;

				while (pos + 2 < f.Length)
				{
					var len = BitConverter.ToUInt16(f.Data.Slice(pos));

					if (len == 0) break;

					var pfr = tileXHigheay.AllocFragment(len);
					var data = f.Data.Slice(pos + USH_LEN, len);

					pfr.Span().Clear();
					data.CopyTo(pfr);
					pos += len + USH_LEN;

					ThreadPool.QueueUserWorkItem((x) =>
					{
						try
						{
							onReceive(x);
						}
						catch (MemoryLaneException mlx)
						{
						}
						catch (Exception ex)
						{
						}
					}, pfr, true);
				}
			}
			catch (Exception ex)
			{
				trace("Ex", "ProcPack", ex.ToString());
			}
		}

		void procCfgX(MemoryFragment frag)
		{
			var f = new TILEX(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				if (((TraceOps.DropFrame & cfg.Log.Flags) == TraceOps.DropFrame && cfg.Log.IsEnabled))
					trace(TraceOps.DropFrame, f.FrameID, $"Cfg R: {f.RefID}");
				return;
			}
#endif
			if (isClone(TraceOps.ProcTile, f.FrameID)) return;
			signal(f.FrameID, (int)SignalKind.ACK);

			if (f.Data.Length == 0) ConfigExchange(f.FrameID).Wait();
			else
			{
				var tcfg = new CfgX(f.Data);
				Volatile.Write(ref targetCfg, tcfg);

				if (tileXAwaits.TryGetValue(f.RefID, out TileXAwait ta) && ta.OnTileExchange != null)
					ta.OnTileExchange(null);

				if (((TraceOps.ProcTile & cfg.Log.Flags) == TraceOps.ProcTile && cfg.Log.IsEnabled))
					trace(TraceOps.ProcTile, f.FrameID, $"K: Cfg " +
					$"MaxBeams: {tcfg.MaxBeamedTilesAtOnce} MaxRcvs: {tcfg.MaxConcurrentReceives} " +
					$"SBuff: {tcfg.SendBufferSize} RBuff: {tcfg.ReceiveBufferSize}");
			}
		}

		void procBlock(MemoryFragment frag)
		{
			var f = new FRAME(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				if (((TraceOps.DropFrame & cfg.Log.Flags) == TraceOps.DropFrame && cfg.Log.IsEnabled))
					trace(TraceOps.DropFrame, f.FrameID, $"ProcBlock B: {f.BlockID} T: {f.TileIndex}");
				return;
			}
#endif
			if (isClone(TraceOps.ProcBlock, f.FrameID)) return;
			if (!blocks.ContainsKey(f.BlockID))
			{
				// Reject logic + Error or:
				if (blocks.TryAdd(f.BlockID, new Block(f.BlockID, f.TotalSize, cfg.TileSizeBytes, blockHighway)))
					signal(f.FrameID, (int)SignalKind.ACK);
			}

			var b = blocks[f.BlockID];
			var logop = ((TraceOps.ProcBlock & cfg.Log.Flags) == TraceOps.ProcBlock && cfg.Log.IsEnabled);

			if (!b.Receive(f))
			{
				if (logop) trace(TraceOps.ProcBlock, f.FrameID, $"Tile ignore B: {b.ID} T: {f.TileIndex}");
				return;
			}

			if (logop) trace(TraceOps.ProcBlock, f.FrameID, $"{f.Length} B: {f.BlockID} T: {f.TileIndex}");

			int fid = f.FrameID;

			if (b.IsComplete && onReceive != null &&
				Interlocked.CompareExchange(ref b.isOnCompleteTriggered, 1, 0) == 0)
				ThreadPool.QueueUserWorkItem((block) =>
				{
					try
					{
						if (logop) trace(TraceOps.ProcBlock, fid, $"B: {block.ID} completed.");
						onReceive(block.Fragment);
					}
					catch { }
				}, b, false);

		}

		void procError(MemoryFragment frag)
		{
			var sg = new SIGNAL(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				if (((TraceOps.DropFrame & cfg.Log.Flags) == TraceOps.DropFrame && cfg.Log.IsEnabled))
					trace(TraceOps.DropFrame, sg.FrameID, $"ProcError");
				return;
			}
#endif
			var logop = ((TraceOps.ProcError & cfg.Log.Flags) == TraceOps.ProcError && cfg.Log.IsEnabled);

			if (!isClone(TraceOps.ProcError, sg.FrameID))
			{
				var err = (ErrorCode)sg.Mark;

				switch (err)
				{
					case ErrorCode.Rejected:
					{
						if (blocks.TryGetValue(sg.RefID, out Block b))
						{
							Volatile.Write(ref b.isRejected, true);
							if (logop) trace(TraceOps.ProcError, sg.FrameID, $"Rejected B: [{sg.RefID}.");
						}

						break;
					}
					case ErrorCode.Unknown:
					default:
					if (logop) trace(TraceOps.ProcError, sg.FrameID, $"Unknown code M: {sg.Mark} R: [{sg.RefID}.");
					break;
				}
			}
		}

		void procStatus(MemoryFragment frag)
		{
			var st = new STATUS(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				if (((TraceOps.DropFrame & cfg.Log.Flags) == TraceOps.DropFrame && cfg.Log.IsEnabled))
					trace(TraceOps.DropFrame, st.FrameID, $"ProcStatus B: {st.BlockID}");
				return;
			}
#endif

			if (!isClone(TraceOps.ProcStatus, st.FrameID))
			{
				var logop = ((TraceOps.ProcStatus & cfg.Log.Flags) == TraceOps.ProcStatus && cfg.Log.IsEnabled);

				if (blocks.TryGetValue(st.BlockID, out Block b))
				{
					// ACK the frame regardless of the context.
					signal(st.FrameID, (int)SignalKind.ACK);

					if (b.IsIncoming)
					{
						if (logop) trace(TraceOps.ProcStatus, st.FrameID, $"All-sent for B: {st.BlockID}");

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
							if (logop) trace(TraceOps.ProcStatus, st.FrameID, $"Out B: {st.BlockID} completed.");
							blocks.TryRemove(st.BlockID, out Block rem);
							b.Dispose();
						}
						else
						{
							if (logop) trace(TraceOps.ProcStatus, st.FrameID,
								$"Re-beam B: {st.BlockID} M: {b.tileMap.ToString()}");

							rebeam(b);
						}
					}
				}
				else
				{
					signal(st.FrameID, (int)SignalKind.UNK);
					if (logop) trace(TraceOps.ProcStatus, st.FrameID, $"Unknown B: {st.BlockID}");
				}
			}
		}

		void procSignal(MemoryFragment frag)
		{
			var sg = new SIGNAL(frag.Span());

#if DEBUG
			if (cfg.dropFrame())
			{
				if (((TraceOps.DropFrame & cfg.Log.Flags) == TraceOps.DropFrame && cfg.Log.IsEnabled))
					trace(TraceOps.DropFrame, sg.FrameID, $"ProcSignal M: {sg.Mark} R: {sg.RefID}");
				return;
			}
#endif
			if (!isClone(TraceOps.ProcSignal, sg.FrameID))
			{
				var logop = ((TraceOps.ProcSignal & cfg.Log.Flags) == TraceOps.ProcSignal && cfg.Log.IsEnabled);

				if (signalAwaits.TryGetValue(sg.RefID, out SignalAwait sa) && sa.OnSignal != null)
					try
					{
						if (logop) trace(TraceOps.ProcSignal, sg.FrameID, $"M: {sg.Mark} R: {sg.RefID}");
						sa.OnSignal(sg.Mark);
					}
					catch { }
				else if (logop) trace(TraceOps.ProcSignal, sg.FrameID, $"NoAwait M: {sg.Mark} R: {sg.RefID}");
			}
		}

		async Task probe()
		{
			await Task.Yield();

			if (cfg.EnableProbes)
				while (Volatile.Read(ref stop) < 1 && Volatile.Read(ref cfg.EnableProbes))
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
				while (Volatile.Read(ref stop) < 1)
					try
					{
						if (lockOnGate.IsAcquired) lockOnRst.Wait();

						var frag = inHighway.Alloc(UDP_MAX);
						var read = await socket.ReceiveAsync(frag, SocketFlags.None).ConfigureAwait(false);

						if (read > 0)
						{
							Interlocked.Increment(ref receivedDgrams);

							if (read == 1) procFrame(frag);
							else ThreadPool.QueueUserWorkItem((f) => procFrame(f), frag, true);

							if (Volatile.Read(ref retries) > 0) Volatile.Write(ref retries, 0);
						}
						else
						{
							trace("Receive", $"Read 0 bytes from socket. Retries: {retries}");
							frag.Dispose();

							if (Interlocked.Increment(ref retries) > cfg.MaxReceiveRetries) break;
							else await Task.Delay(cfg.ErrorAwaitMS).ConfigureAwait(false);
						}
					}
					catch (Exception ex)
					{
						trace("Ex", "Receive", ex.ToString());
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
#if DEBUG
					trace("Cleanup", $"Blocks: {blocks.Count} SignalAwaits: {signalAwaits.Count} SentSignals: {sentSignalsFor.Count}");
#endif
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
					trace("Ex", "Cleanup", ex.ToString());
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
				var logop = (TraceOps.ReqTiles & cfg.Log.Flags) == TraceOps.ReqTiles && cfg.Log.IsEnabled;

				while (Volatile.Read(ref stop) < 1 && !b.IsComplete && !b.isFaulted && !b.isRejected)
				{
					await Task.Delay(cfg.WaitAfterAllSentMS).ConfigureAwait(false);

					if (b.timeToReqTiles(cfg.WaitAfterAllSentMS) && b.RemainingTiles > 0)
					{
						if (logop) trace(TraceOps.ReqTiles, 0, $"B: {b.ID} MT: {b.MarkedTiles}/{b.TilesCount} #{c++}");

						using (var frag = outHighway.Alloc(b.tileMap.Bytes))
						{
							b.tileMap.WriteTo(frag);
							status(b.ID, b.MarkedTiles, false, frag);
						}
					}
				}

				if (logop) trace(TraceOps.ReqTiles, 0, $"Out-of-ReqTiles B: {b.ID} IsComplete: {b.IsComplete}");
			}
			catch (Exception ex)
			{
				trace("Ex", ex.Message, ex.ToString());
			}
		}

		async Task autoPulse()
		{
			await Task.Yield();

			try
			{
				var logop = (TraceOps.AutoPulse & cfg.Log.Flags) == TraceOps.AutoPulse && cfg.Log.IsEnabled;

				while (Volatile.Read(ref stop) < 1)
				{
					autoPulseRst.Reset();
					autoPulseRst.Wait();
					await Task.Delay(cfg.PulseSleepMS);

					if (Monitor.TryEnter(packLock, 0))
						try
						{
							if (logop) trace(TraceOps.AutoPulse, 0, $"Len: {packTileOffset}");
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
				trace("Ex", "AutoPulse", ex.ToString());
			}
		}

		void trace(TraceOps op, int frame, string title, string msg = null)
		{
			// [!!] DO NOT CHANGE THE FORMAT OFFSETS
			// The logmerge tool relies on hard-coded positions to parse the log lines.

			string ttl = string.Empty;
			switch (op)
			{
				case TraceOps.Beam:
				case TraceOps.Status:
				case TraceOps.Signal:
				case TraceOps.Tile:
				case TraceOps.AutoPulse:
				ttl = string.Format("{0,11}:o {1, -12} {2}", frame, op, title);
				break;
				case TraceOps.ReqTiles:
				ttl = string.Format("{0,-13} {1, -12} {2}", " ", op, title);
				break;
				case TraceOps.ProcBlock:
				case TraceOps.ProcError:
				case TraceOps.ProcStatus:
				case TraceOps.ProcSignal:
				case TraceOps.ProcTile:
				case TraceOps.ProcPulse:
				ttl = string.Format("{0,11}:i {1, -12} {2}", frame, op, title);
				break;
#if DEBUG
				case TraceOps.DropFrame:
				ttl = string.Format("{0,11}:i {1, -12} {2}", frame, op, title);
				break;
#endif
				case TraceOps.None:
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
					try { cfg.Log.OnTrace(TraceOps.None, s, msg); }
					catch { }
			}
		}

		Action<MemoryFragment> onReceive;
		HeapHighway outHighway;
		IMemoryHighway blockHighway;
		IMemoryHighway tileXHigheay;
		BeamerCfg cfg;
		IPEndPoint target;
		IPEndPoint source;
		Socket socket;
		Log log;

		int frameCounter;
		int blockCounter;
		int receivedDgrams;
		int stop;
		int retries;
		bool isLocked;
		bool isDisposed;
		long lastReceivedProbeTick;
		long lastReceivedDgramTick;
		byte[] probeLead = new byte[] { (byte)Lead.Probe };
		byte[] probeReqLead = new byte[] { (byte)Lead.ProbeReq };

		SemaphoreSlim ccBeams;

		// [!] Don't make this Slim!
		ManualResetEvent probeReqAwait = new ManualResetEvent(false);
		ManualResetEventSlim lockOnRst = new ManualResetEventSlim(true);
		Gate lockOnGate = new Gate();
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