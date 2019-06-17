﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class OneByteSurface : ITestSurface
	{
		public string Info => "Test the smallest possible transfer.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			Beamer rayA = null;
			Beamer rayB = null;

			try
			{
				var rst = new ManualResetEvent(false);
				var aep = new IPEndPoint(IPAddress.Loopback, 3000);
				var bep = new IPEndPoint(IPAddress.Loopback, 4000);
				rayA = new Beamer((f) => { Console.WriteLine("?"); },
					new BeamerCfg() { Log = new BeamerLogCfg("rayA", true) });
				rayB = new Beamer((f) =>
				{
					if (f.Span()[0] == 77)
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

				}, new BeamerCfg() { Log = new BeamerLogCfg("rayB", true) });

				using (var hw = new HeapHighway(50))
				{
					var ta = new Task(async () =>
					{
						rayA.LockOn(aep, bep).Wait();

						var f = hw.Alloc(1);

						f.Write((byte)77, 0);
						await rayA.Beam(f);
					});

					var tb = new Task(() =>
					{
						rayB.LockOn(bep, aep).Wait();
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