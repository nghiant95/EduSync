using Dapper;
using EduSync.Infrastructure;
using EduSync.Models;
using EduSync.Repositories;
using EduSync.Services.Interfaces;
using EduSync.Utils;
using Serilog;

namespace EduSync.Services;

public class SemesterSyncService : ISyncService
{
    private readonly SourceDbConnectionFactory _sourceFactory;
    private readonly TargetDbConnectionFactory _targetFactory;
    private readonly SyncHashRepository _hashRepository;

    private const string TABLE_NAME = "Semesters";

    public SemesterSyncService(
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

        var semesters =
            await sourceConn.QueryAsync<SourceSemester>(
            @"
            SELECT
                NHHK,
                MaNHHK,
                TenNHHK,
                TenNHHKCongBo
            FROM DMNHHK
            ");

        foreach (var item in semesters)
        {
            try
            {
                var hash = BuildHash(item);

                var oldHash =
                    await _hashRepository.GetHashAsync(
                        TABLE_NAME,
                        item.MaNHHK);

                if (oldHash == null)
                {
                    var exists =
                        await ExistsSemesterAsync(
                            item.MaNHHK);

                    if (exists)
                    {
                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaNHHK,
                            hash);

                        skipped++;
                    }
                    else
                    {
                        await InsertSemester(item);

                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaNHHK,
                            hash);

                        inserted++;
                    }
                }
                else if (oldHash != hash)
                {
                    await UpdateSemester(item);

                    await _hashRepository.SaveHashAsync(
                        TABLE_NAME,
                        item.MaNHHK,
                        hash);

                    updated++;
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
                    item.MaNHHK);
            }
        }

        Log.Information(TABLE_NAME + " Sync Finished. Inserted={Inserted}, Updated={Updated}, Skipped={Skipped}, Errors={Errors}",
            inserted,
            updated,
            skipped,
            errors);
    }

    private string BuildHash(
        SourceSemester item)
    {
        return HashHelper.Compute(
            $"{item.MaNHHK}|{item.TenNHHK}|{GetAcademicYearCode(item.MaNHHK)}");
    }

    private int GetSemesterOrder(
        string maNHHK)
    {
        if (string.IsNullOrWhiteSpace(maNHHK))
            return 0;

        var lastChar =
            maNHHK.Trim().Last();

        return int.TryParse(
            lastChar.ToString(),
            out var value)
            ? value
            : 0;
    }

    private async Task<bool> ExistsSemesterAsync(
        string semesterCode)
    {
        using var conn = _targetFactory.Create();

        var count =
            await conn.ExecuteScalarAsync<int>(
            @"
            SELECT COUNT(*)
            FROM " + TABLE_NAME + @"
            WHERE SemesterCode = @SemesterCode
            ",
            new
            {
                SemesterCode = semesterCode
            });

        return count > 0;
    }

    private async Task InsertSemester(
        SourceSemester item)
    {
        using var conn = _targetFactory.Create();

        var yearId = await GetYearIdAsync(item.MaNHHK);

        await conn.ExecuteAsync(
        @"
        INSERT INTO " + TABLE_NAME + @"
        (
            SemesterID,
            SemesterName,
            SemesterCode,
            SemesterOrder,
            Activated,
            YearID,
            CreatedAt,
            CreatedBy,
            IsDeleted
        )
        VALUES
        (
            @SemesterID,
            @SemesterName,
            @SemesterCode,
            @SemesterOrder,
            @Activated,
            @YearID,
            @CreatedAt,
            @CreatedBy,
            @IsDeleted
        )
        ",
        new
        {
            SemesterID = Guid.NewGuid(),

            SemesterName = item.TenNHHK,

            SemesterCode = item.MaNHHK,

            SemesterOrder =
                GetSemesterOrder(
                    item.MaNHHK),

            Activated = true,

            YearID = yearId,

            CreatedAt = DateTime.Now,

            CreatedBy = "SyncSystem",

            IsDeleted = false
        });
    }

    private async Task UpdateSemester(
        SourceSemester item)
    {
        using var conn = _targetFactory.Create();

        var yearId =
    await GetYearIdAsync(
        item.MaNHHK);

        await conn.ExecuteAsync(
        @"
        UPDATE " + TABLE_NAME + @"
        SET
            SemesterName = @SemesterName,
            SemesterOrder = @SemesterOrder,
            Activated = @Activated,
            YearID = @YearID,
            UpdatedAt = @UpdatedAt,
            UpdatedBy = @UpdatedBy
        WHERE SemesterCode = @SemesterCode
        ",
        new
        {
            SemesterName = item.TenNHHK,

            SemesterCode = item.MaNHHK,

            SemesterOrder =
                GetSemesterOrder(
                    item.MaNHHK),

            Activated = true,

            YearID = yearId,

            UpdatedAt = DateTime.Now,

            UpdatedBy = "SyncSystem"
        });
    }

    private string GetAcademicYearCode(
    string maNHHK)
    {
        var year =
            int.Parse(
                maNHHK.Substring(0, 4));

        return $"{year}-{year + 1}";
    }

    private async Task<Guid?> GetYearIdAsync(
    string maNHHK)
    {
        var yearCode =
            GetAcademicYearCode(
                maNHHK);

        using var conn =
            _targetFactory.Create();

        return await conn
            .QueryFirstOrDefaultAsync<Guid?>(
            @"
        SELECT YearID
        FROM AcademicYears
        WHERE YearCode = @YearCode
        ",
            new
            {
                YearCode = yearCode
            });
    }
}