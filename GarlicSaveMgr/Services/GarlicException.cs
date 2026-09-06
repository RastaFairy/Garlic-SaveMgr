namespace GarlicSaveMgr.Services;

public sealed class GarlicException : Exception
{
    public GarlicException(string message) : base(message) { }
    public GarlicException(string message, Exception inner) : base(message, inner) { }
}
