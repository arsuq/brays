﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class DroppingFramesSurface : ITestSurface
	{
		public string Info => "Tests beaming with frame drops.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
#if !DEBUG
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
					(f) => { Console.WriteLine("?"); },
					new BeamerCfg()
					{
						Log = new BeamerLogCfg("rayA", targ.Log),
#if DEBUG
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
					Log = new BeamerLogCfg("rayB", targ.Log),
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