using System;

namespace WHSAzureFunction.Exceptions
{
    internal class UnrecognisedFaceException : Exception
    {
        public UnrecognisedFaceException()
        {
        }

        public UnrecognisedFaceException(string message)
            : base(message)
        {
        }

        public UnrecognisedFaceException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}