using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace brays
{
	/// <summary>
	/// Use it like an await-able AutoResetEvent.
	/// </summary>
	public class ResetEvent
	{
		/// <summary>
		/// Creates a TaskCompletionSource.
		/// </summary>
		/// <param name="isAutoResetAllowed">
		/// If false, the autoreset argument in Wait() and Set() is ignored.</param>
		public ResetEvent(bool isAutoResetAllowed = true)
		{
			tcs = new TaskCompletionSource<bool>();
			IsAutoResetAllowed = isAutoResetAllowed;
		}

		/// <summary>
		/// Waits for either a Set() call or a timeout.
		/// </summary>
		/// <param name="timoutMS">Negative is infinite.</param>
		/// <param name="autoreset">On timeout calls Reset() and awaits again.</param>
		/// <returns>True if Set(true), false if Set(false) or timeout.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<bool> Wait(int timoutMS = -1, bool autoreset = true)
		{
			var ts = timoutMS > -1 ?
				TimeSpan.FromMilliseconds(timoutMS) :
				Timeout.InfiniteTimeSpan;

			return Wait(ts, autoreset);
		}

		/// <summary>
		/// Waits for either a Set() call or a timeout.
		/// </summary>
		/// <param name="timout">The timespan before Set(false).</param>
		/// <returns>True if Set(true), false if Set(false) or timeout.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<bool> Wait(TimeSpan timout, bool autoreset = true)
		{
			if (timout != Timeout.InfiniteTimeSpan)
				System.Threading.Tasks.Task.Run(async () =>
				{
					await System.Threading.Tasks.Task.Delay(timout);
					if (tcs.TrySetResult(false) && autoreset && IsAutoResetAllowed) Reset();
				});

			return Task;
		}

		/// <summary>
		/// Signals the completion of the task,
		/// </summary>
		/// <param name="state">The result state.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Set(bool state = true, bool autoreset = true)
		{
			if (tcs.TrySetResult(state) && autoreset && IsAutoResetAllowed) Reset();
		}

		/// <summary>
		/// Creates a new TaskCompletionSource to await.
		/// </summary>
		public void Reset() => Interlocked.Exchange(ref tcs, new TaskCompletionSource<bool>());

		public Task<bool> Task => tcs.Task;
		TaskCompletionSource<bool> tcs;
		readonly bool IsAutoResetAllowed;
	}
}
