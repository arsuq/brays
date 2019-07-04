using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class DroppingFramesSurface : ITestSurface
	{
		public string Info => "Tests beaming with frame drops.";
		public string Tags => "beamer, drops";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Start(IDictionary<string, List<string>> args)
		{
#if !DEBUG && !ASSERT
			return;
#endif
			Beamer rayA = null;
			Beamer rayB = null;

			const int MEG = 1_000_000;

			try
			{
				var targ = new TestArgs(args);
				var rst = new ManualResetEvent(false);
				var aep = targ.AE;
				var bep = targ.BE;

				rayA = new Beamer(
					(f) => { },
					new BeamerCfg()
					{
						Log = new BeamerLogCfg("rayA-DroppingFrames", targ.Log),
#if DEBUG || ASSERT
						dropFrames = true,
						deopFramePercent = 30
#endif
					});
				rayB = new Beamer((f) =>
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
				}, new BeamerCfg()
				{
					Log = new BeamerLogCfg("rayB-DroppingFrames", targ.Log),
#if DEBUG || ASSERT
					dropFrames = true,
					deopFramePercent = 30
#endif
				});

				using (var hw = new HeapHighway())
				{
					if ( rayB.LockOn(bep, aep) && rayA.LockOn(aep, bep) && await rayA.TargetIsActive())
					{
						var f = hw.Alloc(MEG);

						for (int i = 0; i < MEG; i++)
							f[i] = 43;

						await rayA.Beam(f);


						rst.WaitOne();
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
	}
}