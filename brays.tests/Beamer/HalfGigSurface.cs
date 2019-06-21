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
			var targ = new TestArgs(args);

			for (int i = 0; i < 4; i++)
				await halfGigNoLogNoVerify(targ);

			IsComplete = true;
			Passed = true;
		}

		async Task halfGigNoLogNoVerify(TestArgs targ)
		{
			"[In] halfGigNoLogNoVerify()".AsTestInfo();
			var started = DateTime.Now;

			Beamer rayA = null;
			Beamer rayB = null;

			const int CAP = 500_000_000;
			int totalSend = 0;
			int totalReceived = 0;
			int totalFragsOut = 0;
			int totalFragsIn = 0;
			var rdmSize = new Random();

			try
			{
				var rst = new ManualResetEvent(false);
				
				var aep = targ.AE;
				var bep = targ.BE;

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

				rayA = new Beamer((f) => { }, new BeamerCfg()
				{
					//Log = new LogCfg("rayA", true, traceops),
					TileSizeBytes = 60000
				});
				rayB = new Beamer(receive, new BeamerCfg()
				{
					Log = new BeamerLogCfg("rayB", targ.Log, traceops),
					TileSizeBytes = 60000
				});

				using (var hw = new HeapHighway())
				{
					var ta = new Task(() =>
					{
						rayA.LockOn(aep, bep);

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
						rayB.LockOn(bep, aep);
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

				await Task.Yield();

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
