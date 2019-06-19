using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class PulseSurface : ITestSurface
	{
		public string Info => "Test the Beamer small data packing into dgrams.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			var targ = new TestArgs(args);

			await WithCheck(targ);
			await NoCheck(targ);
		}

		async Task WithCheck(TestArgs targ)
		{
			Beamer rayA = null;
			Beamer rayB = null;

			try
			{
				var done = new ResetEvent(false);
				var aep = targ.AE;
				var bep = targ.BE;

				const int BYTES_TO_TRANSFER = 10_000_000;
				const int MAX_RANDOM_SIZE = 1000;
				int totalSent = 0;
				int totalReceived = 0;

				rayA = new Beamer(
					(f) => { Console.WriteLine("?"); },
					new BeamerCfg()
					{
						PulseSleepMS = 0,
						Log = new BeamerLogCfg("rayA", targ.Log)
					});

				rayB = new Beamer((f) =>
				{
					try
					{
						if (f == null || f.IsDisposed || f.Length < 1)
							throw new Exception("Frag");

						var fs = f.Span();
						var v = fs[0];

						for (int i = 1; i < fs.Length; i++)
							if (v != fs[i] || fs[i] == 0)
							{
								FailureMessage = "Received wrong data";
								Passed = false;
								done.Set(false);
							}

						var tr = Interlocked.Add(ref totalReceived, f.Length);

						//$"TR: {tr}".AsInfo();

						if (tr >= BYTES_TO_TRANSFER)
							done.Set();
					}
					catch (Exception ex)
					{
						ex.ToString().AsError();
					}
					finally
					{
						if (f != null) f.Dispose();
					}

				}, new BeamerCfg() { Log = new BeamerLogCfg("rayB", targ.Log) });

				using (var hw = new HeapHighway())
				{
					var ta = new Task(async () =>
					{
						await Task.Yield();

						rayA.LockOn(aep, bep).Wait();
						var rdm = new Random();

						while (true)
							try
							{
								if (done.Task.Status == TaskStatus.RanToCompletion) return;

								using (var f = hw.Alloc(rdm.Next(1, MAX_RANDOM_SIZE)))
								{
									var v = (byte)rdm.Next(1, 255);

									for (int j = 0; j < f.Length; j++)
										f[j] = v;

									rayA.Pulse(f);
									var ts = Interlocked.Add(ref totalSent, f.Length);
									if (ts >= BYTES_TO_TRANSFER)
										break;
								}
							}
							catch (Exception ex)
							{

							}
					});

					var tb = new Task(() =>
					{
						rayB.LockOn(bep, aep).Wait();
					});

					ta.Start();
					tb.Start();

					if (await done.Wait() > 0)
					{
						Passed = true;
						IsComplete = true;
					}
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

		async Task NoCheck(TestArgs targ)
		{
			Beamer rayA = null;
			Beamer rayB = null;

			try
			{
				var done = new ResetEvent(false);
				var aep = targ.AE;
				var bep = targ.BE;

				const int BYTES_TO_TRANSFER = 100_000_000;
				const int MAX_RANDOM_SIZE = 1000;
				int totalSent = 0;
				int totalReceived = 0;

				rayA = new Beamer((f) => { }, new BeamerCfg() { Log = new BeamerLogCfg("rayA", targ.Log) });

				rayB = new Beamer((f) =>
				{
					try
					{
						if (f == null || f.IsDisposed || f.Length < 1)
							throw new Exception("Frag");

						var tr = Interlocked.Add(ref totalReceived, f.Length);
						if (tr >= BYTES_TO_TRANSFER)
							done.Set();
					}
					catch (Exception ex)
					{
						ex.ToString().AsError();
					}
					finally
					{
						if (f != null) f.Dispose();
					}

				}, new BeamerCfg() { Log = new BeamerLogCfg("rayB", targ.Log) });

				var hw = new HeapHighway();
				var ta = new Task(async () =>
				{
					await Task.Yield();

					rayA.LockOn(aep, bep).Wait();
					var rdm = new Random();

					while (true)
						try
						{
							if (done.Task.Status == TaskStatus.RanToCompletion) return;

							using (var f = hw.Alloc(rdm.Next(1, MAX_RANDOM_SIZE)))
							{
								rayA.Pulse(f);
								var ts = Interlocked.Add(ref totalSent, f.Length);
								if (ts >= BYTES_TO_TRANSFER)
									break;
							}
						}
						catch (Exception ex)
						{
							ex.Message.AsError();
						}

					rayA.trace("Out of pulsing", null);
				});

				var tb = new Task(() =>
				{
					rayB.LockOn(bep, aep).Wait();
				});

				ta.Start();
				tb.Start();

				if (await done.Wait() > 0)
				{
					Passed = true;
					IsComplete = true;
				}

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