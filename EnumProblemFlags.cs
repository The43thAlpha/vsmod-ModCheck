namespace ModCheck
{
    internal enum EnumProblemFlags
    {
        None = 0,

        /// <summary>Offset: 0</summary>
        UnrecognizedModId = 1,

        /// <summary>Offset: 1</summary>
        UnrecognizedVersion = 2,

        /// <summary>Offset: 2</summary>
        UnrecognizedSourceType = 4,

        /// <summary>Offset: 3</summary>
        UnrecognizedFingerprint = 8,

        /// <summary>Offset: 4</summary>
        EmptyReports = 16,

        All = UnrecognizedModId | UnrecognizedVersion | UnrecognizedSourceType | UnrecognizedFingerprint | EmptyReports,
    }
}
