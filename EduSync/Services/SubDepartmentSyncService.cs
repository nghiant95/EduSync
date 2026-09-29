using Dapper;
using EduSync.Infrastructure;
using EduSync.Models;
using EduSync.Repositories;
using EduSync.Services.Interfaces;
using EduSync.Utils;
using Serilog;

namespace EduSync.Services;

public class SubDepartmentSyncService : ISyncService
{
    private readonly SourceDbConnectionFactory _sourceFactory;
    private readonly TargetDbConnectionFactory _targetFactory;
    private readonly SyncHashRepository _hashRepository;

    private const string TABLE_NAME = "[Departments.Subs]";

    public SubDepartmentSyncService(
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
        Log.Information("========== START SYNC " + TABLE_NAME + " ==========");

        int inserted = 0;
        int updated = 0;
        int skipped = 0;
        int errors = 0;

        using var sourceConn = _sourceFactory.Create();

        var boMons =
            await sourceConn.QueryAsync<SourceBoMon>(
            @"
            SELECT
                MaBM,
                MaKH,
                TenBM,
                TruongBM
            FROM BoMon
            ");

        foreach (var item in boMons)
        {
            try
            {
                var hash = BuildHash(item);

                var oldHash =
                    await _hashRepository.GetHashAsync(
                        TABLE_NAME,
                        item.MaBM);

                var departmentId =
                    await GetDepartmentIdAsync(item.MaKH);

                if (departmentId == null)
                {
                    Log.Warning(
                        "Department not found. MaKH={MaKH}, MaBM={MaBM}",
                        item.MaKH,
                        item.MaBM);

                    continue;
                }

                if (oldHash == null)
                {
                    var exists =
                        await ExistsSubDepartmentAsync(
                            item.MaBM);

                    if (exists)
                    {
                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaBM,
                            hash);

                        skipped++;

                        Log.Information(
                            "[HASH CREATED] " + TABLE_NAME + " {MaBM}",
                            item.MaBM);
                    }
                    else
                    {
                        await InsertSubDepartment(
                            item,
                            departmentId.Value);

                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaBM,
                            hash);

                        inserted++;

                        Log.Information(
                            "[INSERT] " + TABLE_NAME + " {MaBM}",
                            item.MaBM);
                    }
                }
                else if (oldHash != hash)
                {
                    await UpdateSubDepartment(
                        item,
                        departmentId.Value);

                    await _hashRepository.SaveHashAsync(
                        TABLE_NAME,
                        item.MaBM,
                        hash);

                    updated++;

                    Log.Information(
                        "[UPDATE] " + TABLE_NAME + " {MaBM}",
                        item.MaBM);
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
                    "Error sync " + TABLE_NAME + " {MaBM}",
                    item.MaBM);
            }
        }

        Log.Information(TABLE_NAME +
            " Sync Finished. Inserted={Inserted}, Updated={Updated}, Skipped={Skipped}, Errors={Errors}",
            inserted,
            updated,
            skipped,
            errors);
    }

    private string BuildHash(SourceBoMon item)
    {
        return HashHelper.Compute(
            $"{item.MaBM}|{item.MaKH}|{item.TenBM}");
    }

    private async Task<Guid?> GetDepartmentIdAsync(
        string maKH)
    {
        using var conn = _targetFactory.Create();

        return await conn.QueryFirstOrDefaultAsync<Guid?>(
            @"
            SELECT DepartmentID
            FROM Departments
            WHERE DepartmentCode = @DepartmentCode
            ",
            new
            {
                DepartmentCode = maKH
            });
    }

    private async Task<bool> ExistsSubDepartmentAsync(
        string maBM)
    {
        using var conn = _targetFactory.Create();

        var count = await conn.ExecuteScalarAsync<int>(
            @"
            SELECT COUNT(*)
            FROM " + TABLE_NAME + @"
            WHERE SubDepartmentCode = @Code
            ",
            new
            {
                Code = maBM
            });

        return count > 0;
    }

    private async Task InsertSubDepartment(
        SourceBoMon item,
        Guid departmentId)
    {
        using var conn = _targetFactory.Create();

        await conn.ExecuteAsync(
        @"
        INSERT INTO " + TABLE_NAME + @"
        (
            SubDepartmentID,
            SubDepartmentName,
            EnglishName,
            SubDepartmentShortName,
            SubDepartmentCode,
            Category,
            [Order],
            DepartmentID,
            CreatedAt,
            CreatedBy,
            IsDeleted
        )
        VALUES
        (
            @SubDepartmentID,
            @SubDepartmentName,
            @EnglishName,
            @SubDepartmentShortName,
            @SubDepartmentCode,
            @Category,
            @Order,
            @DepartmentID,
            @CreatedAt,
            @CreatedBy,
            @IsDeleted
        )
        ",
        new
        {
            SubDepartmentID = Guid.NewGuid(),
            SubDepartmentName = item.TenBM,
            EnglishName = item.TenBM,
            SubDepartmentShortName = item.MaBM,
            SubDepartmentCode = item.MaBM,
            Category = "Bộ môn",
            Order = 0,
            DepartmentID = departmentId,
            CreatedAt = DateTime.Now,
            CreatedBy = "SyncSystem",
            IsDeleted = false
        });
    }

    private async Task UpdateSubDepartment(
        SourceBoMon item,
        Guid departmentId)
    {
        using var conn = _targetFactory.Create();

        await conn.ExecuteAsync(
        @"
        UPDATE " + TABLE_NAME + @"
        SET
            SubDepartmentName = @SubDepartmentName,
            EnglishName = @EnglishName,
            SubDepartmentShortName = @SubDepartmentShortName,
            DepartmentID = @DepartmentID,
            UpdatedAt = @UpdatedAt,
            UpdatedBy = @UpdatedBy
        WHERE SubDepartmentCode = @SubDepartmentCode
        ",
        new
        {
            SubDepartmentName = item.TenBM,
            EnglishName = item.TenBM,
            SubDepartmentShortName = item.MaBM,
            SubDepartmentCode = item.MaBM,
            DepartmentID = departmentId,
            UpdatedAt = DateTime.Now,
            UpdatedBy = "SyncSystem"
        });
    }
}