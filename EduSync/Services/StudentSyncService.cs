using Dapper;
using EduSync.Infrastructure;
using EduSync.Models;
using EduSync.Repositories;
using EduSync.Services.Interfaces;
using EduSync.Utils;

namespace EduSync.Services
{
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
            Console.WriteLine("=================================");
            Console.WriteLine("START SYNC STUDENTS");
            Console.WriteLine("=================================");

            int inserted = 0;
            int updated = 0;
            int skipped = 0;

            using var sourceConn = _sourceFactory.Create();

            var sourceStudents =
                await sourceConn.QueryAsync<SourceStudent>(
                @"
            SELECT
                Id,
                StudentCode,
                FullName,
                Email
            FROM Student
            ");

            Console.WriteLine(
                $"Source records: {sourceStudents.Count()}");

            foreach (var student in sourceStudents)
            {
                try
                {
                    //var newHash = HashHelper.Compute(
                    //    $"{student.StudentCode}|{student.FullName}|{student.Email}");

                    //var oldHash =
                    //    await _hashRepository.GetHashAsync(
                    //        TABLE_NAME,
                    //        student.Id);

                    //if (oldHash == null)
                    //{
                    //    await InsertStudent(student);

                    //    await _hashRepository.SaveHashAsync(
                    //        TABLE_NAME,
                    //        student.Id,
                    //        newHash);

                    //    inserted++;

                    //    Console.WriteLine(
                    //        $"[INSERT] Student {student.StudentCode}");
                    //}
                    //else if (oldHash != newHash)
                    //{
                    //    await UpdateStudent(student);

                    //    await _hashRepository.SaveHashAsync(
                    //        TABLE_NAME,
                    //        student.Id,
                    //        newHash);

                    //    updated++;

                    //    Console.WriteLine(
                    //        $"[UPDATE] Student {student.StudentCode}");
                    //}
                    //else
                    //{
                    //    skipped++;
                    //}
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[ERROR] StudentId={student.Id}");

                    Console.WriteLine(ex.Message);
                }
            }

            Console.WriteLine("=================================");
            Console.WriteLine("SYNC COMPLETED");
            Console.WriteLine($"Inserted : {inserted}");
            Console.WriteLine($"Updated  : {updated}");
            Console.WriteLine($"Skipped  : {skipped}");
            Console.WriteLine("=================================");
        }

        private async Task InsertStudent(
            SourceStudent student)
        {
            using var targetConn = _targetFactory.Create();

            await targetConn.ExecuteAsync(
            @"
            INSERT INTO Students
            (
                Id,
                SourceId,
                StudentCode,
                FullName,
                Email
            )
            VALUES
            (
                @Id,
                @SourceId,
                @StudentCode,
                @FullName,
                @Email
            )
            ",
            new
            {
                Id = Guid.NewGuid(),
                SourceId = student.Id,
                StudentCode = student.StudentCode,
                FullName = student.FullName,
                Email = student.Email
            });
        }

        private async Task UpdateStudent(
            SourceStudent student)
        {
            using var targetConn = _targetFactory.Create();

            await targetConn.ExecuteAsync(
            @"
            UPDATE Students
            SET
                StudentCode = @StudentCode,
                FullName = @FullName,
                Email = @Email
            WHERE SourceId = @SourceId
            ",
            new
            {
                SourceId = student.Id,
                StudentCode = student.StudentCode,
                FullName = student.FullName,
                Email = student.Email
            });
        }
    }
}
