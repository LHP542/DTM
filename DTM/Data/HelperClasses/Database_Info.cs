namespace DTM
{
    public enum Database_Status
    {
        down,
        up,
        transitional
    }
    public sealed record Database_Info
    {
        public required string Name { get; init; }
        public required string Id { get; init; }
        public required string? FQDN { get; init; }
        public required Database_Status Status { get; init; }

    }
}