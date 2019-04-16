using System;
using System.Threading;

namespace brays
{
	/// <summary>
	/// Allows one thread at a time to enter.
	/// </summary>
	public class Gate
	{
		public Gate(bool threadAffinity = false) => this.threadAffinity = threadAffinity;

		/// <summary>
		/// Only one thread can enter at a time.
		/// </summary>
		/// <returns>True if entered.</returns>
		public bool Enter()
		{
			if (Interlocked.CompareExchange(ref acq, 1, 0) < 1)
			{
				if (threadAffinity)
					threadID = Thread.CurrentThread.ManagedThreadId;

				return true;
			}
			else return false;
		}


		/// <summary>
		/// Leaves the gate. If the gate is initialized with threadAffinity
		/// and the calling thread is not the one which entered will throw an InvariantException.
		/// </summary>
		/// <exception cref="System.InvariantException">When created with thread affinity and 
		/// the thread that calls Exit() is not the same that entered. </exception>
		public void Exit()
		{
			if (threadAffinity && Thread.CurrentThread.ManagedThreadId != threadID)
				throw new InvariantException($"Only the thread with managed id {threadID} could leave the gate.");

			Interlocked.Exchange(ref acq, 0);
		}

		public int LastEnteredThread => threadID;

		bool threadAffinity;
		int threadID = -1;
		int acq;
	}
}
