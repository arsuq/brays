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
		public RayEmitter(IPEndPoint listen, Action<Block> onReceived, EmitterCfg cfg)
		{
			highway = new HeapHighway(
				new HighwaySettings(UDP_MAX),
				UDP_MAX, UDP_MAX, UDP_MAX, UDP_MAX);

			this.onReceived = onReceived;
			this.cfg = cfg;
			this.source = listen;
			socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		}

		public void Dispose()
		{
			if (highway != null)
			{
				highway.Dispose();
				socket.Dispose();
				highway = null;
			}
		}

		public async Task LockOn(IPEndPoint ep)
		{
			socket.Bind(source);
			await socket.ConnectAsync(target);
			receiver = receive();
			Volatile.Write(ref isLocked, true);
		}

		public void Probe()
		{
			var probe = Interlocked.Increment(ref frameCounter);
			Span<byte> lead = stackalloc byte[1];
			socket.Send(lead);
		}

		public bool IsLocked => isLocked;
		public int FrameCounter => frameCounter;
		public IPEndPoint Target => target;
		public DateTime LastProbe => new DateTime(Volatile.Read(ref lastProbeTick));


		void beam(Block b)
		{
			if (b == null) throw new ArgumentNullException();
			var lead = (byte)Lead.Block;
			var size = cfg.TileSize;
			var count = b.Memory.Length / size;
			if (b.Memory.Length % size != 0) count++;

			for (int i = 0; i < count; i++)
				using (var mf = highway.Alloc(size))
				{
					var s = b.Memory.Span().Slice(i * size);
					var fid = Interlocked.Increment(ref frameCounter);
					var f = new FRAME(fid, b.ID, count, i, (ushort)s.Length, 0, s);
					mf.Write(lead, 0);
					f.Write(mf.Span().Slice(1));
					socket.Send(mf);
				}

			status(true, b.ID, count, count);
		}

		void beam(Block b, int tileIdx)
		{
			if (b == null) throw new ArgumentNullException();
			var lead = (byte)Lead.Block;

			using (var mf = highway.Alloc(b.TileSize + 50))
			{
				var s = b.Memory.Span().Slice(tileIdx * b.TileSize);
				var fid = Interlocked.Increment(ref frameCounter);
				var f = new FRAME(fid, b.ID, b.TileCount, tileIdx, (ushort)s.Length, 0, s);
				mf.Write(lead, 0);
				f.Write(mf.Span().Slice(1));
				socket.Send(mf);
				b.Mark(tileIdx, false);
			}
		}

		void status(bool ask, int blockID, int tilesCount, int tileIdx)
		{
			var fid = Interlocked.Increment(ref frameCounter);

			Span<byte> s = stackalloc byte[17];
			s[0] = ask ? (byte)Lead.AskStatus : (byte)Lead.Status;
			var status = new STATUS(fid, blockID, tilesCount, tileIdx);

			status.Write(s.Slice(1));
			socket.Send(s);
		}

		void signal(byte lead, int refID, int mark)
		{
			var fid = Interlocked.Increment(ref frameCounter);

			Span<byte> s = stackalloc byte[13];
			var signal = new SIGNAL(lead, fid, refID, mark);

			signal.Write(s);
			socket.Send(s);
		}

		async Task receive()
		{
			while (Volatile.Read(ref stop) < 1)
				try
				{
					using (var f = highway.Alloc(UDP_MAX))
					{
						var read = await socket.ReceiveAsync(f, SocketFlags.None);
						if (read > 0) processFrame(f.Span());
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.Message);
				}
		}

		void processFrame(Span<byte> s)
		{
			var lead = (Lead)s[0];

			switch (lead)
			{
				case Lead.Probe:
				{
					Volatile.Write(ref lastProbeTick, DateTime.Now.Ticks);
					Probe();
					break;
				}
				case Lead.Signal:
				{
					var sg = new SIGNAL(s.Slice(1));

					if (refAwaits.TryGetValue(sg.RefID, out Action<int> a))
						try { a(sg.Mark); }
						catch { }

					break;
				}
				case Lead.Block:
				{
					var f = new FRAME(s.Slice(1));

					// [!] Handle the case when the last tile is received first
					// and the Length would be wrong.
					if (!blockMap.ContainsKey(f.BlockID))
						blockMap.TryAdd(f.BlockID, new Block(f.BlockID, f.TileCount, f.Length, highway));

					var b = blockMap[f.BlockID];

					b.Receive(f);

					if (b.IsComplete)
						Task.Run(() =>
						{
							try
							{
								onReceived(b);
							}
							catch { }
						});

					break;
				}
				case Lead.AskTile:
				{
					var f = new FRAME(s.Slice(1));

					if (blockMap.ContainsKey(f.BlockID) &&
						blockMap.TryGetValue(f.BlockID, out Block b)) beam(b, f.TileIndex);
					else signal((byte)Lead.Signal, f.BlockID, (int)SignalKind.NACK);

					break;
				}
				case Lead.AskBlock:
				{
					var f = new FRAME(s.Slice(1));

					if (blockMap.ContainsKey(f.BlockID) &&
						blockMap.TryGetValue(f.BlockID, out Block b)) beam(b);
					else signal((byte)Lead.Signal, f.BlockID, (int)SignalKind.NACK);

					break;
				}
				case Lead.AskStatus:
				{
					var st = new STATUS(s.Slice(1));

					if (blockMap.TryGetValue(st.BlockID, out Block b))
						status(false, st.BlockID, b.MarkedTiles, b.MarkedLine());
					else status(false, st.BlockID, 0, 0);

					break;
				}
				case Lead.Status:
				{
					var st = new STATUS(s.Slice(1));

					if (blockMap.TryGetValue(st.BlockID, out Block b))
					{
						b.Mark(st.TileIndex, true);

						if (st.TileCount == b.TileCount || b.IsComplete)
						{
							blockMap.TryRemove(st.BlockID, out Block rem);
							b.Dispose();
						}
					}

					break;
				}
				default:
				break;
			}
		}


		Action<Block> onReceived;
		HeapHighway highway;
		EmitterCfg cfg;
		IPEndPoint target;
		IPEndPoint source;
		Socket socket;
		int frameCounter;
		int stop;
		bool isLocked;
		long lastProbeTick;
		Task receiver;

		ConcurrentDictionary<int, Action<int>> refAwaits = new ConcurrentDictionary<int, Action<int>>();
		ConcurrentDictionary<int, Block> blockMap = new ConcurrentDictionary<int, Block>();

		ushort UDP_MAX = ushort.MaxValue;
	}
}
