using System;
using System.Threading;

namespace brays
{
	public class TakeArray<T> where T : class
	{
		public TakeArray(int length)
		{
			if (length < 1) throw new ArgumentOutOfRangeException();

			Length = length;
			array = new T[length];
		}

		public T this[int index]
		{
			get => Volatile.Read(ref array[index]);
			set => Volatile.Write(ref array[index], value);
		}

		public T[] Take(bool share = true) =>
			share ? Interlocked.CompareExchange(ref array, new T[Length], array) :
			Interlocked.Exchange(ref array, new T[Length]);


		public int Length;
		T[] array;
	}
}
