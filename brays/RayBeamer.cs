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
	public class RayBeamer : IDisposable
	{
		public RayBeamer(Action<MemoryFragment> onReceived, BeamerCfg cfg)
		{
			if (onReceived == null || cfg == null) throw new ArgumentNullException();

			ID = Guid.NewGuid();
			this.onReceived = onReceived;
			this.cfg = cfg;
			outHighway = new HeapHighway(new HighwaySettings(UDP_MAX), UDP_MAX, UDP_MAX, UDP_MAX);
			blockHighway = cfg.ReceiveHighway;
			tileXHigheay = cfg.TileExchangeHighway;

			if (cfg.Log != null)
				log = new Log(cfg.Log.LogFilePath, cfg.Log.Ext, cfg.Log.RotationLogFileKB);
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
				catch { }
				finally
				{
					isDisposed = true;
				}
		}

		public void Stop() => Volatile.Write(ref stop, 1);

		public bool LockOn(IPEndPoint listen, IPEndPoint target, bool configExchange = false)
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

					if (configExchange)
					{
						if (!ConfigExchange(0, true))
						{
							Volatile.Write(ref stop, 1);
							return false;
						}

						// 2d0: settings update
					}

					Volatile.Write(ref isLocked, true);

					trace("Lock-on", $"{source.ToString()} target: {target.ToString()}");
					return true;
				}
				catch (Exception ex)
				{
					if (!lockOnRst.IsSet) lockOnRst.Set();
					log.Write($"Ex LockOn {ex.Message}", ex.ToString());
				}
				finally
				{

					lockOnGate.Exit();
				}

			return false;
		}

		public bool ConfigExchange(int refID = 0, bool awaitRemote = false)
		{
			Span<byte> s = refID > 0 ? stackalloc byte[CfgX.LENGTH] : default;

			// If the refID is 0 the cfg is treated as a request.
			if (refID > 0)
			{
				var cfgx = new CfgX(cfg, source);
				cfgx.Write(s);
			}

			var fid = Interlocked.Increment(ref frameCounter);
			ManualResetEventSlim rst = null;

			if (awaitRemote)
			{
				rst = new ManualResetEventSlim(false);

				void cfgReceived(MemoryFragment frag)
				{
					rst.Set();
					if (frag != null) frag.Dispose();
				}

				if (tilex((byte)Lead.CfgX, s, refID, fid, cfgReceived) != (int)SignalKind.ACK) return false;

				return rst.Wait(cfg.ConfigExchangeTimeout);
			}
			else return tilex((byte)Lead.CfgX, s, refID, fid) == (int)SignalKind.ACK;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TileXFF(Span<byte> data) => tilexOnce((byte)Lead.TileX, data, 0, 0, null);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public async Task<int> TileX(MemoryFragment f)
			=> await Task.Run(() => tilex((byte)Lead.TileX, f.Span(), 0)).ConfigureAwait(false);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public async Task<int> TileX(MemoryFragment f, int start, int len)
			=> await Task.Run(() => tilex((byte)Lead.TileX, f.Span().Slice(start, len), 0)).ConfigureAwait(false);

		public async Task<bool> Beam(MemoryFragment f)
		{
			var bid = Interlocked.Increment(ref blockCounter);
			var b = new Block(bid, cfg.TileSizeBytes, f);

			if (blocks.TryAdd(bid, b))
				try
				{
					b.beamTiles = beamLoop(b);
					return await b.beamTiles;
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
		public bool IsLocked => isLocked;
		public int FrameCounter => frameCounter;
		public int ReceivedDgrams => receivedDgrams;
		public IPEndPoint Target => target;
		public IPEndPoint Source => source;
		public DateTime LastProbe => new DateTime(Volatile.Read(ref lastReceivedProbeTick));
		public CfgX GetTargetConfig() => targetCfg.Clone();

		bool beam(Block b)
		{
			// [!!] This method should be invoked once at a time per block because
			// the MemoryFragment bits are mutated. Consequently the block should
			// never be shared outside brays as it's not safe to read while sending.

#if DEBUG
			if (Interlocked.Increment(ref ccBeamsFuse) > 1)
				throw new InvariantException($"ccBeamsFuse: {ccBeamsFuse}");
#endif
			int sent = 0;

			for (int i = 0; i < b.tileMap.Count; i++)
				if (!b.tileMap[i])
				{
					Span<byte> data = default;

					try
					{
						if (Volatile.Read(ref b.isRejected) || Volatile.Read(ref stop) > 0)
						{
							trace("Beam", "Stopped");
							return false;
						}

						// Wait if the socket is reconfiguring.
						if (lockOnGate.IsAcquired) lockOnRst.Wait();

						var cc = Volatile.Read(ref ccBeams);

						while (cc > cfg.MaxBeamedTilesAtOnce)
						{
							cc = Volatile.Read(ref ccBeams);
							Thread.Sleep(10);
						}

						Interlocked.Increment(ref ccBeams);
						var fid = Interlocked.Increment(ref frameCounter);
						var sentBytes = 0;
						var dgramLen = 0;

						// [!] In order to avoid copying the tile data into a FRAME span 
						// just to prepend the FRAME header, the block MemoryFragment 
						// is modified in place. This doesn't work for the first tile. 

						if (i > 0)
						{
							data = b.Fragment.Span().Slice((i * b.TileSize) - FRAME.HEADER);
							data.Slice(0, FRAME.HEADER).CopyTo(b.frameHeader);

							if (data.Length > b.TileSize + FRAME.HEADER)
								data = data.Slice(0, b.TileSize + FRAME.HEADER);
							dgramLen = data.Length;

							var f = new FRAME(fid, b.ID, b.TotalSize, i, (ushort)(dgramLen - FRAME.HEADER), 0, default);

							// After this call the block fragment will be corrupted
							// until the finally clause.
							f.Write(data.Slice(0, FRAME.HEADER), true);
							sentBytes = socket.Send(data);
						}
						else
						{
							data = b.Fragment.Span().Slice(i * b.TileSize);
							if (data.Length > b.TileSize) data = data.Slice(0, b.TileSize);
							dgramLen = data.Length;

							var f = new FRAME(fid, b.ID, b.TotalSize, i, 0, 0, data);

							using (var mf = outHighway.Alloc(f.LENGTH))
							{
								var dgram = mf.Span();
								f.Write(dgram);
								dgramLen = dgram.Length;
								sentBytes = socket.Send(dgram);
							}
						}

						if (sentBytes == dgramLen)
						{
							trace(TraceOps.Beam, fid, $"{sentBytes} B: {b.ID} T: {i}");
							sent++;
						}
						else
						{
							trace(TraceOps.Beam, fid,
								$"Faulted for sending {sentBytes} of {dgramLen} byte dgram.");
							b.isFaulted = true;
							return false;
						}
					}
					catch (Exception ex)
					{
						trace("Ex", ex.Message, ex.StackTrace);
					}
					finally
					{
						// [!] Restore the original bytes no matter what happens.
						if (i > 0) b.frameHeader.AsSpan().CopyTo(data);
						Interlocked.Decrement(ref ccBeams);
#if DEBUG
						Interlocked.Decrement(ref ccBeamsFuse);
#endif
					}
				}

			b.sentTime = DateTime.Now;
			return status(b.ID, sent, true, null);
		}

		async Task<bool> beamLoop(Block b)
		{
			try
			{
				for (int i = 0; i < cfg.TotalReBeamsCount; i++)
					if (beam(b))
					{
						if (Interlocked.CompareExchange(ref b.isRebeamRequestPending, 0, 1) == 1)
							Volatile.Write(ref b.beamTiles, beamLoop(b));
						else Volatile.Write(ref b.beamTiles, null);
						return true;
					}
					else await Task.Delay(cfg.BeamAwaitMS).ConfigureAwait(false);
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

		bool status(int blockID, int tilesCount, bool awaitRsp, Span<byte> tileMap)
		{
			if (Volatile.Read(ref stop) > 0) return false;

			var fid = Interlocked.Increment(ref frameCounter);
			var len = tileMap.Length + STATUS.HEADER;
			var rsp = false;

			ManualResetEventSlim rst = null;
			if (awaitRsp) rst = new ManualResetEventSlim(false);

			using (var frag = outHighway.Alloc(len))
			{
				STATUS.Make(fid, blockID, tilesCount, tileMap, frag.Span());

				var ackq = !awaitRsp || signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
				{
					if ((SignalKind)mark == SignalKind.ACK) Volatile.Write(ref rsp, true);
					else trace(TraceOps.Status, fid, $"NACK B: {blockID}");
					rst.Set();
				}));

				if (ackq)
				{
					var map = string.Empty;
					var awaitMS = cfg.RetryDelayStartMS;

					if (blocks.TryGetValue(blockID, out Block b) && tileMap.Length > 0)
						map = $"M: {b.tileMap.ToString()} ";

					for (int i = 0; i < cfg.SendRetries; i++)
					{
						if (lockOnGate.IsAcquired) lockOnRst.Wait();

						var sent = socket.Send(frag, SocketFlags.None);

						if (sent == 0) trace(TraceOps.Status, fid, $"Socket sent 0 bytes. B: {blockID} #{i + 1}");
						else trace(TraceOps.Status, fid, $"B: {blockID} {map} #{i + 1}");

						if (!awaitRsp || rst.Wait(awaitMS)) break;
						awaitMS = (int)(awaitMS * cfg.RetryDelayStepMultiplier);
					}
				}

				return Volatile.Read(ref rsp);
			}
		}

		int signal(int refID, int mark, bool isError = false, bool keep = true)
		{
			var fid = Interlocked.Increment(ref frameCounter);
			var signal = new SIGNAL(fid, refID, mark, isError);
			Span<byte> s = stackalloc byte[signal.LENGTH];

			signal.Write(s);
			var sent = socket.Send(s);

			if (sent > 0)
			{
				if (keep) sentSignalsFor.TryAdd(refID, new SignalResponse(signal.Mark, isError));
				trace(TraceOps.Signal, fid, $"M: {mark} R: {refID}");
			}
			else trace(TraceOps.Signal, fid, $"Failed to send M: {mark} R: {refID}");

			return sent;
		}

		int tilex(byte kind, Span<byte> data, int refid, int fid = 0, Action<MemoryFragment> onTileXAwait = null)
		{
			if (data.Length > cfg.TileSizeBytes) throw new ArgumentException("data size");
			if (Volatile.Read(ref stop) > 0) return (int)SignalKind.NOTSET;

			ManualResetEventSlim rst = null;

			if (fid == 0) fid = Interlocked.Increment(ref frameCounter);
			var len = data.Length + TILEX.HEADER;
			var rsp = 0;
			var tilex = new TILEX(kind, fid, refid, 0, data);

			if (onTileXAwait != null) tileXAwaits.TryAdd(fid, new TileXAwait(onTileXAwait));

			if (signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
				{
					Volatile.Write(ref rsp, mark);
					rst.Set();
				})))
			{
				using (var frag = outHighway.Alloc(len))
				using (rst = new ManualResetEventSlim(false))
				{
					tilex.Write(frag);

					var awaitMS = cfg.RetryDelayStartMS;

					for (int i = 0; i < cfg.SendRetries; i++)
					{
						if (lockOnGate.IsAcquired) lockOnRst.Wait();

						var sent = socket.Send(frag, SocketFlags.None);

						if (sent == frag.Length) trace(TraceOps.TileX, fid, $"K: {(Lead)kind} #{i + 1}");
						else trace(TraceOps.TileX, fid, $"Failed K: {(Lead)kind}");

						if (rst.Wait(awaitMS)) break;

						awaitMS = (int)(awaitMS * cfg.RetryDelayStepMultiplier);
					}
				}
			}

			return Volatile.Read(ref rsp);
		}

		void tilexOnce(byte kind, Span<byte> data, int refid, int fid = 0, Action<MemoryFragment> onTileXAwait = null)
		{
			if (data.Length > cfg.TileSizeBytes) throw new ArgumentException("data size");
			if (Volatile.Read(ref stop) > 0) return;

			if (fid == 0) fid = Interlocked.Increment(ref frameCounter);
			var len = data.Length + TILEX.HEADER;

			if (onTileXAwait != null) tileXAwaits.TryAdd(fid, new TileXAwait(onTileXAwait));

			using (var frag = outHighway.Alloc(len))
			{
				var tilex = new TILEX(kind, fid, refid, 0, data);

				tilex.Write(frag);

				if (lockOnGate.IsAcquired) lockOnRst.Wait();
				var sent = socket.Send(frag, SocketFlags.None);

				if (sent == frag.Length) trace(TraceOps.TileX, fid, $"TileXOnce K: {kind}");
				else trace(TraceOps.TileX, fid, $"TileXOnce Failed:  Sent {sent} of {frag.Length} bytes");
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

				trace(op, frameID, $"Clone Cached reply: {crpl}");
				return true;
			}
			else return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void procProbe()
		{
			Volatile.Write(ref lastReceivedProbeTick, DateTime.Now.Ticks);

			if (!cfg.EnableProbes)
			{
				if (lockOnGate.IsAcquired) lockOnRst.Wait();

				socket.Send(probeLead);
			}
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
					case Lead.Signal: procSignal(f); break;
					case Lead.Error: procError(f); break;
					case Lead.Block: procBlock(f); break;
					case Lead.Status: procStatus(f); break;
					case Lead.TileX: procTileX(f); break;
					case Lead.CfgX: procCfgX(f); break;
					default:
					break;
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
				trace(TraceOps.DropFrame, f.FrameID, $"TileX R: {f.RefID}");
				return;
			}
#endif
			if (isClone(TraceOps.ProcTileX, f.FrameID)) return;

			signal(f.FrameID, (int)SignalKind.ACK);
			trace(TraceOps.ProcTileX, f.FrameID, $"K: {(Lead)f.Kind}");

			MemoryFragment xf = null;

			// The copying technically is not needed, but since the frames are
			// internal types, the consumer would have to guess the data offset.
			// Also the procTileX fragment will be disposed immediately after 
			// this method exits (i.e. no async for the callback), unlike
			// The newly created OnTileExchange frag which MUST be disposed 
			// in the callback (or wait for a GC pass as the default 
			// TileExchangeHighway tracks ghosts).

			if (f.Data.Length > 0)
			{
				xf = tileXHigheay.AllocFragment(f.Data.Length);
				f.Data.CopyTo(xf);
			}

			if (tileXAwaits.TryGetValue(f.RefID, out TileXAwait ta))
				if (ta.OnTileExchange != null) ta.OnTileExchange(xf);
				else onReceived(xf);
		}

		void procCfgX(MemoryFragment frag)
		{
			var f = new TILEX(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				trace(TraceOps.DropFrame, f.FrameID, $"CfgX R: {f.RefID}");
				return;
			}
#endif
			if (isClone(TraceOps.ProcTileX, f.FrameID)) return;
			signal(f.FrameID, (int)SignalKind.ACK);

			if (f.Data.Length == 0) ConfigExchange(f.FrameID);
			else
			{
				var tcfg = new CfgX(f.Data);
				Volatile.Write(ref targetCfg, tcfg);

				if (tileXAwaits.TryGetValue(f.RefID, out TileXAwait ta) && ta.OnTileExchange != null)
					ta.OnTileExchange(null);

				trace(TraceOps.ProcTileX, f.FrameID, $"K: CfgX " +
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

			if (!b.Receive(f))
			{
				trace(TraceOps.ProcBlock, f.FrameID, $"Tile ignore B: {b.ID} T: {f.TileIndex}");
				return;
			}

			trace(TraceOps.ProcBlock, f.FrameID, $"{f.Length} B: {f.BlockID} T: {f.TileIndex}");

			int fid = f.FrameID;

			if (b.IsComplete && onReceived != null &&
				Interlocked.CompareExchange(ref b.isOnCompleteTriggered, 1, 0) == 0)
				Task.Run(() =>
				{
					try
					{
						trace(TraceOps.ProcBlock, fid, $"B: {b.ID} completed.");
						onReceived(b.Fragment);
					}
					catch { }
				});

		}

		void procError(MemoryFragment frag)
		{
			var sg = new SIGNAL(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				trace(TraceOps.DropFrame, sg.FrameID, $"ProcError");
				return;
			}
#endif

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
							trace(TraceOps.ProcError, sg.FrameID, $"Rejected B: [{sg.RefID}.");
						}

						break;
					}
					case ErrorCode.Unknown:
					default:
					trace(TraceOps.ProcError, sg.FrameID, $"Unknown code M: {sg.Mark} R: [{sg.RefID}.");
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
				trace(TraceOps.DropFrame, st.FrameID, $"ProcStatus B: {st.BlockID}");
				return;
			}
#endif
			if (!isClone(TraceOps.ProcStatus, st.FrameID))
				if (blocks.TryGetValue(st.BlockID, out Block b))
				{
					// ACK the frame regardless of the context.
					signal(st.FrameID, (int)SignalKind.ACK);

					if (b.IsIncoming)
					{
						trace(TraceOps.ProcStatus, st.FrameID, $"All-sent for B: {st.BlockID}");

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
							trace(TraceOps.ProcStatus, st.FrameID, $"Out B: {st.BlockID} completed.");
							blocks.TryRemove(st.BlockID, out Block rem);
							b.Dispose();
						}
						else
						{
							trace(TraceOps.ProcStatus, st.FrameID,
								$"Re-beam B: {st.BlockID} M: {b.tileMap.ToString()}");

							rebeam(b);
						}
					}
				}
				else
				{
					signal(st.FrameID, (int)SignalKind.UNK);
					trace(TraceOps.ProcStatus, st.FrameID, $"Unknown B: {st.BlockID}");
				}
		}

		void procSignal(MemoryFragment frag)
		{
			var sg = new SIGNAL(frag.Span());

#if DEBUG
			if (cfg.dropFrame())
			{
				trace(TraceOps.DropFrame, sg.FrameID, $"ProcSignal M: {sg.Mark} R: {sg.RefID}");
				return;
			}
#endif
			if (!isClone(TraceOps.ProcSignal, sg.FrameID))
				if (signalAwaits.TryGetValue(sg.RefID, out SignalAwait sa) && sa.OnSignal != null)
					try
					{
						trace(TraceOps.ProcSignal, sg.FrameID, $"M: {sg.Mark} R: {sg.RefID}");
						sa.OnSignal(sg.Mark);
					}
					catch { }
				else trace(TraceOps.ProcSignal, sg.FrameID, $"NoAwait M: {sg.Mark} R: {sg.RefID}");
		}

		async Task probe()
		{
			if (cfg.EnableProbes)
				while (Volatile.Read(ref stop) < 1 && Volatile.Read(ref cfg.EnableProbes))
					try
					{
						socket.Send(probeLead);
						await Task.Delay(cfg.ProbeFreqMS).ConfigureAwait(false);
					}
					catch { }
		}

		async Task receive()
		{
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
							new Task((mf) => procFrame((MemoryFragment)mf), frag).Start();

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
			while (true)
			{
				var DTN = DateTime.Now;

				try
				{
					trace("Cleanup", $"Blocks: {blocks.Count} SignalAwaits: {signalAwaits.Count} SentSignals: {sentSignalsFor.Count}");

					tileXHigheay.FreeGhosts();

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
			try
			{
				int c = 0;

				while (Volatile.Read(ref stop) < 1 && !b.IsComplete && !b.isFaulted && !b.isRejected)
				{
					await Task.Delay(cfg.WaitAfterAllSentMS).ConfigureAwait(false);

					if (b.timeToReqTiles(cfg.WaitAfterAllSentMS) && b.RemainingTiles > 0)
					{
						trace(TraceOps.ReqTiles, 0, $"B: {b.ID} MT: {b.MarkedTiles}/{b.TilesCount} #{c++}");

						using (var frag = outHighway.Alloc(b.tileMap.Bytes))
						{
							b.tileMap.WriteTo(frag);
							status(b.ID, b.MarkedTiles, false, frag);
						}
					}
				}

				trace(TraceOps.ReqTiles, 0, $"Out-of-ReqTiles B: {b.ID} IsComplete: {b.IsComplete}");
			}
			catch (Exception ex)
			{
				trace("Ex", ex.Message, ex.ToString());
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void trace(TraceOps op, int frame, string title, string msg = null)
		{
			if (log != null && Volatile.Read(ref cfg.Log.Enabled) && (op & cfg.Log.Flags) == op)
			{
				string ttl = string.Empty;
				switch (op)
				{
					case TraceOps.Beam:
					case TraceOps.Status:
					case TraceOps.Signal:
					case TraceOps.TileX:
					ttl = string.Format("{0,10}:o {1, -12} {2}", frame, op, title);
					break;
					case TraceOps.ReqTiles:
					ttl = string.Format("{0,-12} {1, -12} {2}", " ", op, title);
					break;
					case TraceOps.ProcBlock:
					case TraceOps.ProcError:
					case TraceOps.ProcStatus:
					case TraceOps.ProcSignal:
					case TraceOps.ProcTileX:
					ttl = string.Format("{0,10}:i {1, -12} {2}", frame, op, title);
					break;
#if DEBUG
					case TraceOps.DropFrame:
					ttl = string.Format("{0,10}:? {1, -12} {2}", frame, op, title);
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
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void trace(string op, string title, string msg = null)
		{
			if (log != null && Volatile.Read(ref cfg.Log.Enabled))
			{
				var s = string.Format("{0,-12} {1, -12} {2}", " ", op, title);
				log.Write(s, msg);
				if (cfg.Log.OnTrace != null)
					try { cfg.Log.OnTrace(TraceOps.None, s, msg); }
					catch { }
			}
		}

		Action<MemoryFragment> onReceived;

		HeapHighway outHighway;
		IMemoryHighway blockHighway;
		IMemoryHighway tileXHigheay;
		BeamerCfg cfg;
		IPEndPoint target;
		IPEndPoint source;
		Socket socket;
		Log log;

#if DEBUG
		int ccBeamsFuse;
#endif
		int ccBeams;

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

		ManualResetEventSlim lockOnRst = new ManualResetEventSlim(true);
		Gate lockOnGate = new Gate();
		Task probingTask;
		Task cleanupTask;
		Task[] receivers;
		CfgX targetCfg;

		// All keys are frame or block IDs

		ConcurrentDictionary<int, SignalAwait> signalAwaits = new ConcurrentDictionary<int, SignalAwait>();
		ConcurrentDictionary<int, Block> blocks = new ConcurrentDictionary<int, Block>();
		ConcurrentDictionary<int, DateTime> procFrames = new ConcurrentDictionary<int, DateTime>();
		ConcurrentDictionary<int, SignalResponse> sentSignalsFor = new ConcurrentDictionary<int, SignalResponse>();
		ConcurrentDictionary<int, TileXAwait> tileXAwaits = new ConcurrentDictionary<int, TileXAwait>();

		public const ushort UDP_MAX = ushort.MaxValue;
	}
}
