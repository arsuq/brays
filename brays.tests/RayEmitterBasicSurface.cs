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
			//			await oneByteDgram();
			//			await oneMeg();
			//#if DEBUG
			//			await missingTiles();
			//			await M30();
			//#endif

			for (int i = 0; i < 4; i++)
				await halfGigNoLogNoVerify();
		}

		async Task oneByteDgram()
		{
			"[In] oneByteDgram()".AsTestInfo();

			RayEmitter rayA = null;
			RayEmitter rayB = null;

			try
			{
				var rst = new ManualResetEvent(false);
				var aep = new IPEndPoint(IPAddress.Loopback, 3000);
				var bep = new IPEndPoint(IPAddress.Loopback, 4000);
				rayA = new RayEmitter((f) => { Console.WriteLine("?"); },
					new EmitterCfg() { Log = new LogCfg("rayA", true) });
				rayB = new RayEmitter((f) =>
				{
					if (f.Span()[0] == 1)
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

				}, new EmitterCfg() { Log = new LogCfg("rayB", true) });

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

		async Task oneMeg()
		{
			"[In] oneMeg()".AsTestInfo();

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
					new EmitterCfg() { Log = new LogCfg("rayA", true) });
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
				}, new EmitterCfg() { Log = new LogCfg("rayB", true) });

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

		async Task missingTiles()
		{
			"[In] missingTiles()".AsTestInfo();

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
					new EmitterCfg()
					{
						Log = new LogCfg("rayA", true),
#if DEBUG
						dropFrames = true,
						deopFramePercent = 30
#endif
					});
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
							"OK: Send/Receive 1meg with dropped random frames on both sides.".AsSuccess();
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
				}, new EmitterCfg()
				{
					Log = new LogCfg("rayB", true),
#if DEBUG
					dropFrames = true,
					deopFramePercent = 30
#endif
				});

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

		async Task M30()
		{
			"[In] M30()".AsTestInfo();

			RayEmitter rayA = null;
			RayEmitter rayB = null;

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

				rayA = new RayEmitter(
					(f) => { Console.WriteLine("?"); },
					new EmitterCfg()
					{
						Log = new LogCfg("rayA", true, (TraceOps)1023) { OnTrace = null },
#if DEBUG
						dropFrames = true,
						deopFramePercent = 20
#endif
					});

				rayB = new RayEmitter(receive, new EmitterCfg()
				{
					Log = new LogCfg("rayB", true, (TraceOps)1023) { OnTrace = null },
#if DEBUG
					dropFrames = true,
					deopFramePercent = 20
#endif
				});

				using (var hw = new HeapHighway())
				{
					var ta = new Task(() =>
					{
						rayA.LockOn(aep, bep);

						while (!rayA.IsStopped)
						{
							var len = rdmSize.Next(10, 1_000_000);
							var f = hw.Alloc(len);

							for (int i = 0; i < len; i++)
								f[i] = 43;

							f.Write(len, 0);
							rayA.Beam(f);

							var fo = Interlocked.Increment(ref totalFragsOut);
							var ts = Interlocked.Add(ref totalSend, len);

							string.Format("S: {0, -10} TS: {1, -10} FO: {2, -3}", len, ts, fo).AsInnerInfo();

							if (ts > CAP) break;
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
					}
				}

				await Task.Delay(0);

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

		async Task halfGigNoLogNoVerify()
		{
			"[In] halfGigNoLogNoVerify()".AsTestInfo();
			var started = DateTime.Now;

			RayEmitter rayA = null;
			RayEmitter rayB = null;

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

				var traceops = (TraceOps)1023;

				rayA = new RayEmitter((f) => { }, new EmitterCfg()
				{
					//Log = new LogCfg("rayA", true, traceops),
					TileSize = 60000
				});
				rayB = new RayEmitter(receive, new EmitterCfg()
				{
					//Log = new LogCfg("rayB", true, traceops),
					TileSize = 60000
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
							rayA.Beam(f);

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
