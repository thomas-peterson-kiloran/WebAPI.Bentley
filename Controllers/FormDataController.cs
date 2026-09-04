using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using WebAPI.Bentley.Data;
using WebAPI.Bentley.Utilities;
using WebAPI.Bentley.Authorization;
using System.Linq;
using System;
using WebAPI.Bentley.Interfaces;

namespace WebAPI.Bentley.Controllers
{
    [ApiController]
    [Route("api/forms")]
    public class FormDataController : ControllerBase
    {
        private readonly IFormDataRepository _formDataRepository;
        private readonly ILogger<FormDataController> _logger;
        private readonly IAuthorizationService _authorizationService;

        public FormDataController(IFormDataRepository formDataRepository, ILogger<FormDataController> logger, IAuthorizationService authorizationService)
        {
            _formDataRepository = formDataRepository;
            _logger = logger;
            _authorizationService = authorizationService;
        }

        // POST /api/forms - create
        [HttpPost]
        public async Task<ActionResult<CreateOrUpdateFormRequest>> Create([FromBody] CreateOrUpdateFormRequest request)
        {
            if (User?.Identity == null || !User.Identity.IsAuthenticated)
            {
                return Unauthorized();
            }

           if (request == null) return BadRequest(new { message = "Request body is required." });

           var userName = User.Identity.Name ?? string.Empty;


            // set CreatedBy from authenticated user and sanitize
            request.CreatedBy = Sanitizer.Sanitize(userName, 200) ?? string.Empty;
            request.Subject = Sanitizer.Sanitize(request.Subject, 200) ?? string.Empty;
            request.Description = Sanitizer.Sanitize(request.Description, 2000);

            if (!TryValidateModel(request)) return ValidationProblem(ModelState);

            if (request.Id == Guid.Empty) request.Id = Guid.NewGuid();
            request.CreatedAt = DateTime.UtcNow;
            request.IsDeleted = false;

            try
            {
                var auth = await _authorizationService.AuthorizeAsync(User, request, Operations.Create);
                if (!auth.Succeeded) return Forbid();

                _formDataRepository.Context.FormDatas.Add(request);
                await _formDataRepository.Context.SaveChangesAsync();
                return CreatedAtAction(nameof(GetById), new { id = request.Id }, request);
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database update failed while creating FormData.");
                return StatusCode(503, new { message = "Service temporarily unavailable. Please try again later." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while creating FormData.");
                return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        // GET /api/forms/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<CreateOrUpdateFormRequest>> GetById(Guid id)
        {
            try
            {
                var item = await _formDataRepository.Context.FormDatas.FindAsync(id);
                if (item == null) return NotFound();

                // Authorization: ensure user can view
                var authResult = await _authorizationService.AuthorizeAsync(User, item, Operations.View);
                if (!authResult.Succeeded)
                {
                    if (User?.Identity == null || !User.Identity.IsAuthenticated) return Unauthorized();
                    return Forbid();
                }

                return Ok(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving FormData with id {Id}.", id);
                return StatusCode(503, new { message = "Service temporarily unavailable. Please try again later." });
            }
        }

        // GET /api/forms - list with pagination and filtering
        [HttpGet]
        public async Task<ActionResult> List([
            FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string? subject = null,
            [FromQuery] bool? critical = null,
            [FromQuery] int? minPriority = null,
            [FromQuery] int? maxPriority = null,
            [FromQuery] string? createdBy = null)
        {
            if (User?.Identity == null || !User.Identity.IsAuthenticated) return Unauthorized();

            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;

            IQueryable<CreateOrUpdateFormRequest> query = _formDataRepository.Context.FormDatas.AsQueryable();

            // If not admin or manager, restrict to user's own items
            var userName = User.Identity.Name ?? string.Empty;
            var isAdmin = User.IsInRole("Admin");
            var isManager = User.IsInRole("Manager");

            if (!isAdmin && !isManager)
            {
                query = query.Where(f => f.CreatedBy == userName);
            }

            if (!string.IsNullOrWhiteSpace(subject))
            {
                // sanitize filter input
                subject = Sanitizer.Sanitize(subject, 200);
                query = query.Where(f => f.Subject!.Contains(subject!));
            }

            if (critical.HasValue)
            {
                query = query.Where(f => f.Critical == critical.Value);
            }

            if (minPriority.HasValue)
            {
                query = query.Where(f => f.Priority.HasValue && f.Priority.Value >= minPriority.Value);
            }

            if (maxPriority.HasValue)
            {
                query = query.Where(f => f.Priority.HasValue && f.Priority.Value <= maxPriority.Value);
            }

            if (!string.IsNullOrWhiteSpace(createdBy))
            {
                createdBy = Sanitizer.Sanitize(createdBy, 200);
                if (!isAdmin && createdBy != userName)
                {
                    return Forbid();
                }
                query = query.Where(f => f.CreatedBy == createdBy);
            }

            try
            {
                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var items = await query
                    .OrderByDescending(f => f.CreatedAt)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var result = new
                {
                    Items = items,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing FormData.");
                return StatusCode(503, new { message = "Service temporarily unavailable. Please try again later." });
            }
        }

        // PUT /api/forms/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] CreateOrUpdateFormRequest request)
        {
            if (request == null || id != request.Id) return BadRequest();

            var existing = await _formDataRepository.Context.FormDatas.FindAsync(id);
            if (existing == null) return NotFound();

            // Authorization: ensure user can modify
            var authModify = await _authorizationService.AuthorizeAsync(User, existing, Operations.Modify);
            if (!authModify.Succeeded)
            {
                if (User?.Identity == null || !User.Identity.IsAuthenticated) return Unauthorized();
                return Forbid();
            }

            // Require If-Match header with client's RowVersion for optimistic concurrency
            var ifMatch = Request?.Headers["If-Match"].FirstOrDefault();
            if (string.IsNullOrEmpty(ifMatch))
            {
                return StatusCode(428, "If-Match header required for concurrency control.");
            }

            // Remove optional quotes
            ifMatch = ifMatch.Trim('"');
            byte[] clientRowVersion;
            try
            {
                clientRowVersion = Convert.FromBase64String(ifMatch);
            }
            catch
            {
                return BadRequest("Invalid If-Match header value (must be base64 of RowVersion).");
            }

            // Sanitize inputs
            request.Subject = Sanitizer.Sanitize(request.Subject, 200) ?? string.Empty;
            request.Description = Sanitizer.Sanitize(request.Description, 2000);

            if (!TryValidateModel(request)) return ValidationProblem(ModelState);

            // apply updates
            existing.Subject = request.Subject;
            existing.Description = request.Description;
            existing.DueDate = request.DueDate;
            existing.Critical = request.Critical;
            existing.Priority = request.Priority;
            existing.UpdatedAt = DateTime.UtcNow;

            // Set original RowVersion to client's value so EF can detect concurrency conflicts
            _formDataRepository.Context.Entry(existing!).Property(e => e.RowVersion).OriginalValue = clientRowVersion;
            _formDataRepository.Context.Entry(existing!).State = EntityState.Modified;

            try
            {
                await _formDataRepository.Context.SaveChangesAsync();
                return NoContent();
            }
            catch (DbUpdateConcurrencyException)
            {
                var current = await _formDataRepository.Context.FormDatas.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
                return Conflict(new { message = "The record you attempted to edit was modified by another user.", current });
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database update failed while updating FormData {Id}.", id);
                return StatusCode(503, new { message = "Service temporarily unavailable. Please try again later." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while updating FormData {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }

        // DELETE /api/forms/{id} - soft delete
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> SoftDelete(Guid id)
        {
            var existing = await _formDataRepository.Context.FormDatas.FindAsync(id);
            if (existing == null) return NotFound();

            // Authorization: ensure user can delete
            var authDelete = await _authorizationService.AuthorizeAsync(User, existing, Operations.Delete);
            if (!authDelete.Succeeded)
            {
                if (User?.Identity == null || !User.Identity.IsAuthenticated) return Unauthorized();
                return Forbid();
            }

            // Require If-Match header for concurrency
            var ifMatch = Request?.Headers["If-Match"].FirstOrDefault();
            if (string.IsNullOrEmpty(ifMatch))
            {
                return StatusCode(428, "If-Match header required for concurrency control.");
            }

            ifMatch = ifMatch.Trim('"');
            byte[] clientRowVersion;
            try
            {
                clientRowVersion = Convert.FromBase64String(ifMatch);
            }
            catch
            {
                return BadRequest("Invalid If-Match header value (must be base64 of RowVersion).");
            }

            existing.IsDeleted = true;
            existing.DeletedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;

            _formDataRepository.Context.Entry(existing!).Property(e => e.RowVersion).OriginalValue = clientRowVersion;
            _formDataRepository.Context.Entry(existing!).State = EntityState.Modified;

            try
            {
                await _formDataRepository.Context.SaveChangesAsync();
                return NoContent();
            }
            catch (DbUpdateConcurrencyException)
            {
                var current = await _formDataRepository.Context.FormDatas.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
                return Conflict(new { message = "The record you attempted to delete was modified by another user.", current });
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database update failed while deleting FormData {Id}.", id);
                return StatusCode(503, new { message = "Service temporarily unavailable. Please try again later." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while deleting FormData {Id}.", id);
                return StatusCode(500, new { message = "An unexpected error occurred." });
            }
        }
    }
}
