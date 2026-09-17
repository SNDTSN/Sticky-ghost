using App.Core.Domain.Entities;

namespace App.Core.Domain.Repositories;

public interface IMemoRepository
{
    void Save(MemoNote memo);

    void Delete(Guid id);

    IEnumerable<MemoNote> GetAll();
}
