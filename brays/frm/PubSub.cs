/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace brays
{
	/// <summary>
	/// Keeps a dictionary of events and their corresponding subscribers.
	/// </summary>
	/// <typeparam name="E">Event type</typeparam>
	/// <typeparam name="D">Data type for the subscriber Action</typeparam>
	public class PubSub<E, D>
	{
		public class Subscribers : List<Action<D>>
		{
			/// <summary>
			/// It useful only when TriggerSequentially is true.
			/// </summary>
			public void Randomize()
			{
				Action<D> tmp;
				var r = new Random();
				var idx = 0;

				for (int i = 0; i < Count; i++)
				{
					idx = r.Next(0, Count - 1);
					tmp = this[i];
					this[i] = this[idx];
					this[idx] = tmp;
				}
			}

			/// <summary>
			/// The default is false
			/// </summary>
			public bool TriggerSequentially;

			/// <summary>
			/// The default is true
			/// </summary>
			public bool IgnoreExceptions = true;

			/// <summary>
			/// The number of subscriber Actions to be triggered.
			/// By default is -1 which means all.
			/// </summary>
			public int TriggerCount = -1;

		}

		/// <summary>
		/// Registers new event
		/// </summary>
		/// <param name="e">The event</param>
		/// <returns>False if the event already exists</returns>
		public bool AddEvent(E e)
		{
			if (!Map.ContainsKey(e))
			{
				Map.Add(e, new Subscribers());
				return true;
			}
			else return false;
		}

		/// <summary>
		/// Appends new subscriber to the event. 
		/// </summary>
		/// <param name="e">The event</param>
		/// <param name="sub">The subscriber delegate</param>
		/// <returns>The subscribers list</returns>
		public Subscribers Subscribe(E e, Action<D> sub)
		{
			if (sub == null) throw new ArgumentNullException();

			AddEvent(e);

			var S = Map[e];
			S.Add(sub);

			return S;
		}

		/// <summary>
		/// Replaces the subscribers collection
		/// </summary>
		/// <param name="e">The event</param>
		/// <param name="subs">The subscribers list</param>
		public void Subscribe(E e, Subscribers subs)
		{
			if (Map.ContainsKey(e)) Map[e] = subs;
			else Map.Add(e, subs);
		}

		public void ClearSubscribers(E e)
		{
			if (Map.ContainsKey(e))
				Map[e].Clear();
		}

		public Subscribers this[E e]
		{
			get => GetSubscribers(e);
			set => Subscribe(e, value);
		}

		public Subscribers GetSubscribers(E e) => Map.ContainsKey(e) ? Map[e] : null;

		/// <summary>
		/// Invokes all subscribers for the event. 
		/// </summary>
		/// <remarks>
		/// By default all callbacks are triggered in parallel and the exceptions are ignored.
		/// Use subscribers' TriggerSequentially and IgnoreExceptions flags to change that mode.
		/// </remarks> 
		/// <param name="e">The event</param>
		/// <param name="data">The event data argument</param>
		/// <returns>A task to be awaited if the exceptions are not ignored</returns>
		public Task Publish(E e, D data)
		{
			if (!Map.ContainsKey(e)) return null;

			var C = Map[e];

			if (C == null) return null;

			return Task.Run(() =>
			{
				var L = C.TriggerCount < 0 ? C.Count : Math.Min(C.TriggerCount, C.Count);

				if (C.TriggerSequentially || L < 2)
				{
					if (C.IgnoreExceptions)
						for (int i = 0; i < L; i++)
							try { C[i](data); }
							catch { }
					else for (int i = 0; i < L; i++)
							C[i](data);
				}
				else
				{
					if (C.IgnoreExceptions)
						Parallel.For(0, L, (i) =>
						{
							try { C[i](data); }
							catch { }
						});
					else Parallel.For(0, L, (i) =>
						{
							C[i](data);
						});
				}
			});
		}

		Dictionary<E, Subscribers> Map = new Dictionary<E, Subscribers>();
	}
}
