using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class HalfGigSurface : ITestSurface
	{
		public string Info => "Test moving data without verification.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			for (int i = 0; i < 4; i++)
				await halfGigNoLogNoVerify();

			IsComplete = true;
			Passed = true;
		}

		async Task halfGigNoLogNoVerify()
		{
			"[In] halfGigNoLogNoVerify()".AsTestInfo();
			var started = DateTime.Now;

			RayBeamer rayA = null;
			RayBeamer rayB = null;

			const int CAP = 500_000_000;
			int totalSend = 0;
			int totalReceived = 0;
			int totalFragsOut = 0;
			int totalFragsIn = 0;
			var rdmSize = new Random();

			try
			{
				var rst = new ManualResetEvent(false);
				var aep = new IPEndPoint(IPAddress.Loopback, 3000);
				var bep = new IPEndPoint(IPAddress.Loopback, 4000);

				void receive(MemoryFragment f)
				{
					int len = 0;
					bool err = false;
					int rec = 0;

					try
					{
						var s = f.Span();
						f.Read(ref len, 0);
						var fi = Interlocked.Increment(ref totalFragsIn);

						if (s.Length == len) rec = Interlocked.Add(ref totalReceived, len);
						else err = true;
					}
					catch (Exception ex)
					{
						ex.ToString().AsError();
					}
					finally
					{
						f.Dispose();
						var rem = CAP - rec;

						if (rem < 0) rem = 0;
						$"Rem: {rem} ".AsInnerInfo();

						if (err)
						{
							"rayB receive failed.".AsError();
							rst.Set();
						}
						else if (rec >= CAP)
						{
							Passed = true;
							IsComplete = true;
							var ts = DateTime.Now.Subtract(started);

							$"OK: halfGigNoLogNoVerify() {ts.Seconds}s {ts.Milliseconds}ms".AsSuccess();
							rst.Set();
						}

					}
				}

				var traceops = (TraceOps.ReqTiles | TraceOps.Tile | TraceOps.ProcTile);

				rayA = new RayBeamer((f) => { }, new BeamerCfg()
				{
					//Log = new LogCfg("rayA", true, traceops),
					TileSizeBytes = 60000
				});
				rayB = new RayBeamer(receive, new BeamerCfg()
				{
					Log = new LogCfg("rayB", true, traceops),
					TileSizeBytes = 60000
				});

				using (var hw = new HeapHighway())
				{
					var ta = new Task(() =>
					{
						rayA.LockOn(aep, bep, true).Wait();

						while (!rayA.IsStopped)
						{
							var len = rdmSize.Next(10, 10_000_000);
							var f = hw.Alloc(len);

							f.Write(len, 0);
							rayA.Beam(f).Wait();

							Interlocked.Increment(ref totalFragsOut);
							if (Interlocked.Add(ref totalSend, len) > CAP) break;
						}

						$"Out of beaming loop".AsInnerInfo();
					});

					var tb = new Task(() =>
					{
						rayB.LockOn(bep, aep, true).Wait();
					});

					ta.Start();
					tb.Start();
					if (!rst.WaitOne(new TimeSpan(0, 2, 0)))
					{
						Passed = false;
						FailureMessage = "Timeout.";
						FailureMessage.AsError();
					}
				}

				await Task.Delay(0);

				if (Passed.HasValue && !Passed.Value || !IsComplete) "halfGigNoLogNoVerify() failed".AsError();
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
