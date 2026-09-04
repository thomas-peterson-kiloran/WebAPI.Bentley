using WebAPI.Bentley.Data;
using WebAPI.Bentley.Interfaces;

namespace WebAPI.Bentley
{
    public class FormDataRepository : IFormDataRepository
    {
        internal readonly AppDbContext _context;

        public FormDataRepository(AppDbContext context)
        {
            _context = context;
        }

        public AppDbContext Context
        {
            get { return _context; }
        }
    }
}
