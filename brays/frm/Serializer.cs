using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization.Json;
using System.Xml.Serialization;

namespace brays
{
	public static class Serializer
	{
		public static T Deserialize<T>(this MemoryFragment f, SerializationType st, int from = 0)
		{
			if (f == null || f.IsDisposed) throw new ArgumentNullException("f");

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
					case SerializationType.Xml:
					{
						var s = new XmlSerializer(typeof(T));
						o = s.Deserialize(fs);
						break;
					}
					case SerializationType.None:
					default:
					break;
				}


				return (T)o;
			}
		}

		public static MemoryFragment Serialize<T>(this T o, SerializationType st, IMemoryHighway hw)
		{
			if (hw == null || hw.IsDisposed) throw new ArgumentNullException("hw");

			using (var ms = new MemoryStream())
			{
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
					case SerializationType.Xml:
					{
						var s = new XmlSerializer(typeof(T));
						s.Serialize(ms, o);
						break;
					}
					case SerializationType.None:
					default:
					break;
				}

				var f = hw.AllocFragment((int)ms.Length);
				var fs = f.CreateStream();
				ms.CopyTo(fs);

				return f;
			}
		}
	}
}
