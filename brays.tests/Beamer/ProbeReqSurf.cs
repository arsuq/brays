﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class ProbeReqSurf : ITestSurface
	{
		public string Info => "Tests the Beamer probing.";
		public string Tags => "beamer";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Start(IDictionary<string, List<string>> args)
		{
			Beamer a = null;
			Beamer b = null;

			try
			{
				var targ = new TestArgs(args);
				var s = targ.AE;
				var t = targ.BE;

				a = new Beamer((_) => { }, new BeamerCfg()
				{
					EnableProbes = true,
					ProbeFreqMS = 400
				});

				b = new Beamer((_) => { }, new BeamerCfg()
				{
					EnableProbes = true,
					ProbeFreqMS = 100
				});

				a.LockOn(s, t);
				b.LockOn(t, s);

				await Task.Delay(2000);

				var ap = DateTime.Now.Subtract(b.LastProbe).TotalMilliseconds;
				var bp = DateTime.Now.Subtract(a.LastProbe).TotalMilliseconds;

				if (ap > 500)
				{
					FailureMessage = $"Expected at least one probe in the {a.Config.ProbeFreqMS} interval";
					Passed = false;
					return;
				}

				if (bp > 150)
				{
					FailureMessage = $"Expected at least one probe in the {b.Config.ProbeFreqMS} interval";
					Passed = false;
					return;
				}

				"OK: Probing loop.".AsSuccess();

				// Stop the probing
				Volatile.Write(ref a.Config.EnableProbes, false);
				Volatile.Write(ref b.Config.EnableProbes, false);

				await Task.Delay(2000);

				ap = DateTime.Now.Subtract(b.LastProbe).TotalMilliseconds;
				bp = DateTime.Now.Subtract(a.LastProbe).TotalMilliseconds;

				if (ap < 1000 || bp < 1000)
				{
					FailureMessage = $"The probing should have stopped";
					Passed = false;
					return;
				}

				"OK: Stopping the probing loop.".AsSuccess();

				var apr = a.Probe();

				if (!apr)
				{
					FailureMessage = $"Probe request failed";
					Passed = false;
					return;
				}


				"OK: Probe request.".AsSuccess();

				b.Dispose();

				apr = a.Probe(300);

				if (apr)
				{
					FailureMessage = $"A probe request succeeded after the remote Beamer was disposed.";
					Passed = false;
					return;
				}

				Task.Delay(1500).ContinueWith((_) =>
				{
					b = new Beamer((f) => { }, new BeamerCfg());
					b.LockOn(t, s);
				});

				Task.Run(() =>
				{
					if (!a.TargetIsActive(8000).Result)
					{
						Passed = false;
						FailureMessage = "Second TargetIsActive() await failed";
					}
					else "OK: Second TargetIsActive() await.".AsSuccess();
				});

				apr = await a.TargetIsActive(8000);

				if (!apr)
				{
					FailureMessage = "Awaiting for target to become active with a probe failed!";
					Passed = false;
					return;
				}

				"OK: Awaiting for target to become active with a probe.".AsSuccess();

				await Task.Delay(300);

				Passed = true;
				IsComplete = true;
			}
			catch (Exception ex)
			{
				Passed = false;
				FailureMessage = ex.Message;
			}
			finally
			{
				a.Dispose();
				b.Dispose();
			}
		}
	}
}
