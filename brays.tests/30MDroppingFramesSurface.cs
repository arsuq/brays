using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class DroppingFrames30MSurface : ITestSurface
	{
		public string Info => "Tests beaming 30M with frame drops.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
#if !DEBUG
			return;
#endif
			RayBeamer rayA = null;
			RayBeamer rayB = null;

			const int CAP = 30_000_000;
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

					try
					{
						var s = f.Span();
						f.Read(ref len, 0);
						var fi = Interlocked.Increment(ref totalFragsIn);

						if (s.Length == len)
						{
							for (int i = 4; i < len; i++)
								if (f[i] != 43)
								{
									Passed = false;
									"rayB received incorrect data.".AsError();
									break;
								}

							var ts = Interlocked.Add(ref totalReceived, len);
							string.Format("R: {0, -10} TR: {1, -10} FI: {2, -3}", len, ts, fi).AsInnerInfo();
						}
						else
						{
							Passed = false;
							"rayB receive failed.".AsError();
						}
					}
					finally
					{
						if ((Passed.HasValue && !Passed.Value) || Volatile.Read(ref totalReceived) >= CAP)
						{
							if (!Passed.HasValue)
							{
								Passed = true;
								"OK: Send/Receive with dropped random frames on both sides.".AsSuccess();
							}

							rst.Set();
						}
					}
				}

				rayA = new RayBeamer(
					(f) => { Console.WriteLine("?"); },
					new BeamerCfg()
					{
						Log = new LogCfg("rayA", true) { OnTrace = null },
#if DEBUG
						dropFrames = true,
						deopFramePercent = 20
#endif
					});


				rayB = new RayBeamer(receive, new BeamerCfg()
				{
					Log = new LogCfg("rayB", true) { OnTrace = null },
#if DEBUG
					dropFrames = true,
					deopFramePercent = 20
#endif
				});

				using (var hw = new HeapHighway())
				{
					var ta = new Task(() =>
					{
						rayA.LockOn(aep, bep).Wait();

						while (!rayA.IsStopped)
						{
							var len = rdmSize.Next(10, 1_000_000);
							var f = hw.Alloc(len);

							for (int i = 0; i < len; i++)
								f[i] = 43;

							f.Write(len, 0);
							rayA.Beam(f).Wait();

							var fo = Interlocked.Increment(ref totalFragsOut);
							var ts = Interlocked.Add(ref totalSend, len);

							string.Format("S: {0, -10} TS: {1, -10} FO: {2, -3}", len, ts, fo).AsInnerInfo();

							if (ts > CAP) break;
						}

						$"Out of beaming loop".AsInnerInfo();
					});

					var tb = new Task(() =>
					{
						rayB.LockOn(bep, aep).Wait();
					});

					ta.Start();
					tb.Start();
					if (!rst.WaitOne(new TimeSpan(0, 2, 0)))
					{
						Passed = false;
						FailureMessage = "Timeout.";
					}
				}

				await Task.Yield();

				if (!Passed.HasValue) Passed = true;
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