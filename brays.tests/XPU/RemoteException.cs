using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestSurface;


namespace brays.tests
{
	class RemoteException : ITestSurface
	{
		public string Info => "Tests the XPU exception propagation.";
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
				 new XLogCfg("a", ta.Log),
				 new HeapHighway(ushort.MaxValue)));

			var b = new XPU(new XCfg(
				 new BeamerCfg() { UseTCP = ta.UseTCP, Log = new BeamerLogCfg("b", ta.Log) },
				 new XLogCfg("b", ta.Log),
				 new HeapHighway(ushort.MaxValue)));

			try
			{
				string RES = "WillThrow";

				b.RegisterAPI(RES, (f) =>
				{
					"Throwing NotSupportedException".AsInfo();

					throw new NotSupportedException();
				});

				a.Start(s, t);
				b.Start(t, s);

				await a.TargetIsActive();
				await b.TargetIsActive();

				using (var ix = await a.Request(RES))
					if (!ix.IsOK && ix.ErrorCode == (int)XErrorCode.SerializedException)
					{
						var ex = ix.Make<Exception>();

						if (ex is NotSupportedException)
						{
							Passed = true;
							IsComplete = true;
						}
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
