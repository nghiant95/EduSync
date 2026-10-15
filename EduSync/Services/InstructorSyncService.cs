using Dapper;
using EduSync.Infrastructure;
using EduSync.Models;
using EduSync.Repositories;
using EduSync.Services.Interfaces;
using EduSync.Utils;
using Serilog;
using System.Globalization;

namespace EduSync.Services;

public class InstructorSyncService : ISyncService
{
    private readonly SourceDbConnectionFactory _sourceFactory;
    private readonly TargetDbConnectionFactory _targetFactory;
    private readonly SyncHashRepository _hashRepository;

    private const string TABLE_NAME = "Instructors";

    public InstructorSyncService(
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
        Log.Information("========== START SYNC INSTRUCTORS ==========");

        int inserted = 0;
        int updated = 0;
        int skipped = 0;
        int errors = 0;

        using var sourceConn = _sourceFactory.Create();

        var instructors =
            await sourceConn.QueryAsync<SourceCanBo>(
            @"
            SELECT
                [MaCB],
                [TenCB],
                [MaBM],
                [CanBoTG],
                [EmailCB1],
                [TelCB1],
                [ngaysinh],
                [HocHam],
                [HocVi],
                [PhaiNV]
            FROM [CanBo]
            ");

        foreach (var item in instructors)
        {
            try
            {
                // =====================================================
                // 1. Validate MaCB
                // =====================================================

                if (string.IsNullOrWhiteSpace(item.MaCB))
                {
                    Log.Warning(
                        "Skip Instructor because MaCB is empty.");

                    continue;
                }

                // =====================================================
                // 2. Build hash
                // =====================================================

                var hash = BuildHash(item);

                var oldHash =
                    await _hashRepository.GetHashAsync(
                        TABLE_NAME,
                        item.MaCB);

                // =====================================================
                // 3. Tìm DepartmentID
                //
                // MaBM ví dụ:
                //     "IT IT"
                //
                // Lấy phần đầu:
                //     "IT"
                //
                // Sau đó tìm:
                //     Departments.DepartmentCode = "IT"
                // =====================================================

                var departmentId =
                    await GetDepartmentIdAsync(item.MaBM);

                if (departmentId == null)
                {
                    Log.Warning(
                        "Department not found. " +
                        "MaBM={MaBM}, " +
                        "DepartmentCode={DepartmentCode}, " +
                        "MaCB={MaCB}",
                        item.MaBM,
                        GetDepartmentCode(item.MaBM),
                        item.MaCB);

                    continue;
                }

                // =====================================================
                // 4. INSERT
                // =====================================================

                if (oldHash == null)
                {
                    var exists =
                        await ExistsInstructorAsync(item.MaCB);

                    // -------------------------------------------------
                    // Target đã có dữ liệu nhưng SyncHash chưa có
                    // -> Không insert trùng
                    // -> Chỉ tạo hash
                    // -------------------------------------------------

                    if (exists)
                    {
                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaCB,
                            hash);

                        skipped++;

                        Log.Information(
                            "[HASH CREATED] Instructor {Code}",
                            item.MaCB);
                    }
                    else
                    {
                        await InsertInstructor(
                            item,
                            departmentId.Value);

                        await _hashRepository.SaveHashAsync(
                            TABLE_NAME,
                            item.MaCB,
                            hash);

                        inserted++;

                        Log.Information(
                            "[INSERT] Instructor {Code}",
                            item.MaCB);
                    }
                }

                // =====================================================
                // 5. UPDATE
                // =====================================================

                else if (oldHash != hash)
                {
                    await UpdateInstructor(
                        item,
                        departmentId.Value);

                    await _hashRepository.SaveHashAsync(
                        TABLE_NAME,
                        item.MaCB,
                        hash);

                    updated++;

                    Log.Information(
                        "[UPDATE] Instructor {Code}",
                        item.MaCB);
                }

                // =====================================================
                // 6. SKIP
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
                    "Error sync Instructor {Code}",
                    item.MaCB);
            }
        }

