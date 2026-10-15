namespace E2e4.Chess.Fics.Protocol;

/// <summary>The unterminated prompt strings a FICS server sends; the tokenizer recognises them and the client reacts to them.</summary>
internal static class FicsPrompts
{
    public const string Login = "login: ";
    public const string Password = "password: ";
    public const string Command = "fics% ";
}
