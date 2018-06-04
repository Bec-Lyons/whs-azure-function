using System;

namespace WHSAzureFunction.Exceptions
{
    internal class NoFaceException : Exception
    {
        public NoFaceException()
        {
        }

        public NoFaceException(string message)
            : base(message)
        {
        }

        public NoFaceException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}