using WebAPI.Bentley.Data;

namespace WebAPI.Bentley.Interfaces
{
    public interface IFormDataRepository
    {
        public abstract AppDbContext Context { get;  }
    }
}