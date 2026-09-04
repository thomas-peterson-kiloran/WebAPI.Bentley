using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace WebAPI.Bentley.Authorization
{
    public static class Operations
    {
        public static OperationAuthorizationRequirement Create = new OperationAuthorizationRequirement { Name = nameof(Create) };
        public static OperationAuthorizationRequirement View = new OperationAuthorizationRequirement { Name = nameof(View) };
        public static OperationAuthorizationRequirement Modify = new OperationAuthorizationRequirement { Name = nameof(Modify) };
        public static OperationAuthorizationRequirement Delete = new OperationAuthorizationRequirement { Name = nameof(Delete) };
    }
}
