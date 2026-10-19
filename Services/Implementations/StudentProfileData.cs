using EduLibrary.Data;
using EduLibrary.Helpers;
using EduLibrary.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduLibrary.Services.Implementations;

internal static class StudentProfileData
{
    public static async Task SetPreferencesAsync(ApplicationDbContext db, Student student, IEnumerable<int> subjectIds, string? interests, CancellationToken ct)
    {
        var ids = subjectIds.Distinct().ToArray();
        if (ids.Length > 30 || ids.Any(id => id <= 0) || await db.Subjects.CountAsync(s => ids.Contains(s.Id), ct) != ids.Length)
            throw new BusinessException("Пәндер тізімі дұрыс емес. Тізімнен қайта таңдаңыз.");

        var names = (interests ?? "").Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length > 20 || names.Any(n => n.Length > 100))
            throw new BusinessException("Ең көбі 20 қызығушылық енгізіңіз. Әр атау 100 таңбадан аспасын.");

        foreach (var link in student.StudentSubjects.Where(s => !ids.Contains(s.SubjectId)).ToList())
        {
            db.StudentSubjects.Remove(link);
            student.StudentSubjects.Remove(link);
        }
        foreach (var id in ids.Where(id => student.StudentSubjects.All(s => s.SubjectId != id)))
            student.StudentSubjects.Add(new StudentSubject { SubjectId = id });

        foreach (var interest in student.Interests.Where(i => !names.Contains(i.Name, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            db.StudentInterests.Remove(interest);
            student.Interests.Remove(interest);
        }
        foreach (var name in names.Where(n => student.Interests.All(i => !string.Equals(i.Name, n, StringComparison.OrdinalIgnoreCase))))
            student.Interests.Add(new StudentInterest { Name = name });
    }
}
