using System;

namespace brays
{
	public class Exchange
	{
		public Exchange(MemoryFragment f)
		{
			int tid = 0;
			int pos = 0;
			pos = f.Read(ref tid, pos);
			IsValid = tid == EXCHANGE_TYPE_ID;

			if (IsValid)
			{
				frag = f;

				pos = f.Read(ref ID, pos);
				pos = f.Read(ref refID, pos);
				pos = f.Read(ref type, pos);
				pos = f.Read(ref srlzType, pos);
				pos = f.Read(ref created, pos);
				pos = f.Read(ref TTL, pos);
				pos = f.Read(ref procDur, pos);
				pos = f.Read(ref isError, pos);
				pos = f.Read(ref state, pos);
				pos = f.Read(ref resIDLen, pos);
				resID = new string(f.ToSpan<char>().Slice(pos, resIDLen));
				dataOffset = pos += resIDLen;
			}
		}

		public MemoryFragment ToMemoryFragment(IMemoryHighway hw)
		{
			return null;
		}

		public readonly bool IsValid;

		internal int ID;
		internal int refID;
		internal byte type;
		internal byte srlzType;
		internal DateTime created;
		internal long TTL;
		internal long procDur;
		internal bool isError;
		internal int state;
		internal short resIDLen;
		internal string resID;
		internal short netTypeIDLen;
		internal string netTypeID;
		internal int dataOffset;
		internal MemoryFragment frag;

		// The fragment must begin with this value if it's an exchange type
		const int EXCHANGE_TYPE_ID = 7777777;
	}
}
