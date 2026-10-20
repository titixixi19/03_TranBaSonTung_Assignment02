using BackEnd.BusinessObjects;
using BackEnd.DataAccess;

namespace BackEnd.Repositories;

public class SystemAccountRepository : ISystemAccountRepository
{
    public List<SystemAccount> GetAccounts() => SystemAccountDAO.Instance.GetAccounts();
    public SystemAccount? GetAccountById(short id) => SystemAccountDAO.Instance.GetAccountById(id);
    public bool EmailExists(string email, short? excludeId = null) => SystemAccountDAO.Instance.EmailExists(email, excludeId);
    public SystemAccount AddAccount(SystemAccount account) => SystemAccountDAO.Instance.AddAccount(account);
    public void UpdateAccount(SystemAccount account) => SystemAccountDAO.Instance.UpdateAccount(account);
    public void DeleteAccount(short id) => SystemAccountDAO.Instance.DeleteAccount(id);
}
