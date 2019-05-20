using System;
using System.Threading;
using System.Threading.Tasks;

namespace brays
{
	public static class TaskEx
	{
		public static Task<bool> AsTask(this WaitHandle wh) =>
			AsTask(wh, Timeout.InfiniteTimeSpan);

		public static Task<bool> AsTask(this WaitHandle wh, int timeoutMS) =>
			AsTask(wh, TimeSpan.FromMilliseconds(timeoutMS));

		public static Task<bool> AsTask(this WaitHandle wh, TimeSpan timeout)
		{
			var tcs = new TaskCompletionSource<bool>();
			var reg = ThreadPool.RegisterWaitForSingleObject(wh, (state, timedOut) =>
			{
				var ltcs = (TaskCompletionSource<bool>)state;

				if (timedOut) ltcs.TrySetResult(false);
				else ltcs.TrySetResult(true);
			}, tcs, timeout, true);

			tcs.Task.ContinueWith((task, state) =>
				{
					var rwh = state as RegisteredWaitHandle;
					rwh?.Unregister(null);
				}, reg, TaskScheduler.Default);

			return tcs.Task;
		}
	}
}
