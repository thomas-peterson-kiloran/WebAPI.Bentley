using System;
using System.ComponentModel.DataAnnotations;

namespace WebAPI.Bentley.Validation
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class FutureDateAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value == null) return true; // not our job to validate required

            if (value is DateTime dt)
            {
                // Consider future as strictly greater than now (UTC)
                return dt > DateTime.UtcNow;
            }

            return false;
        }
    }
}
