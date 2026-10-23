using Emplyo.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Emplyo.Web.Filters;

/// <summary>
/// Translates domain exceptions into user-friendly HTTP responses instead of raw errors,
/// while leaving unexpected exceptions to the global exception handler.
/// </summary>
public class DomainExceptionFilter : IExceptionFilter
{
    private readonly ILogger<DomainExceptionFilter> _logger;

    public DomainExceptionFilter(ILogger<DomainExceptionFilter> logger) => _logger = logger;

    public void OnException(ExceptionContext context)
    {
        var exception = context.Exception;
        if (exception is not DomainException domain)
        {
            return;
        }

        _logger.LogWarning(domain, "Domain rule violation handled: {Message}", domain.Message);
        context.ExceptionHandled = true;

        var isAjax = context.HttpContext.Request.Headers["X-Requested-With"] == "XMLHttpRequest"
            || context.HttpContext.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

        switch (domain)
        {
            case NotFoundException:
                context.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Result = new ViewResult
                {
                    ViewName = "~/Views/Home/Status.cshtml",
                    ViewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
                    {
                        ["Code"] = 404,
                        ["Title"] = "Not found",
                        ["Message"] = domain.Message
                    }
                };
                break;

            case ForbiddenException:
                context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Result = new RedirectResult("/Account/AccessDenied");
                break;

            default:
                if (isAjax)
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    context.Result = new JsonResult(new { success = false, message = domain.Message });
                }
                else
                {
                    var tempDataFactory = context.HttpContext.RequestServices.GetRequiredService<ITempDataDictionaryFactory>();
                    var tempData = tempDataFactory.GetTempData(context.HttpContext);
                    tempData["Error"] = domain.Message;

                    context.Result = new RedirectResult(
                        GetSafeReferer(context.HttpContext, context.HttpContext.Request.Headers.Referer.ToString()));
                }
                break;
        }
    }

    private static string GetSafeReferer(HttpContext context, string? referer)
    {
        if (string.IsNullOrWhiteSpace(referer)) return "/";

        if (Uri.TryCreate(referer, UriKind.Absolute, out var absolute))
        {
            var sameHost = string.Equals(absolute.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
            var webScheme = absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps;
            return sameHost && webScheme ? absolute.PathAndQuery + absolute.Fragment : "/";
        }

        return referer.StartsWith('/') && !referer.StartsWith("//") && !referer.StartsWith("/\\")
            ? referer
            : "/";
    }
}
