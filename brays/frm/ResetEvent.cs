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
			tcs = new TaskCompletionSource<int>();
			IsAutoResetAllowed = isAutoResetAllowed;
		}

		/// <summary>
		/// Waits for the first of either a Set() call or a timeout.
		/// </summary>
		/// <param name="timoutMS">Negative is infinite.</param>
		/// <param name="autoreset">On timeout calls Reset() and awaits again.</param>
		/// <returns>The Set() value or -1 if timeouts.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<int> Wait(int timoutMS = -1, bool autoreset = true)
		{
			var ts = timoutMS > -1 ?
				TimeSpan.FromMilliseconds(timoutMS) :
				Timeout.InfiniteTimeSpan;

			return Wait(ts, autoreset);
		}

		/// <summary>
		/// Waits for either a Set() call or a timeout.
		/// </summary>
		/// <param name="timeout">The timespan before Set(false).</param>
		/// <returns>The Set() value or -1 if timeouts.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public Task<int> Wait(TimeSpan timeout, bool autoreset = true)
		{
			if (timeout != Timeout.InfiniteTimeSpan)
				System.Threading.Tasks.Task.Delay(timeout).ContinueWith((t, o) =>
				{
					// [!] Work only with the original TCS
					var tcso = o as TaskCompletionSource<int>;

					if (autoreset && IsAutoResetAllowed)
					{
						var otcs = Interlocked.CompareExchange(ref tcs, new TaskCompletionSource<int>(), tcso);
						if (otcs == tcso) otcs.TrySetResult(-1);
					}
					else tcso.TrySetResult(-1);
				}, tcs);

			return Task;
		}

		/// <summary>
		/// Signals the completion of the task.
		/// </summary>
		/// <param name="state">The result state. Default is 1.</param>
		/// <param name="autoreset">Sets the ResetEvent to wait again.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Set(int state = 1, bool autoreset = true)
		{
			if (autoreset && IsAutoResetAllowed)
			{
				var otcs = Interlocked.Exchange(ref tcs, new TaskCompletionSource<int>());
				otcs.TrySetResult(state);

			}
			else Volatile.Read(ref tcs).TrySetResult(state);
		}

		/// <summary>
		/// Signals the completion of the task.
		/// </summary>
		/// <param name="state">The result state. Default is 1.</param>
		/// <param name="autoreset">Sets the ResetEvent to wait again.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Set(bool state, bool autoreset = true) => Set(state ? 1 : 0, autoreset);

		/// <summary>
		/// Creates a new TaskCompletionSource to await.
		/// </summary>
		/// <param name="setPrevState">A state to try resolving the previous task with.</param>
		public void Reset(int? setPrevState)
		{
			var otcs = Interlocked.Exchange(ref tcs, new TaskCompletionSource<int>());
			if (setPrevState.HasValue) otcs.TrySetResult(setPrevState.Value);
		}

		public Task<int> Task => Volatile.Read(ref tcs).Task;
		TaskCompletionSource<int> tcs;
		readonly bool IsAutoResetAllowed;
	}
}
