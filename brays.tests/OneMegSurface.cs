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
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			RayBeamer rayA = null;
			RayBeamer rayB = null;

			const int MEG = 1_000_000;

			try
			{
				var rst = new ManualResetEvent(false);
				var aep = new IPEndPoint(IPAddress.Loopback, 3000);
				var bep = new IPEndPoint(IPAddress.Loopback, 4000);
				rayA = new RayBeamer(
					(f) => { Console.WriteLine("?"); },
					new BeamerCfg() { Log = new LogCfg("rayA", true) });
				rayB = new RayBeamer((f) =>
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
				}, new BeamerCfg() { Log = new LogCfg("rayB", true) });

				using (var hw = new HeapHighway())
				{
					var ta = new Task(async () =>
					{
						rayA.LockOn(aep, bep).Wait();

						var f = hw.Alloc(MEG);

						for (int i = 0; i < MEG; i++)
							f[i] = 43;

						await rayA.Beam(f);
					});

					var tb = new Task(() =>
					{
						rayB.LockOn(bep, aep).Wait();
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