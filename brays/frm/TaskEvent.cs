using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace brays
{
	/// <summary>
	/// Use it like an await-able ManualResetEvent.
	/// </summary>
	public class TaskEvent
	{
		public TaskEvent()
		{
			tcs = new TaskCompletionSource<bool>();
			Task = tcs.Task;
		}

		/// <summary>
		/// Waits for either a Set() call or a timeout.
		/// </summary>
		/// <param name="timoutMS">Zero or negative is infinite.</param>
		/// <returns>True if Set(true), false if Set(false) or timeout.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<bool> Wait(int timoutMS)
		{
			var ts = timoutMS > 0 ?
				TimeSpan.FromMilliseconds(timoutMS) :
				Timeout.InfiniteTimeSpan;

			return Wait(ts);
		}

		/// <summary>
		/// Waits for either a Set() call or a timeout.
		/// </summary>
		/// <param name="timout">The timespan before Set(false).</param>
		/// <returns>True if Set(true), false if Set(false) or timeout.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<bool> Wait(TimeSpan timout)
		{
			if (timout != Timeout.InfiniteTimeSpan)
				System.Threading.Tasks.Task.Run(async () =>
				{
					await System.Threading.Tasks.Task.Delay(timout);
					tcs.TrySetResult(false);
				});

			return Task;
		}

		/// <summary>
		/// Signals the completion of the task,
		/// </summary>
		/// <param name="state">The result state.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Set(bool state = true) => tcs.TrySetResult(state);

		public readonly Task<bool> Task;
		readonly TaskCompletionSource<bool> tcs;
	}
}
