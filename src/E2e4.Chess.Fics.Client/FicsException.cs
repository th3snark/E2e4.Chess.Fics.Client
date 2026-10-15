namespace E2e4.Chess.Fics.Client;

/// <summary>Raised for protocol-level failures such as a rejected login.</summary>
public sealed class FicsException : Exception
{
    public FicsException(string message)
        : base(message)
    {
    }

    public FicsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
