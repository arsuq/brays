/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;

namespace brays
{
	struct SignalAwait
	{
		public SignalAwait(Action<int> onSignal)
		{
			OnSignal = onSignal;
			Created = DateTime.Now;
		}

		public Action<int> OnSignal;
		public DateTime Created;
	}
}
