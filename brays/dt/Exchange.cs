using System;
using System.IO;
using System.Text;

namespace brays
{
	public class Exchange : IDisposable
	{
		internal Exchange(XPU xpu, MemoryFragment f)
		{
			XPU = xpu;

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
				ResID = Encoding.UTF8.GetString(f.Span().Slice(pos, ResIDLen));
				DataOffset = pos + ResIDLen;
			}
		}

		internal Exchange(
			XPU xpu,
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
			XPU = xpu;

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

		internal Exchange(int errorCode) { ErrorCode = errorCode; }
		internal Exchange(XPUErrorCode errorCode) { ErrorCode = (int)errorCode; }

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

		public readonly XPU XPU;
		public readonly bool IsValid;
		public Span<byte> Data => Fragment.Span().Slice(DataOffset);
		public readonly MemoryFragment Fragment;
		public SerializationType SerializationType => (SerializationType)SrlType;
		public XPUErrorCode KnownError => (XPUErrorCode)ErrorCode;

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
		public const int HEADER_LEN = 32;
	}

	public class Exchange<T>
	{
		internal Exchange(XPU xpu, MemoryFragment f)
		{
			var x = new Exchange(xpu, f);

			if (!x.TryDeserialize(out Arg)) Instance = new Exchange(XPUErrorCode.Deserialization);
			else Instance = x;
		}

		internal Exchange(
			XPU xpu,
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
				BitConverter.TryWriteBytes(header.Slice(14), DateTime.Now.Ticks);
				BitConverter.TryWriteBytes(header.Slice(22), errorCode);
				BitConverter.TryWriteBytes(header.Slice(26), state);
				BitConverter.TryWriteBytes(header.Slice(30), resLen);

				resBytes.CopyTo(header.Slice(Exchange.HEADER_LEN));
				ms.Write(header);
			});

			Instance = new Exchange(xpu, f);
			Arg = arg;
		}

		internal Exchange(int errorCode) => Instance = new Exchange(errorCode);
		internal Exchange(XPUErrorCode errorCode) => Instance = new Exchange(errorCode);

		public readonly Exchange Instance;
		public readonly T Arg;
	}
}
