using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class ConfigExchangeSurface : ITestSurface
	{
		public string Info => "Tests the config exchange.";
		public string Tags => "beamer, cfg";
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

			const int CFGA = 11;
			const int CFGB = 12;

			try
			{
				var targ = new TestArgs(args);
				var aep = targ.AE;
				var bep = targ.BE;

				rayA = new Beamer((f) => { }, new BeamerCfg()
				{
					UseTCP = targ.UseTCP,
					Log = new BeamerLogCfg("rayA-ConfigExchange", targ.Log),
					MaxBeamedTilesAtOnce = CFGA
#if DEBUG
					, dropFrames = true,
					deopFramePercent = 50
#endif
				});

				rayB = new Beamer((f) => { }, new BeamerCfg()
				{
					UseTCP = targ.UseTCP,
					Log = new BeamerLogCfg("rayB-ConfigExchange", targ.Log),
					MaxBeamedTilesAtOnce = CFGB
#if DEBUG
					, dropFrames = true,
					deopFramePercent = 50
#endif
				});

				var ta = new Task(async () =>
				{
					if (await rayA.LockOn(aep, bep) && await rayA.ConfigRequest())
					{
						// The remote config must be available here
						var tc = rayA.GetTargetConfig();
						if (tc == null || tc.MaxBeamedTilesAtOnce != CFGB)
						{
							Passed = false;
							FailureMessage = "The remote config B is not correct or is missing.";
						}
					}
				});

				var tb = new Task(async () =>
				{
					if (await rayB.LockOn(bep, aep) && await rayB.ConfigRequest())
					{
						var tc = rayB.GetTargetConfig();
						if (tc == null || tc.MaxBeamedTilesAtOnce != CFGA)
						{
							Passed = false;
							FailureMessage = "The remote config A is not correct or is missing.";
						}
					}
				});

				ta.Start();
				tb.Start();

				if (!Task.WaitAll(new Task[] { ta, tb }, new TimeSpan(0, 2, 0)))
				{
					Passed = false;
					FailureMessage = "Timeout.";
					FailureMessage.AsError();
				}

				await Task.Yield();

				if (Passed.HasValue && !Passed.Value) "configExchange() failed".AsError();
				else
				{
					"OK: configExchange()".AsSuccess();
					Passed = true;
				}
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