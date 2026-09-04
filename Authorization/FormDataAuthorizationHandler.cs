using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using System.Threading.Tasks;
using WebAPI.Bentley;

namespace WebAPI.Bentley.Authorization
{
    public class FormDataAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, CreateOrUpdateFormRequest>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OperationAuthorizationRequirement requirement, CreateOrUpdateFormRequest resource)
        {
            // If no user info, do not authorize
            var user = context.User;
            if (user?.Identity == null || !user.Identity.IsAuthenticated)
            {
                return Task.CompletedTask;
            }

            var userName = user.Identity.Name ?? string.Empty;

            // Admins can do anything
            if (user.IsInRole("Admin"))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // View: allow owners or users in Manager role
            if (requirement.Name == Operations.View.Name)
            {
                if (resource.CreatedBy == userName || user.IsInRole("Manager"))
                {
                    context.Succeed(requirement);
                }
                return Task.CompletedTask;
            }

            // Modify/Delete: only owner may modify/delete
            if (requirement.Name == Operations.Modify.Name || requirement.Name == Operations.Delete.Name)
            {
                if (resource.CreatedBy == userName)
                {
                    context.Succeed(requirement);
                }
                return Task.CompletedTask;
            }

            // Create: any authenticated user
            if (requirement.Name == Operations.Create.Name)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
