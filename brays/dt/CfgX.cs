using System;
using System.Net;

namespace brays
{
	public class CfgX
	{
		public CfgX(BeamerCfg cfg, IPEndPoint ep)
		{
			MaxBeamedTilesAtOnce = cfg.MaxBeamedTilesAtOnce;
			MaxConcurrentReceives = cfg.MaxConcurrentReceives;
			SendBufferSize = cfg.SendBufferSize;
			ReceiveBufferSize = cfg.ReceiveBufferSize;
			ep.Address.TryWriteBytes(IP, out int bw);
			Port = (ushort)ep.Port;
			IsIPv4 = ep.Address.GetAddressBytes().Length < 16;
		}

		public CfgX(Span<byte> s)
		{
			MaxBeamedTilesAtOnce = BitConverter.ToInt32(s);
			MaxConcurrentReceives = BitConverter.ToInt32(s.Slice(4));
			SendBufferSize = BitConverter.ToInt32(s.Slice(8));
			ReceiveBufferSize = BitConverter.ToInt32(s.Slice(12));
			s.Slice(16, 16).CopyTo(IP);
			Port = BitConverter.ToUInt16(s.Slice(32));
			IsIPv4 = BitConverter.ToBoolean(s.Slice(34));
		}

		public void Write(Span<byte> s)
		{
			BitConverter.TryWriteBytes(s, MaxBeamedTilesAtOnce);
			BitConverter.TryWriteBytes(s.Slice(4), MaxConcurrentReceives);
			BitConverter.TryWriteBytes(s.Slice(8), SendBufferSize);
			BitConverter.TryWriteBytes(s.Slice(12), ReceiveBufferSize);
			IP.AsSpan().CopyTo(s.Slice(16));
			BitConverter.TryWriteBytes(s.Slice(32), Port);
			BitConverter.TryWriteBytes(s.Slice(34), IsIPv4);
		}

		public CfgX Clone()
		{
			Span<byte> buff = stackalloc byte[LENGTH];
			Write(buff);
			return new CfgX(buff);
		}

		public const int LENGTH = 35;

		public int MaxBeamedTilesAtOnce;
		public int MaxConcurrentReceives;
		public int SendBufferSize;
		public int ReceiveBufferSize;
		public byte[] IP = new byte[16];
		public ushort Port;
		public bool IsIPv4;
	}
}
