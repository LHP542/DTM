namespace DTM
{
    public enum DatabaseStatus
    {
        down,
        up,
        transitional
    }
    public sealed record DatabaseInfo
    {
        public required string Name { get; init; }
        public required string Id { get; init; }
        public required string? FQDN { get; init; }
        public required DatabaseStatus Status { get; init; }

    }
}