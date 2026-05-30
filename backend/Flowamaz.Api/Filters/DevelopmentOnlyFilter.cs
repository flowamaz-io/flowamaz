using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;

namespace Flowamaz.Api.Filters;

/// <summary>
/// Short-circuits any decorated action to 404 outside the Development environment, so dev-only
/// surfaces (breakpoint/resume/step) do not exist at all in staging/production — defense-in-depth
/// on top of the per-action environment guards.
/// </summary>
public sealed class DevelopmentOnlyFilter : IActionFilter
{
    private readonly IHostEnvironment _environment;

    public DevelopmentOnlyFilter(IHostEnvironment environment) => _environment = environment;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!_environment.IsDevelopment())
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
