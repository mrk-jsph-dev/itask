using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ITask.Api.Common.Persistence;

/// <summary>
/// Runs work under a database-wide named lock, so only one replica does it at a time.
/// SQL Server uses <c>sp_getapplock</c> owned by a transaction; SQLite is single-process and runs
/// the work directly.
/// </summary>
public static class JobLock
{
    /// <summary>
    /// Runs <paramref name="work"/> inside a transaction holding the lock <paramref name="name"/>.
    /// Returns false without running it when another session holds the lock.
    /// </summary>
    public static async Task<bool> TryRunAsync(AppDbContext db, string name, Func<CancellationToken, Task> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(work);

        if (db.Database.IsSqlite())
        {
            await work(ct);
            return true;
        }

        // Retrying execution strategies need the transaction inside ExecuteAsync.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            if (!await TryAcquireAsync(db, transaction, name, token))
            {
                return false;
            }

            await work(token);
            await transaction.CommitAsync(token);
            return true;
        }, ct);
    }

    private static async Task<bool> TryAcquireAsync(AppDbContext db, IDbContextTransaction transaction, string name, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new SqlParameter("@Resource", $"itask:{name}"));
        command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
        command.Parameters.Add(new SqlParameter("@LockOwner", "Transaction"));
        command.Parameters.Add(new SqlParameter("@LockTimeout", 0));
        var result = new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
        command.Parameters.Add(result);

        await command.ExecuteNonQueryAsync(ct);

        // 0 = granted, 1 = granted after waiting; negative = timeout, deadlock or error.
        return (int)result.Value! >= 0;
    }
}
