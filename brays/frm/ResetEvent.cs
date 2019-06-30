/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace brays
{
	/// <summary>
	/// An await-able ManualResetEvent.
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
		/// <param name="timeout">The timespan before Set(-1).</param>
		/// <returns>The Set() value or -1 if timeouts.</returns>
		public Task<int> Wait(TimeSpan timeout, bool autoreset = true)
		{
			// [!] Must use the same tcs instance for waiting and in the Timeout callback.
			var tcsr = Volatile.Read(ref tcs);

			if (timeout != Timeout.InfiniteTimeSpan)
				System.Threading.Tasks.Task.Delay(timeout).ContinueWith((t, o) =>
				{
					// [!] Work only with the original TCS
					var tcso = o as TaskCompletionSource<int>;

					// If not completed - reload another TCS.
					if (tcso.Task.Status != TaskStatus.RanToCompletion)
					{
						if (autoreset && IsAutoResetAllowed)
							Interlocked.CompareExchange(ref tcs, new TaskCompletionSource<int>(), tcso);

						tcso.TrySetResult(-1);
					}

				}, tcsr);

			return tcsr.Task;
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
				var o = Interlocked.Exchange(ref tcs, new TaskCompletionSource<int>());
				o.TrySetResult(state);
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
		/// <param name="setPrevState">A state to resolve the previous task with, if not completed.</param>
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