        Log.Information(
            "Instructor Sync Finished. " +
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

    private string BuildHash(SourceCanBo item)
    {
        return HashHelper.Compute(
            $"{item.MaCB}|" +
            $"{item.TenCB}|" +
            $"{item.MaBM}|" +
            $"{item.CanBoTG}|" +
            $"{item.EmailCB1}|" +
            $"{item.TelCB1}|" +
            $"{item.ngaysinh}|" +
            $"{item.HocHam}|" +
            $"{item.HocVi}|" +
            $"{item.PhaiNV}");
    }

    // =============================================================
    // GET DEPARTMENT CODE
    //
    // MaBM:
    //     "IT IT"
    //
    // Result:
    //     "IT"
    // =============================================================

    private string? GetDepartmentCode(string maBM)
    {
        if (string.IsNullOrWhiteSpace(maBM))
            return null;

        maBM = maBM.Trim();

        // Trường hợp đặc biệt: IEMIEM -> IEM
        if (maBM.Equals("IEMIEM", StringComparison.OrdinalIgnoreCase))
            return "IEM";

        // Trường hợp thông thường:
        // "IT IT" -> "IT"
        return maBM
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    // =============================================================
    // GET DEPARTMENT ID
    //
    // Từ:
    //     MaBM = "IT IT"
    //
    // Lấy:
    //     DepartmentCode = "IT"
    //
    // Sau đó:
    //     Departments.DepartmentCode = "IT"
    //
    // Trả về:
    //     Departments.DepartmentID
    // =============================================================

    private async Task<Guid?> GetDepartmentIdAsync(
        string maBM)
    {
        var departmentCode =
            GetDepartmentCode(maBM);

        if (string.IsNullOrWhiteSpace(departmentCode))
            return null;

        using var conn = _targetFactory.Create();

        return await conn.QueryFirstOrDefaultAsync<Guid?>(
            @"
            SELECT
                DepartmentID
            FROM Departments
            WHERE DepartmentCode = @DepartmentCode
            ",
            new
            {
                DepartmentCode = departmentCode
            });
    }

    // =============================================================
    // CHECK INSTRUCTOR EXISTS
    // =============================================================

    private async Task<bool> ExistsInstructorAsync(
        string code)
    {
        using var conn = _targetFactory.Create();

        var count =
            await conn.ExecuteScalarAsync<int>(
            @"
            SELECT COUNT(*)
            FROM Instructors
            WHERE Code = @Code
            ",
            new
            {
                Code = code
            });

        return count > 0;
    }

    // =============================================================
    // INSERT INSTRUCTOR
    // =============================================================

    private async Task InsertInstructor(
        SourceCanBo item,
        Guid departmentId)
    {
        using var conn = _targetFactory.Create();

        await conn.ExecuteAsync(
        @"
        INSERT INTO Instructors
        (
            ID,
            Code,
            Fullname,
            Gender,
            Type,
            DOB,
            Title,
            Degree,
            PersonalPhone,
            Email,
            DepartmentID,
            CreatedAt,
            CreatedBy,
            IsDeleted,
            SubDepartmentCode
        )
        VALUES
        (
            @ID,
            @Code,
            @Fullname,
            @Gender,
            @Type,
            @DOB,
            @Title,
            @Degree,
            @PersonalPhone,
            @Email,
            @DepartmentID,
            @CreatedAt,
            @CreatedBy,
            @IsDeleted,
            @SubDepartmentCode
        )
        ",
        new
        {
            ID = Guid.NewGuid(),

            // MaCB -> Code
            Code = item.MaCB,

            // TenCB -> Fullname
            Fullname = item.TenCB,

            // PhaiNV:
            // 1 = Nữ
            // 0 = Nam
            Gender = MapGender(item.PhaiNV),

            // CanBoTG:
            // F = Cơ hữu
            // T = Thỉnh giảng
            Type = MapInstructorType(item.CanBoTG),

            // dd/MM/yyyy -> DateTime
            DOB = ParseDate(item.ngaysinh),

            // HocHam -> Title
            Title = item.HocHam,

            // HocVi -> Degree
            Degree = item.HocVi,

            // TelCB1 -> PersonalPhone
            PersonalPhone = item.TelCB1,

            // EmailCB1 -> Email
            Email = GetEmail(item.EmailCB1),

            // DepartmentID lấy từ Departments
            DepartmentID = departmentId,

            CreatedAt = DateTime.Now,
            CreatedBy = "SyncSystem",

            IsDeleted = false,

            // Giữ nguyên MaBM
            // Ví dụ: "IT IT"
            SubDepartmentCode = item.MaBM
        });
    }

    // =============================================================
    // UPDATE INSTRUCTOR
    // =============================================================

    private async Task UpdateInstructor(
        SourceCanBo item,
        Guid departmentId)
    {
        using var conn = _targetFactory.Create();

        await conn.ExecuteAsync(
        @"
        UPDATE Instructors
        SET
            Fullname = @Fullname,
            Gender = @Gender,
            Type = @Type,
            DOB = @DOB,
            Title = @Title,
            Degree = @Degree,
            PersonalPhone = @PersonalPhone,
            Email = @Email,
            DepartmentID = @DepartmentID,
            SubDepartmentCode = @SubDepartmentCode,
            UpdatedAt = @UpdatedAt,
            UpdatedBy = @UpdatedBy
        WHERE Code = @Code
        ",
        new
        {
            Code = item.MaCB,

            Fullname = item.TenCB,

            Gender = MapGender(item.PhaiNV),

            Type = MapInstructorType(item.CanBoTG),

            DOB = ParseDate(item.ngaysinh),

            Title = item.HocHam,

            Degree = item.HocVi,

            PersonalPhone = item.TelCB1,

            Email = GetEmail(item.EmailCB1),

            // DepartmentID được tìm từ Departments
            DepartmentID = departmentId,

            // Giữ nguyên MaBM
            // Ví dụ: "IT IT"
            SubDepartmentCode = item.MaBM,

            UpdatedAt = DateTime.Now,
            UpdatedBy = "SyncSystem"
        });
    }

    // =============================================================
    // MAP INSTRUCTOR TYPE
    //
    // F = Cơ hữu
    // T = Thỉnh giảng
    // =============================================================

    private string? MapInstructorType(
        string canBoTG)
    {
        if (string.IsNullOrWhiteSpace(canBoTG))
            return null;

        return canBoTG
            .Trim()
            .ToUpper() switch
        {
            "F" => "Cơ hữu",
            "T" => "Thỉnh giảng",
            _ => canBoTG
        };
    }

    // =============================================================
    // MAP GENDER
    //
    // PhaiNV:
    //     1 = Nữ
    //     0 = Nam
    // =============================================================

    private int? MapGender(int? phaiNV)
    {
        if (!phaiNV.HasValue)
            return null;

        return phaiNV.Value switch
        {
            1 => 1,
            0 => 0,
            _ => null
        };
    }

    // =============================================================
    // PARSE DOB
    //
    // Source:
    //     dd/MM/yyyy
    //
    // Target:
    //     DateTime
    //
    // SQL Server sẽ lưu theo kiểu date/datetime tương ứng.
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

    private string? GetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var firstEmail = email
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim();

        if (string.IsNullOrWhiteSpace(firstEmail))
            return null;

        if (firstEmail.Length > 50)
        {
            Log.Warning(
                "Email exceeds 50 characters. Email={Email}",
                firstEmail);

            return firstEmail[..50];
        }

        return firstEmail;
    }
}