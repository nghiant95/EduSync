using Dapper;
using EduSync.Infrastructure;
using EduSync.Models;
using EduSync.Repositories;
using EduSync.Services.Interfaces;
using EduSync.Utils;
using Serilog;

namespace EduSync.Services;

public class CampusSyncService : ISyncService
{
    private readonly SourceDbConnectionFactory _sourceFactory;
    private readonly TargetDbConnectionFactory _targetFactory;
    private readonly SyncHashRepository _hashRepository;

    private const string TABLE_NAME = "Campuses";

    public CampusSyncService(
        SourceDbConnectionFactory sourceFactory,
        TargetDbConnectionFactory targetFactory,
        SyncHashRepository hashRepository)
    {
        _sourceFactory = sourceFactory;
        _targetFactory = targetFactory;
        _hashRepository = hashRepository;
    }

    public async Task SyncAsync()
    {
        Log.Information(
            "========== START SYNC " + TABLE_NAME + " ==========");

        int inserted = 0;
        int updated = 0;
        int skipped = 0;
        int errors = 0;

        using var sourceConn = _sourceFactory.Create();

        var campuses =
            await sourceConn.QueryAsync<SourceCampus>(
            @"
            SELECT
                MaCoSo,
                TenCoSo,
                DiaChiCS
            FROM CoSo
            ");

        foreach (var item in campuses)
        {
            try
            {
                var hash = BuildHash(item);

                var oldHash =
                    await _hashRepository.GetHashAsync(
                        TABLE_NAME,
                        item.MaCoSo);

                if (oldHash == null)
                {
                    var exists =
                        await ExistsCampusAsync(
                            item.MaCoSo);

                    if (exists)
                    {
                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaCoSo,
                            hash);

                        skipped++;

                        Log.Information(
                            "[HASH CREATED] " + TABLE_NAME + " {Code}",
                            item.MaCoSo);
                    }
                    else
                    {
                        await InsertCampus(item);

                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaCoSo,
                            hash);

                        inserted++;

                        Log.Information(
                            "[INSERT] " + TABLE_NAME + " {Code}",
                            item.MaCoSo);
                    }
                }
                else if (oldHash != hash)
                {
                    await UpdateCampus(item);

                    await _hashRepository.SaveHashAsync(
                        TABLE_NAME,
                        item.MaCoSo,
                        hash);

                    updated++;

                    Log.Information(
                        "[UPDATE] " + TABLE_NAME + " {Code}",
                        item.MaCoSo);
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex)
            {
                errors++;

                Log.Error(
                    ex,
                    "Error sync " + TABLE_NAME + " {Code}",
                    item.MaCoSo);
            }
        }

        Log.Information(TABLE_NAME + " Sync Finished. Inserted={Inserted}, Updated={Updated}, Skipped={Skipped}, Errors={Errors}",
            inserted,
            updated,
            skipped,
            errors);
    }

    private string BuildHash(SourceCampus item)
    {
        return HashHelper.Compute(
            $"{item.MaCoSo}|{item.TenCoSo}|{item.DiaChiCS}");
    }

    private async Task<bool> ExistsCampusAsync(
        string campusCode)
    {
        using var conn = _targetFactory.Create();

        var count =
            await conn.ExecuteScalarAsync<int>(
            @"
            SELECT COUNT(*)
            FROM " + TABLE_NAME + @"
            WHERE CampusCode = @CampusCode
            ",
            new
            {
                CampusCode = campusCode
            });

        return count > 0;
    }

    private async Task InsertCampus(
        SourceCampus item)
    {
        using var conn = _targetFactory.Create();

        await conn.ExecuteAsync(
        @"
        INSERT INTO " + TABLE_NAME + @"
        (
            CampusID,
            CampusName,
            CampusCode,
            Address,
            CreatedAt,
            CreatedBy,
            IsDeleted
        )
        VALUES
        (
            @CampusID,
            @CampusName,
            @CampusCode,
            @Address,
            @CreatedAt,
            @CreatedBy,
            @IsDeleted
        )
        ",
        new
        {
            CampusID = Guid.NewGuid(),

            CampusName = item.TenCoSo,

            CampusCode = item.MaCoSo,

            Address = item.DiaChiCS,

            CreatedAt = DateTime.Now,

            CreatedBy = "SyncSystem",

            IsDeleted = false
        });
    }

    private async Task UpdateCampus(
        SourceCampus item)
    {
        using var conn = _targetFactory.Create();

        await conn.ExecuteAsync(
        @"
        UPDATE " + TABLE_NAME + @"
        SET
            CampusName = @CampusName,
            Address = @Address,
            UpdatedAt = @UpdatedAt,
            UpdatedBy = @UpdatedBy
        WHERE CampusCode = @CampusCode
        ",
        new
        {
            CampusName = item.TenCoSo,

            Address = item.DiaChiCS,

            CampusCode = item.MaCoSo,

            UpdatedAt = DateTime.Now,

            UpdatedBy = "SyncSystem"
        });
    }
}