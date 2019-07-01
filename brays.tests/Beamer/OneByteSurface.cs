using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class OneByteSurface : ITestSurface
	{
		public string Info => "Test the smallest possible transfer.";
		public string Tags => "beamer";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Start(IDictionary<string, List<string>> args)
		{
			Beamer rayA = null;
			Beamer rayB = null;

			try
			{
				var rst = new ManualResetEvent(false);
				var targ = new TestArgs(args);
				var aep = targ.AE;
				var bep = targ.BE;

				rayA = new Beamer((f) => { Console.WriteLine("?"); },
					new BeamerCfg()
					{
						UseTCP = targ.UseTCP,
						Log = new BeamerLogCfg("rayA-OneByte", targ.Log)
					});
				rayB = new Beamer((f) =>
				{
					if (f.Span()[0] == 77)
					{
						Passed = true;
						"OK: rayB received 1 byte.".AsSuccess();
					}
					else
					{
						Passed = false;
						"rayB receive failed.".AsError();
					}

					rst.Set();

				}, new BeamerCfg()
				{
					UseTCP = targ.UseTCP,
					Log = new BeamerLogCfg("rayB-OneByte", targ.Log)
				});

				using (var hw = new HeapHighway(50))
				{
					var ta = new Task(async () =>
					{
						await rayA.LockOn(aep, bep);

						var f = hw.Alloc(1);

						f.Write((byte)77, 0);
						await rayA.Beam(f);
					});

					var tb = new Task(async () =>
					{
						await rayB.LockOn(bep, aep);
					});

					ta.Start();
					tb.Start();
					rst.WaitOne();
				}

				await Task.Yield();

				Passed = true;
				IsComplete = true;
			}
			catch (Exception ex)
			{
				FailureMessage = ex.Message;
				Passed = false;
			}
			finally
			{
				rayA.Dispose();
				rayB.Dispose();
			}
		}
	}
}