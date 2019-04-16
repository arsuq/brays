using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace brays
{
	public static class Serializer
	{
		public static string ToXml<T>(this T o)
		{
			using (var ms = new MemoryStream())
			{
				var s = new XmlSerializer(typeof(T));

				s.Serialize(ms, o);
				var bytes = ms.ToArray();

				return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
			}
		}

		public static void ToXmlFile<T>(this T o, string path)
		{
			using (var ms = new FileStream(path, FileMode.OpenOrCreate))
			{
				var s = new XmlSerializer(typeof(T));

				s.Serialize(ms, o);
				ms.Flush();
			}
		}

		public static T FromXml<T>(string xml)
		{
			using (var sr = new StringReader(xml))
			{
				var s = new XmlSerializer(typeof(T));

				return (T)s.Deserialize(sr);
			}
		}

		public static T FromXmlFile<T>(string path)
		{
			using (var sr = new FileStream(path, FileMode.Open))
			{
				var s = new XmlSerializer(typeof(T));

				return (T)s.Deserialize(sr);
			}
		}
	}
}
