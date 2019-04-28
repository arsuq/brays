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
