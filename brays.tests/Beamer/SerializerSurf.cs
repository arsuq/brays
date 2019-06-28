using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Serialization;
using TestSurface;

namespace brays.tests
{
	public class SerializerSurf : ITestSurface
	{
		public string Info => "Tests the Serializer class.";
		public string Tags => "xpu, frm";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Start(IDictionary<string, List<string>> args)
		{
			await Task.Yield();

			using (var hw = new HeapHighway(ushort.MaxValue))
			{
				var d = new Dummy();

				var bs = d.Serialize(hw);
				var bd = bs.Deserialize<Dummy>();

				if (!Assert.SameValues(d, bd, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
				{
					Passed = false;
					FailureMessage = "Binary serialization mismatch.";
					return;
				}

				Passed = true;
				IsComplete = true;
			}
		}

		[Serializable]
		public class Dummy
		{
			public Dummy()
			{
				inner = new Inner();
				created = DateTime.Now;
				ID = Guid.NewGuid().ToString();
			}

			public Inner Inner => inner;

			[XmlAttribute]
			public string ID;
			DateTime created;
			Inner inner;
		}

		[Serializable]
		public class Inner
		{
			public Inner()
			{
				items = new List<int>(System.Linq.Enumerable.Range(0, 100));
			}

			public List<int> Items => items;

			List<int> items;
		}
	}
}