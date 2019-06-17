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

			var te = new TestEndpoints(args);

			var s = te.Listen;
			var t = te.Target;
			var a = new XPU(new XCfg(
				 new BeamerCfg() { Log = new BeamerLogCfg("a") },
				 new XLogCfg("a", true),
				 new HeapHighway(ushort.MaxValue)));

			var b = new XPU(new XCfg(
				 new BeamerCfg() { Log = new BeamerLogCfg("b") },
				 new XLogCfg("b", true),
				 new HeapHighway(ushort.MaxValue)));

			const string ADD_ONE = "addOne";
			const string ADD_ONE_GEN = "addOneGen";

			try
			{
				b.RegisterAPI(ADD_ONE, add_one);
				b.RegisterAPI<int>(ADD_ONE_GEN, add_oneg);

				await a.Start(s, t);
				await b.Start(t, s);

				using (var ix = await a.Request<int, int>(ADD_ONE, 3))
					if (!ix.IsOK || ix.Arg != 4)
					{
						Passed = false;
						FailureMessage = "Exchange failure.";
						return;
					}

				using (var ix = await a.Request<int, int>(ADD_ONE_GEN, 8))
					if (!ix.IsOK || ix.Arg != 9)
					{
						Passed = false;
						FailureMessage = "Exchange failure.";
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

			ix.XPU.Reply(ix, data).Wait();
		}

		void add_oneg(Exchange<int> ix)
		{
			// Implicit cast as T
			int v = ix;

			ix.Instance.Reply(++v).Wait();
		}
	}
}
