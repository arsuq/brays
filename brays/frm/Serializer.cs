using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace brays
{
	public static class Serializer
	{
		public static T Deserialize<T>(this MemoryFragment f, int from = 0)
		{
			if (f == null || f.IsDisposed) throw new ArgumentNullException("f");

			object o = null;

			using (var fs = f.CreateStream())
			{
				if (from > 0) fs.Seek(from, SeekOrigin.Begin);

				var bf = new BinaryFormatter();
				o = bf.Deserialize(fs);

				return (T)o;
			}
		}

		public static T Deserialize<T>(this Exchange ix) => Deserialize<T>(ix.Fragment, ix.DataOffset);

		public static bool TryDeserialize<T>(this Exchange ix, out T o)
		{
			o = default;

			try
			{
				o = Deserialize<T>(ix);

				return true;
			}
			catch { return false; }
		}

		public static MemoryFragment Serialize<T>(this T o, IMemoryHighway hw, Action<MemoryStream> pre = null)
		{
			if (hw == null || hw.IsDisposed) throw new ArgumentNullException("hw");

			using (var ms = new MemoryStream())
			{
				if (pre != null) pre(ms);

				var bf = new BinaryFormatter();
				bf.Serialize(ms, o);

				var f = hw.AllocFragment((int)ms.Length);
				var fs = f.CreateStream();
				ms.Seek(0, SeekOrigin.Begin);
				fs.Seek(0, SeekOrigin.Begin);
				ms.CopyTo(fs);

				return f;
			}
		}

		public static MemoryStream Serialize<T>(this T o, Action<MemoryStream> pre = null)
		{
			var ms = new MemoryStream();
			if (pre != null) pre(ms);

			var bf = new BinaryFormatter();

			bf.Serialize(ms, o);
			ms.Seek(0, SeekOrigin.Begin);

			return ms;
		}
	}
}
