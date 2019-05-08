using System;

namespace brays
{
	class CfgX
	{
		public CfgX(EmitterCfg cfg)
		{
			MaxBeamedTilesAtOnce = cfg.MaxBeamedTilesAtOnce;
			MaxConcurrentReceives = cfg.MaxConcurrentReceives;
			SendBufferSize = cfg.SendBufferSize;
			ReceiveBufferSize = cfg.ReceiveBufferSize;
		}

		public CfgX(Span<byte> s)
		{
			MaxBeamedTilesAtOnce = BitConverter.ToInt32(s);
			MaxConcurrentReceives = BitConverter.ToInt32(s.Slice(4));
			SendBufferSize = BitConverter.ToInt32(s.Slice(8));
			ReceiveBufferSize = BitConverter.ToInt32(s.Slice(12));
		}

		public void Write(Span<byte> s)
		{
			BitConverter.TryWriteBytes(s, MaxBeamedTilesAtOnce);
			BitConverter.TryWriteBytes(s.Slice(4), MaxConcurrentReceives);
			BitConverter.TryWriteBytes(s.Slice(8), SendBufferSize);
			BitConverter.TryWriteBytes(s.Slice(12), ReceiveBufferSize);
		}

		public const int LENGTH = 16;
		public int MaxBeamedTilesAtOnce;
		public int MaxConcurrentReceives;
		public int SendBufferSize;
		public int ReceiveBufferSize;
	}
}
