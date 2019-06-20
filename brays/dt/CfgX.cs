/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Net;

namespace brays
{
	public class CfgX
	{
		public CfgX(BeamerCfg cfg, IPEndPoint ep)
		{
			TileSizeBytes = cfg.TileSizeBytes;
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
			TileSizeBytes = BitConverter.ToUInt16(s);
			MaxBeamedTilesAtOnce = BitConverter.ToInt32(s.Slice(2));
			MaxConcurrentReceives = BitConverter.ToInt32(s.Slice(6));
			SendBufferSize = BitConverter.ToInt32(s.Slice(10));
			ReceiveBufferSize = BitConverter.ToInt32(s.Slice(14));
			s.Slice(18, 16).CopyTo(IP);
			Port = BitConverter.ToUInt16(s.Slice(34));
			IsIPv4 = BitConverter.ToBoolean(s.Slice(36));
		}

		public void Write(Span<byte> s)
		{
			BitConverter.TryWriteBytes(s, TileSizeBytes);
			BitConverter.TryWriteBytes(s.Slice(2), MaxBeamedTilesAtOnce);
			BitConverter.TryWriteBytes(s.Slice(6), MaxConcurrentReceives);
			BitConverter.TryWriteBytes(s.Slice(10), SendBufferSize);
			BitConverter.TryWriteBytes(s.Slice(14), ReceiveBufferSize);
			IP.AsSpan().CopyTo(s.Slice(18));
			BitConverter.TryWriteBytes(s.Slice(34), Port);
			BitConverter.TryWriteBytes(s.Slice(36), IsIPv4);
		}

		public CfgX Clone()
		{
			Span<byte> buff = stackalloc byte[LENGTH];
			Write(buff);
			return new CfgX(buff);
		}

		public const int LENGTH = 37;

		public ushort TileSizeBytes;
		public int MaxBeamedTilesAtOnce;
		public int MaxConcurrentReceives;
		public int SendBufferSize;
		public int ReceiveBufferSize;
		public byte[] IP = new byte[16];
		public ushort Port;
		public bool IsIPv4;
	}
}
