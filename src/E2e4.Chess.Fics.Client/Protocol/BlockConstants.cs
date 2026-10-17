namespace E2e4.Chess.Fics.Protocol;

/// <summary>
/// Control characters framing "iset block 1" command responses:
/// BLOCK_START id BLOCK_SEPARATOR code BLOCK_SEPARATOR output BLOCK_END.
/// Server-initiated (posed) output uses BLOCK_POSE_START/END with id 0.
/// See FICS "help block".
/// </summary>
internal static class BlockConstants
{
    public const byte BlockStart = 0x15;     // 21
    public const byte BlockSeparator = 0x16; // 22
    public const byte BlockEnd = 0x17;       // 23
    public const byte BlockPoseStart = 0x18; // 24
    public const byte BlockPoseEnd = 0x19;   // 25
}

/// <summary>
/// Command/error codes carried in a block frame (FICS "help block_codes").
/// Only the identifiers the library reasons about are named; any other code
/// passes through as its raw integer value.
/// </summary>
public static class BlockCodes
{
    public const int BlkNull = 0;
    public const int BlkGameMove = 1;
    public const int BlkAbort = 2;

    /// <summary>Codes at or above this value are errors.</summary>
    public const int FirstErrorCode = 512;

    public const int BlkErrorBadCommand = 512;
    public const int BlkErrorBadParams = 513;
    public const int BlkErrorAmbiguous = 514;
    public const int BlkErrorRights = 515;
    public const int BlkErrorObsolete = 516;
    public const int BlkErrorRemoved = 517;
    public const int BlkErrorNotPlaying = 518;
    public const int BlkErrorNoSequence = 519;
    public const int BlkErrorLength = 520;
}
