using App.Core.Domain.Entities;

namespace App.Core.Domain.Repositories;

public interface ICategoryRepository
{
    void Save(Category category);

    void Delete(Guid id);

    IEnumerable<Category> GetAll();
}
