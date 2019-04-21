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
				var rayA = new RayEmitter((f) => { }, new EmitterCfg());
				var rayB = new RayEmitter((f) => { }, new EmitterCfg());

				var ta = new Task(() =>
				{
					rayA.LockOn(aep, bep);
				});

				var tb = new Task(() =>
				{
					rayB.LockOn(bep, aep);
				});

				ta.Start();
				tb.Start();

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
