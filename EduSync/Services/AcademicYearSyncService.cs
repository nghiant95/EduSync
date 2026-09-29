using Dapper;
using EduSync.Infrastructure;
using EduSync.Models;
using EduSync.Repositories;
using EduSync.Services.Interfaces;
using EduSync.Utils;
using Serilog;

namespace EduSync.Services;

public class AcademicYearSyncService : ISyncService
{
    private readonly SourceDbConnectionFactory _sourceFactory;
    private readonly TargetDbConnectionFactory _targetFactory;
    private readonly SyncHashRepository _hashRepository;

    private const string TABLE_NAME = "AcademicYears";

    public AcademicYearSyncService(
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

        var academicYears = semesters
            .Select(x => GetAcademicYearCode(x.MaNHHK))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        foreach (var yearCode in academicYears)
        {
            try
            {
                var hash =
                    HashHelper.Compute(yearCode);

                var oldHash =
                    await _hashRepository.GetHashAsync(
                        TABLE_NAME,
                        yearCode);

                if (oldHash == null)
                {
                    var exists =
                        await ExistsAcademicYearAsync(
                            yearCode);

                    if (exists)
                    {
                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            yearCode,
                            hash);

                        skipped++;

                        Log.Information(
                            "[HASH CREATED] " + TABLE_NAME + " {YearCode}",
                            yearCode);
                    }
                    else
                    {
                        await InsertAcademicYear(
                            yearCode);

                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            yearCode,
                            hash);

                        inserted++;

                        Log.Information(
                            "[INSERT] " + TABLE_NAME + " {YearCode}",
                            yearCode);
                    }
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
                    "Error sync " + TABLE_NAME + " {YearCode}",
                    yearCode);
            }
        }

        Log.Information(TABLE_NAME + " Sync Finished. Inserted={Inserted}, Updated={Updated}, Skipped={Skipped}, Errors={Errors}",
            inserted,
            updated,
            skipped,
            errors);
    }

    private string GetAcademicYearCode(
        string maNHHK)
    {
        var year =
            int.Parse(
                maNHHK.Substring(0, 4));

        return $"{year}-{year + 1}";
    }

    private async Task<bool> ExistsAcademicYearAsync(
        string yearCode)
    {
        using var conn =
            _targetFactory.Create();

        var count =
            await conn.ExecuteScalarAsync<int>(
            @"
            SELECT COUNT(*)
            FROM " + TABLE_NAME + @"
            WHERE YearCode = @YearCode
            ",
            new
            {
                YearCode = yearCode
            });

        return count > 0;
    }

    private async Task InsertAcademicYear(
        string yearCode)
    {
        using var conn =
            _targetFactory.Create();

        await conn.ExecuteAsync(
        @"
        INSERT INTO " + TABLE_NAME + @"
        (
            YearID,
            YearName,
            YearCode,
            [Order],
            Activated,
            CreatedAt,
            CreatedBy,
            IsDeleted
        )
        VALUES
        (
            @YearID,
            @YearName,
            @YearCode,
            @Order,
            @Activated,
            @CreatedAt,
            @CreatedBy,
            @IsDeleted
        )
        ",
        new
        {
            YearID = Guid.NewGuid(),

            YearName = yearCode,

            YearCode = yearCode,

            Order = 0,

            Activated = true,

            CreatedAt = DateTime.Now,

            CreatedBy = "SyncSystem",

            IsDeleted = false
        });
    }
}