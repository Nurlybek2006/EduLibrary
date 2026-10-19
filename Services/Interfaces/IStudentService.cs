using EduLibrary.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduLibrary.Services.Interfaces;

public interface IStudentService
{
    Task<StudentListViewModel> ListAsync(StudentListViewModel filter, CancellationToken cancellationToken = default);
    Task<StudentDetailsViewModel?> GetDetailsAsync(int id, string currentUserId, bool canManage, CancellationToken cancellationToken = default);
    Task<int?> GetOwnIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<StudentEditViewModel?> GetEditAsync(int id, CancellationToken cancellationToken = default);
    Task<List<SelectListItem>> GetSubjectOptionsAsync(CancellationToken cancellationToken = default);
    Task<int> CreateAsync(StudentEditViewModel model, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, StudentEditViewModel model, CancellationToken cancellationToken = default);
    Task DeactivateAsync(int id, CancellationToken cancellationToken = default);
}
