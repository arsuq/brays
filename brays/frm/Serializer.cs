using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization.Json;

namespace brays
{
	public static class Serializer
	{
		public static T Deserialize<T>(this MemoryFragment f, SerializationType st, int from = 0)
		{
			if (f == null || f.IsDisposed) throw new ArgumentNullException("f");
			if (st == SerializationType.None) return default;

			object o = null;

			using (var fs = f.CreateStream())
			{
				if (from > 0) fs.Seek(from, SeekOrigin.Begin);

				switch (st)
				{
					case SerializationType.Binary:
					{
						var bf = new BinaryFormatter();
						o = bf.Deserialize(fs);
						break;
					}
					case SerializationType.Json:
					{
						var ds = new DataContractJsonSerializer(typeof(T));
						o = ds.ReadObject(fs);
						break;
					}
					default: return default;
				}

				return (T)o;
			}
		}

		public static T Deserialize<T>(this Exchange ix) =>
			Deserialize<T>(ix.Fragment, ix.SerializationType, ix.DataOffset);

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

		public static MemoryFragment Serialize<T>(this T o, SerializationType st,
			IMemoryHighway hw, Action<MemoryStream> addHeader = null)
		{
			if (hw == null || hw.IsDisposed) throw new ArgumentNullException("hw");
			if (st == SerializationType.None) return null;

			using (var ms = new MemoryStream())
			{
				if (addHeader != null) addHeader(ms);

				switch (st)
				{
					case SerializationType.Binary:
					{
						var bf = new BinaryFormatter();
						bf.Serialize(ms, o);
						break;
					}
					case SerializationType.Json:
					{
						var ds = new DataContractJsonSerializer(typeof(T));
						ds.WriteObject(ms, o);
						break;
					}
					default: return null;
				}

				var f = hw.AllocFragment((int)ms.Length);
				var fs = f.CreateStream();
				ms.Seek(0, SeekOrigin.Begin);
				fs.Seek(0, SeekOrigin.Begin);
				ms.CopyTo(fs);

				return f;
			}
		}

		public static MemoryStream Serialize<T>(this T o, SerializationType st, Action<MemoryStream> addHeader = null)
		{
			if (st == SerializationType.None) return null;

			var ms = new MemoryStream();
			if (addHeader != null) addHeader(ms);

			switch (st)
			{
				case SerializationType.Binary:
				{
					var bf = new BinaryFormatter();
					bf.Serialize(ms, o);
					break;
				}
				case SerializationType.Json:
				{
					var ds = new DataContractJsonSerializer(typeof(T));
					ds.WriteObject(ms, o);
					break;
				}
				default: return null;
			}

			ms.Seek(0, SeekOrigin.Begin);

			return ms;
		}
	}
}
