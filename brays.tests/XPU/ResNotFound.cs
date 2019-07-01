using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestSurface;


namespace brays.tests
{
	class ResNotFound : ITestSurface
	{
		public string Info => "Tests the XPU error reply.";
		public string Tags => "xpu, ex";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Start(IDictionary<string, List<string>> args)
		{
			var ta = new TestArgs(args);

			var s = ta.AE;
			var t = ta.BE;

			var a = new XPU(new XCfg(
				 new BeamerCfg() { UseTCP = ta.UseTCP, Log = new BeamerLogCfg("a", ta.Log) },
				 new XLogCfg("a-ResNotFound", ta.Log),
				 new HeapHighway(ushort.MaxValue)));

			var b = new XPU(new XCfg(
				 new BeamerCfg() { UseTCP = ta.UseTCP, Log = new BeamerLogCfg("b", ta.Log) },
				 new XLogCfg("b-ResNotFound", ta.Log),
				 new HeapHighway(ushort.MaxValue)));

			try
			{
				a.Start(s, t);
				b.Start(t, s);

				await a.TargetIsActive();
				await b.TargetIsActive();

				using (var ix = await a.Request("NonExistingResKey", 3))
					if (!ix.IsOK && ix.ErrorCode == (int)XErrorCode.ResourceNotFound)
					{
						Passed = true;
						IsComplete = true;
					}

				if (!Passed.HasValue) Passed = false;
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
	}
}
