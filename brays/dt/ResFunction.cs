/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Threading.Tasks;

namespace brays
{
	// [i] Instead of casting from object, this struct will keep 
	// both the synchronous and asynchronous callbacks.

	struct ResFunction
	{
		public ResFunction(Func<Exchange, Task> f)
		{
			Func = f;
			Action = null;
		}

		public ResFunction(Action<Exchange> a)
		{
			Func = null;
			Action = a;
		}

		public Func<Exchange, Task> Func;
		public Action<Exchange> Action;
	}
}
