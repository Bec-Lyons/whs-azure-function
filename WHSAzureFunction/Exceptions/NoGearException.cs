using System;

namespace WHSAzureFunction.Exceptions
{
    internal class NoGearException : Exception
    {
        public NoGearException()
        {
        }

        public NoGearException(string message)
            : base(message)
        {
        }

        public NoGearException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}