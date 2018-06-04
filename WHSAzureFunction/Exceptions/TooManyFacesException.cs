using System;

namespace WHSAzureFunction.Exceptions
{
    internal class TooManyFacesException : Exception
    {
        public TooManyFacesException()
        {
        }

        public TooManyFacesException(string message)
            : base(message)
        {
        }

        public TooManyFacesException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}