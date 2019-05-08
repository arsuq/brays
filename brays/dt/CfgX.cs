using System;

namespace brays
{
	class CfgX
	{
		public CfgX(EmitterCfg cfg)
		{
			MaxBeamedTilesAtOnce = cfg.MaxBeamedTilesAtOnce;
			MaxConcurrentReceives = cfg.MaxConcurrentReceives;
		}

		public CfgX(Span<byte> s)
		{
			MaxBeamedTilesAtOnce = BitConverter.ToInt32(s);
			MaxConcurrentReceives = BitConverter.ToInt32(s.Slice(4));
		}

		public void Write(Span<byte> s)
		{
			BitConverter.TryWriteBytes(s, MaxBeamedTilesAtOnce);
			BitConverter.TryWriteBytes(s.Slice(4), MaxConcurrentReceives);
		}

		public const int LENGTH = 8;
		public int MaxBeamedTilesAtOnce;
		public int MaxConcurrentReceives;

	}
}
