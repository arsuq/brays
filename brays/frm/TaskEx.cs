/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

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
			// [!!] Will deadlock on mutex as the same thread must release.

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
