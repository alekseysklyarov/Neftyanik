using System.ComponentModel.DataAnnotations;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

public class MemberInputModel
{
    [StringLength(256, ErrorMessage = "Логин не должен превышать 256 символов.")]
    [Display(Name = "Логин")]
    public string? Login { get; set; }

    [Required(ErrorMessage = "Укажите ФИО.")]
    [StringLength(200, ErrorMessage = "ФИО не должно превышать 200 символов.")]
    [Display(Name = "ФИО")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Номер телефона не должен превышать 50 символов.")]
    [Phone(ErrorMessage = "Введите корректный номер телефона.")]
    [Display(Name = "Телефон")]
    public string? PhoneNumber { get; set; }

    [StringLength(256, ErrorMessage = "Электронная почта не должна превышать 256 символов.")]
    [EmailAddress(ErrorMessage = "Введите корректный адрес электронной почты.")]
    [Display(Name = "Электронная почта")]
    public string? Email { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Дата вступления")]
    public DateOnly? JoinedAt { get; set; }

    [StringLength(2000, ErrorMessage = "Примечание не должно превышать 2000 символов.")]
    [Display(Name = "Примечание")]
    public string? Notes { get; set; }

    [Display(Name = "Активен")]
    public bool IsActive { get; set; } = true;
}
