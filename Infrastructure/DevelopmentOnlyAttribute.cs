using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace XR50TrainingAssetRepo.Infrastructure
{
    /// <summary>
    /// Restricts an action to the Development hosting environment. In any other environment the action
    /// responds with 404 (Not Found), which hides the endpoint's existence rather than advertising a 403.
    ///
    /// Used to fence off destructive / enumeration troubleshooting endpoints (drop database, rebuild,
    /// force-recreate, list all tenant databases) which are currently unauthenticated. This is a defence-in-depth
    /// gate that is independent of the (still to be decided) JWT authorization model; once auth is enabled these
    /// endpoints should additionally carry an admin authorization policy.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class DevelopmentOnlyAttribute : Attribute, IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var env = context.HttpContext.RequestServices.GetService<IWebHostEnvironment>();
            if (env == null || !env.IsDevelopment())
            {
                context.Result = new NotFoundResult();
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
