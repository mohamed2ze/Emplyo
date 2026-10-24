using System.ComponentModel.DataAnnotations;
using System.Net;
using Emplyo.Application.Common.Interfaces;
using Emplyo.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Emplyo.Web.Controllers;

public class LoginViewModel
{
    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(100), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(4096)]
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
    private const string InvalidResetLinkMessage =
        "This password reset link is invalid or has expired. Request a new link and try again.";

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIdentityService _identity;
    private readonly IAppEmailSender _email;
    private readonly ILogger<AccountController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IIdentityService identity,
        IAppEmailSender email,
        ILogger<AccountController> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _identity = identity;
        _email = email;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
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
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["Heading"] = "Welcome back";
        ViewData["Subheading"] = "Sign in to your Emplyo account";

        if (!ModelState.IsValid) return View(model);

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user is null || !user.IsActive)
            {
                await _signInManager.SignOutAsync();
                ModelState.AddModelError(string.Empty, "Invalid sign-in attempt. Check your email and password.");
                _logger.LogWarning("Sign-in rejected for an inactive or missing account.");
                return View(model);
            }

            await _identity.RecordLoginAsync(user.Id);
            _logger.LogInformation("Successful sign-in for {Email}.", model.Email);
            return RedirectToLocal(model.ReturnUrl);
        }

        ModelState.AddModelError(string.Empty, "Invalid sign-in attempt. Check your email and password.");
        if (result.IsLockedOut)
            _logger.LogWarning("Locked-out sign-in attempt for {Email}.", model.Email);
        else
            _logger.LogWarning("Failed sign-in attempt for {Email}.", model.Email);

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
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        ViewData["Heading"] = "Reset your password";
        ViewData["Subheading"] = "We will send reset instructions if the account exists";

        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is not null && user.IsActive)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link = BuildPasswordResetLink(user.Email!, token);
            if (link is not null)
            {
                var encodedLink = WebUtility.HtmlEncode(link);
                // The token is never logged. Email delivery is optional and may be disabled.
                await _email.SendAsync(user.Email!, "Reset your Emplyo password",
                    $"<p>Use the following link to reset your password:</p><p><a href=\"{encodedLink}\">Reset password</a></p>");
            }
            else if (_email.IsEnabled)
            {
                _logger.LogError(
                    "Password reset email was not sent because App:PublicBaseUrl is missing or invalid. " +
                    "Configure an absolute HTTPS base URL for this environment.");
            }
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
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        ViewData["Heading"] = "Choose a new password";
        ViewData["Subheading"] = "Enter a strong password for your account";

        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, InvalidResetLinkMessage);
            return View(model);
        }

        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
        if (result.Succeeded)
        {
            TempData["Success"] = "Your password has been reset. You can now sign in.";
            return RedirectToAction(nameof(Login));
        }

        var invalidLinkReported = false;
        foreach (var error in result.Errors)
        {
            if (error.Code == "InvalidToken")
            {
                if (invalidLinkReported) continue;
                ModelState.AddModelError(string.Empty, InvalidResetLinkMessage);
                invalidLinkReported = true;
            }
            else
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        ViewData["Title"] = "Access denied";
        return View();
    }

    /// <summary>
    /// Builds the password-reset URL from an explicitly configured public base URL.
    /// In non-development environments the request Host header is never trusted, which
    /// prevents an attacker from poisoning the reset link sent to a victim.
    /// </summary>
    private string? BuildPasswordResetLink(string email, string token)
    {
        var action = Url.Action(nameof(ResetPassword), "Account", new { email, token });
        if (string.IsNullOrEmpty(action)) return null;

        var configured = _configuration["App:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var candidate = configured.Trim().TrimEnd('/') + "/";
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp) ||
                baseUri.UserInfo.Length > 0 ||
                !string.IsNullOrEmpty(baseUri.Query) ||
                !string.IsNullOrEmpty(baseUri.Fragment))
            {
                return null;
            }

            if (!_environment.IsDevelopment() && baseUri.Scheme != Uri.UriSchemeHttps)
                return null;

            return new Uri(baseUri, action.TrimStart('/')).AbsoluteUri;
        }

        return _environment.IsDevelopment()
            ? $"{Request.Scheme}://{Request.Host}{action}"
            : null;
    }

    private IActionResult RedirectToLocal(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Home");
}
