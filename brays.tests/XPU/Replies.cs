using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestSurface;


namespace brays.tests
{
	class Replies : ITestSurface
	{
		public string Info => "Tests reply sequencing.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
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

			try
			{
				b.RegisterAPI<int>("ep", entry_point);

				a.Start(s, t);
				b.Start(t, s);

				await a.TargetIsActive();
				await b.TargetIsActive();

				var ms = new MarshalSlot(4);
				ms.Write(33, 0);

				var x = await a.RequestRaw("ep", ms);
				var data = ms.ToSpan<int>()[0];

				while (data >= 0)
				{
					data--;
					ms.Write(data, 0);

					x = await x.ReplyRaw(ms);

					if (!x.IsOK) break;

					x.Fragment.Read(ref data, x.DataOffset);
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

		async Task entry_point(Exchange<int> ix)
		{
			var data = ix.Arg;
			var ms = new MarshalSlot(4);
			Exchange x = ix;

			while (data >= 0)
			{
				data--;
				ms.Write(data, 0);
				x = await x.ReplyRaw(ms);

				if (!x.IsOK) break;

				x.Fragment.Read(ref data, x.DataOffset);
			}
		}
	}
}
