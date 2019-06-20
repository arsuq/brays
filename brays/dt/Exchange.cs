/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Text;
using System.Threading;

namespace brays
{
	public class Exchange : IDisposable
	{
		internal Exchange(XPU xpu, MemoryFragment f, bool isCopy = false)
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
				pos = f.Read(ref Flags, pos);
				pos = f.Read(ref Created, pos);
				pos = f.Read(ref ErrorCode, pos);
				pos = f.Read(ref ResIDLen, pos);
				ResID = Encoding.UTF8.GetString(f.Span().Slice(pos, ResIDLen));
				DataOffset = pos + ResIDLen;
				state = isCopy ? (int)XState.Created : (int)XState.Received;
			}
		}

		internal Exchange(
			XPU xpu,
			int refID,
			int xflags,
			int errorCode,
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

			this.ID = xpu.nextExchangeID();
			this.RefID = refID;
			this.Flags = xflags;
			this.Created = DateTime.Now.Ticks;
			this.ErrorCode = errorCode;
			this.state = (int)XState.Created;
			this.ResID = resID;

			pos = Fragment.Write(EXCHANGE_TYPE_ID, pos);
			pos = Fragment.Write(ID, pos);
			pos = Fragment.Write(refID, pos);
			pos = Fragment.Write((int)xflags, pos);
			pos = Fragment.Write(Created, pos);
			pos = Fragment.Write(errorCode, pos);
			pos = Fragment.Write(ResIDLen, pos);
			pos = Fragment.Write(resBytes, pos);

			DataOffset = pos;

			Fragment.Write(data, pos);
		}

		internal Exchange(int errorCode)
		{
			ErrorCode = errorCode;
			state = (int)XState.Faulted;
		}

		public void Dispose()
		{
			Fragment?.Dispose();
			Interlocked.Exchange(ref state, (int)XState.Disposed);
		}

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

		public bool Mark(XState s)
		{
			switch (s)
			{
				case XState.Beamed:
				return Interlocked.CompareExchange(ref state, (int)XState.Beamed, (int)XState.Created) == (int)XState.Created;
				case XState.Processing:
				return Interlocked.CompareExchange(ref state, (int)XState.Processing, (int)XState.Received) == (int)XState.Received;
				case XState.Faulted:
				{ Interlocked.Exchange(ref state, (int)XState.Faulted); return true; }
				default: return false;
			}
		}

		public readonly XPU XPU;
		public readonly bool IsValid;
		public Span<byte> Data => Fragment.Span().Slice(DataOffset);
		public readonly MemoryFragment Fragment;
		public XPUErrorCode KnownError => (XPUErrorCode)ErrorCode;
		public XFlags ExchangeFlags => (XFlags)Flags;
		public XState State => (XState)state;

		public bool IsOK => ErrorCode == 0;

		public readonly int ID;
		public readonly int RefID;
		public readonly int Flags;
		public readonly long Created;
		public readonly int ErrorCode;
		public readonly ushort ResIDLen;
		public readonly string ResID;
		public readonly int DataOffset;

		int state;

		// [i] The fragment begins with a special value indicating that it is an exchange type.
		// Technically this is not mandatory since the Beamer is not shared and all received frags
		// can only be exchanges. 
		public const int EXCHANGE_TYPE_ID = 7777777;
		public const int HEADER_LEN = 30;
	}

	public class Exchange<T> : IDisposable
	{
		internal Exchange(XPU xpu, MemoryFragment f, bool isCopy = false)
		{
			var x = new Exchange(xpu, f, isCopy);

			if (!x.TryDeserialize(out Arg)) Instance = new Exchange((int)XPUErrorCode.Deserialization);
			else Instance = x;
		}

		internal Exchange(
			XPU xpu,
			int refID,
			int xflags,
			int errorCode,
			string resID,
			T arg,
			IMemoryHighway hw)
		{
			var f = Serializer.Serialize(arg, hw, (ms) =>
			{
				var resBytes = Encoding.UTF8.GetBytes(resID);
				var resLen = (ushort)resBytes.Length;
				Span<byte> header = stackalloc byte[Exchange.HEADER_LEN + resLen];

				BitConverter.TryWriteBytes(header, Exchange.EXCHANGE_TYPE_ID);
				BitConverter.TryWriteBytes(header.Slice(4), xpu.nextExchangeID());
				BitConverter.TryWriteBytes(header.Slice(8), refID);
				BitConverter.TryWriteBytes(header.Slice(12), xflags);
				BitConverter.TryWriteBytes(header.Slice(16), DateTime.Now.Ticks);
				BitConverter.TryWriteBytes(header.Slice(24), errorCode);
				BitConverter.TryWriteBytes(header.Slice(28), resLen);

				resBytes.CopyTo(header.Slice(Exchange.HEADER_LEN));
				ms.Write(header);
			});

			Instance = new Exchange(xpu, f, true);
			Arg = arg;
		}

		internal Exchange(int errorCode) => Instance = new Exchange(errorCode);

		public void Dispose() => Instance?.Dispose();

		public static implicit operator Exchange(Exchange<T> gx) => gx != null ? gx.Instance : null;
		public static implicit operator T(Exchange<T> gx) => gx != null ? gx.Arg : default;

		public bool IsOK => Instance != null && Instance.IsOK;
		public readonly Exchange Instance;
		public readonly T Arg;
	}
}
