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
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			Beamer rayA = null;
			Beamer rayB = null;

			var rst = new ManualResetEvent(false);
			var rdm = new Random();
			var data = new byte[50_000_000];
			byte[] hash = null;

			rdm.NextBytes(data);

			using (MD5 h = MD5.Create()) hash = h.ComputeHash(data);

			try
			{
				var targ = new TestArgs(args);
				var aep = targ.AE;
				var bep = targ.BE;

				void receive(MemoryFragment f)
				{
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
						deopFramePercent = 20
#endif
					});

				if (targ.B)
					rayB = new Beamer(receive, new BeamerCfg()
					{
						Log = new BeamerLogCfg("rayB", targ.Log),
#if DEBUG
						dropFrames = true,
						deopFramePercent = 20
#endif
					});

				if (targ.A)
				{
					var ta = new Task(async () =>
					{
						rayA.LockOn(aep, bep);
						await rayA.TargetIsActive();

						if (!await rayA.Beam(new HeapSlot(data)))
						{
							FailureMessage = "Failed to beam the bits";
							Passed = false;
							rst.Set();
						}
						else "Beamed".AsInfo();
					});

					ta.Start();

					if (targ.B)
					{
						if (!rayB.LockOn(bep, aep)) $"Failed to lock on rayA".AsError();
					}
				}

				if (!rst.WaitOne(new TimeSpan(0, 2, 0)))
				{
					Passed = false;
					FailureMessage = "Timeout.";
				}

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
