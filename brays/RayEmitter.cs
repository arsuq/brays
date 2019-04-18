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

			highway = new HeapHighway(new HighwaySettings(UDP_MAX), UDP_MAX, UDP_MAX, UDP_MAX);
			this.cfg = cfg;
		}

		public void Dispose()
		{
			if (highway != null)
				try
				{
					Interlocked.Exchange(ref stop, 1);
					highway.Dispose();
					socket.Dispose();
					highway = null;
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

		public async Task<bool> Beam(MemoryFragment f, int reqAckRetries = 4, int reqAckDelayMS = 1200)
		{
			var bid = Interlocked.Increment(ref blockCounter);
			var b = new Block(bid, cfg.TileSize, f);

			if (blockMap.TryAdd(bid, b))
			{
				if (b.TileCount > 1 && !await reqack(b, reqAckRetries, reqAckDelayMS))
					return false;

				await Task.Run(() => beam(b));
				return true;
			}

			return false;
		}

		public bool IsLocked => isLocked;
		public int FrameCounter => frameCounter;
		public IPEndPoint Target => target;
		public DateTime LastProbe => new DateTime(Volatile.Read(ref lastReceivedProbeTick));
		public Exception Exception => exception;

		void beam(Block b)
		{
			if (b == null) throw new ArgumentNullException();
			if (cfg.TileSize != b.TileSize) throw new ArgumentException("TileSize mismatch");

			int sent = 0;

			for (int i = 0; i < b.tileMap.Length; i++)
				if (!b.tileMap[i])
					using (var mf = highway.Alloc(b.TileSize + 50))
					{
						var s = b.Fragment.Span().Slice(i * b.TileSize);
						var fid = Interlocked.Increment(ref frameCounter);
						var f = new FRAME(fid, b.ID, b.TileCount, i, (ushort)s.Length, 0, s);

						f.Write(mf.Span());
						socket.Send(mf);
						sent++;
					}

			b.sentTime = DateTime.Now;
			status(b.ID, sent, null);
		}

		void status(int blockID, int tilesCount, Span<byte> tileMap)
		{
			var fid = Interlocked.Increment(ref frameCounter);
			var len = tileMap.Length + 13;
			Span<byte> s = stackalloc byte[len];

			STATUS.Make(fid, blockID, tilesCount, tileMap, s);
			socket.Send(s);
		}

		void signal(int refID, int mark, bool isError = false)
		{
			var fid = Interlocked.Increment(ref frameCounter);
			var signal = new SIGNAL(fid, refID, mark, isError);
			Span<byte> s = stackalloc byte[signal.LENGTH];

			signal.Write(s);
			socket.Send(s);
		}

		void procFrame(Span<byte> s)
		{
			try
			{
				var lead = (Lead)s[0];

				switch (lead)
				{
					case Lead.Probe: Volatile.Write(ref lastReceivedProbeTick, DateTime.Now.Ticks); break;
					case Lead.Signal: procSignal(s); break;
					case Lead.Error: procError(s); break;
					case Lead.Block: procBlock(s); break;
					case Lead.Status: procStatus(s); break;
					default:
					break;
				}
			}
			catch (Exception ex)
			{
				// log
			}
		}

		void procBlock(Span<byte> s)
		{
			var f = new FRAME(s);

			if (!blockMap.ContainsKey(f.BlockID))
			{
				// If it's just one tile there is no need of ReqAck
				if (f.TileCount == 1)
				{
					if (!blockMap.ContainsKey(f.BlockID))
						blockMap.TryAdd(f.BlockID, new Block(f.BlockID, f.TileCount, f.Length, highway));

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

			if (b.IsComplete)
				Task.Run(() =>
				{
					try
					{
						onReceived(b.Fragment);
					}
					catch { }
				});
		}

		void procError(Span<byte> s)
		{
			var sg = new SIGNAL(s);
		}

		void procStatus(Span<byte> s)
		{
			var st = new STATUS(s);

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

		void procSignal(Span<byte> s)
		{
			var sg = new SIGNAL(s);

			if (signalAwaits.TryGetValue(sg.RefID, out SignalAwait sa) && sa.OnSignal != null)
				try { sa.OnSignal(sg.Mark); }
				catch { }
		}

		async Task<bool> reqack(Block b, int n, int delayMS)
		{
			await Task.Run(() =>
			{
				var fid = Interlocked.Increment(ref frameCounter);
				var opt = (byte)FrameOptions.ReqAckBlockTransfer;
				var frame = new FRAME(fid, b.ID, b.TileCount, 0, b.TileSize, opt, null);
				Span<byte> s = stackalloc byte[frame.LENGTH];

				frame.Write(s);
				Interlocked.Exchange(ref b.reqAckDgram, s.ToArray());
			});

			for (int i = 0; i < n; i++)
			{
				var dgram = Volatile.Read(ref b.reqAckDgram);

				if (dgram != null)
				{
					await socket.SendAsync(dgram, SocketFlags.None);
					await Task.Delay(delayMS);
				}
				else return true;
			}

			Volatile.Write(ref b.reqAckDgram, null);

			return false;
		}

		async Task probe()
		{
			while (Volatile.Read(ref stop) < 1)
				try
				{
					socket.Send(probeLead);
					await Task.Delay(cfg.ProbeFreqMS);
				}
				catch (Exception ex)
				{
					Interlocked.Exchange(ref exception, ex);
					Interlocked.Exchange(ref stop, 1);
				}
		}

		async Task receive()
		{
			while (Volatile.Read(ref stop) < 1)
				try
				{
					using (var f = highway.Alloc(UDP_MAX))
					{
						var read = await socket.ReceiveAsync(f, SocketFlags.None);
						if (read > 0) new Task(() => procFrame(f.Span())).Start();
						else if (Interlocked.Increment(ref retries) > cfg.MaxRetries) break;
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
				foreach (var b in blockMap.Values)
					if (DateTime.Now.Subtract(b.sentTime) > cfg.SentBlockRetention)
						blockMap.TryRemove(b.ID, out Block x);

				foreach (var sa in signalAwaits)
					if (sa.Value.OnSignal != null && DateTime.Now.Subtract(sa.Value.Created) > cfg.SignalAwait)
						signalAwaits.TryRemove(sa.Key, out SignalAwait x);

				await Task.Delay(cfg.CleanupFreqMS);
			}
		}

		Action<MemoryFragment> onReceived;

		HeapHighway highway;
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

		public const ushort UDP_MAX = ushort.MaxValue;
	}
}
