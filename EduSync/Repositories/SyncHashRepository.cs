using Dapper;
using EduSync.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EduSync.Repositories
{
    public class SyncHashRepository
    {
        private readonly TargetDbConnectionFactory _factory;

        public SyncHashRepository(
            TargetDbConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<string?> GetHashAsync(
    string tableName,
    string sourceKey)
        {
            using var conn = _factory.Create();

            return await conn.QueryFirstOrDefaultAsync<string>(
                @"
                SELECT DataHash
                FROM SyncHashes
                WHERE TableName = @TableName
                AND SourceKey = @SourceKey
                ",
                new
                {
                    TableName = tableName,
                    SourceKey = sourceKey
                });
        }

        public async Task InsertHashAsync(string tableName, int sourceId, string hash)
        {
            using var conn = _factory.Create();

            await conn.ExecuteAsync(
                @"
            INSERT INTO SyncHashes
            (
                TableName,
                SourceId,
                DataHash,
                LastSyncTime
            )
            VALUES
            (
                @TableName,
                @SourceId,
                @DataHash,
                GETDATE()
            )
            ",
                new
                {
                    TableName = tableName,
                    SourceId = sourceId,
                    DataHash = hash
                });
        }

        public async Task UpdateHashAsync(string tableName, int sourceId, string hash)
        {
            using var conn = _factory.Create();

            await conn.ExecuteAsync(
                @"
            UPDATE SyncHashes
            SET
                DataHash = @DataHash,
                LastSyncTime = GETDATE()
            WHERE TableName = @TableName
              AND SourceId = @SourceId
            ",
                new
                {
                    TableName = tableName,
                    SourceId = sourceId,
                    DataHash = hash
                });
        }

        public async Task SaveHashAsync(
    string tableName,
    string sourceKey,
    string hash)
        {
            using var conn = _factory.Create();

            await conn.ExecuteAsync(
            @"
            MERGE SyncHashes AS Target
            USING
            (
                SELECT
                    @TableName AS TableName,
                    @SourceKey AS SourceKey
            ) AS Source
            ON Target.TableName = Source.TableName
               AND Target.SourceKey = Source.SourceKey

            WHEN MATCHED THEN
                UPDATE SET
                    DataHash = @DataHash,
                    LastSyncTime = GETDATE()

            WHEN NOT MATCHED THEN
                INSERT
                (
                    TableName,
                    SourceKey,
                    DataHash,
                    LastSyncTime
                )
                VALUES
                (
                    @TableName,
                    @SourceKey,
                    @DataHash,
                    GETDATE()
                );
            ",
            new
            {
                TableName = tableName,
                SourceKey = sourceKey,
                DataHash = hash
            });
        }

    }
}
