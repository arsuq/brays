using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class OneMegSurface : ITestSurface
	{
		public string Info => "Tests 1M beam.";
		public string Tags => "beamer";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Start(IDictionary<string, List<string>> args)
		{
			Beamer rayA = null;
			Beamer rayB = null;

			const int MEG = 1_000_000;

			try
			{
				var rst = new ManualResetEvent(false);
				var targ = new TestArgs(args);
				var aep = targ.AE;
				var bep = targ.BE;

				rayA = new Beamer(
					(f) => { Console.WriteLine("?"); },
					new BeamerCfg()
					{
						Log = new BeamerLogCfg("rayA-OneMeg", targ.Log)
					});
				rayB = new Beamer((f) =>
				{
					try
					{
						var s = f.Span();

						if (s.Length == MEG)
						{
							for (int i = 0; i < MEG; i++)
								if (f[i] != 43)
								{
									Passed = false;
									"rayB received incorrect data.".AsError();
									break;
								}

							Passed = true;
							"OK: Send/Receive 1meg.".AsSuccess();
						}
						else
						{
							Passed = false;
							"rayB receive failed.".AsError();
						}
					}
					finally
					{
						rst.Set();
					}
				}, new BeamerCfg()
				{
					Log = new BeamerLogCfg("rayB-OneMeg", targ.Log)
				});

				using (var hw = new HeapHighway())
				{
					var ta = new Task(async () =>
					{
						await rayA.LockOn(aep, bep);

						var f = hw.Alloc(MEG);

						for (int i = 0; i < MEG; i++)
							f[i] = 43;

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