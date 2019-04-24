using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class RayEmitterBasicSurface : ITestSurface
	{
		public string Info => "Test the RayEmitter fragment beamer.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			await OneByteDgram();
			await OneMegDgrams();
		}

		async Task OneByteDgram()
		{
			RayEmitter rayA = null;
			RayEmitter rayB = null;

			try
			{
				var rst = new ManualResetEvent(false);
				var aep = new IPEndPoint(IPAddress.Loopback, 3000);
				var bep = new IPEndPoint(IPAddress.Loopback, 4000);
				rayA = new RayEmitter((f) => { Console.WriteLine("?"); },
					new EmitterCfg() { Log = true, LogFilePath = "rayA" });
				rayB = new RayEmitter((f) =>
				{
					if (f.Span()[0] == 1)
					{
						Passed = true;
						"OK: rayB received 1".AsSuccess();
					}
					else
					{
						Passed = false;
						"rayB receive failed.".AsError();
					}

					rst.Set();

				}, new EmitterCfg() { Log = true, LogFilePath = "rayB" });

				using (var hw = new HeapHighway(50))
				{
					var ta = new Task(async () =>
					{
						rayA.LockOn(aep, bep);

						var f = hw.Alloc(1);

						f.Write(true, 0);
						await rayA.Beam(f);
					});

					var tb = new Task(() =>
					{
						rayB.LockOn(bep, aep);
					});

					ta.Start();
					tb.Start();
					rst.WaitOne();
				}

				await Task.Delay(0);

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

		async Task OneMegDgrams()
		{
			RayEmitter rayA = null;
			RayEmitter rayB = null;

			const int MEG = 1_000_000;

			try
			{
				var rst = new ManualResetEvent(false);
				var aep = new IPEndPoint(IPAddress.Loopback, 3000);
				var bep = new IPEndPoint(IPAddress.Loopback, 4000);
				rayA = new RayEmitter(
					(f) => { Console.WriteLine("?"); },
					new EmitterCfg() { Log = true, LogFilePath = "rayA" });
				rayB = new RayEmitter((f) =>
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
							"OK: rayB received the correct data.".AsSuccess();
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
				}, new EmitterCfg() { Log = true, LogFilePath = "rayB" });

				using (var hw = new HeapHighway())
				{
					var ta = new Task(async () =>
					{
						rayA.LockOn(aep, bep);

						var f = hw.Alloc(MEG);

						for (int i = 0; i < MEG; i++)
							f[i] = 43;

						await rayA.Beam(f);
					});

					var tb = new Task(() =>
					{
						rayB.LockOn(bep, aep);
					});

					ta.Start();
					tb.Start();
					rst.WaitOne();
				}

				await Task.Delay(0);

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
