namespace OpenLimiter.Windows.Service;

public sealed class PolicyServiceUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
