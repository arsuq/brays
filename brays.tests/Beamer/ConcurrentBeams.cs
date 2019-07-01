using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	public class ConcurrentBeams : ITestSurface
	{
		public string Info => "Test concurrent beams.";
		public string Tags => "beamer, cc";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Start(IDictionary<string, List<string>> args)
		{
			Beamer rayA = null;
			Beamer rayB = null;

			var rdmSize = new Random();
			const int TOTAL_SENDS = 100;
			const int MIN_SIZE = 10_000;
			const int MAX_SIZE = 10_000_000;
			var rmask = new bool[TOTAL_SENDS];
			var smask = new bool[TOTAL_SENDS];
			try
			{
				var targ = new TestArgs(args);
				var rst = new CountdownEvent(TOTAL_SENDS);

				void receive(MemoryFragment f)
				{
					int len = 0;

					try
					{
						var s = f.Span();
						f.Read(ref len, 0);

						if (s.Length == len)
						{
							rmask[f[4]] = true;

							for (int i = 4; i < len; i++)
								if (f[i] != f[4])
								{
									Passed = false;
									"Received incorrect data.".AsError();
									break;
								}
						}
						else
						{
							Passed = false;
							"rayB receive failed.".AsError();
						}
					}
					catch (Exception ex)
					{
						Passed = false;
						ex.Message.AsError();
					}
					finally
					{
						$"Signal {rst.CurrentCount}".AsInnerInfo();
						rst.Signal();
					}
				}

				rayA = new Beamer((f) => { },
					new BeamerCfg()
					{
						UseTCP = targ.UseTCP,
						Log = new BeamerLogCfg("rayA-ConcurrentBeams", targ.Log) { OnTrace = null },
					});

				rayB = new Beamer((f) => receive(f), new BeamerCfg()
				{
					UseTCP = targ.UseTCP,
					Log = new BeamerLogCfg("rayB-ConcurrentBeams", targ.Log) { OnTrace = null },
				});

				var ta = new Task(async () =>
				{
					await rayA.LockOn(targ.AE, targ.BE);
					await rayA.TargetIsActive();
					using (var hw = new HeapHighway())
					{
						Parallel.For(0, TOTAL_SENDS, async (i) =>
						{
							if (rayA.IsStopped) return;
							var len = rdmSize.Next(MIN_SIZE, MAX_SIZE);
							using (var f = hw.Alloc(len))
							{
								for (int j = 0; j < len; j++)
									f[j] = (byte)i;

								f.Write(len, 0);

								if (!await rayA.Beam(f))
								{
									"Failed to beam".AsError();
								}
							}

							smask[i] = true;
							$"Lanes :{hw.GetLanesCount()} i:{i}".AsInfo();
						});
					}

					$"Out of beaming loop".AsInnerInfo();
				});

				ta.Start();

				if (!await rayB.LockOn(targ.BE, targ.AE)) $"Failed to lock on rayA".AsError();

				if (!rst.Wait(new TimeSpan(0, 4, 0)))
				{
					Passed = false;
					FailureMessage = "Timeout.";
				}

				if (rmask.Contains(false))
				{
					Passed = false;
					FailureMessage = "Non received data";
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
				if (rayA != null) rayA.Dispose();
				if (rayB != null) rayB.Dispose();
			}
		}
	}
}