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
			const string F2 = "f2";

			try
			{
				a.RegisterAPI(F1, f1);
				b.RegisterAPI(F2, f2);

				await a.Start(s, t);
				await b.Start(t, s);

				var a2b = Task.Run(() =>
				{
					//a.QueueExchange(F1, )
				});
			}
			catch (Exception ex)
			{

			}
		}


		void f1(Exchange ix)
		{

		}

		void f2(Exchange ix)
		{

		}
	}
}
