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
