using System;
using System.ComponentModel.DataAnnotations;

namespace WebAPI.Bentley
{
    public class CreateOrUpdateFormRequest
    {
        public Guid Id { get; set; }
        [Required]
        [StringLength(200, MinimumLength = 1)]
        public string Subject { get; set; }
        public string? Description { get; set; }
        [WebAPI.Bentley.Validation.FutureDate(ErrorMessage = "DueDate must be a future date.")]
        public DateTime? DueDate { get; set; }
        public bool? Critical { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string CreatedBy { get; set; }        
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }
        public CreateOrUpdateFormRequest()
        {
            Id = Guid.NewGuid();
            Subject = string.Empty;
            CreatedAt = DateTime.UtcNow;
            IsDeleted = false;
            CreatedBy = string.Empty;
        }
        [Range(1, 10, ErrorMessage = "Priority must be between 1 and 10.")]
        public int? Priority { get; set; }
    }
}
