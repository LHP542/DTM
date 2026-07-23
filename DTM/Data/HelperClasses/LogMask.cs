namespace DTM;

internal static class LogMask
{
    private static readonly System.Text.RegularExpressions.Regex _pwd =
        new(@"(PWD|Password)\s*=[^;]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    internal static string MaskConnectionString(string cs) =>
        string.IsNullOrEmpty(cs) ? cs : _pwd.Replace(cs, "$1=***");
}
