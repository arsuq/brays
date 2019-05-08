using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using System.Linq;

[assembly: InternalsVisibleTo("brays.tests")]

namespace brays
{
	public class RayEmitter : IDisposable
	{
		public RayEmitter(Action<MemoryFragment> onReceived, EmitterCfg cfg)
		{
			if (onReceived == null || cfg == null) throw new ArgumentNullException();

			ID = Guid.NewGuid();
			this.onReceived = onReceived;
			this.cfg = cfg;
			outHighway = new HeapHighway(new HighwaySettings(UDP_MAX), UDP_MAX, UDP_MAX, UDP_MAX);
			blockHighway = cfg.ReceiveHighway;

			if (cfg.Log != null)
				log = new Log(cfg.Log.LogFilePath, cfg.Log.Ext, cfg.Log.RotationLogFileKB);
		}

		public void Dispose()
		{
			if (outHighway != null)
				try
				{
					Interlocked.Exchange(ref stop, 1);
					outHighway.Dispose();
					blockHighway.Dispose();
					socket.Dispose();
					outHighway = null;
					blockHighway = null;
					if (log != null) log.Dispose();

					foreach (var b in blockMap.Values)
						if (b != null) b.Dispose();
				}
				catch { }
		}

		public void Stop() => Volatile.Write(ref stop, 1);

		public bool LockOn(IPEndPoint listen, IPEndPoint target, bool configexchange = false)
		{
			Volatile.Write(ref isLocked, false);

			this.source = listen;
			this.target = target;

			if (socket != null)
			{
				Interlocked.Exchange(ref stop, 1);
				probingTask?.Dispose();
				cleanupTask?.Dispose();

				if (receivers != null)
					foreach (var r in receivers)
						r.Dispose();

				socket.Close();
			}

			socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(this.source);
			socket.Connect(target);
			socket.ReceiveBufferSize = cfg.ReceiveBufferSize;
			socket.SendBufferSize = cfg.SendBufferSize;

			Interlocked.Exchange(ref stop, 0);
			receivers = new Task[cfg.MaxConcurrentReceives];

			for (int i = 0; i < receivers.Length; i++)
				receivers[i] = receive();

			probingTask = probe();
			cleanupTask = cleanup();
			Volatile.Write(ref isLocked, true);

			if (configexchange)
			{
				if (!configx()) return false;

				// 2d0: settings update
			}

			trace("Lock-on", $"{source.ToString()} target: {target.ToString()}");
			return true;
		}

		public bool ConfigExchange() => configx();

