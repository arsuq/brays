using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class PackSurface : ITestSurface
	{
		public string Info => "Test the RayEmitter small data packing into dgrams.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			RayBeamer rayA = null;
			RayBeamer rayB = null;

			try
			{
				var done = new ResetEvent(false);
				var aep = new IPEndPoint(IPAddress.Loopback, 3000);
				var bep = new IPEndPoint(IPAddress.Loopback, 4000);

				const int BYTES_TO_TRANSFER = 10_000_000;
				const int MAX_RANDOM_SIZE = 500;
				int totalSent = 0;
				int totalReceived = 0;

				rayA = new RayBeamer(
					(f) => { Console.WriteLine("?"); },
					new BeamerCfg() { Log = new LogCfg("rayA", true) });

				rayB = new RayBeamer((f) =>
				{
					try
					{
						if (f.Length < 1) return;

						var v = f[0];

						for (int i = 1; i < f.Length; i++)
							if (v != f[i] || f[i] == 0)
							{
								FailureMessage = "Received wrong data";
								Passed = false;
								done.Set(false);
							}

						var tr = Interlocked.Add(ref totalReceived, f.Length);
						if (tr >= BYTES_TO_TRANSFER)
							done.Set();
					}
					catch (Exception ex)
					{
						ex.ToString().AsError();
					}
					finally { if (f != null) f.Dispose(); }

				}, new BeamerCfg() { Log = new LogCfg("rayB", true) });

				using (var hw = new HeapHighway())
				{
					var ta = new Task(async () =>
					{
						await Task.Delay(0);

						rayA.LockOn(aep, bep).Wait();
						var rdm = new Random();

						while (true)
							try
							{
								using (var f = hw.Alloc(rdm.Next(1, MAX_RANDOM_SIZE)))
								{
									var v = (byte)rdm.Next(1, 255);

									for (int j = 0; j < f.Length; j++)
										f[j] = v;

									rayA.Pulse(f);
									if (Interlocked.Add(ref totalSent, f.Length) >= BYTES_TO_TRANSFER)
										break;
								}
							}
							catch { }
					});

					var tb = new Task(() =>
					{
						rayB.LockOn(bep, aep).Wait();
					});

					ta.Start();
					tb.Start();

					if (await done.Wait())
					{
						Passed = true;
						IsComplete = true;
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