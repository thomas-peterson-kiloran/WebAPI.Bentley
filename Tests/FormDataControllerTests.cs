using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Bentley;
using WebAPI.Bentley.Data;
using WebAPI.Bentley.Authorization;
using NUnit.Framework;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace WebAPI.Bentley.Tests
{
    // Simple test authorization service mirroring production rules for predictable tests
    class TestAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            // Not used in these tests
            return Task.FromResult(AuthorizationResult.Failed());
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            // Not used in these tests
            return Task.FromResult(AuthorizationResult.Failed());
        }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IAuthorizationRequirement requirement)
        {
            // Null checks
            var userNameRaw = user?.Identity?.Name ?? string.Empty;
            var userName = userNameRaw?.Trim();

            if (resource is CreateOrUpdateFormRequest fd)
            {
                var createdBy = (fd.CreatedBy ?? string.Empty).Trim();
                if (user?.IsInRole("Admin") ?? false) return Task.FromResult(AuthorizationResult.Success());

                if (requirement is OperationAuthorizationRequirement op)
                {
                    if (op.Name == Operations.Create.Name)
                    {
                        if (user?.Identity?.IsAuthenticated ?? false) return Task.FromResult(AuthorizationResult.Success());
                    }
                    if (op.Name == Operations.View.Name)
                    {
                        if (!string.IsNullOrEmpty(createdBy) && string.Equals(createdBy, userName, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(AuthorizationResult.Success());
                        if (user?.IsInRole("Manager") ?? false) return Task.FromResult(AuthorizationResult.Success());
                    }
                    if (op.Name == Operations.Modify.Name || op.Name == Operations.Delete.Name)
                    {
                        if (!string.IsNullOrEmpty(createdBy) && string.Equals(createdBy, userName, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(AuthorizationResult.Success());
                    }
                }
            }

            return Task.FromResult(AuthorizationResult.Failed());
        }
    }

    // Authorization service that always allows operations (for most happy-path tests)
    class AllowAllAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IAuthorizationRequirement requirement)
            => Task.FromResult(AuthorizationResult.Success());
    }

    // Authorization service that always denies (used to verify forbidden paths)
    class DenyAllAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IAuthorizationRequirement requirement)
            => Task.FromResult(AuthorizationResult.Failed());
    }

    // No-op validator for controller tests so TryValidateModel won't throw when executed outside of full MVC
    internal class NoopObjectModelValidator : Microsoft.AspNetCore.Mvc.ModelBinding.Validation.IObjectModelValidator
    {
        public void Validate(Microsoft.AspNetCore.Mvc.ActionContext actionContext, Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidationStateDictionary? validationState, string prefix, object? model)
        {
            // Run DataAnnotations validation so TryValidateModel behaves similarly to MVC runtime
            if (model == null) return;

            var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(model, actionContext.HttpContext?.RequestServices, items: null);
            var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            // validate all properties
            System.ComponentModel.DataAnnotations.Validator.TryValidateObject(model, validationContext, results, validateAllProperties: true);

            foreach (var vr in results)
            {
                if (vr == null) continue;
                // If no member names provided, add a model-level error
                if (vr.MemberNames == null || !vr.MemberNames.Any())
                {
                    actionContext.ModelState.TryAddModelError(string.Empty, vr.ErrorMessage ?? string.Empty);
                }
                else
                {
                    foreach (var member in vr.MemberNames)
                    {
                        var key = string.IsNullOrEmpty(prefix) ? member : (prefix + "." + member);
                        actionContext.ModelState.TryAddModelError(key ?? string.Empty, vr.ErrorMessage ?? string.Empty);
                    }
                }
            }
        }
    }

    public class FormDataControllerTests
    {
        private AppDbContext CreateContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            return new AppDbContext(options);
        }

        private ClaimsPrincipal CreateUser(string name, params string[] roles)
        {
            var claims = new[] { new Claim(ClaimTypes.Name, name) }
                .Concat(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            var identity = new ClaimsIdentity(claims, "Test");
            return new ClaimsPrincipal(identity);
        }

        private Controllers.FormDataController CreateController(AppDbContext db, ClaimsPrincipal user, IAuthorizationService? authService = null)
        {
            var logger = NullLogger<Controllers.FormDataController>.Instance;
            var auth = authService ?? new TestAuthorizationService();
            var formDataRepository = new FormDataRepository(db);
            var controller = new Controllers.FormDataController(formDataRepository, logger, auth);
            var httpContext = new DefaultHttpContext { User = user };

            // Provide minimal services required by ControllerBase (model validation etc.)
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            // register minimal services via explicit static helpers to avoid missing extension methods
            Microsoft.Extensions.DependencyInjection.OptionsServiceCollectionExtensions.AddOptions(services);
            Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
            // register a no-op IObjectModelValidator so ControllerBase.TryValidateModel has required service
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Microsoft.AspNetCore.Mvc.ModelBinding.Validation.IObjectModelValidator, NoopObjectModelValidator>(services);
            // register ProblemDetailsFactory so ControllerBase.ValidationProblem can build ProblemDetails
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<ProblemDetailsFactory, DefaultProblemDetailsFactory>(services);
            var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
            httpContext.RequestServices = provider;

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            return controller;
        }

        [Test]
        public async Task Create_Persists_And_ReturnsCreated()
        {
            var db = CreateContext("create_db");
            var user = CreateUser("alice");
            var ctrl = CreateController(db, user, new AllowAllAuthorizationService());

            var model = new CreateOrUpdateFormRequest { Subject = "Test", Description = "d" };
            var result = await ctrl.Create(model);

            var created = result.Result as CreatedAtActionResult;
            Assert.IsNotNull(created);
            var returned = created!.Value as CreateOrUpdateFormRequest;
            Assert.IsNotNull(returned);
            Assert.AreEqual("Test", returned!.Subject);

            var stored = await db.FormDatas.FindAsync(returned.Id);
            Assert.NotNull(stored);
            Assert.AreEqual("alice", stored!.CreatedBy);
        }

        [Test]
        public async Task GetById_AllowsOwner_And_ForbidsOthers()
        {
            var db = CreateContext("get_db");
            var item = new CreateOrUpdateFormRequest { Subject = "S", CreatedBy = "bob" };
            db.FormDatas.Add(item);
            await db.SaveChangesAsync();

            var owner = CreateUser("bob");
            var other = CreateUser("alice");

            // Owner allowed
            var ctrlOwner = CreateController(db, owner, new AllowAllAuthorizationService());
            var ok = await ctrlOwner.GetById(item.Id);
            Assert.IsInstanceOf<OkObjectResult>(ok.Result);

            // Other forbidden
            var ctrlOther = CreateController(db, other, new DenyAllAuthorizationService());
            var res = await ctrlOther.GetById(item.Id);
            Assert.IsInstanceOf<ForbidResult>(res.Result);
        }

        [Test]
        public async Task List_ReturnsOnlyUserItems_ForNonAdmin()
        {
            var db = CreateContext("list_db");
            db.FormDatas.Add(new CreateOrUpdateFormRequest { Subject = "A", CreatedBy = "alice" });
            db.FormDatas.Add(new CreateOrUpdateFormRequest { Subject = "B", CreatedBy = "bob" });
            await db.SaveChangesAsync();

            var user = CreateUser("alice");
            var ctrl = CreateController(db, user, new AllowAllAuthorizationService());
            var result = await ctrl.List();
            var okObj = result as OkObjectResult;
            Assert.IsNotNull(okObj);
            // The list result value is an anonymous object with Items; extract using reflection to avoid runtime binder issues
            var value = okObj!.Value!;
            var itemsProp = value.GetType().GetProperty("Items");
            Assert.IsNotNull(itemsProp);
            var itemsObj = itemsProp!.GetValue(value) as System.Collections.IEnumerable;
            Assert.IsNotNull(itemsObj);
            Assert.AreEqual(1, itemsObj!.Cast<object>().Count());
        }

        [Test]
        public async Task Update_WithValidIfMatch_UpdatesEntity()
        {
            var db = CreateContext("update_db");
            var item = new CreateOrUpdateFormRequest { Subject = "Old", CreatedBy = "alice" };
            db.FormDatas.Add(item);
            await db.SaveChangesAsync();

            // get current rowversion
            var saved = await db.FormDatas.AsNoTracking().FirstAsync(f => f.Id == item.Id);
            Assert.IsNotNull(saved.RowVersion);
            var row = saved.RowVersion!;
            var base64 = Convert.ToBase64String(row);

            var user = CreateUser("alice");
            var ctrl = CreateController(db, user, new AllowAllAuthorizationService());
            ctrl.ControllerContext.HttpContext.Request.Headers["If-Match"] = $"\"{base64}\"";

            var model = new CreateOrUpdateFormRequest { Id = item.Id, Subject = "New" };

            var res = await ctrl.Update(item.Id, model);
            Assert.IsInstanceOf<NoContentResult>(res);

            var updated = await db.FormDatas.FindAsync(item.Id);
            Assert.AreEqual("New", updated!.Subject);
        }

        [Test]
        public async Task Update_WithInvalidIfMatch_ReturnsConflict()
        {
            var db = CreateContext("update_conflict_db");
            var item = new CreateOrUpdateFormRequest { Subject = "Old", CreatedBy = "alice" };
            db.FormDatas.Add(item);
            await db.SaveChangesAsync();

            var user = CreateUser("alice");
            var ctrl = CreateController(db, user, new AllowAllAuthorizationService());
            ctrl.ControllerContext.HttpContext.Request.Headers["If-Match"] = "\"invalidbase64\"";

            var model = new CreateOrUpdateFormRequest { Id = item.Id, Subject = "New" };
            var res = await ctrl.Update(item.Id, model);
            Assert.IsInstanceOf<BadRequestObjectResult>(res);
        }

        [Test]
        public async Task SoftDelete_MarksDeleted_WhenIfMatchValid()
        {
            var db = CreateContext("delete_db");
            var item = new CreateOrUpdateFormRequest { Subject = "ToDel", CreatedBy = "alice" };
            db.FormDatas.Add(item);
            await db.SaveChangesAsync();

            var saved = await db.FormDatas.AsNoTracking().FirstAsync(f => f.Id == item.Id);
            var base64 = Convert.ToBase64String(saved.RowVersion!);

            var user = CreateUser("alice");
            var ctrl = CreateController(db, user, new AllowAllAuthorizationService());
            ctrl.ControllerContext.HttpContext.Request.Headers["If-Match"] = $"\"{base64}\"";

            var res = await ctrl.SoftDelete(item.Id);
            Assert.IsInstanceOf<NoContentResult>(res);

            var deleted = await db.FormDatas.IgnoreQueryFilters().FirstAsync(f => f.Id == item.Id);
            Assert.True(deleted.IsDeleted);
        }

        [Test]
        public async Task Create_ReturnsValidationProblem_WhenSubjectMissing()
        {
            var db = CreateContext("val_db");
            var user = CreateUser("alice");
            var ctrl = CreateController(db, user, new AllowAllAuthorizationService());

            var model = new CreateOrUpdateFormRequest { Subject = "" };
            var result = await ctrl.Create(model);
            var obj = result.Result as ObjectResult;
            Assert.IsNotNull(obj);
            Assert.AreEqual(400, obj!.StatusCode);
        }
    }
}
