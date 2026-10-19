using Microsoft.AspNetCore.Identity;
namespace EduLibrary.Helpers;
public class KazakhIdentityErrorDescriber : IdentityErrorDescriber
{
    private static IdentityError Error(string code,string text)=>new(){Code=code,Description=text};
    public override IdentityError DefaultError()=>Error(nameof(DefaultError),"Операция орындалмады. Қайталап көріңіз.");
    public override IdentityError ConcurrencyFailure()=>Error(nameof(ConcurrencyFailure),"Деректер өзгертілген. Бетті жаңартып, қайталап көріңіз.");
    public override IdentityError DuplicateEmail(string email)=>Error(nameof(DuplicateEmail),"Бұл электрондық пошта тіркелген.");
    public override IdentityError DuplicateUserName(string name)=>Error(nameof(DuplicateUserName),"Бұл пайдаланушы тіркелген.");
    public override IdentityError InvalidEmail(string? email)=>Error(nameof(InvalidEmail),"Электрондық пошта мекенжайы жарамсыз.");
    public override IdentityError InvalidUserName(string? name)=>Error(nameof(InvalidUserName),"Пайдаланушы аты жарамсыз.");
    public override IdentityError PasswordTooShort(int length)=>Error(nameof(PasswordTooShort),$"Құпиясөз кемінде {length} таңбадан тұруы тиіс.");
    public override IdentityError PasswordRequiresNonAlphanumeric()=>Error(nameof(PasswordRequiresNonAlphanumeric),"Құпиясөзде арнайы таңба болуы тиіс.");
    public override IdentityError PasswordRequiresDigit()=>Error(nameof(PasswordRequiresDigit),"Құпиясөзде сан болуы тиіс.");
    public override IdentityError PasswordRequiresLower()=>Error(nameof(PasswordRequiresLower),"Құпиясөзде кіші әріп болуы тиіс.");
    public override IdentityError PasswordRequiresUpper()=>Error(nameof(PasswordRequiresUpper),"Құпиясөзде бас әріп болуы тиіс.");
    public override IdentityError PasswordRequiresUniqueChars(int count)=>Error(nameof(PasswordRequiresUniqueChars),$"Құпиясөзде кемінде {count} әртүрлі таңба болуы тиіс.");
    public override IdentityError PasswordMismatch()=>Error(nameof(PasswordMismatch),"Құпиясөз дұрыс емес.");
    public override IdentityError InvalidToken()=>Error(nameof(InvalidToken),"Тексеру коды жарамсыз.");
    public override IdentityError UserAlreadyInRole(string role)=>Error(nameof(UserAlreadyInRole),"Пайдаланушы бұл рөлге ие.");
    public override IdentityError UserNotInRole(string role)=>Error(nameof(UserNotInRole),"Пайдаланушы бұл рөлге ие емес.");
    public override IdentityError UserLockoutNotEnabled()=>Error(nameof(UserLockoutNotEnabled),"Бұл пайдаланушыны бұғаттау қолжетімсіз.");
    public override IdentityError DuplicateRoleName(string role)=>Error(nameof(DuplicateRoleName),"Бұл рөл бұрыннан бар.");
    public override IdentityError InvalidRoleName(string? role)=>Error(nameof(InvalidRoleName),"Рөл атауы жарамсыз.");
}
