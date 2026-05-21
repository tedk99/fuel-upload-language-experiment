using System;

namespace FuelUploadEngine.Services
{
    // Thrown by the validator when a row fails. We use an exception because
    // the .Validate method "just feels cleaner" if it can throw and bubble
    // up, instead of returning a list of errors.
    public class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
    }
}
