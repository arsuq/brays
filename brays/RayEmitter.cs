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
	public class RayEmitter : IDisposable
	{
		public RayEmitter(Action<MemoryFragment> onReceived, EmitterCfg cfg)
		{
			if (onReceived == null || cfg == null) throw new ArgumentNullException();

			ID = Guid.NewGuid();
			this.onReceived = onReceived;
			this.cfg = cfg;
			outHighway = new HeapHighway(new HighwaySettings(UDP_MAX), UDP_MAX, UDP_MAX, UDP_MAX);
			inHighway = new HeapHighway(new HighwaySettings(UDP_MAX), UDP_MAX, UDP_MAX);
			blockHighway = cfg.ReceiveHighway;

			if (cfg.Log) log = new Log(cfg.LogFilePath, "log", 500);
		}

		public void Dispose()
		{
			if (outHighway != null)
				try
				{
					Interlocked.Exchange(ref stop, 1);
					outHighway.Dispose();
					blockHighway.Dispose();
					inHighway.Dispose();
					socket.Dispose();
					outHighway = null;
					blockHighway = null;
					inHighway = null;
					if (log != null) log.Dispose();
				}
				catch { }
		}

		public void LockOn(IPEndPoint listen, IPEndPoint target)
		{
			Volatile.Write(ref isLocked, false);

			this.source = listen;
			this.target = target;

			if (socket != null)
			{
				Interlocked.Exchange(ref stop, 1);
				receiveTask?.Dispose();
				probingTask?.Dispose();
				cleanupTask?.Dispose();
				socket.Close();
			}

			socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

			socket.Bind(this.source);
			socket.Connect(target);
			Interlocked.Exchange(ref stop, 0);
			receiveTask = receive();
			probingTask = probe();
			cleanupTask = cleanup();
			Volatile.Write(ref isLocked, true);

			if (log != null) log.Write($"Lock-on source: {source.ToString()} target: {target.ToString()}");
		}

		public async Task<bool> Beam(MemoryFragment f)
		{
			var bid = Interlocked.Increment(ref blockCounter);
			var b = new Block(bid, cfg.TileSize, f);

			if (blockMap.TryAdd(bid, b))
			{
				if (b.TilesCount > 1 && !reqack(b))
					return false;

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
					if (log != null) log.Write("Beam()", aex.Flatten().ToString());
				}
			}

			return false;
		}

		public readonly Guid ID;
		public bool IsLocked => isLocked;
		public int FrameCounter => frameCounter;
		public int ReceivedDgrams => receivedDgrams;
		public IPEndPoint Target => target;
		public IPEndPoint Source => source;
		public DateTime LastProbe => new DateTime(Volatile.Read(ref lastReceivedProbeTick));
		public Exception Exception => exception;

		bool beam(Block b)
		{
			if (b == null) throw new ArgumentNullException();
			if (cfg.TileSize != b.TileSize) throw new ArgumentException("TileSize mismatch");

			int sent = 0;

			for (int i = 0; i < b.tileMap.Count; i++)
				if (!b.tileMap[i])
					using (var mf = outHighway.Alloc(b.TileSize + 50))
					{
						var s = b.Fragment.Span().Slice(i * b.TileSize);
						var fid = Interlocked.Increment(ref frameCounter);
						var f = new FRAME(fid, b.ID, b.TotalSize, i, b.TileSize, 0, s);

						f.Write(mf.Span());
						var dgram = mf.Span().Slice(0, f.LENGTH);
						var sbytes = socket.Send(dgram);

						if (sbytes != dgram.Length)
						{
							b.isFaulted = true;

							if (log != null)
								traceOutFrame("Beam", fid, $"Faulted for sending {sbytes} of {dgram.Length} byte dgram.");

							return false;
						}

						if (log != null) traceOutFrame("Beam", fid, $"{sbytes} B:{b.ID} T:{i}");

						sent++;
					}

			b.sentTime = DateTime.Now;
			return status(b.ID, sent, true, null);
		}

		bool status(int blockID, int tilesCount, bool awaitRsp, Span<byte> tileMap)
		{
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
					else if (log != null) traceInFrame("NACK", fid, string.Empty);

					signalAwaits.TryRemove(fid, out SignalAwait x);
					rst.Set();
				}));

				if (ackq)
				{
					var map = string.Empty;

					if (blockMap.TryGetValue(blockID, out Block b) && tileMap.Length > 0)
						map = $"M: {b.tileMap.ToBinaryString()} ";

					for (int i = 0; i < cfg.SendRetries; i++)
					{
						var sent = socket.Send(frag, SocketFlags.None);

						if (log != null)
							if (sent == 0) traceOutFrame("Status", fid, $"Socket sent 0 bytes. B: {blockID} #{i + 1}");
							else traceOutFrame("Status", fid, $"B: {blockID} {map} #{i + 1}");

						if (!awaitRsp || rst.Wait(cfg.RetryDelayMS)) break;
					}
				}

				return Volatile.Read(ref rsp);
			}
		}

		int signal(int refID, int mark, bool isError = false)
		{
			var fid = Interlocked.Increment(ref frameCounter);
			var signal = new SIGNAL(fid, refID, mark, isError);
			Span<byte> s = stackalloc byte[signal.LENGTH];

			signal.Write(s);
			var sent = socket.Send(s);

			if (sent > 0 && log != null) traceOutFrame("Signal", fid, $"Mark [{mark}] RefID [{refID}]");

			return sent;
		}

		bool reqack(Block b)
		{
			var rst = new ManualResetEventSlim(false);
			var fid = Interlocked.Increment(ref frameCounter);
			var opt = (byte)FrameOptions.ReqAckBlockTransfer;
			var frm = new FRAME(fid, b.ID, b.TotalSize, 0, b.TileSize, opt, null);
			var rsp = false;

			var ackq = signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
			{
				if ((SignalKind)mark == SignalKind.ACK)
				{
					Volatile.Write(ref b.reqAckDgram, null);
					Volatile.Write(ref rsp, true);
				}

				signalAwaits.TryRemove(fid, out SignalAwait x);
				rst.Set();
			}));

			if (ackq)
			{
				b.reqAckDgram = new byte[FRAME.HEADER];
				frm.Write(b.reqAckDgram);

				for (int i = 0; i < cfg.SendRetries; i++)
					if (Volatile.Read(ref b.reqAckDgram) != null)
					{
						fid = Interlocked.Increment(ref frameCounter);
						frm = new FRAME(fid, b.ID, b.TotalSize, 0, b.TileSize, opt, null);

						frm.Write(b.reqAckDgram);
						socket.Send(b.reqAckDgram, SocketFlags.None);

						if (log != null)
							traceOutFrame("ReqAck", frm.FrameID, $"B: {b.ID} Total: {b.TotalSize} Tile: {b.TileSize}, #{i + 1}");

						if (rst.Wait(cfg.RetryDelayMS)) break;
					}
					else break;

				Volatile.Write(ref b.reqAckDgram, null);
			}

			return Volatile.Read(ref rsp);

		}

		void procFrame(MemoryFragment f)
		{
			try
			{
				var lead = (Lead)f.Span()[0];

				switch (lead)
				{
					case Lead.Probe: Volatile.Write(ref lastReceivedProbeTick, DateTime.Now.Ticks); break;
					case Lead.Signal: procSignal(f); break;
					case Lead.Error: procError(f); break;
					case Lead.Block: procBlock(f); break;
					case Lead.Status: procStatus(f); break;
					default:
					break;
				}
			}
			catch (AggregateException aex)
			{
				if (log != null) log.Write("ProcFrame", aex.Flatten().ToString());
			}
			catch (Exception ex)
			{
				if (log != null) log.Write(ex.Message, ex.ToString());
			}
			finally { f.Dispose(); }
		}

		void procBlock(MemoryFragment frag)
		{
			var f = new FRAME(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				if (log != null) traceInFrame("Dropping", f.FrameID, "[Block]");
				return;
			}
#endif
			if (!procFrames.TryAdd(f.FrameID, DateTime.Now))
			{
				if (log != null) traceInFrame("Clone", f.FrameID, string.Empty);
				return;
			}

			if (!blockMap.ContainsKey(f.BlockID))
			{
				// If it's just one tile there is no need of ReqAck.
				if (f.TotalSize < cfg.TileSize || f.Options == (byte)FrameOptions.ReqAckBlockTransfer)
				{
					// Reject logic + NACK here or:
					blockMap.TryAdd(f.BlockID, new Block(f.BlockID, f.TotalSize, f.Length, blockHighway));
					signal(f.FrameID, (int)SignalKind.ACK);

					if (f.Options == (byte)FrameOptions.ReqAckBlockTransfer) return;
				}
				else
				{
					signal(f.FrameID, (int)ErrorCode.NoTranferAck, true);
					return;
				}
			}
			else
			{
				signal(f.FrameID, (int)SignalKind.ACK);
				if (f.Options == (byte)FrameOptions.ReqAckBlockTransfer)
				{
					if (log != null) traceInFrame("Clone REQACK", f.FrameID, null);
					return;
				}
			}

			var b = blockMap[f.BlockID];

			if (!b.Receive(f))
			{
				if (log != null) traceInFrame("Tile ignore", f.FrameID, $"B: {b.ID} T: {f.TileIndex}");
				return;
			}

			if (log != null) traceInFrame("Block", f.FrameID, $"{f.Length} B:{f.BlockID} T: {f.TileIndex}");

			int fid = f.FrameID;

			if (b.IsComplete && onReceived != null && Interlocked.CompareExchange(ref b.isOnCompleteTriggered, 1, 0) == 0)
				Task.Run(() =>
				{
					try
					{
						if (log != null) traceInFrame("Block", fid, $"B: {b.ID} completed. Triggering the onReceived callback.");
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
				if (log != null) traceInFrame("Dropping", sg.FrameID, "[Error]");
				return;
			}
#endif
			if (!procFrames.TryAdd(sg.FrameID, DateTime.Now)) return;
			if (log != null) traceInFrame("Error", sg.FrameID, $"Code [{sg.Mark}] RefID [{sg.RefID}].");
		}

		void procStatus(MemoryFragment frag)
		{
			var st = new STATUS(frag.Span());
#if DEBUG
			if (cfg.dropFrame())
			{
				if (log != null) traceInFrame("Dropping", st.FrameID, "[Status]");
				return;
			}
#endif
			if (!procFrames.TryAdd(st.FrameID, DateTime.Now)) return;
			if (blockMap.TryGetValue(st.BlockID, out Block b))
			{
				// ACK the frame regardless of the context.
				signal(st.FrameID, (int)SignalKind.ACK);

				if (b.IsIncoming)
				{
					if (log != null) traceInFrame("InStatus", st.FrameID, $"In B: {st.BlockID}");

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
						if (log != null) traceInFrame("InStatus", st.FrameID, $"Out B: {st.BlockID} completed.");
						if (blockMap.TryRemove(st.BlockID, out Block rem)) b.Dispose();
					}
					else
					{
						if (log != null) traceInFrame("InStatus", st.FrameID,
							$"[Re-beam] B: {st.BlockID} M: {b.tileMap.ToBinaryString()}");

						beam(b);
					}
				}
			}
			else
			{
				signal(st.FrameID, (int)SignalKind.UNK);
				if (log != null) traceInFrame("InStatus", st.FrameID, $"[Unknown] B: {st.BlockID}");
			}
		}

		void procSignal(MemoryFragment frag)
		{
			var sg = new SIGNAL(frag.Span());

#if DEBUG
			if (cfg.dropFrame())
			{
				if (log != null) traceInFrame("Dropping", sg.FrameID, "[Signal]");
				return;
			}
#endif
			if (!procFrames.TryAdd(sg.FrameID, DateTime.Now))
			{
				if (log != null) traceInFrame("Clone", sg.FrameID, string.Empty);
				return;
			}

			if (signalAwaits.TryGetValue(sg.RefID, out SignalAwait sa) && sa.OnSignal != null)
				try
				{
					if (log != null) traceInFrame("InSignal", sg.FrameID, $"Mark: [{sg.Mark}] RefID: {sg.RefID}");
					sa.OnSignal(sg.Mark);
				}
				catch { }
			else if (log != null) traceInFrame("NoAwait", sg.FrameID, string.Empty);
		}

		void suggestTileSize()
		{

		}

		async Task probe()
		{
			if (cfg.EnableProbes)
				while (Volatile.Read(ref stop) < 1)
					try
					{
						socket.Send(probeLead);
						await Task.Delay(cfg.ProbeFreqMS);
					}
					catch { }
		}

		async Task receive()
		{
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
						frag.Dispose();

						if (Interlocked.Increment(ref retries) > cfg.MaxReceiveRetries) break;
						else await Task.Delay(cfg.ErrorAwaitMS);
					}
				}
				catch (Exception ex)
				{
					Interlocked.Exchange(ref exception, ex);
					Interlocked.Exchange(ref stop, 1);
				}

			if (log != null) log.Write("Not listening.");
		}

		async Task cleanup()
		{
			while (true)
			{
				try
				{
					if (log != null) log.Write($"[Cleanup] Blocks: {blockMap.Count}; Signals: {signalAwaits.Count}");

					foreach (var b in blockMap.Values)
						if (DateTime.Now.Subtract(b.sentTime) > cfg.SentBlockRetention)
							blockMap.TryRemove(b.ID, out Block x);

					foreach (var sa in signalAwaits)
						if (sa.Value.OnSignal != null && DateTime.Now.Subtract(sa.Value.Created) > cfg.SignalAwait)
							signalAwaits.TryRemove(sa.Key, out SignalAwait x);

					var DTN = DateTime.Now;

					foreach (var fi in procFrames)
						if (DTN.Subtract(fi.Value) > cfg.ProcessedFramesIDRetention)
							procFrames.TryRemove(fi.Key, out DateTime x);
				}
				catch { }
				await Task.Delay(cfg.CleanupFreqMS);
			}
		}

		async Task reqMissingTiles(Block b)
		{
			try
			{
				while (!b.IsComplete || !b.isFaulted)
				{
					await Task.Delay(cfg.WaitForTilesAfterAllSentMS);

					if (b.shouldReqTiles(cfg.WaitForTilesAfterAllSentMS))
					{
						if (log != null) log.Write($"[ReqTiles] B: {b.ID} m/t: {b.MarkedTiles}/{b.TilesCount}");

						using (var frag = outHighway.Alloc(b.tileMap.Bytes))
						{
							b.tileMap.WriteTo(frag);
							status(b.ID, b.MarkedTiles, false, frag);
						}
					}
				}

				if (log != null) log.Write($"Out of ReqTiles B: {b.ID} IsComplete: {b.IsComplete} IsFaulted: {b.isFaulted}");
			}
			catch (Exception ex)
			{
				if (log != null) log.Write(ex.Message, ex.ToString());
			}
		}

		void traceOutFrame(string op, int frame, string msg)
		{
			var s = string.Format("o:{0,-10} [{1}] {2}", frame, op, msg);
			log.Write(s);
		}

		void traceInFrame(string op, int frame, string msg)
		{
			var s = string.Format("i:{0,-10} [{1}] {2}", frame, op, msg);
			log.Write(s);
		}

		Action<MemoryFragment> onReceived;

		HeapHighway outHighway;
		HeapHighway inHighway;
		IMemoryHighway blockHighway;
		EmitterCfg cfg;
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
		long lastReceivedProbeTick;
		byte[] probeLead = new byte[] { (byte)Lead.Probe };

		Exception exception;
		Task probingTask;
		Task receiveTask;
		Task cleanupTask;

		ConcurrentDictionary<int, SignalAwait> signalAwaits = new ConcurrentDictionary<int, SignalAwait>();
		ConcurrentDictionary<int, Block> blockMap = new ConcurrentDictionary<int, Block>();
		ConcurrentDictionary<int, DateTime> procFrames = new ConcurrentDictionary<int, DateTime>();

		public const ushort UDP_MAX = ushort.MaxValue;
	}
}
