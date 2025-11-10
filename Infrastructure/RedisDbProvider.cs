namespace Agg.Test.Infrastructure
{
    using Microsoft.Extensions.Configuration;
    using StackExchange.Redis;
    public interface IRedisDbProvider
    {
        IDatabase Db { get; }
    }

    public sealed class RedisDbProvider : IRedisDbProvider
    {
        private readonly IConnectionMultiplexer _mux;
        private readonly int _dbIdx;

        public RedisDbProvider(IConnectionMultiplexer mux, IConfiguration cfg)
        {
            _mux = mux;
            _dbIdx = cfg.GetValue<int>("Redis:DefaultDb", 0);
        }

        public IDatabase Db => _mux.GetDatabase(_dbIdx);
    }
}
