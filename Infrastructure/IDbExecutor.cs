using System.Data.Common;

namespace Agg.Test.Infrastructure
{
    public interface IDbExecutor
    {
        Task<int> ExecSpAsync(
            string spName,
            IEnumerable<DbParameter>? parameters = null,
            CancellationToken ct = default);

        Task<IReadOnlyList<T>> QuerySpAsync<T>(
            string spName,
            Func<DbDataReader, T> map,
            IEnumerable<DbParameter>? parameters = null,
            CancellationToken ct = default);

        Task<string?> ExecJsonSpWithOutParamAsync(
            string spName,
            string json,
            string jsonInName = "@p_json",
            string outName = "@p_result",
            CancellationToken ct = default);
    }
}