		public async Task<bool> Beam(MemoryFragment f)
		{
			var bid = Interlocked.Increment(ref blockCounter);
			var b = new Block(bid, cfg.TileSizeBytes, f);

			if (blockMap.TryAdd(bid, b))
				try
				{
					return await Task.Run(async () =>
					{
						for (int i = 0; i < cfg.TotalReBeamsCount; i++)
							if (beam(b)) return true;
							else await Task.Delay(cfg.BeamAwaitMS);

						return false;
					});
				}
				catch (AggregateException aex)
				{
					trace("Ex", "Beam", aex.Flatten().ToString());
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
		public Exception Exception => exception;

		bool beam(Block b)
		{
			int sent = 0;

			for (int i = 0; i < b.tileMap.Count; i++)
				if (!b.tileMap[i])
					try
					{
						if (Volatile.Read(ref b.isRejected) || Volatile.Read(ref stop) > 0)
						{
							trace("Beam", "Stopped");
							return false;
						}

						var cc = Volatile.Read(ref ccBeams);

						while (cc > cfg.MaxBeamedTilesAtOnce)
						{
							cc = Volatile.Read(ref ccBeams);
							Thread.Sleep(10);
						}

						Interlocked.Increment(ref ccBeams);

						var data = b.Fragment.Span().Slice(i * b.TileSize);
						if (data.Length > b.TileSize) data = data.Slice(0, b.TileSize);

						var fid = Interlocked.Increment(ref frameCounter);
						var f = new FRAME(fid, b.ID, b.TotalSize, i, b.TileSize, 0, data);

						using (var mf = outHighway.Alloc(f.LENGTH))
						{
							var dgram = mf.Span();
							f.Write(dgram);
							var sbytes = socket.Send(dgram);

							if (sbytes == dgram.Length)
							{
								trace(TraceOps.Beam, fid, $"{sbytes} B: {b.ID} T: {i}");
								sent++;
							}
							else
							{
								trace(TraceOps.Beam, fid, $"Faulted for sending {sbytes} of {dgram.Length} byte dgram.");
								b.isFaulted = true;
								return false;
							}
						}
					}
					catch (Exception ex)
					{
						trace("Ex", ex.Message, ex.StackTrace);
					}
					finally
					{
						Interlocked.Decrement(ref ccBeams);
					}

			b.sentTime = DateTime.Now;
			return status(b.ID, sent, true, null);
		}

		bool status(int blockID, int tilesCount, bool awaitRsp, Span<byte> tileMap)
		{
			if (Volatile.Read(ref stop) > 0) return false;

			var rst = new ManualResetEventSlim(false);
			var fid = Interlocked.Increment(ref frameCounter);
			var len = tileMap.Length + STATUS.HEADER;
			var rsp = false;

			using (var frag = outHighway.Alloc(len))
			{
				STATUS.Make(fid, blockID, tilesCount, tileMap, frag.Span());

				var ackq = !awaitRsp || signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
				{
					if ((SignalKind)mark == SignalKind.ACK) Volatile.Write(ref rsp, true);
					else trace(TraceOps.Status, fid, $"NACK B: {blockID}");

					//signalAwaits.TryRemove(fid, out SignalAwait x);
					rst.Set();
				}));

				if (ackq)
				{
					var map = string.Empty;

					if (blockMap.TryGetValue(blockID, out Block b) && tileMap.Length > 0)
						map = $"M: {b.tileMap.ToString()} ";

					for (int i = 0; i < cfg.SendRetries; i++)
					{
						var sent = socket.Send(frag, SocketFlags.None);

						if (sent == 0) trace(TraceOps.Status, fid, $"Socket sent 0 bytes. B: {blockID} #{i + 1}");
						else trace(TraceOps.Status, fid, $"B: {blockID} {map} #{i + 1}");

						if (!awaitRsp || rst.Wait(cfg.RetryDelayMS)) break;
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

		bool configx()
		{
			if (Volatile.Read(ref stop) > 0) return false;
			Volatile.Write(ref lastCfgxSendTick, DateTime.Now.Ticks);

			var rst = new ManualResetEventSlim(false);
			var cfgx = new CfgX(cfg);
			var fid = Interlocked.Increment(ref frameCounter);
			var len = CfgX.LENGTH + CFGX.HEADER;
			var rsp = false;
			Span<byte> s = stackalloc byte[len];
			cfgx.Write(s.Slice(CFGX.HEADER));
			var dgram = new CFGX(fid, 0, s.Slice(CFGX.HEADER));
			dgram.Write(s);

			var ackq = signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
			{
				if ((SignalKind)mark == SignalKind.ACK)
					Volatile.Write(ref rsp, true);

				rst.Set();
			}));

			if (ackq)
			{
				for (int i = 0; i < cfg.SendRetries; i++)
				{
					var sent = socket.Send(s, SocketFlags.None);

					if (sent == 0) trace(TraceOps.CfgX, fid, "Socket sent 0 bytes");
					else trace(TraceOps.CfgX, fid, $"#{i + 1}");
					if (rst.Wait(cfg.RetryDelayMS)) break;
				}
			}

			return Volatile.Read(ref rsp);
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		void procProbe()
		{
			Volatile.Write(ref lastReceivedProbeTick, DateTime.Now.Ticks);
			signal(0, (byte)SignalKind.ACK, false, false);
		}

		void procCfgX(MemoryFragment frag)
		{
			var f = new CFGX(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				trace(TraceOps.DropFrame, f.FrameID, "CfgX");
				return;
			}
#endif
			if (isClone(TraceOps.CfgX, f.FrameID, false)) return;

			var tcfg = new CfgX(f.Data);
			var oldc = Interlocked.CompareExchange(ref targetCfg, tcfg, null);
			signal(f.FrameID, (int)SignalKind.ACK, false);
			trace(TraceOps.ProcCfgX, f.FrameID,
				$"MaxBeams: {tcfg.MaxBeamedTilesAtOnce} MaxRcvs: {tcfg.MaxConcurrentReceives} " +
				$"SBuff: {tcfg.SendBufferSize} RBuff: {tcfg.ReceiveBufferSize}");

			if (DateTime.Now.Ticks - Volatile.Read(ref lastCfgxSendTick) > cfg.SendCfxOnceEveryXTicks)
				configx();
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
			if (!blockMap.ContainsKey(f.BlockID))
			{
				// Reject logic + Error or:
				if (blockMap.TryAdd(f.BlockID, new Block(f.BlockID, f.TotalSize, f.Length, blockHighway)))
					signal(f.FrameID, (int)SignalKind.ACK);
			}

			var b = blockMap[f.BlockID];

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
						if (blockMap.TryGetValue(sg.RefID, out Block b))
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
				if (blockMap.TryGetValue(st.BlockID, out Block b))
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
							blockMap.TryRemove(st.BlockID, out Block rem);
							b.Dispose();
						}
						else
						{
							trace(TraceOps.ProcStatus, st.FrameID,
								$"Re-beam B: {st.BlockID} M: {b.tileMap.ToString()}");

							beam(b);
						}
					}
				}
				else
				{
					signal(st.FrameID, (int)SignalKind.UNK);
					trace(TraceOps.ProcStatus, st.FrameID, $"Unknown B: {st.BlockID}");
				}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		bool isClone(TraceOps op, int frameID, bool signalWithLastReply = true)
		{
			if (!procFrames.TryAdd(frameID, DateTime.Now))
			{
				var s = string.Empty;

				if (signalWithLastReply && sentSignalsFor.TryGetValue(frameID, out SignalResponse sr))
				{
					signal(frameID, sr.Mark, sr.IsError);
					s = $"Cached Reply M: {sr.Mark}";
				}

				trace(op, frameID, $"Clone {s}");

				return true;
			}
			else return false;
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

		void suggestTileSize()
		{
			// 2d0
		}

		async Task probe()
		{
			if (cfg.EnableProbes)
				while (Volatile.Read(ref stop) < 1 && Volatile.Read(ref cfg.EnableProbes))
					try
					{
						socket.Send(probeLead);
						await Task.Delay(cfg.ProbeFreqMS);
					}
					catch { }
		}

		async Task receive()
		{
			using (var inHighway = new HeapHighway(new HighwaySettings(UDP_MAX), UDP_MAX, UDP_MAX))
				while (Volatile.Read(ref stop) < 1)
					try
					{
						var frag = inHighway.Alloc(UDP_MAX);
						var read = await socket.ReceiveAsync(frag, SocketFlags.None);

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
							else await Task.Delay(cfg.ErrorAwaitMS);
						}
					}
					catch (Exception ex)
					{
						Interlocked.Exchange(ref exception, ex);
					}

			trace("Receive", "Not listening.");
			Interlocked.Exchange(ref stop, 1);
		}

		async Task cleanup()
		{
			while (true)
			{
				var DTN = DateTime.Now;

				try
				{
					trace("Cleanup", $"Blocks: {blockMap.Count} SignalAwaits: {signalAwaits.Count} SentSignals: {sentSignalsFor.Count}");

					foreach (var b in blockMap.Values)
						if (DateTime.Now.Subtract(b.sentTime) > cfg.SentBlockRetention)
							blockMap.TryRemove(b.ID, out Block x);

					foreach (var sa in signalAwaits)
						if (sa.Value.OnSignal != null && DateTime.Now.Subtract(sa.Value.Created) > cfg.SignalAwait)
							signalAwaits.TryRemove(sa.Key, out SignalAwait x);

					foreach (var fi in procFrames)
						if (DTN.Subtract(fi.Value) > cfg.ProcessedFramesIDRetention)
							procFrames.TryRemove(fi.Key, out DateTime x);

					foreach (var ss in sentSignalsFor)
						if (DTN.Subtract(ss.Value.Created) > cfg.SentSignalsRetention)
							sentSignalsFor.TryRemove(ss.Key, out SignalResponse x);
				}
				catch { }
				await Task.Delay(cfg.CleanupFreqMS);
			}
		}

		async Task reqMissingTiles(Block b)
		{
			try
			{
				int c = 0;

				while (Volatile.Read(ref stop) < 1 && !b.IsComplete && !b.isFaulted && !b.isRejected)
				{
					await Task.Delay(cfg.WaitAfterAllSentMS);

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
					case TraceOps.CfgX:
					ttl = string.Format("{0,10}:o {1, -12} {2}", frame, op, title);
					break;
					case TraceOps.ReqTiles:
					ttl = string.Format("{0,-12} {1, -12} {2}", " ", op, title);
					break;
					case TraceOps.ProcBlock:
					case TraceOps.ProcError:
					case TraceOps.ProcStatus:
					case TraceOps.ProcSignal:
					case TraceOps.ProcCfgX:
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

		internal Block[] notComplete() => blockMap.Values.Where(x => !x.IsComplete).ToArray();

		Action<MemoryFragment> onReceived;

		HeapHighway outHighway;
		IMemoryHighway blockHighway;
		EmitterCfg cfg;
		IPEndPoint target;
		IPEndPoint source;
		Socket socket;
		Log log;

#if DEBUG
		int ccProcsCount;
#endif

		int ccBeams;
		long lastBeamBurstTick;
		long lastCfgxSendTick;

		int frameCounter;
		int blockCounter;
		int receivedDgrams;
		int stop;
		int retries;
		bool isLocked;
		long lastReceivedProbeTick;
		long lastReceivedDgramTick;
		byte[] probeLead = new byte[] { (byte)Lead.Probe };

		Exception exception;
		Task probingTask;
		Task cleanupTask;
		Task[] receivers;
		CfgX targetCfg;

		ConcurrentDictionary<int, SignalAwait> signalAwaits = new ConcurrentDictionary<int, SignalAwait>();
		ConcurrentDictionary<int, Block> blockMap = new ConcurrentDictionary<int, Block>();
		ConcurrentDictionary<int, DateTime> procFrames = new ConcurrentDictionary<int, DateTime>();
		ConcurrentDictionary<int, SignalResponse> sentSignalsFor = new ConcurrentDictionary<int, SignalResponse>();

		public const ushort UDP_MAX = ushort.MaxValue;
	}
}
