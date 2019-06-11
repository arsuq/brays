﻿using System;
using System.IO;
using System.Text;

namespace brays
{
	public class Exchange : IDisposable
	{
		public Exchange(MemoryFragment f)
		{
			int tid = 0;
			int pos = 0;
			pos = f.Read(ref tid, pos);
			IsValid = tid == EXCHANGE_TYPE_ID;

			if (IsValid)
			{
				Fragment = f;

				pos = f.Read(ref ID, pos);
				pos = f.Read(ref RefID, pos);
				pos = f.Read(ref XType, pos);
				pos = f.Read(ref SrlType, pos);
				pos = f.Read(ref Created, pos);
				pos = f.Read(ref ErrorCode, pos);
				pos = f.Read(ref XState, pos);
				pos = f.Read(ref ResIDLen, pos);
				ResID = new string(f.ToSpan<char>().Slice(pos, ResIDLen));
				DataOffset = pos + ResIDLen;
			}
		}

		public Exchange(
			int ID,
			int refID,
			byte type,
			SerializationType st,
			int errorCode,
			int state,
			string resID,
			Span<byte> data,
			IMemoryHighway hw)
		{
			var resBytes = Encoding.UTF8.GetBytes(resID);

			ResIDLen = (ushort)resBytes.Length;
			var fl = data.Length + ResIDLen + HEADER_LEN;

			Fragment = hw.AllocFragment(fl);

			if (Fragment == null) throw new ArgumentNullException("Fragment");

			var pos = 0;

			this.ID = ID;
			this.RefID = refID;
			this.XType = type;
			this.SrlType = (byte)st;
			this.Created = DateTime.Now.Ticks;
			this.ErrorCode = errorCode;
			this.XState = state;
			this.ResID = resID;

			pos = Fragment.Write(EXCHANGE_TYPE_ID, pos);
			pos = Fragment.Write(ID, pos);
			pos = Fragment.Write(refID, pos);
			pos = Fragment.Write(type, pos);
			pos = Fragment.Write((byte)st, pos);
			pos = Fragment.Write(Created, pos);
			pos = Fragment.Write(errorCode, pos);
			pos = Fragment.Write(state, pos);
			pos = Fragment.Write(ResIDLen, pos);
			pos = Fragment.Write(resBytes, pos);

			DataOffset = pos;

			Fragment.Write(data, pos);
		}

		public void Dispose() => Fragment?.Dispose();

		public T Make<T>() => Serializer.Deserialize<T>(this);

		public bool TryMake<T>(out T o)
		{
			o = default;

			try
			{
				o = Serializer.Deserialize<T>(this);

				return true;
			}
			catch
			{
				return false;
			}
		}

		public readonly bool IsValid;
		public Span<byte> Data => Fragment.Span().Slice(DataOffset);
		public readonly MemoryFragment Fragment;
		public SerializationType SerializationType => (SerializationType)SrlType;

		// [i] These fields could be props reading from the Fragment at offset...

		public readonly int ID;
		public readonly int RefID;
		public readonly byte XType;
		public readonly byte SrlType;
		public readonly long Created;
		public readonly int ErrorCode;
		public readonly int XState;
		public readonly ushort ResIDLen;
		public readonly string ResID;
		public readonly int DataOffset;

		// [i] The fragment must begin with a special value to indicate that it is an exchange type.
		// This is technically not mandatory since the Beamer is not shared and all received frags
		// can only be exchanges. 
		public const int EXCHANGE_TYPE_ID = 7777777;
		public const int HEADER_LEN = 28;
	}

	public class Exchange<T>
	{
		public Exchange(MemoryFragment f)
		{
			Instance = new Exchange(f);
			Arg = Serializer.Deserialize<T>(Instance);
		}

		public Exchange(
			int ID,
			int refID,
			byte type,
			SerializationType st,
			int errorCode,
			int state,
			string resID,
			T arg,
			IMemoryHighway hw)
		{
			var f = Serializer.Serialize(arg, st, hw, (ms) =>
			{
				var resBytes = Encoding.UTF8.GetBytes(resID);
				var resLen = (ushort)resBytes.Length;
				Span<byte> header = stackalloc byte[Exchange.HEADER_LEN + resLen];

				BitConverter.TryWriteBytes(header, Exchange.EXCHANGE_TYPE_ID);
				BitConverter.TryWriteBytes(header.Slice(4), ID);
				BitConverter.TryWriteBytes(header.Slice(8), refID);
				BitConverter.TryWriteBytes(header.Slice(12), type);
				BitConverter.TryWriteBytes(header.Slice(13), (byte)st);
				BitConverter.TryWriteBytes(header.Slice(14), errorCode);
				BitConverter.TryWriteBytes(header.Slice(18), state);

				resBytes.CopyTo(header.Slice(22));
				ms.Write(header);
			});

			Instance = new Exchange(f);
			Arg = arg;
		}

		public readonly Exchange Instance;
		public readonly T Arg;
	}
}
