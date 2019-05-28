using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class ResetEventSurface : ITestSurface
	{
		public string Info => "Test the ResetEvent class.";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => true;

		public async Task Run(IDictionary<string, List<string>> args)
		{
			//autoReset();
			//noReset();
			autoResetCompetition();
		}

		void autoReset()
		{
			var rst = new ResetEvent();

			var retries = retry(rst, 10);
			ThreadPool.QueueUserWorkItem((r) =>
			{
				Thread.Sleep(251);
				r.Set(true);
			}, rst, true);

			retries.Wait();
			$"autoReset retries: {retries.Result} [Expect > 0]".AsInfo();
		}

		void noReset()
		{
			var rst = new ResetEvent(false);

			var retries = retry(rst, 300);
			ThreadPool.QueueUserWorkItem((r) =>
			{
				Thread.Sleep(10);
				r.Set(true, false);
			}, rst, true);

			retries.Wait();
			$"noReset retries: {retries.Result} [Expect 0]".AsInfo();
		}

		void autoResetCompetition()
		{
			var rst = new ResetEvent();
			var retries = retry(rst, 10);

			for (int i = 0; i < 20; i++)
				ThreadPool.QueueUserWorkItem((r) =>
				{
					Thread.Sleep(10000);
					r.Set(64);
				}, rst, true);

			for (int i = 0; i < 20; i++)
				ThreadPool.QueueUserWorkItem((r) =>
				{
					Thread.Sleep(10000);
					r.Set(32);
				}, rst, true);

			for (int i = 0; i < 20; i++)
				ThreadPool.QueueUserWorkItem((r) =>
				{
					Thread.Sleep(10000);
					r.Set(16);
				}, rst, true);

			var rs = retries.Result.s;

			if (rs != 16 && rs != 32 && rs != 64)
			{
				Passed = false;
				FailureMessage = $"autoResetCompetition fails with mutated state: {rs}";
			} 
			else $"autoReset retries: {retries.Result.r} reset value: {rs}".AsInfo();
		}

		async Task<(int s, int r)> retry(ResetEvent rst, int awaitMS)
		{
			var retries = 0;
			var state = -1;

			while (true)
			{
				state = await rst.Wait(awaitMS);
				if (state < 1) retries++;
				else break;
			}

			return (s: state, retries);
		}
	}
}