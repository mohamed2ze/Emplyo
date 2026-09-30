using System.ComponentModel.DataAnnotations;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Emplyo.Web.Controllers;

public class LoginViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required, EmailAddress]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 12), DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIdentityService _identity;
    private readonly IAppEmailSender _email;
    private readonly ILogger<AccountController> _logger;
    private readonly IAppSettingsService _settings;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IIdentityService identity,
        IAppEmailSender email,
        ILogger<AccountController> logger,
        IAppSettingsService settings)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _identity = identity;
        _email = email;
        _logger = logger;
        _settings = settings;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewData["Heading"] = "Welcome back";
        ViewData["Subheading"] = "Sign in to your Emplyo account";
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["Heading"] = "Welcome back";
        ViewData["Subheading"] = "Sign in to your Emplyo account";

        if (!ModelState.IsValid) return View(model);

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user is not null)
            {
                await _identity.RecordLoginAsync(user.Id);
            }
            _logger.LogInformation("Successful sign-in for {Email}.", model.Email);
            return RedirectToLocal(model.ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked. Please try again later or contact an administrator.");
            _logger.LogWarning("Locked-out sign-in attempt for {Email}.", model.Email);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Invalid sign-in attempt. Check your email and password.");
            _logger.LogWarning("Failed sign-in attempt for {Email}.", model.Email);
        }

        return View(model);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPassword()
    {
        ViewData["Heading"] = "Reset your password";
        ViewData["Subheading"] = "We will send reset instructions if the account exists";
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        ViewData["Heading"] = "Reset your password";
        ViewData["Subheading"] = "We will send reset instructions if the account exists";

        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is not null && user.IsActive)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = Url.Action(nameof(ResetPassword), "Account", new { email = user.Email, token }, Request.Scheme);
            // The token is never logged. Email delivery is optional and may be disabled.
            await _email.SendAsync(user.Email!, "Reset your Emplyo password",
                $"<p>Use the following link to reset your password:</p><p><a href=\"{link}\">Reset password</a></p>");
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPasswordConfirmation()
    {
        ViewData["Heading"] = "Check your inbox";
        ViewData["Subheading"] = "If an account exists, reset instructions have been sent";
        ViewData["EmailEnabled"] = _email.IsEnabled;
        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            return RedirectToAction(nameof(Login));
        }

        ViewData["Heading"] = "Choose a new password";
        ViewData["Subheading"] = "Enter a strong password for your account";
        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        ViewData["Heading"] = "Choose a new password";
        ViewData["Subheading"] = "Enter a strong password for your account";

        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            TempData["Success"] = "Your password has been reset. You can now sign in.";
            return RedirectToAction(nameof(Login));
        }

        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
        if (result.Succeeded)
        {
            TempData["Success"] = "Your password has been reset. You can now sign in.";
            return RedirectToAction(nameof(Login));
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        ViewData["Title"] = "Access denied";
        return View();
    }

    private IActionResult RedirectToLocal(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Home");
}
