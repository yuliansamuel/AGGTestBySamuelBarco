using MySqlConnector;
using System.Data;
using System.Data.Common;

namespace Agg.Test.Infrastructure
{
    /// <summary>
    /// Provides a lightweight abstraction for executing stored procedures
    /// and database operations against a MySQL database using <see cref="MySqlConnector"/>.
    /// </summary>
    /// <remarks>
    /// This class is registered as a singleton and provides:
    /// <list type="bullet">
    /// <item><description>Execution of stored procedures with or without parameters.</description></item>
    /// <item><description>Query mapping from <see cref="DbDataReader"/> to strongly typed models.</description></item>
    /// <item><description>Execution of stored procedures that consume and return JSON payloads.</description></item>
    /// </list>
    /// Connection string and timeout values are read from <c>appsettings.json</c> under the <c>Database</c> section.
    /// </remarks>
    public sealed class MySqlDbExecutor : IDbExecutor
    {
        private readonly string _cs;
        private readonly int _timeout;
        private readonly ILogger<MySqlDbExecutor> _log;

        public MySqlDbExecutor(IConfiguration cfg, ILogger<MySqlDbExecutor> log)
        {
            _cs = cfg.GetSection("Database:ConnectionString").Get<string>()
                  ?? throw new InvalidOperationException("Database:ConnectionString not found");
            _timeout = cfg.GetValue<int?>("Database:CommandTimeout") ?? 30;
            _log = log;
        }

        private MySqlConnection NewConn() => new MySqlConnection(_cs);

        /// <summary>
        /// Executes a stored procedure asynchronously without returning a result set.
        /// </summary>
        /// <param name="spName">The name of the stored procedure to execute.</param>
        /// <param name="parameters">Optional list of parameters to pass to the stored procedure.</param>
        /// <param name="ct">Cancellation token for the asynchronous operation.</param>
        /// <returns>The number of affected rows.</returns
        public async Task<int> ExecSpAsync(string spName, IEnumerable<DbParameter>? parameters = null, CancellationToken ct = default)
        {
            await using var conn = NewConn();
            await conn.OpenAsync(ct);

            await using var cmd = new MySqlCommand(spName, conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = _timeout
            };

            if (parameters is not null)
                foreach (var p in parameters) cmd.Parameters.Add(p);

            return await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Executes a stored procedure asynchronously and maps each record in the result set
        /// to a strongly typed object using a provided mapping function.
        /// </summary>
        /// <typeparam name="T">The target type to map each database row to.</typeparam>
        /// <param name="spName">The name of the stored procedure to execute.</param>
        /// <param name="map">A delegate that maps a <see cref="DbDataReader"/> record to an object of type <typeparamref name="T"/>.</param>
        /// <param name="parameters">Optional list of parameters to pass to the stored procedure.</param>
        /// <param name="ct">Cancellation token for the asynchronous operation.</param>
        /// <returns>A read-only list of mapped objects of type <typeparamref name="T"/>.</returns>
        public async Task<IReadOnlyList<T>> QuerySpAsync<T>(
            string spName, Func<DbDataReader, T> map,
            IEnumerable<DbParameter>? parameters = null, CancellationToken ct = default)
        {
            var list = new List<T>();
            await using var conn = NewConn();
            await conn.OpenAsync(ct);

            await using var cmd = new MySqlCommand(spName, conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = _timeout
            };

            if (parameters is not null)
                foreach (var p in parameters) cmd.Parameters.Add(p);

            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            while (await rdr.ReadAsync(ct))
                list.Add(map(rdr));

            return list;
        }

        /// <summary>
        /// Executes a stored procedure that accepts a JSON input parameter and returns an output JSON result.
        /// </summary>
        /// <remarks>
        /// The stored procedure must define two parameters:
        /// <list type="bullet">
        /// <item><description><paramref name="jsonInName"/> — input JSON parameter (typically <c>LONGTEXT</c> or <c>JSON</c> type).</description></item>
        /// <item><description><paramref name="outName"/> — output parameter (typically <c>LONGTEXT</c> type for large JSON responses).</description></item>
        /// </list>
        /// </remarks>
        /// <param name="spName">The name of the stored procedure to execute.</param>
        /// <param name="json">The JSON string to send to the stored procedure.</param>
        /// <param name="jsonInName">The name of the input parameter (default: <c>@p_json</c>).</param>
        /// <param name="outName">The name of the output parameter (default: <c>@p_result</c>).</param>
        /// <param name="ct">Cancellation token for the asynchronous operation.</param>
        /// <returns>
        /// The JSON string returned by the stored procedure through the output parameter,
        /// or <c>null</c> if no output was returned.
        /// </returns>
        public async Task<string?> ExecJsonSpWithOutParamAsync(
            string spName,
            string json,
            string jsonInName = "@p_json",
            string outName = "@p_result",
            CancellationToken ct = default)
                {
                    await using var conn = NewConn();
                    await conn.OpenAsync(ct);

                    await using var cmd = new MySqlCommand(spName, conn)
                    {
                        CommandType = CommandType.StoredProcedure,
                        CommandTimeout = _timeout
                    };

                    var pJson = new MySqlParameter(jsonInName, MySqlDbType.JSON) { Value = json };

                    var pOut = new MySqlParameter(outName, MySqlDbType.LongText)
                    {
                        Direction = ParameterDirection.Output
                    };

                    cmd.Parameters.Add(pJson);
                    cmd.Parameters.Add(pOut);

                    await cmd.ExecuteNonQueryAsync(ct);

                    return pOut.Value as string;
        }
    }
}
