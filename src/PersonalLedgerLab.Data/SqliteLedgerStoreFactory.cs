using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.Data;

/// <summary>
/// Builds a default <see cref="SqliteLedgerStore"/> for the desktop application.
/// </summary>
public static class SqliteLedgerStoreFactory
{
    /// <summary>
    /// Creates a store rooted at the per-user local application data folder.
    /// Override the path with the PERSONAL_LEDGER_DB environment variable.
    /// </summary>
    public static ILedgerStore CreateDefault()
    {
        string? path = Environment.GetEnvironmentVariable("PERSONAL_LEDGER_DB");
        if (string.IsNullOrWhiteSpace(path))
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PersonalLedgerLab");
            Directory.CreateDirectory(folder);
            path = Path.Combine(folder, "ledger.sqlite");
        }

        return new SqliteLedgerStore(path);
    }
}