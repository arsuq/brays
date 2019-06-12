using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using TestSurface;


namespace brays.tests
{
	class XPUSurf : ITestSurface
	{
		public string Info => "Tests the XPU class.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			await Task.Yield();

			var s = new IPEndPoint(IPAddress.Loopback, 3000);
			var t = new IPEndPoint(IPAddress.Loopback, 4000);
			var a = new XPU(new XCfg(
				 new BeamerCfg(),
				 new XLogCfg("a", true),
				 new HeapHighway(ushort.MaxValue)));

			var b = new XPU(new XCfg(
				 new BeamerCfg(),
				 new XLogCfg("b", true),
				 new HeapHighway(ushort.MaxValue)));

			const string F1 = "f1";

			try
			{
				b.RegisterAPI(F1, add_one);

				await a.Start(s, t);
				await b.Start(t, s);

				await a.QueueExchange<int>(F1, 3, (ix) =>
				{
					var rpl = ix.Make<int>();

					if (rpl != 4)
					{
						Passed = false;
						FailureMessage = "Exchanged wrong values.";
						return;
					}
				});


			}
			catch (Exception ex)
			{
				Passed = false;
				FailureMessage = ex.ToString();
			}
		}


		void add_one(Exchange ix)
		{
			var data = ix.Make<int>();

			data++;

			ix.XPU.Reply(ix, data);
		}
	}
}
