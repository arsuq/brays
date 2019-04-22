using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace brays
{
	public class RayEmitter : IDisposable
	{
		public RayEmitter(Action<MemoryFragment> onReceived, EmitterCfg cfg)
		{
			if (onReceived == null || cfg == null) throw new ArgumentNullException();

			this.onReceived = onReceived;
			this.cfg = cfg;
			outHighway = new HeapHighway(new HighwaySettings(UDP_MAX), UDP_MAX, UDP_MAX, UDP_MAX);
			inHighway = cfg.ReceiveHighway;
		}

		public void Dispose()
		{
			if (outHighway != null)
				try
				{
					Interlocked.Exchange(ref stop, 1);
					outHighway.Dispose();
					inHighway.Dispose();
					socket.Dispose();
					outHighway = null;
					inHighway = null;
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
		}

		public async Task<bool> Beam(MemoryFragment f)
		{
			var bid = Interlocked.Increment(ref blockCounter);
			var b = new Block(bid, cfg.TileSize, f);

			if (blockMap.TryAdd(bid, b))
			{
				if (b.TileCount > 1 && !reqack(b))
					return false;

				await Task.Run(() => beam(b));
				return true;
			}

			return false;
		}

		public bool IsLocked => isLocked;
		public int FrameCounter => frameCounter;
		public IPEndPoint Target => target;
		public IPEndPoint Source => source;
		public DateTime LastProbe => new DateTime(Volatile.Read(ref lastReceivedProbeTick));
		public Exception Exception => exception;

		void beam(Block b)
		{
			if (b == null) throw new ArgumentNullException();
			if (cfg.TileSize != b.TileSize) throw new ArgumentException("TileSize mismatch");

			int sent = 0;

			for (int i = 0; i < b.tileMap.Length; i++)
				if (!b.tileMap[i])
					using (var mf = outHighway.Alloc(b.TileSize + 50))
					{
						var s = b.Fragment.Span().Slice(i * b.TileSize);
						var fid = Interlocked.Increment(ref frameCounter);
						var f = new FRAME(fid, b.ID, b.TileCount, i, (ushort)s.Length, 0, s);

						f.Write(mf.Span());
						var dgram = mf.Span().Slice(0, f.LENGTH);

						if (socket.Send(dgram) < 1)
						{
							b.isFaulted = true;
							return;
						}

						sent++;
					}

			b.sentTime = DateTime.Now;
			status(b.ID, sent, null);
		}

		bool status(int blockID, int tilesCount, Span<byte> tileMap)
		{
			var rst = new ManualResetEventSlim(false);
			var fid = Interlocked.Increment(ref frameCounter);
			var len = tileMap.Length + 13;
			var rsp = false;

			using (var frag = outHighway.Alloc(len))
			{
				STATUS.Make(fid, blockID, tilesCount, tileMap, frag.Span());

				var ackq = signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
				{
					if ((SignalKind)mark == SignalKind.ACK)
						rsp = true;

					signalAwaits.TryRemove(fid, out SignalAwait x);
					rst.Set();
				}));

				if (ackq)
					for (int i = 0; i < cfg.SendRetries; i++)
					{
						socket.Send(frag, SocketFlags.None);
						if (rst.Wait(cfg.RetryDelayMS)) return rsp;
					}

				return false;
			}
		}

		void signal(int refID, int mark, bool isError = false)
		{
			var fid = Interlocked.Increment(ref frameCounter);
			var signal = new SIGNAL(fid, refID, mark, isError);
			Span<byte> s = stackalloc byte[signal.LENGTH];

			signal.Write(s);
			socket.Send(s);
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
			catch (Exception ex)
			{
			}
			finally { f.Dispose(); }
		}

		void procBlock(MemoryFragment frag)
		{
			var f = new FRAME(frag.Span());

			if (!procFrames.TryAdd(f.FrameID, DateTime.Now)) return;

			if (!blockMap.ContainsKey(f.BlockID))
			{
				// If it's just one tile there is no need of ReqAck
				if (f.TileCount == 1 || f.Options == (byte)FrameOptions.ReqAckBlockTransfer)
				{
					if (!blockMap.ContainsKey(f.BlockID))
						blockMap.TryAdd(f.BlockID, new Block(f.BlockID, f.TileCount, f.Length, inHighway));

					signal(f.FrameID, (int)SignalKind.ACK);
				}
				else
				{
					signal(f.FrameID, (int)ErrorCode.NoTranferAck, true);
					return;
				}
			}

			var b = blockMap[f.BlockID];

			b.Receive(f);

			if (b.IsComplete && onReceived != null)
				Task.Run(() =>
				{
					try
					{
						onReceived(b.Fragment);
					}
					catch { }
				});
		}

		void procError(MemoryFragment frag)
		{
			var sg = new SIGNAL(frag.Span());

			if (!procFrames.TryAdd(sg.FrameID, DateTime.Now)) return;

		}

		void procStatus(MemoryFragment frag)
		{
			var st = new STATUS(frag.Span());

			if (!procFrames.TryAdd(st.FrameID, DateTime.Now)) return;

			if (blockMap.TryGetValue(st.BlockID, out Block b))
			{
				if (b.IsIncoming)
				{
					Span<byte> map = stackalloc byte[b.tileMap.Bytes];

					b.tileMap.WriteTo(map);
					status(st.BlockID, b.MarkedTiles, map);
				}
				else
				{
					b.Mark(st.TileMap);

					if (st.TileCount == b.TileCount || b.IsComplete)
					{
						blockMap.TryRemove(st.BlockID, out Block rem);
						b.Dispose();
					}
					else beam(b);
				}
			}
			else signal(st.FrameID, (int)SignalKind.UNK);
		}

		void procSignal(MemoryFragment frag)
		{
			var sg = new SIGNAL(frag.Span());

			if (!procFrames.TryAdd(sg.FrameID, DateTime.Now)) return;

			if (signalAwaits.TryGetValue(sg.RefID, out SignalAwait sa) && sa.OnSignal != null)
				try { sa.OnSignal(sg.Mark); }
				catch { }
		}

		bool reqack(Block b)
		{
			var rst = new ManualResetEventSlim(false);
			var fid = Interlocked.Increment(ref frameCounter);
			var opt = (byte)FrameOptions.ReqAckBlockTransfer;
			var frm = new FRAME(fid, b.ID, b.TileCount, 0, b.TileSize, opt, null);
			var rsp = false;
			Span<byte> s = stackalloc byte[frm.LENGTH];

			var ackq = signalAwaits.TryAdd(fid, new SignalAwait((mark) =>
			{
				if ((SignalKind)mark == SignalKind.ACK)
				{
					Volatile.Write(ref b.reqAckDgram, null);
					rsp = true;
				}

				signalAwaits.TryRemove(fid, out SignalAwait x);
				rst.Set();
			}));

			if (ackq)
			{
				frm.Write(s);
				Volatile.Write(ref b.reqAckDgram, s.ToArray());

				for (int i = 0; i < cfg.SendRetries; i++)
				{
					var dgram = Volatile.Read(ref b.reqAckDgram);

					if (dgram != null)
					{
						socket.Send(dgram, SocketFlags.None);
						if (rst.Wait(cfg.RetryDelayMS)) return rsp;
					}
					else return rsp;
				}

				Volatile.Write(ref b.reqAckDgram, null);
			}

			return false;
		}

		void suggestTileSize()
		{

		}

		async Task probe()
		{
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
					var f = inHighway.AllocFragment(UDP_MAX);
					var read = socket.Receive(f, SocketFlags.None);
					if (read > 0)
					{
						if (read == 1) procFrame(f);
						else new Task(() => procFrame(f)).Start();
						if (Volatile.Read(ref retries) > 0) Volatile.Write(ref retries, 0);
					}
					else
					{
						f.Dispose();

						if (Interlocked.Increment(ref retries) > cfg.MaxReceiveRetries) break;
						else await Task.Delay(cfg.ErrorAwaitMS);
					}
				}
				catch (Exception ex)
				{
					Interlocked.Exchange(ref exception, ex);
					Interlocked.Exchange(ref stop, 1);
				}
		}

		async Task cleanup()
		{
			while (true)
			{
				try
				{
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

		Action<MemoryFragment> onReceived;

		HeapHighway outHighway;
		IMemoryHighway inHighway;
		EmitterCfg cfg;
		IPEndPoint target;
		IPEndPoint source;
		Socket socket;

		int frameCounter;
		int blockCounter;
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
