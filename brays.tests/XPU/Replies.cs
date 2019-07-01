using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestSurface;


namespace brays.tests
{
	class Replies : ITestSurface
	{
		public string Info => "Tests reply sequencing with RawRequest-RawReply.";
		public string Tags => "xpu";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		readonly TimeSpan TIMEOUT = TimeSpan.FromMilliseconds(1000);

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

			MarshalSlot ms = null;

			try
			{
				b.RegisterAPI("ep", entry_point);

				a.Start(s, t);
				b.Start(t, s);

				await a.TargetIsActive();
				await b.TargetIsActive();

				ms = MarshalSlot.Store(8);

				var x = await a.RawRequest("ep", ms, false, TIMEOUT);
				int data = 0;
				x.Fragment.Read(ref data, x.DataOffset);

				while (data > 0 && !x.DoNotReply)
				{
					data--;
					ms.Write(data, 0);

					x = await x.RawReply(ms, data < 1, true, TIMEOUT);

					if (!x.IsOK)
					{
						Passed = false;
						FailureMessage = "Timeout";
						return;
					}

					if (x.State == XState.Beamed) x.Fragment.Read(ref data, x.DataOffset);
				}

				await Task.Delay(TIMEOUT.Milliseconds * 3);

				if (!Passed.HasValue)
				{
					Passed = true;
					IsComplete = true;
				}
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
				ms.Dispose();
			}
		}

		async Task entry_point(Exchange ix)
		{
			if (!ix.RawBits) throw new ArgumentException("rawBits");

			var ms = new MarshalSlot(4);
			Exchange x = ix;
			int data = 0;

			try
			{
				x.Fragment.Read(ref data, x.DataOffset);

				while (data > 0 && !x.DoNotReply)
				{
					data--;

					ms.Write(data, 0);
					x = await x.RawReply(ms, data < 1, true, TIMEOUT);

					if (!x.IsOK)
					{
						Passed = false;
						FailureMessage = "Timeout";
						return;
					}

					if (x.State == XState.Beamed) x.Fragment.Read(ref data, x.DataOffset);
				}
			}
			finally
			{
				ms.Dispose();
				x.Dispose();
			}
		}
	}
}
