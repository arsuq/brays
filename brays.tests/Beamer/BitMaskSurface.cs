using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class BitMaskSurface : ITestSurface
	{
		public string Info => "Tests BitMask class.";
		public string Tags => "beamer, frm";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => true;

		public Task Start(IDictionary<string, List<string>> args)
		{
			try
			{
				var bm = new BitMask(156);
				var bm3 = new BitMask(56);

				bm[3] = true;
				bm[34] = true;
				bm[134] = true;
				bm3[2] = true;

				var T = bm.GetTiles();
				var s1 = bm.ToBinaryString();
				var s2 = bm.ToUintString();
				var s3 = bm.ToString();
				var s4 = bm3.ToString();


				var bm2 = new BitMask(T, bm.Count);
				bm.Or(bm2);

				Passed = true;
				IsComplete = true;
			}
			catch (Exception ex)
			{
				Passed = false;
				FailureMessage = ex.Message;
			}

			return Task.CompletedTask;
		}
	}
}
