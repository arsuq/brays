using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TestSurface;
using brays;
using System.Net;

namespace brays.tests
{
	class RayEmitterBasicSurface : ITestSurface
	{
		public string Info => "Test the RayEmitter fragment beamer.";

		public string FailureMessage { get; private set; }

		public bool? Passed { get; private set; }

		public bool IsComplete { get; private set; }

		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			try
			{
				var aep = new IPEndPoint(IPAddress.Loopback, 3000);
				var bep = new IPEndPoint(IPAddress.Loopback, 4000);
				var rayA = new RayEmitter((f) => { Console.WriteLine(f.Span()[0]); }, new EmitterCfg());
				var rayB = new RayEmitter((f) => { Console.WriteLine(f.Span()[0]); }, new EmitterCfg());

				using (var hw = new HeapHighway(50))
				{
					var ta = new Task(async () =>
					{
						rayA.LockOn(aep, bep);

						using (var f = hw.Alloc(1))
						{
							f.Write(true, 0);
							await rayA.Beam(f);
						}
					});

					var tb = new Task(() =>
					{
						rayB.LockOn(bep, aep);
					});

					ta.Start();
					tb.Start();

					Task.WaitAll(ta, tb);
				}

				Console.ReadLine();
				Passed = true;
				IsComplete = true;
			}
			catch (Exception ex)
			{
				FailureMessage = ex.Message;
				Passed = false;
			}

			await Task.Delay(0);
		}
	}
}
