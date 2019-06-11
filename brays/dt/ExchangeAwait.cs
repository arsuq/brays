using System;

namespace brays
{
	struct ExchangeAwait
	{
		public ExchangeAwait(Action<Exchange> onReferred)
		{
			OnReferred = onReferred;
			Created = DateTime.Now;
		}

		public Action<Exchange> OnReferred;
		public DateTime Created;
	}
}
