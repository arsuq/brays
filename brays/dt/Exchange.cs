using System;
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
				pos = f.Read(ref refID, pos);
				pos = f.Read(ref type, pos);
				pos = f.Read(ref srlzType, pos);
				pos = f.Read(ref created, pos);
				pos = f.Read(ref isError, pos);
				pos = f.Read(ref state, pos);
				pos = f.Read(ref resIDLen, pos);
				resID = new string(f.ToSpan<char>().Slice(pos, resIDLen));
				dataOffset = pos + resIDLen;
			}
		}

		public Exchange(
			int ID,
			int refID,
			byte type,
			SerializationType st,
			bool isError,
			int state,
			string resID,
			Span<byte> data,
			IMemoryHighway hw)
		{
			var resBytes = Encoding.UTF8.GetBytes(resID);
			resIDLen = (ushort)resBytes.Length;
			var fl = data.Length + resIDLen + HEADER_LEN;
			Fragment = hw.AllocFragment(fl);

			if (Fragment == null) throw new ArgumentNullException("Fragment");

			var pos = 0;

			pos = Fragment.Write(ID, pos);
			pos = Fragment.Write(refID, pos);
			pos = Fragment.Write(type, pos);
			pos = Fragment.Write((byte)st, pos);
			pos = Fragment.Write(DateTime.Now.Ticks, pos);
			pos = Fragment.Write(isError, pos);
			pos = Fragment.Write(state, pos);
			pos = Fragment.Write(resIDLen, pos);
			pos = Fragment.Write(resBytes, pos);
			pos = Fragment.Write(data, pos);
		}

		public void Dispose() => Fragment?.Dispose();

		public T Make<T>() => Serializer.Deserialize<T>(Fragment, (SerializationType)srlzType, dataOffset);

		public readonly bool IsValid;
		public Span<byte> Data => Fragment.Span().Slice(dataOffset);
		public readonly MemoryFragment Fragment;

		internal int ID;
		internal int refID;
		internal byte type;
		internal byte srlzType;
		internal long created;
		internal bool isError;
		internal int state;
		internal ushort resIDLen;
		internal string resID;
		internal int dataOffset;

		// The fragment must begin with this value if it's an exchange type
		const int EXCHANGE_TYPE_ID = 7777777;
		const int HEADER_LEN = 31;
	}
}
