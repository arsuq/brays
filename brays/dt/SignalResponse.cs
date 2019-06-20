/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;

namespace brays
{
	struct SignalResponse
	{
		public SignalResponse(int mark, bool isError = false)
		{
			IsError = isError;
			Checked = 0;
			Mark = mark;
			Created = DateTime.Now;
		}

		public int Checked;
		public int Mark;
		public bool IsError;
		public DateTime Created;
	}
}
