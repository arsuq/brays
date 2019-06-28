using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class SuddenEndpointChange : ITestSurface
	{
		public string Info => "Tests abrupt endpoint update.";
		public string Tags => "xpu, ex";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Start(IDictionary<string, List<string>> args)
		{
#if !DEBUG
			return;
#endif
			const int DROPPED_FRAMES_PERCENT = 20;

			$"Will test abrupt port change on the source while dropping {DROPPED_FRAMES_PERCENT} percent of the tiles ".AsHelp();

			Beamer rayA = null;
			Beamer rayB = null;

			var rst = new ManualResetEvent(false);
			var rdm = new Random();
			var data = new byte[150_000_000];
			byte[] hash = null;

			rdm.NextBytes(data);

			using (MD5 h = MD5.Create()) hash = h.ComputeHash(data);

			try
			{
				var targ = new TestArgs(args);
				var aep = targ.AE;
				var bep = targ.BE;

				async Task receive(MemoryFragment f)
				{
					await Task.Yield();

					using (var fs = f.CreateStream())
					{
						using (MD5 h = MD5.Create())
						{
							var rhash = h.ComputeHash(fs);
							if (!Assert.SameValues(hash, rhash))
							{
								Passed = false;
								FailureMessage = "Hash difference";
							}
							else "Hash match".AsSuccess();

							rst.Set();
						}
					}
				}

				if (targ.A)
					rayA = new Beamer((f) => { },
					new BeamerCfg()
					{
						Log = new BeamerLogCfg("rayA", targ.Log),
#if DEBUG
						dropFrames = true,
						deopFramePercent = DROPPED_FRAMES_PERCENT
#endif
					});

				if (targ.B)
					rayB = new Beamer(receive, new BeamerCfg()
					{
						Log = new BeamerLogCfg("rayB", targ.Log),
#if DEBUG
						dropFrames = true,
						deopFramePercent = DROPPED_FRAMES_PERCENT
#endif
					});

				if (targ.A)
				{
					var ta = new Task(async () =>
					{
						await rayA.LockOn(aep, bep, -1);

						Task.Delay(50).ContinueWith((t) =>
						{
							try
							{
								rayA.Source.Port = 9999;
								if (!rayA.ConfigPush().Result)
								{
									Passed = false;
									FailureMessage = "Faiiled to push the updated config";
									rst.Set();
								}
								else
								{
									rayA.LockOn(rayA.Source, rayA.Target);
									$"The source port has changed and the beamer locked on {rayA.Source.ToString()}".AsWarn();
								}
							}
							catch (Exception ex)
							{
								Passed = false;
								FailureMessage = ex.ToString();
								rst.Set();
							}
						});

						if (!await rayA.Beam(new HeapSlot(data)))
						{
							FailureMessage = "Failed to beam the bits";
							Passed = false;
							rst.Set();
						}
						else $"{data.Length / 1000} KB beamed".AsInfo();
					});

					ta.Start();
				}

				if (targ.B)
				{
					if (!rayB.LockOn(bep, aep)) $"Failed to lock on rayA".AsError();
				}

				if (!rst.WaitOne(new TimeSpan(0, 2, 0)))
				{
					Passed = false;
					FailureMessage = "Timeout.";
				}

				if (!Passed.HasValue)
				{
					Passed = true;
					IsComplete = true;
				}
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
