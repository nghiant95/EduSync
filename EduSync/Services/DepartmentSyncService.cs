using Dapper;
using EduSync.Infrastructure;
using EduSync.Models;
using EduSync.Repositories;
using EduSync.Services.Interfaces;
using EduSync.Utils;
using Serilog;
using System.Collections.Generic;

namespace EduSync.Services
{
    public class DepartmentSyncService : ISyncService
    {
        private readonly SourceDbConnectionFactory _sourceFactory;
        private readonly TargetDbConnectionFactory _targetFactory;
        private readonly SyncHashRepository _hashRepository;

        private const string TABLE_NAME = "Departments";

        public DepartmentSyncService(
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

            using var sourceConn = _sourceFactory.Create();

            var khoas =
                await sourceConn.QueryAsync<SourceKhoa>(
                @"
            SELECT
                MaKH,
                TenKhoa
            FROM Khoa
            ");

            foreach (var khoa in khoas)
            {
                try
                {
                    var hash = BuildHash(khoa);

                    var oldHash =
                       await _hashRepository.GetHashAsync(
                                    TABLE_NAME,
                                    khoa.MaKH);

                    if (oldHash == null)
                    {
                        var exists =
                            await ExistsDepartmentAsync(
                                khoa.MaKH);

                        if (exists)
                        {
                            await _hashRepository.SaveHashAsync(
                                TABLE_NAME,
                                khoa.MaKH,
                                hash);

                            Log.Information(
                                "[HASH CREATED] " + TABLE_NAME + " {Code}",
                                khoa.MaKH);
                        }
                        else
                        {
                            await InsertDepartment(khoa);

                            await _hashRepository.SaveHashAsync(
                                TABLE_NAME,
                                khoa.MaKH,
                                hash);

                            Log.Information(
                                "[INSERT] " + TABLE_NAME + " {Code}",
                                khoa.MaKH);
                        }
                    }
                    else if (oldHash != hash)
                    {
                        await UpdateDepartment(khoa);

                        await _hashRepository.SaveHashAsync(
                                            TABLE_NAME,
                                            khoa.MaKH,
                                            hash);

                        updated++;

                        Log.Information("[UPDATE] " + TABLE_NAME + " {MaKH}",
                            khoa.MaKH);
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error sync " + TABLE_NAME + " {MaKH}", khoa.MaKH);
                }
            }

            Log.Information(TABLE_NAME + " Sync Finished. Inserted={Inserted}, Updated={Updated}, Skipped={Skipped}",
                inserted,
                updated,
                skipped);
        }

        private string BuildHash(SourceKhoa khoa)
        {
            return HashHelper.Compute(
                $"{khoa.MaKH}|{khoa.TenKhoa}");
        }

        private async Task InsertDepartment(
            SourceKhoa khoa)
        {
            using var conn = _targetFactory.Create();

            await conn.ExecuteAsync(
            @"
            INSERT INTO " + TABLE_NAME + @"
            (
                DepartmentID,
                DepartmentCode,
                ShortName,
                DepartmentName,
                EnglishName,
                [Order],
                Category,
                CreatedAt,
                CreatedBy,
                IsDeleted
            )
            VALUES
            (
                @DepartmentID,
                @DepartmentCode,
                @ShortName,
                @DepartmentName,
                @EnglishName,
                @Order,
                @Category,
                @CreatedAt,
                @CreatedBy,
                @IsDeleted
            )
            ",
            new
            {
                DepartmentID = Guid.NewGuid(),

                DepartmentCode = khoa.MaKH,

                ShortName = khoa.MaKH,

                DepartmentName = khoa.TenKhoa,

                EnglishName = khoa.TenKhoa,

                Order = 0,

                Category = "Khoa",

                CreatedAt = DateTime.Now,

                CreatedBy = "SyncSystem",

                IsDeleted = false
            });
        }

        private async Task UpdateDepartment(
            SourceKhoa khoa)
        {
            using var conn = _targetFactory.Create();
            var strQuery = @"
                UPDATE " + TABLE_NAME + @"
                SET
                    ShortName = @ShortName,
                    DepartmentName = @DepartmentName,
                    EnglishName = @EnglishName,
                    UpdatedAt = @UpdatedAt,
                    UpdatedBy = @UpdatedBy
                WHERE DepartmentCode = @DepartmentCode
                ";

            await conn.ExecuteAsync(strQuery,
            new
            {
                DepartmentCode = khoa.MaKH,

                ShortName = khoa.MaKH,

                DepartmentName = khoa.TenKhoa,

                EnglishName = khoa.TenKhoa,

                UpdatedAt = DateTime.Now,

                UpdatedBy = "SyncSystem"
            });
        }

        private async Task<bool> ExistsDepartmentAsync(string departmentCode)
        {
            using var conn = _targetFactory.Create();

            var count = await conn.ExecuteScalarAsync<int>(
            @"
            SELECT COUNT(*)
            FROM " + TABLE_NAME + @"
            WHERE DepartmentCode = @DepartmentCode
            ",
            new
            {
                DepartmentCode = departmentCode
            });

            return count > 0;
        }
    }
}
