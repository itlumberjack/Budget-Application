using System.ComponentModel.DataAnnotations;

namespace BudgetApp.Models;

public class LoginForm
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; } = true;

    public string? ReturnUrl { get; set; }
}

/// <summary>A new sign-in: the first account on the setup page, or another person under Sign-ins.</summary>
public class NewAccountForm
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public class ChangePasswordForm
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public class SignInsModel
{
    public List<string> People { get; init; } = new();
    public string Me { get; init; } = "";
    public NewAccountForm NewPerson { get; init; } = new();
    public ChangePasswordForm ChangePassword { get; init; } = new();
}
