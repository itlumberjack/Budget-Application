using BudgetApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BudgetApp.Controllers;

/// <summary>Signing in and out, the first-run setup page, and the list of people who can sign in.</summary>
public class AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) : Controller
{
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl)
    {
        if (!await users.Users.AnyAsync()) return RedirectToAction(nameof(Setup));
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturn(returnUrl));
        return View(new LoginForm { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);
        var result = await signIn.PasswordSignInAsync(form.Email.Trim(), form.Password, form.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(SafeReturn(form.ReturnUrl));
        ModelState.AddModelError("", result.IsLockedOut
            ? "Too many wrong passwords. Try again in a few minutes."
            : "That email and password don't match.");
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Creates the first account. Only available until someone has one.</summary>
    [AllowAnonymous]
    public async Task<IActionResult> Setup() =>
        await users.Users.AnyAsync() ? RedirectToAction(nameof(Login)) : View(new NewAccountForm());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Setup(NewAccountForm form)
    {
        if (await users.Users.AnyAsync()) return RedirectToAction(nameof(Login));
        if (!ModelState.IsValid) return View(form);
        var user = new IdentityUser { UserName = form.Email.Trim(), Email = form.Email.Trim() };
        if (!AddErrors(await users.CreateAsync(user, form.Password))) return View(form);
        await signIn.SignInAsync(user, isPersistent: true);
        TempData["Message"] = "You're all set. Add your partner's sign-in under Settings > Sign-ins.";
        return RedirectToAction("Index", "Home");
    }

    /// <summary>Who can sign in: add a person, remove one, or change your own password.</summary>
    public async Task<IActionResult> SignIns() => View(await SignInsModelAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPerson([Bind(Prefix = "NewPerson")] NewAccountForm form)
    {
        if (ModelState.IsValid)
        {
            var user = new IdentityUser { UserName = form.Email.Trim(), Email = form.Email.Trim() };
            if (AddErrors(await users.CreateAsync(user, form.Password)))
            {
                TempData["Message"] = $"{user.Email} can sign in now, on the website and the phone app.";
                return RedirectToAction(nameof(SignIns));
            }
        }
        return View(nameof(SignIns), await SignInsModelAsync(newPerson: form));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePerson(string email)
    {
        var user = await users.FindByNameAsync(email);
        if (user is null) return RedirectToAction(nameof(SignIns));
        if (user.UserName == User.Identity!.Name)
            TempData["Error"] = "You can't remove yourself. Sign in as someone else to do that.";
        else
        {
            await users.DeleteAsync(user);
            TempData["Message"] = $"{email} can no longer sign in.";
        }
        return RedirectToAction(nameof(SignIns));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "ChangePassword")] ChangePasswordForm form)
    {
        if (ModelState.IsValid)
        {
            var user = await users.GetUserAsync(User);
            if (user is null) return RedirectToAction(nameof(Login));
            if (AddErrors(await users.ChangePasswordAsync(user, form.CurrentPassword, form.NewPassword)))
            {
                await signIn.RefreshSignInAsync(user);
                TempData["Message"] = "Password changed.";
                return RedirectToAction(nameof(SignIns));
            }
        }
        return View(nameof(SignIns), await SignInsModelAsync(changePassword: form));
    }

    private async Task<SignInsModel> SignInsModelAsync(NewAccountForm? newPerson = null, ChangePasswordForm? changePassword = null) => new()
    {
        People = await users.Users.OrderBy(u => u.UserName).Select(u => u.UserName!).ToListAsync(),
        Me = User.Identity!.Name ?? "",
        NewPerson = newPerson ?? new(),
        ChangePassword = changePassword ?? new()
    };

    /// <summary>Shows Identity's problems (password too weak, email taken) at the top of the form.</summary>
    private bool AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError("", error.Code == "PasswordMismatch" ? "The current password isn't right." : error.Description);
        return result.Succeeded;
    }

    private string SafeReturn(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
