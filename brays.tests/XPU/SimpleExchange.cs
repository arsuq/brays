using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net;
using TestSurface;


namespace brays.tests
{
	class SimpleExchange : ITestSurface
	{
		public string Info => "Tests the basic request reply.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			await Task.Yield();

			var ta = new TestArgs(args);

			var s = ta.AE;
			var t = ta.BE;
			var a = new XPU(new XCfg(
				 new BeamerCfg() { Log = new BeamerLogCfg("a", ta.Log) },
				 new XLogCfg("a", ta.Log),
				 new HeapHighway(ushort.MaxValue)));

			var b = new XPU(new XCfg(
				 new BeamerCfg() { Log = new BeamerLogCfg("b", ta.Log) },
				 new XLogCfg("b", ta.Log),
				 new HeapHighway(ushort.MaxValue)));

			const string ADD_ONE = "addOne";

			try
			{
				b.RegisterAPI(ADD_ONE, add_one);

				a.Start(s, t);
				b.Start(t, s);

				await a.TargetIsActive();
				await b.TargetIsActive();

				using (var ix = await a.Request(ADD_ONE, 3))
					if (!ix.IsOK || ix.Make<int>() != 4)
					{
						Passed = false;
						FailureMessage = "Request failure.";
						return;
					}

				Passed = true;
				IsComplete = true;
			}
			catch (Exception ex)
			{
				Passed = false;
				FailureMessage = ex.ToString();
			}
			finally
			{
				a.Dispose();
				b.Dispose();
			}
		}

		void add_one(Exchange ix)
		{
			var data = ix.Make<int>();

			data++;

			ix.Reply(data);
		}
	}
}
