namespace NOVAxis.Database.Entities
{
    public class GuildRole
    {
        public ulong Id { get; set; }
        public string Name { get; set; }

        public virtual GuildInfo Guild { get; set; }
    }
}
