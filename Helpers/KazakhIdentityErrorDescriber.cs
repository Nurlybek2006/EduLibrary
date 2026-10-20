using Microsoft.AspNetCore.Identity;
namespace EduLibrary.Helpers;
public class KazakhIdentityErrorDescriber : IdentityErrorDescriber
{
    private static IdentityError Error(string code,string text)=>new(){Code=code,Description=text};
    public override IdentityError DefaultError()=>Error(nameof(DefaultError),global::EduLibrary.Localization.UiText.Get("Операция орындалмады. Қайталап көріңіз."));
    public override IdentityError ConcurrencyFailure()=>Error(nameof(ConcurrencyFailure),global::EduLibrary.Localization.UiText.Get("Деректер өзгертілген. Бетті жаңартып, қайталап көріңіз."));
    public override IdentityError DuplicateEmail(string email)=>Error(nameof(DuplicateEmail),global::EduLibrary.Localization.UiText.Get("Бұл электрондық пошта тіркелген."));
    public override IdentityError DuplicateUserName(string name)=>Error(nameof(DuplicateUserName),global::EduLibrary.Localization.UiText.Get("Бұл пайдаланушы тіркелген."));
    public override IdentityError InvalidEmail(string? email)=>Error(nameof(InvalidEmail),global::EduLibrary.Localization.UiText.Get("Электрондық пошта мекенжайы жарамсыз."));
    public override IdentityError InvalidUserName(string? name)=>Error(nameof(InvalidUserName),global::EduLibrary.Localization.UiText.Get("Пайдаланушы аты жарамсыз."));
    public override IdentityError PasswordTooShort(int length)=>Error(nameof(PasswordTooShort),global::EduLibrary.Localization.UiText.Format($"Құпиясөз кемінде {length} таңбадан тұруы тиіс."));
    public override IdentityError PasswordRequiresNonAlphanumeric()=>Error(nameof(PasswordRequiresNonAlphanumeric),global::EduLibrary.Localization.UiText.Get("Құпиясөзде арнайы таңба болуы тиіс."));
    public override IdentityError PasswordRequiresDigit()=>Error(nameof(PasswordRequiresDigit),global::EduLibrary.Localization.UiText.Get("Құпиясөзде сан болуы тиіс."));
    public override IdentityError PasswordRequiresLower()=>Error(nameof(PasswordRequiresLower),global::EduLibrary.Localization.UiText.Get("Құпиясөзде кіші әріп болуы тиіс."));
    public override IdentityError PasswordRequiresUpper()=>Error(nameof(PasswordRequiresUpper),global::EduLibrary.Localization.UiText.Get("Құпиясөзде бас әріп болуы тиіс."));
    public override IdentityError PasswordRequiresUniqueChars(int count)=>Error(nameof(PasswordRequiresUniqueChars),global::EduLibrary.Localization.UiText.Format($"Құпиясөзде кемінде {count} әртүрлі таңба болуы тиіс."));
    public override IdentityError PasswordMismatch()=>Error(nameof(PasswordMismatch),global::EduLibrary.Localization.UiText.Get("Құпиясөз дұрыс емес."));
    public override IdentityError InvalidToken()=>Error(nameof(InvalidToken),global::EduLibrary.Localization.UiText.Get("Тексеру коды жарамсыз."));
    public override IdentityError UserAlreadyInRole(string role)=>Error(nameof(UserAlreadyInRole),global::EduLibrary.Localization.UiText.Get("Пайдаланушы бұл рөлге ие."));
    public override IdentityError UserNotInRole(string role)=>Error(nameof(UserNotInRole),global::EduLibrary.Localization.UiText.Get("Пайдаланушы бұл рөлге ие емес."));
    public override IdentityError UserLockoutNotEnabled()=>Error(nameof(UserLockoutNotEnabled),global::EduLibrary.Localization.UiText.Get("Бұл пайдаланушыны бұғаттау қолжетімсіз."));
    public override IdentityError DuplicateRoleName(string role)=>Error(nameof(DuplicateRoleName),global::EduLibrary.Localization.UiText.Get("Бұл рөл бұрыннан бар."));
    public override IdentityError InvalidRoleName(string? role)=>Error(nameof(InvalidRoleName),global::EduLibrary.Localization.UiText.Get("Рөл атауы жарамсыз."));
}
