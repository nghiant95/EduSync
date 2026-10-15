using Dapper;
using EduSync.Infrastructure;
using EduSync.Models;
using EduSync.Repositories;
using EduSync.Services.Interfaces;
using EduSync.Utils;
using Serilog;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EduSync.Services;

public class StudentSyncService : ISyncService
{
    private readonly SourceDbConnectionFactory _sourceFactory;
    private readonly TargetDbConnectionFactory _targetFactory;
    private readonly SyncHashRepository _hashRepository;

    private const string TABLE_NAME = "Students";

    public StudentSyncService(
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
        Log.Information("========== START SYNC STUDENTS ==========");

        int inserted = 0;
        int updated = 0;
        int skipped = 0;
        int errors = 0;

        using var sourceConn = _sourceFactory.Create();

        var students =
            await sourceConn.QueryAsync<SourceSinhVien>(
            @"
            SELECT
                [MaSV],
                [HoTenSV],
                [NgaySinh],
                [EmailSV1],
                [TelSV1],
                [phai],
                [noisinh]
            FROM [SinhVien]
            ");

        foreach (var item in students)
        {
            try
            {
                // =====================================================
                // 1. Validate StudentID
                // =====================================================

                if (string.IsNullOrWhiteSpace(item.MaSV))
                {
                    Log.Warning(
                        "Skip Student because MaSV is empty.");

                    continue;
                }

                // =====================================================
                // 2. Build hash
                // =====================================================

                var hash = BuildHash(item);

                var oldHash =
                    await _hashRepository.GetHashAsync(
                        TABLE_NAME,
                        item.MaSV);

                // =====================================================
                // 3. INSERT
                // =====================================================

                if (oldHash == null)
                {
                    var exists =
                        await ExistsStudentAsync(item.MaSV);

                    // -------------------------------------------------
                    // Target đã có nhưng SyncHash chưa có
                    // -> Không insert trùng
                    // -> Chỉ tạo hash
                    // -------------------------------------------------

                    if (exists)
                    {
                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaSV,
                            hash);

                        skipped++;

                        Log.Information(
                            "[HASH CREATED] Student {StudentID}",
                            item.MaSV);
                    }
                    else
                    {
                        await InsertStudent(item);

                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaSV,
                            hash);

                        inserted++;

                        Log.Information(
                            "[INSERT] Student {StudentID}",
                            item.MaSV);
                    }
                }

                // =====================================================
                // 4. UPDATE
                // =====================================================

                else if (oldHash != hash)
                {
                    await UpdateStudent(item);

                    await _hashRepository.SaveHashAsync(
                        TABLE_NAME,
                        item.MaSV,
                        hash);

                    updated++;

                    Log.Information(
                        "[UPDATE] Student {StudentID}",
                        item.MaSV);
                }

                // =====================================================
                // 5. SKIP
                // =====================================================

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
                    "Error sync Student {StudentID}",
                    item.MaSV);
            }
        }

        Log.Information(
            "Student Sync Finished. " +
            "Inserted={Inserted}, " +
            "Updated={Updated}, " +
            "Skipped={Skipped}, " +
            "Errors={Errors}",
            inserted,
            updated,
            skipped,
            errors);
    }

    // =============================================================
    // BUILD HASH
    // =============================================================

    private string BuildHash(SourceSinhVien item)
    {
        return HashHelper.Compute(
            $"{item.MaSV}|" +
            $"{item.HoTenSV}|" +
            $"{item.NgaySinh}|" +
            $"{item.EmailSV1}|" +
            $"{item.TelSV1}|" +
            $"{item.phai}|" +
            $"{item.noisinh}");
    }

    // =============================================================
    // CHECK STUDENT EXISTS
    // =============================================================

    private async Task<bool> ExistsStudentAsync(
        string studentId)
    {
        using var conn = _targetFactory.Create();

        var count =
            await conn.ExecuteScalarAsync<int>(
            @"
            SELECT COUNT(*)
            FROM Students
            WHERE StudentID = @StudentID
            ",
            new
            {
                StudentID = studentId
            });

        return count > 0;
    }

    // =============================================================
    // INSERT STUDENT
    // =============================================================

    private async Task InsertStudent(
        SourceSinhVien item)
    {
        using var conn = _targetFactory.Create();

        var nameInfo =
            SplitFullname(item.HoTenSV);

        await conn.ExecuteAsync(
        @"
        INSERT INTO Students
        (
            ID,
            Fullname,
            DOB,
            StudentID,
            IdentityNumber,
            Email,
            Gender,
            Phone,
            FirstName,
            MiddleName,
            ProvincePlaceOfBirth,
            IsDeleted,
            CreatedAt,
            CreatedBy
        )
        VALUES
        (
            @ID,
            @Fullname,
            @DOB,
            @StudentID,
            @IdentityNumber,
            @Email,
            @Gender,
            @Phone,
            @FirstName,
            @MiddleName,
            @ProvincePlaceOfBirth,
            @IsDeleted,
            @CreatedAt,
            @CreatedBy
        )
        ",
        new
        {
            // ID -> GUID
            ID = Guid.NewGuid(),

            // HoTenSV -> Fullname
            Fullname = item.HoTenSV,

            // NgaySinh -> DOB
            DOB = ParseDate(item.NgaySinh),

            // MaSV -> StudentID
            StudentID = item.MaSV,

            // MaSV -> IdentityNumber
            IdentityNumber = item.MaSV,

            // EmailSV1 -> Email
            Email = GetEmail(item.EmailSV1),

            // phai -> Gender
            Gender = MapGender(item.phai),

            // TelSV1 -> Phone
            Phone = item.TelSV1,

            // Tên
            FirstName = nameInfo.FirstName,

            // Họ + tên đệm
            MiddleName = nameInfo.MiddleName,

            // noisinh -> ProvincePlaceOfBirth
            ProvincePlaceOfBirth = item.noisinh,

            // IsDeleted = 0
            IsDeleted = false,

            CreatedAt = DateTime.Now,
            CreatedBy = "SyncSystem"
        });
    }

    // =============================================================
    // UPDATE STUDENT
    // =============================================================

    private async Task UpdateStudent(
        SourceSinhVien item)
    {
        using var conn = _targetFactory.Create();

        var nameInfo =
            SplitFullname(item.HoTenSV);

        await conn.ExecuteAsync(
        @"
        UPDATE Students
        SET
            Fullname = @Fullname,
            DOB = @DOB,
            IdentityNumber = @IdentityNumber,
            Email = @Email,
            Gender = @Gender,
            Phone = @Phone,
            FirstName = @FirstName,
            MiddleName = @MiddleName,
            ProvincePlaceOfBirth = @ProvincePlaceOfBirth,
            IsDeleted = @IsDeleted,
            UpdatedAt = @UpdatedAt,
            UpdatedBy = @UpdatedBy
        WHERE StudentID = @StudentID
        ",
        new
        {
            StudentID = item.MaSV,

            IdentityNumber = item.MaSV,

            Fullname = item.HoTenSV,

            DOB = ParseDate(item.NgaySinh),

            Email = GetEmail(item.EmailSV1),

            Gender = MapGender(item.phai),

            Phone = item.TelSV1,

            FirstName = nameInfo.FirstName,

            MiddleName = nameInfo.MiddleName,

            ProvincePlaceOfBirth = item.noisinh,

            IsDeleted = false,

            UpdatedAt = DateTime.Now,
            UpdatedBy = "SyncSystem"
        });
    }

    // =============================================================
    // SPLIT FULLNAME
    //
    // Nguyễn Văn An
    //     FirstName  = An
    //     MiddleName = Nguyễn Văn
    //
    // Nguyễn An
    //     FirstName  = An
    //     MiddleName = Nguyễn
    //
    // An
    //     FirstName  = An
    //     MiddleName = null
    // =============================================================

    private (string? FirstName, string? MiddleName)
        SplitFullname(string fullname)
    {
        if (string.IsNullOrWhiteSpace(fullname))
            return (null, null);

        var parts = fullname
            .Trim()
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return (null, null);

        // Chỉ có một từ
        if (parts.Length == 1)
        {
            return (
                parts[0],
                null
            );
        }

        // Từ cuối cùng là FirstName
        var firstName = parts[^1];

        // Các từ còn lại là MiddleName
        var middleName = string.Join(
            " ",
            parts.Take(parts.Length - 1));

        return (
            firstName,
            middleName
        );
    }

    // =============================================================
    // MAP GENDER
    //
    // phai:
    //     N  -> Nữ
    //     "" -> Nam
    // =============================================================

    private string MapGender(string phai)
    {
        if (string.IsNullOrWhiteSpace(phai))
            return "Nam";

        return phai.Trim().ToUpper() switch
        {
            "N" => "Nữ",
            _ => "Nam"
        };
    }

    // =============================================================
    // GET EMAIL
    //
    // Nếu có nhiều email:
    //
    // abc@gmail.com; xyz@ump.edu.vn
    //
    // -> lấy email đầu tiên
    //
    // Giới hạn 50 ký tự theo destination.
    // =============================================================

    private string? GetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var firstEmail = email
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim();

        if (string.IsNullOrWhiteSpace(firstEmail))
            return null;

        if (firstEmail.Length > 50)
        {
            Log.Warning(
                "Student email exceeds 50 characters. " +
                "OriginalEmail={Email}",
                firstEmail);

            return firstEmail[..50];
        }

        return firstEmail;
    }

    // =============================================================
    // PARSE DOB
    //
    // Source:
    //     20/05/2020
    //
    // Target:
    //     2020-05-20
    //
    // Trường hợp:
    //     /  /1965
    //
    // Mặc định:
    //     1965-01-01
    // =============================================================

    private string? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        // =========================================================
        // 1. Trường hợp chỉ có năm
        //
        // Ví dụ:
        //     "/  /1965"
        //     "/ /1965"
        //     "//1965"
        //
        // Mặc định ngày/tháng = 01/01
        // Kết quả:
        //     1965-01-01
        // =========================================================

        var yearOnlyMatch = System.Text.RegularExpressions.Regex.Match(
            value,
            @"^\s*/\s*/\s*(\d{4})\s*$");

        if (yearOnlyMatch.Success)
        {
            if (int.TryParse(
                yearOnlyMatch.Groups[1].Value,
                out var year) &&
                year >= 1900 &&
                year <= DateTime.Now.Year)
            {
                var defaultDate = new DateTime(year, 1, 1);

                Log.Warning(
                    "DOB only contains year. Original={DOB}, DefaultDate={DefaultDate}",
                    value,
                    defaultDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture));

                return defaultDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);
            }
        }

        // =========================================================
        // 2. Các format ngày đầy đủ
        // =========================================================

        string[] formats =
        {
        // Oct 20 1985
        "MMM dd yyyy",
        "MMM d yyyy",

        // 06/02/1992
        "dd/MM/yyyy",
        "d/M/yyyy",

        // 1985-10-20
        "yyyy-MM-dd",

        // 1985/10/20
        "yyyy/MM/dd"
    };

        if (DateTime.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var date))
        {
            return date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
        }

        // =========================================================
        // 3. Fallback
        // =========================================================

        if (DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out date))
        {
            return date.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
        }

        // =========================================================
        // 4. Không xử lý được
        // =========================================================

        Log.Warning(
            "Invalid DOB format: {DOB}",
            value);

        return null;
    }
}