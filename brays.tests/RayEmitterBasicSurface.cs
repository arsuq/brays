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
			//await oneByteDgram();
			//await oneMeg();
#if DEBUG
			//await missingTiles();
			await oneGig();
#endif
		}

		async Task oneByteDgram()
		{
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
						deopFrameProb = 0.3
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
					deopFrameProb = 0.3
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

		async Task oneGig()
		{
			RayEmitter rayA = null;
			RayEmitter rayB = null;

			const int GIG = 10_000_000;
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
					try
					{
						var s = f.Span();
						int len = 0;
						f.Read(ref len, 0);
						Interlocked.Increment(ref totalFragsIn);

						if (s.Length == len)
						{
							for (int i = 4; i < len; i++)
								if (f[i] != 43)
								{
									Passed = false;
									"rayB received incorrect data.".AsError();
									break;
								}

							Interlocked.Add(ref totalReceived, len);
						}
						else
						{
							Passed = false;
							"rayB receive failed.".AsError();
						}
					}
					finally
					{
						if ((Passed.HasValue && !Passed.Value) || Volatile.Read(ref totalReceived) >= GIG)
						{
							if (!Passed.HasValue)
							{
								Passed = true;
								"OK: Send/Receive 1gig with dropped random frames on both sides.".AsSuccess();
							}

							rst.Set();
						}
					}
				}

				rayA = new RayEmitter(
					(f) => { Console.WriteLine("?"); },
					new EmitterCfg()
					{
						Log = new LogCfg("rayA", true),
#if DEBUG
						dropFrames = true,
						deopFrameProb = 0.2
#endif
					});

				rayB = new RayEmitter(receive, new EmitterCfg()
				{
					Log = new LogCfg("rayB", true),
#if DEBUG
					dropFrames = true,
					deopFrameProb = 0.2
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
							Interlocked.Increment(ref totalFragsOut);

							if (Interlocked.Add(ref totalSend, len) > GIG)
								break;
						}
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
						FailureMessage = "Send reset timeout.";
					}
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
